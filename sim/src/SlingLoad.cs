namespace Rotorwash.Sim;

/// <summary>
/// Something that hangs off the airframe and pulls on it.
///
/// The flight model owns one of these at most (<see cref="Helicopter.Hook"/>) and steps it
/// from inside <see cref="Helicopter.ComputeWrench"/>, so a caller cannot forget to advance
/// the load and quietly fly an aircraft that is carrying nothing.
/// </summary>
public interface IExternalLoad
{
    /// <summary>
    /// Hang the load straight down under the hook, at rest relative to the aircraft.
    ///
    /// The trim solver needs this. Its residual has to be a pure function of the six
    /// unknowns, and a load left swinging from a previous evaluation makes it a function of
    /// evaluation order instead - the same failure the rotor state already had, in a slower
    /// and more embarrassing form. A 420 kg load three degrees off vertical is 215 N of
    /// lateral force, which is thousands of times the convergence tolerance.
    /// </summary>
    void Reset(Helicopter h);

    /// <summary>
    /// Advance the load and return the wrench it applies to the airframe about the centre of
    /// gravity, body axes. Gravity on the LOAD is the load's own business; what the aircraft
    /// feels is cable tension at the hook and nothing else.
    /// </summary>
    void Update(Helicopter h, double dt, out Vec3 forceBody, out Vec3 momentBody);
}

/// <summary>
/// Where there is open water, and how high its surface is.
///
/// Deliberately two methods and no more. The world layer (<c>WorldHeight</c>) owns the real
/// answer; this is the whole of what a bucket needs to know, so the two systems can be built
/// at the same time by different hands and joined with one adapter.
///
/// <para><b>Axes.</b> Sim world frame, NED: X north, Y east, and the returned surface height
/// is metres ABOVE datum, positive up - the same convention as
/// <see cref="IEnvironment.GroundHeight"/>. The Godot layer is X east / Z south, so the
/// bridge adapter is the same flip <c>HelicopterController</c> already does for terrain:
/// <code>
/// bool IsWater(double n, double e) => WorldHeight.IsWater((float)e, (float)-n);
/// double SurfaceHeight(double n, double e) => WorldHeight.WaterLevel;   // -5 m, per D-040
/// </code>
/// </para>
/// </summary>
public interface IWaterSource
{
    /// <summary>True where there is open water deep enough to dip a bucket in.</summary>
    bool IsWater(double north, double east);

    /// <summary>Water surface height above datum, m positive up. Only meaningful on water.</summary>
    double SurfaceHeight(double north, double east);
}

/// <summary>The default: a dry world. A bucket over this never fills.</summary>
public sealed class NoWater : IWaterSource
{
    public static readonly NoWater Instance = new();
    public bool IsWater(double north, double east) => false;
    public double SurfaceHeight(double north, double east) => double.NegativeInfinity;
}

/// <summary>A round lake at a fixed level. All the headless tests need.</summary>
public sealed class FlatWater : IWaterSource
{
    /// <summary>Centre of the lake; X is north, Y is east. Z is ignored.</summary>
    public Vec3 Centre { get; set; } = Vec3.Zero;
    public double Radius { get; set; } = 250.0;

    /// <summary>Surface height above datum, m. -5 m matches <c>WorldHeight.WaterLevel</c> (D-040).</summary>
    public double Level { get; set; } = -5.0;

    public bool IsWater(double north, double east)
    {
        double dn = north - Centre.X, de = east - Centre.Y;
        return dn * dn + de * de <= Radius * Radius;
    }

    public double SurfaceHeight(double north, double east) => Level;
}

