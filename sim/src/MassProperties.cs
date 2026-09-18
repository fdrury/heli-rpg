namespace Rotorwash.Sim;

/// <summary>A lump of mass at a place on the airframe.</summary>
public readonly struct MassItem
{
    public readonly string Name;
    /// <summary>Position in the airframe reference frame (FRD, origin at the datum), m.</summary>
    public readonly Vec3 Position;
    public readonly double Mass;
    /// <summary>Own inertia about its own centre, kg.m^2 (diagonal). Zero treats it as a point.</summary>
    public readonly Vec3 OwnInertia;

    public MassItem(string name, Vec3 position, double mass, Vec3 ownInertia = default)
    {
        Name = name; Position = position; Mass = mass; OwnInertia = ownInertia;
    }
}

/// <summary>
/// Accumulates mass items into a total mass, a centre of gravity and an inertia tensor.
///
/// This is the bridge between the block builder and the flight model: every block the
/// player places is a MassItem, so a tail-heavy build is tail-heavy in the air, a
/// stripped-out airframe accelerates harder, and hanging a winch off one side really
/// does make the aircraft want to roll. No fudge factors, just the parallel axis theorem.
/// </summary>
public sealed class MassProperties
{
    private readonly List<MassItem> _items = new();

    public IReadOnlyList<MassItem> Items => _items;

    public void Add(MassItem item) => _items.Add(item);
    public void Add(string name, Vec3 position, double mass, Vec3 ownInertia = default)
        => _items.Add(new MassItem(name, position, mass, ownInertia));
    public void Clear() => _items.Clear();

    public bool Remove(string name)
    {
        int i = _items.FindIndex(x => x.Name == name);
        if (i < 0) return false;
        _items.RemoveAt(i);
        return true;
    }

    public void SetMass(string name, double mass)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Name == name)
            {
                _items[i] = new MassItem(name, _items[i].Position, mass, _items[i].OwnInertia);
                return;
            }
        }
    }

    public double TotalMass
    {
        get { double m = 0; foreach (var it in _items) m += it.Mass; return m; }
    }

    /// <summary>Centre of gravity in the airframe datum frame, m.</summary>
    public Vec3 CentreOfGravity
    {
        get
        {
            double m = 0; Vec3 s = Vec3.Zero;
            foreach (var it in _items) { m += it.Mass; s += it.Position * it.Mass; }
            return m > 1e-9 ? s / m : Vec3.Zero;
        }
    }

    /// <summary>Full inertia tensor about the centre of gravity, body axes, kg.m^2.</summary>
    public Mat3 InertiaAboutCg()
    {
        Vec3 cg = CentreOfGravity;
        double ixx = 0, iyy = 0, izz = 0, ixy = 0, ixz = 0, iyz = 0;

        foreach (var it in _items)
        {
            Vec3 r = it.Position - cg;
            double m = it.Mass;
            ixx += m * (r.Y * r.Y + r.Z * r.Z) + it.OwnInertia.X;
            iyy += m * (r.X * r.X + r.Z * r.Z) + it.OwnInertia.Y;
            izz += m * (r.X * r.X + r.Y * r.Y) + it.OwnInertia.Z;
            ixy -= m * r.X * r.Y;
            ixz -= m * r.X * r.Z;
            iyz -= m * r.Y * r.Z;
        }

        // A degenerate build (one block, or everything on a line) would give a singular
        // tensor and blow up the angular integrator. Floor the diagonal.
        double floor = Math.Max(TotalMass * 0.01, 1e-3);
        ixx = Math.Max(ixx, floor); iyy = Math.Max(iyy, floor); izz = Math.Max(izz, floor);

        return new Mat3(ixx, ixy, ixz,
                        ixy, iyy, iyz,
                        ixz, iyz, izz);
    }
}