/// <summary>
/// A mass on the end of a cable, underneath the aircraft.
///
/// <para><b>Why this is not a MassItem.</b> The cheap version of an underslung load - the
/// one story.md section 7.7 proposes as the "smallest version today" - is 420 kg added to
/// <c>Airframe.Mass</c> at the hook position. That gets the weight right and everything else
/// wrong. A load on a line is a second body. It swings with a period set by the cable and
/// nothing else; it LAGS the aircraft, so moving the stick moves the load a quarter of a
/// cycle later; and a pilot who corrects what he sees underneath him is applying exactly the
/// input that feeds the swing. The classic accident is not a heavy load, it is a load the
/// pilot drove into resonance and then kept driving. None of that exists in a lump of mass
/// bolted to the airframe, and all of it is the reason this system is interesting.</para>
///
/// <para><b>The model.</b> A point mass integrated in world NED, joined to the hook by a
/// cable that pulls and never pushes. The cable is a stiff spring-damper rather than a hard
/// constraint: it makes slack, snatch, breaking and ground contact all fall out of the same
/// few lines, and the stiffness is set from a physical quantity - strain at the load's own
/// weight, <see cref="CableStrain"/> - so the axial mode sits at sqrt(g / (L * strain)),
/// about 31 rad/s, twenty times faster than the swing it must not contaminate. Nothing
/// anywhere in this file knows what a pendulum period is. It comes out of the geometry,
/// which is the only way to be sure it is right.</para>
///
/// <para><b>Stored relative to the hook.</b> The load's position is kept as an OFFSET from
/// the hook, not as an absolute world position. That is not a micro-optimisation: the trim
/// solver pins the airframe at the origin and re-applies that pin every step while telling
/// the aircraft it is doing 30 m/s, so an absolutely-positioned load flies forty-five metres
/// out in front of a stationary hook during a single residual evaluation and reports a cable
/// tension of several meganewtons. An offset is carried along by any teleport for free, and
/// the dynamics are identical: d(offset)/dt = v_load - v_hook.</para>
/// </summary>
public class SlingLoad : IExternalLoad
{
    public string Name { get; set; } = "load";

    /// <summary>Mass of the thing itself, kg. Two Huey blades with grips and tie bars: 420.</summary>
    public double EmptyMass { get; set; } = 420.0;

    /// <summary>Whatever it has picked up since - water, mostly. kg.</summary>
    public double ContentsMass { get; protected set; }

    public double Mass => EmptyMass + ContentsMass;

    /// <summary>
    /// Cable length, m. This is the only number the swing period depends on, and a pilot
    /// choosing it is choosing how the aircraft will handle: short is stiff and snatchy,
    /// long is slow and forgiving and puts the load in the trees.
    /// </summary>
    public double CableLength { get; set; } = 5.0;

    /// <summary>Hook attachment point, airframe datum frame. Matches Loadout's "hook" module.</summary>
    public Vec3 HookPosition { get; set; } = new(0.1, 0, 0.4);

    /// <summary>
    /// Cable strain carrying the load's own static weight. 0.01 is five centimetres of
    /// stretch on a five-metre strop, which is a polyester round sling - nobody hangs a load
    /// straight off bare wire rope, and the soft link is there in real life for the same
    /// reason it is useful here.
    ///
    /// <para>It sets the axial mode at sqrt(g / (L * strain)) = 14 rad/s, ten times faster
    /// than the swing and slow enough that the airframe's 2/rev shake does not arrive at the
    /// load as tension. Stiffer is not more accurate: at 0.002 the hook's own 2/rev motion
    /// (about 16 mm, from 1.3 m of arm under a teetering head) exceeds the cable's static
    /// stretch, and the cable goes slack twice a revolution in a steady hover.</para>
    /// </summary>
    public double CableStrain { get; set; } = 0.01;

    /// <summary>Damping on the cable's axial mode, as a fraction of critical.</summary>
    public double CableDampingRatio { get; set; } = 0.15;

    /// <summary>
    /// Smoothing on the hook velocity the cable DAMPER sees, s. The spring is not smoothed.
    ///
    /// A two-bladed teetering head shakes the airframe hard at 2/rev - about 11 Hz here, and
    /// tens of degrees per second of body rate. The hook hangs 1.3 m below the centre of
    /// gravity, so that shake is a metre per second of hook velocity, and an undamped-by-
    /// nothing viscous term transcribes all of it straight into cable tension: measured 17.7
    /// kN of ripple on a 4.9 kN load, hovering steadily. A strop and a fitting and half a
    /// tonne of inertia do not do that. Eighty milliseconds of smoothing leaves the cable's
    /// own axial mode (31 rad/s) fully damped and stops it acting as a strain gauge for the
    /// rotor.
    /// </summary>
    public double DamperSmoothing { get; set; } = 0.08;

    /// <summary>
    /// Tension the cable parts at, N. 60 kN is roughly four times the static weight of the
    /// heaviest load this aircraft can lift - enough margin for ordinary snatch, not enough
    /// to drag an anchored load out of the ground with a helicopter.
    /// </summary>
    public double BreakingTension { get; set; } = 60_000.0;

    /// <summary>Drag area of the load in air, Cd*A, m^2.</summary>
    public double DragArea { get; set; } = 1.5;

    /// <summary>Longest internal integration step, s. Keeps a 60 Hz host honest.</summary>
    public double MaxSubStep { get; set; } = 1.0 / 480.0;

    /// <summary>False once the load has been released or the cable has parted.</summary>
    public bool Attached { get; private set; } = true;

    /// <summary>True if the cable broke rather than being released on purpose.</summary>
    public bool CableParted { get; private set; }

    /// <summary>True while the load is resting on the ground.</summary>
    public bool OnGround { get; private set; }

    // --- Read-outs ----------------------------------------------------------

    public Vec3 PositionWorld { get; private set; }
    public Vec3 VelocityWorld { get; private set; }

    /// <summary>
    /// Cable tension as a load cell would report it, N. Zero when slack.
    ///
    /// Lightly damped, for the reason the power telemetry is averaged over a revolution: the
    /// instantaneous number carries the rotor's 2/rev and is a phase reading rather than a
    /// measurement. <see cref="CableTensionInstant"/> is the raw one, and it is what decides
    /// whether the cable parts, because cables part on peaks.
    /// </summary>
    public double CableTension { get; private set; }

    /// <summary>Tension this instant, N, 2/rev ripple and all.</summary>
    public double CableTensionInstant { get; private set; }

    /// <summary>What a hook load gauge would read, kg.</summary>
    public double HookLoadKg => CableTension / Atmosphere.Gravity;

    public bool CableSlack => Attached && CableTensionInstant <= 0.0;

    /// <summary>Angle of the cable from vertical, degrees. This is the number a pilot chases.</summary>
    public double SwingAngleDeg
    {
        get
        {
            double len = _offset.Length;
            if (len < 1e-6) return 0;
            return Math.Acos(Math.Clamp(_offset.Z / len, -1, 1)) * 180.0 / Math.PI;
        }
    }

    /// <summary>Displacement of the load from the hook, world axes, m.</summary>
    public Vec3 Offset => _offset;

    /// <summary>
    /// The textbook period for this cable, s. Nothing in the dynamics reads this - it exists
    /// so a test can check the model against theory, and so a HUD can tell a pilot which
    /// frequency he must not fly at.
    /// </summary>
    public double AnalyticPeriod => 2.0 * Math.PI * Math.Sqrt(CableLength / Atmosphere.Gravity);

    private Vec3 _offset = new(0, 0, 5.0);
    private Vec3 _vel;
    private Vec3 _hookVelSmoothed;

    // ------------------------------------------------------------------ release

    /// <summary>
    /// Let it go, now.
    ///
    /// This is the answer to a swing that has got away, and it has to be instant and
    /// unconditional for that to be true. Whatever is on the cable is worth less than the
    /// aircraft, and the moment the pilot has to wonder whether the release will work is the
    /// moment the swing wins.
    /// </summary>
    public void Jettison()
    {
        Attached = false;
        CableTension = 0;
    }

    /// <summary>Put a jettisoned or parted load back on the hook, as a ground crew would.</summary>
    public void Reattach()
    {
        Attached = true;
        CableParted = false;
    }

    // ------------------------------------------------------------------ reset

    public void Reset(Helicopter h)
    {
        if (!Attached) return;
        HookFrame(h, out Vec3 hookPos, out Vec3 hookVel);
        ResetAt(hookPos, hookVel);
    }

    /// <summary>Hang it under a hook given directly, for a bench with no aircraft attached.</summary>
    public void ResetAt(Vec3 hookPosWorld, Vec3 hookVelWorld)
    {
        // Hanging, stretched by exactly the amount that carries its own weight, so it is in
        // equilibrium on the first step rather than starting with a snatch.
        _offset = new Vec3(0, 0, CableLength * (1.0 + CableStrain));
        _vel = hookVelWorld;
        _hookVelSmoothed = hookVelWorld;
        PositionWorld = hookPosWorld + _offset;
        VelocityWorld = _vel;
        CableTension = CableTensionInstant = Mass * Atmosphere.Gravity;
        OnGround = false;
    }

    /// <summary>
    /// Put the load at an angle off vertical without changing its speed.
    ///
    /// A bench needs a starting swing to measure a period, and the game needs to be able to
    /// spawn a load that is already moving - a snatch off a slope does not leave it hanging
    /// tidily underneath.
    /// </summary>
    public void SetSwing(double angleRad, double azimuthRad = 0)
    {
        double len = CableLength * (1.0 + CableStrain);
        _offset = new Vec3(Math.Sin(angleRad) * Math.Cos(azimuthRad) * len,
                           Math.Sin(angleRad) * Math.Sin(azimuthRad) * len,
                           Math.Cos(angleRad) * len);
    }

    // ------------------------------------------------------------------ step

    public void Update(Helicopter h, double dt, out Vec3 forceBody, out Vec3 momentBody)
    {
        forceBody = Vec3.Zero;
        momentBody = Vec3.Zero;
        if (dt <= 0) return;

        Vec3 rBody = HookPosition - h.CentreOfGravity;
        HookFrame(h, out Vec3 hookPos, out Vec3 hookVel);

        Vec3 impulse = Advance(h.Env, hookPos, hookVel, dt);
        if (!Attached) return;

        // Averaged over the substeps rather than sampled at the end of them: a snatch lasts
        // a few milliseconds, and sampling it reports either nothing or a spike.
        Vec3 fWorld = impulse / dt;
        forceBody = h.State.Orientation.InverseRotate(fWorld);

        // The hook sits about 1.3 m BELOW the centre of gravity on this airframe, so a load
        // that is not hanging straight down rolls and pitches the aircraft as well as pulling
        // it sideways. That moment arm is most of why a swinging load feels alive.
        momentBody = Vec3.Cross(rBody, forceBody);
    }

    /// <summary>
    /// Advance the load against a hook whose motion is given directly, without an aircraft.
    ///
    /// For benches that want a pinned pivot, and for the game to keep simulating a load it
    /// has put down. Returns the impulse the cable delivered to the hook, N.s, world axes.
    /// </summary>
    public Vec3 StepAgainstHook(Vec3 hookPosWorld, Vec3 hookVelWorld, IEnvironment env, double dt)
        => Advance(env, hookPosWorld, hookVelWorld, dt);

    private void HookFrame(Helicopter h, out Vec3 hookPos, out Vec3 hookVel)
    {
        Vec3 rBody = HookPosition - h.CentreOfGravity;
        Vec3 rWorld = h.State.Orientation.Rotate(rBody);
        hookPos = h.State.Position + rWorld;
        hookVel = h.State.Velocity
                  + h.State.Orientation.Rotate(Vec3.Cross(h.State.AngularVelocity, rBody));
    }

    private Vec3 Advance(IEnvironment env, Vec3 hookPos, Vec3 hookVel, double dt)
    {
        double kf = 1.0 - Math.Exp(-dt / Math.Max(DamperSmoothing, 1e-6));
        _hookVelSmoothed += (hookVel - _hookVelSmoothed) * kf;

        int sub = Math.Max(1, (int)Math.Ceiling(dt / Math.Max(MaxSubStep, 1e-6)));
        double step = dt / sub;
        Vec3 impulse = Vec3.Zero;
        for (int i = 0; i < sub; i++)
            impulse += Integrate(env, hookPos, hookVel, step) * step;
        return impulse;
    }

    /// <summary>One substep. Returns the cable force on the AIRCRAFT, world axes, N.</summary>
    private Vec3 Integrate(IEnvironment env, Vec3 hookPos, Vec3 hookVel, double dt)
    {
        double m = Math.Max(Mass, 1e-3);
        Vec3 pos = Attached ? hookPos + _offset : PositionWorld;
        Vec3 onAircraft = Vec3.Zero;

        // Weight. NED, so down is +Z.
        Vec3 f = new(0, 0, m * Atmosphere.Gravity);

        // --- Cable ------------------------------------------------------------
        CableTensionInstant = 0;
        if (Attached)
        {
            double len = _offset.Length;
            if (len > 1e-6)
            {
                Vec3 u = _offset / len;                       // hook -> load
                double stretch = len - CableLength;
                if (stretch > 0)
                {
                    // Stiffness from strain, so the axial mode sits at sqrt(g/(L*strain))
                    // whatever the load weighs. A heavier load does not make a stiffer
                    // problem for the integrator, it stretches the same cable further.
                    double k = m * Atmosphere.Gravity / Math.Max(CableLength * CableStrain, 1e-6);
                    double c = 2.0 * CableDampingRatio * Math.Sqrt(k * m);
                    double rate = Vec3.Dot(_vel - _hookVelSmoothed, u);
                    double tension = k * stretch + c * rate;

                    // A cable cannot push, and a damper on a slackening cable must not pull
                    // the load back up.
                    if (tension > 0)
                    {
                        if (tension > BreakingTension)
                        {
                            CableParted = true;
                            Attached = false;
                            PositionWorld = pos;
                        }
                        else
                        {
                            CableTensionInstant = tension;
                            f += u * -tension;
                            onAircraft = u * tension;
                        }
                    }
                }
            }
        }

        // --- Air ---------------------------------------------------------------
        double rho = env.Atmosphere.DensityAt(-pos.Z);
        Vec3 vAir = _vel - env.Wind(pos);
        double speed = vAir.Length;
        if (speed > 1e-4) f += vAir * (-0.5 * rho * DragArea * speed);

        // --- Whatever this particular load does (a bucket fills) ----------------
        f += PayloadForces(env, pos, dt);

        // --- Ground -------------------------------------------------------------
        double ground = env.GroundHeight(pos.X, pos.Y);
        double penetration = pos.Z - (-ground);
        OnGround = penetration > 0;
        if (penetration > 0)
        {
            double pen = Math.Min(penetration, 2.0);
            double normal = Math.Max(0, m * 40.0 * pen + (_vel.Z > 0 ? m * 8.0 * _vel.Z : 0));
            f += new Vec3(0, 0, -normal);

            Vec3 vt = new(_vel.X, _vel.Y, 0);
            double t = vt.Length;
            if (t > 1e-4)
            {
                // Enough friction that a load put down stays put and the cable has to drag
                // it, which is what makes lifting off the ground a distinct event.
                double friction = Math.Min(0.8 * normal, m * t / Math.Max(dt, 1e-4) * 0.5);
                f += -vt / t * friction;
            }
        }

        // --- Integrate -----------------------------------------------------------
        _vel += f / m * dt;
        if (Attached)
        {
            _offset += (_vel - hookVel) * dt;
            PositionWorld = hookPos + _offset;
        }
        else
        {
            PositionWorld += _vel * dt;
        }
        VelocityWorld = _vel;
        CableTension += (CableTensionInstant - CableTension) * (1.0 - Math.Exp(-dt / 0.15));

        if (!PositionWorld.IsFinite || !_vel.IsFinite)
            throw new InvalidOperationException(
                $"Sling load '{Name}' diverged - the cable produced a non-finite state.");

        return onAircraft;
    }

    /// <summary>
    /// Extra world-axis force from whatever this load is (buoyancy, water drag), and the
    /// place a load gets to change its own mass. Zero for an inert load.
    /// </summary>
    protected virtual Vec3 PayloadForces(IEnvironment env, Vec3 posWorld, double dt) => Vec3.Zero;
}

/// <summary>
/// A collapsible water bucket on the end of the cable.
///
/// <para><b>Filling.</b> Hover low enough to put the bucket in the lake and it floods. The
/// rate scales with how much of it is under, so a shallow dip is a slow one and the pilot's
/// incentive is to get it properly in - which means flying lower, over water, getting
/// heavier, with a cable that is already moving. It takes <see cref="FillSeconds"/> fully
/// immersed.</para>
///
/// <para><b>Why it gets heavier while he holds the hover.</b> Water inside the bucket that
/// lies BELOW the outside waterline is already being held up by the lake; only the water
/// above it has to be held up by the rotor. So the felt weight is not the contents, it is
/// the contents minus what the lake is supporting, and that difference grows as the bucket
/// fills past the depth it is dipped to. Dip it half in and the load comes on steadily as it
/// fills. Put it right under and almost nothing happens until the pull-up, and then half a
/// tonne arrives in the second and a half it takes to clear the surface. Both of those are
/// real, and neither is written down below - they are the same two lines of buoyancy seen
/// from two depths.</para>
///
/// <para><b>Dumping</b> is a valve. It is instant, and the aircraft leaps.</para>
/// </summary>
public sealed class WaterBucket : SlingLoad
{
    public const double WaterDensity = 1000.0;   // kg/m^3

    /// <summary>Where the water is. Defaults to a dry world, which never fills.</summary>
    public IWaterSource Water { get; set; } = NoWater.Instance;

    /// <summary>Bucket volume, m^3. 0.5 m^3 is 500 litres - half a tonne, on a 4.3 t aircraft.</summary>
    public double Capacity { get; set; } = 0.5;

    /// <summary>Depth of the bucket from rim to base, m. Sets how much of a dip counts.</summary>
    public double Height { get; set; } = 1.6;

    /// <summary>Seconds to fill from empty when fully submerged.</summary>
    public double FillSeconds { get; set; } = 6.0;

    /// <summary>Displaced volume of the bucket structure itself, m^3. Small: it is a bag.</summary>
    public double ShellVolume { get; set; } = 0.06;

    /// <summary>Drag area in water, Cd*A, m^2. Water is 800x air - do not taxi with it in.</summary>
    public double WaterDragArea { get; set; } = 0.8;

    /// <summary>Full capacity expressed as mass, kg.</summary>
    public double CapacityKg => Capacity * WaterDensity;

    /// <summary>0 to 1.</summary>
    public double FillFraction => CapacityKg > 0 ? ContentsMass / CapacityKg : 0;

    /// <summary>How much of the bucket is under the surface, 0 to 1.</summary>
    public double Immersion { get; private set; }

    public bool InWater => Immersion > 0;

    /// <summary>How much of the contents the lake is currently holding up, kg.</summary>
    public double SupportedByWater { get; private set; }

    /// <summary>What the rotor is actually carrying of this load right now, kg.</summary>
    public double FeltMass =>
        Math.Max(0, Mass - SupportedByWater - WaterDensity * ShellVolume * Immersion);

    public WaterBucket()
    {
        Name = "bucket";
        EmptyMass = 35.0;      // a folded fabric bag, a frame and a valve
        DragArea = 0.9;
    }

    /// <summary>Open the valve. All of it, at once.</summary>
    public void Dump() => ContentsMass = 0;

    /// <summary>For setting up a test or restoring a save: put a known amount of water in it.</summary>
    public void SetContents(double kg) => ContentsMass = Math.Clamp(kg, 0, CapacityKg);

    protected override Vec3 PayloadForces(IEnvironment env, Vec3 posWorld, double dt)
    {
        Immersion = 0;
        SupportedByWater = 0;

        if (!Water.IsWater(posWorld.X, posWorld.Y)) return Vec3.Zero;

        // NED: down is +Z, so the water surface is at -SurfaceHeight and deeper is larger.
        double waterZ = -Water.SurfaceHeight(posWorld.X, posWorld.Y);
        double baseZ = posWorld.Z + Height;              // the bucket hangs below its bridle
        Immersion = Math.Clamp((baseZ - waterZ) / Math.Max(Height, 1e-3), 0, 1);
        if (Immersion <= 0) return Vec3.Zero;

        if (ContentsMass < CapacityKg)
            ContentsMass = Math.Min(
                CapacityKg,
                ContentsMass + CapacityKg / Math.Max(FillSeconds, 1e-3) * Immersion * dt);

        // The lake holds up the shell's own displacement, and whatever contents lie below the
        // outside waterline.
        SupportedByWater = CapacityKg * Math.Min(FillFraction, Immersion);
        double up = (SupportedByWater + WaterDensity * ShellVolume * Immersion) * Atmosphere.Gravity;

        Vec3 v = VelocityWorld;
        double speed = v.Length;
        Vec3 drag = speed > 1e-4
            ? v * (-0.5 * WaterDensity * WaterDragArea * Immersion * speed)
            : Vec3.Zero;

        return new Vec3(0, 0, -up) + drag;
    }
}
