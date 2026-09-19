using System;
using System.Collections.Generic;
using Godot;

namespace Rotorwash;

/// <summary>
/// Builds the aircraft as a parametric loft rather than a pile of boxes.
///
/// The fuselage is defined as a series of cross-sections along the length - exactly how a
/// real airframe is drawn - and skinned between them. Each station carries a half-width,
/// a half-height, a vertical offset and a superellipse exponent, so the shape can go from
/// a rounded nose through a squared-off cabin to a slender tail boom without a single
/// hand-placed vertex.
///
/// Two reasons this is worth doing properly now rather than dropping in a downloaded model:
///
///  1. The refit system (D-011) needs hardpoints in known places on a known shape, and
///     needs to add and remove geometry - doors off, tanks on, armour plate, a hoist. A
///     parametric airframe can answer "where does this bolt on" analytically.
///  2. Late game lets the player build airframes that are no longer a Huey. The loft is
///     the thing that makes that possible at all.
///
/// The proportions here are Huey-like without being a UH-1: 12.9 m long, 2.7 m across the
/// cabin, mast 2.35 m above the datum, skids 1.15 m below it - matching the mass and
/// hardpoint positions the flight model already uses.
/// </summary>
public static class AirframeBuilder
{
    /// <summary>One fuselage cross-section.</summary>
    private readonly record struct Station(
        float S,          // distance aft of the nose, m
        float HalfWidth,
        float HalfHeight,
        float CentreY,
        float Exponent);  // 2 = ellipse, 4 = nearly square

    private const float NoseZ = -4.30f;   // Godot Z of the nose tip (-Z is forward)

    private static readonly Station[] Fuselage =
    {
        new(0.00f, 0.16f, 0.20f, -0.62f, 2.4f),   // nose tip, low and rounded
        new(0.45f, 0.72f, 0.60f, -0.52f, 2.5f),
        new(1.05f, 1.12f, 0.95f, -0.30f, 2.7f),
        new(1.85f, 1.33f, 1.18f, -0.10f, 3.0f),   // through the cockpit
        new(2.70f, 1.37f, 1.28f, -0.02f, 3.2f),
        new(4.10f, 1.37f, 1.30f,  0.00f, 3.3f),   // cabin, parallel sided
        new(5.60f, 1.36f, 1.28f,  0.02f, 3.3f),
        new(6.50f, 1.22f, 1.12f,  0.10f, 3.0f),
        new(7.30f, 0.86f, 0.80f,  0.26f, 2.8f),   // boom junction
        new(8.30f, 0.50f, 0.48f,  0.42f, 2.6f),
        new(9.80f, 0.38f, 0.37f,  0.52f, 2.5f),
        new(11.4f, 0.31f, 0.31f,  0.58f, 2.5f),
        new(12.6f, 0.27f, 0.28f,  0.60f, 2.5f),   // tail rotor gearbox
        new(12.9f, 0.20f, 0.22f,  0.60f, 2.5f),
    };

    /// <summary>Radial resolution of each cross-section. 16 is plenty at this scale.</summary>
    private const int Segments = 16;

    public sealed class Materials
    {
        public Material Body = null!;
        public Material Glass = null!;
        public Material Dark = null!;
        public Material Metal = null!;
        public Material Interior = null!;
    }

    public static Materials DefaultMaterials(Color liveryColour)
    {
        return new Materials
        {
            // Worn olive drab. Roughness high and non-uniform is what stops a painted
            // metal aircraft looking like injection-moulded plastic.
            Body = new StandardMaterial3D
            {
                AlbedoColor = liveryColour,
                Roughness = 0.68f,
                Metallic = 0.10f,
                MetallicSpecular = 0.35f,
            },
            Glass = new StandardMaterial3D
            {
                // Alpha was 0.62, which is a welding visor. You are meant to be able to
                // see the ground through this.
                AlbedoColor = new Color(0.38f, 0.43f, 0.45f, 0.11f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.10f,
                Metallic = 0.0f,
                MetallicSpecular = 0.9f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            Dark = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.135f, 0.142f, 0.132f),
                Roughness = 0.55f,
                Metallic = 0.25f,
            },
            Metal = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.38f, 0.38f, 0.37f),
                Roughness = 0.42f,
                Metallic = 0.75f,
            },
            // Cockpit interior. Much lighter than the exterior Dark, and not because
            // real panels are light - they are not. An enclosed cockpit receives no
            // direct sun at all, so everything inside is lit by whatever skylight finds
            // its way through the glass. At the exterior's 0.135 albedo the whole
            // interior rendered as a silhouette of solid black shapes.
            Interior = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.27f, 0.28f, 0.26f),
                Roughness = 0.82f,
                Metallic = 0.0f,
            },
        };
    }

    /// <summary>
    /// Build the complete airframe under a parent node. Rotor and tail rotor hubs are
    /// created as empty Node3Ds named MainRotor and TailRotor, which the controller
    /// animates; everything else is static geometry.
    /// </summary>
    public static void Build(Node3D parent, Rotorwash.Sim.Airframe airframe, Materials mats)
    {
        parent.AddChild(Mesh("Fuselage", LoftFuselage(), mats.Body));
        parent.AddChild(Mesh("Canopy", BuildCanopy(), mats.Glass));
        parent.AddChild(Mesh("Doors", BuildDoors(), mats.Dark));
        parent.AddChild(Mesh("Cowling", BuildCowling(), mats.Dark));
        parent.AddChild(Mesh("Fin", BuildFin(), mats.Body));
        parent.AddChild(Mesh("Stabiliser", BuildStabiliser(), mats.Body));
        parent.AddChild(Mesh("CockpitShell", BuildCockpitShell(), mats.Interior));
        parent.AddChild(Mesh("CockpitFittings", BuildCockpitFittings(), mats.Metal));
        parent.AddChild(CockpitFill());
        parent.AddChild(Mesh("Skids", BuildSkids(), mats.Metal));
        parent.AddChild(Mesh("Mast", BuildMast(), mats.Metal));

        BuildRotorHub(parent, airframe, mats);
        BuildTailRotor(parent, airframe, mats);
    }

    /// <summary>
    /// Bounced skylight inside the cockpit.
    ///
    /// The engine gives the interior nothing: the sun is blocked by the airframe and the
    /// sky contribution that would bounce around a real cabin is not modelled at this
    /// quality tier. One soft omni standing in for it is what almost every flight sim
    /// does, and it costs one light.
    /// </summary>
    private static Node3D CockpitFill()
    {
        // Two, not one. A single source low between the seats leaves the roof and the
        // overhead console unlit, because everything up there faces away from it - and an
        // unlit box in the top of frame reads as a hole in the aircraft.
        var root = new Node3D { Name = "CockpitFill" };
        root.AddChild(Fill("Lower", new Vector3(0, 0.15f, NoseZ + 2.00f), 1.5f, 3.6f));
        root.AddChild(Fill("Upper", new Vector3(0, 0.62f, NoseZ + 1.55f), 1.1f, 2.8f));
        return root;
    }

    private static OmniLight3D Fill(string name, Vector3 at, float energy, float range) => new()
    {
        Name = name,
        Position = at,
        LightColor = new Color(0.80f, 0.84f, 0.92f),
        LightEnergy = energy,
        OmniRange = range,
        OmniAttenuation = 1.1f,
        ShadowEnabled = false,
        LightSpecular = 0.25f,
    };

    // ---------------------------------------------------------------- cockpit

    /// <summary>
    /// The inside of the cockpit: floor, roof, side walls, bulkhead, panel and seats.
    ///
    /// None of this existed, and the absence was invisible from outside and glaring from
    /// within. The fuselage is a single-sided shell, so with the winding fixed every body
    /// panel is backface-culled when viewed from inside it - the pilot was sitting in an
    /// open frame looking straight out through the floor, the roof and both walls, with
    /// only the double-sided glass left hanging in mid-air.
    ///
    /// Everything here is a closed box rather than a single-sided panel, deliberately. A
    /// panel has to be wound to face the right way and there is no way to be sure which
    /// that is except by rendering it; a box is correct from every side by construction.
    /// The cost is a few dozen triangles nobody will ever count.
    /// </summary>
    private static ArrayMesh BuildCockpitShell()
    {
        var st = new SurfaceTool();
        st.Begin(Godot.Mesh.PrimitiveType.Triangles);

        // --- The box the crew sit in -----------------------------------------
        AddBox(st, new Vector3(0, -1.12f, NoseZ + 2.10f), new Vector3(2.32f, 0.08f, 3.10f));   // floor
        AddBox(st, new Vector3(0, 1.02f, NoseZ + 2.70f), new Vector3(2.00f, 0.08f, 1.90f));    // roof
        AddBox(st, new Vector3(-1.18f, -0.55f, NoseZ + 2.70f), new Vector3(0.08f, 1.05f, 1.90f));
        AddBox(st, new Vector3(1.18f, -0.55f, NoseZ + 2.70f), new Vector3(0.08f, 1.05f, 1.90f));
        AddBox(st, new Vector3(0, -0.10f, NoseZ + 3.62f), new Vector3(2.30f, 2.10f, 0.08f));   // bulkhead

        // --- Panel, glareshield, pedestal, overhead --------------------------
        // The glareshield is the piece that makes a cockpit read as a cockpit from the
        // seat: it puts a hard horizontal edge across the bottom of the windscreen.
        AddBox(st, new Vector3(0, -0.52f, NoseZ + 1.08f), new Vector3(1.62f, 0.70f, 0.20f));
        AddBox(st, new Vector3(0, -0.14f, NoseZ + 1.24f), new Vector3(1.70f, 0.10f, 0.48f));
        AddBox(st, new Vector3(0, -0.68f, NoseZ + 1.95f), new Vector3(0.36f, 0.76f, 1.45f));
        AddBox(st, new Vector3(0, 0.82f, NoseZ + 1.80f), new Vector3(0.88f, 0.16f, 1.05f));

        // --- Pillars ----------------------------------------------------------
        // The windscreen centre post and the door frames. Without them the glass has no
        // structure holding it and the panes read as floating.
        AddBox(st, new Vector3(0, 0.10f, NoseZ + 0.84f), new Vector3(0.07f, 1.60f, 0.09f));
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            AddBox(st, new Vector3(side * 1.02f, 0.20f, NoseZ + 1.55f), new Vector3(0.07f, 1.15f, 0.09f));
            AddBox(st, new Vector3(side * 1.26f, 0.42f, NoseZ + 2.92f), new Vector3(0.09f, 1.05f, 0.10f));
        }

        // --- Seats -------------------------------------------------------------
        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -0.62f : 0.62f;
            AddBox(st, new Vector3(x, -0.80f, NoseZ + 2.55f), new Vector3(0.58f, 0.14f, 0.58f));
            AddBox(st, new Vector3(x, -0.40f, NoseZ + 2.88f), new Vector3(0.58f, 0.78f, 0.12f));
        }

        st.GenerateNormals();
        return st.Commit();
    }

    /// <summary>The metal bits: controls and instrument bezels.</summary>
    private static ArrayMesh BuildCockpitFittings()
    {
        var st = new SurfaceTool();
        st.Begin(Godot.Mesh.PrimitiveType.Triangles);

        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -0.62f : 0.62f;

            // Cyclic, between the knees, with a grip on top.
            AddBox(st, new Vector3(x, -0.72f, NoseZ + 2.05f), new Vector3(0.05f, 0.62f, 0.05f));
            AddBox(st, new Vector3(x, -0.38f, NoseZ + 2.05f), new Vector3(0.09f, 0.18f, 0.09f));

            // Collective, outboard, lying along the floor with the grip aft.
            float outboard = x < 0 ? -1.00f : 1.00f;
            AddBox(st, new Vector3(outboard, -0.86f, NoseZ + 2.35f), new Vector3(0.05f, 0.05f, 0.85f));
            AddBox(st, new Vector3(outboard, -0.82f, NoseZ + 2.80f), new Vector3(0.08f, 0.10f, 0.22f));

            // Pedals.
            AddBox(st, new Vector3(x - 0.16f, -1.00f, NoseZ + 1.52f), new Vector3(0.15f, 0.07f, 0.26f));
            AddBox(st, new Vector3(x + 0.16f, -1.00f, NoseZ + 1.52f), new Vector3(0.15f, 0.07f, 0.26f));

            // Instrument bezels, proud of the panel so they catch a highlight.
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 3; c++)
                    AddBox(st, new Vector3(x - 0.20f + c * 0.20f, -0.34f - r * 0.22f, NoseZ + 0.97f),
                                new Vector3(0.15f, 0.15f, 0.04f));
        }

        st.GenerateNormals();
        return st.Commit();
    }

    private static MeshInstance3D Mesh(string name, ArrayMesh mesh, Material mat) =>
        new() { Name = name, Mesh = mesh, MaterialOverride = mat };

    // --------------------------------------------------------------- fuselage

    /// <summary>Point on a superellipse cross-section at parameter t in [0, tau).</summary>
    private static Vector3 SectionPoint(Station st, float t)
    {
        float c = Mathf.Cos(t), s = Mathf.Sin(t);
        float n = 2.0f / st.Exponent;
        float x = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), n) * st.HalfWidth;
        float y = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), n) * st.HalfHeight + st.CentreY;
        return new Vector3(x, y, NoseZ + st.S);
    }

    private static ArrayMesh LoftFuselage()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh3DPrimitive);

        for (int i = 0; i < Fuselage.Length - 1; i++)
        {
            Station a = Fuselage[i], b = Fuselage[i + 1];
            for (int j = 0; j < Segments; j++)
            {
                float t0 = Mathf.Tau * j / Segments, t1 = Mathf.Tau * (j + 1) / Segments;
                Vector3 p00 = SectionPoint(a, t0), p01 = SectionPoint(a, t1);
                Vector3 p10 = SectionPoint(b, t0), p11 = SectionPoint(b, t1);

                float u0 = j / (float)Segments, u1 = (j + 1) / (float)Segments;
                float v0 = a.S / 13.0f, v1 = b.S / 13.0f;

                AddQuad(st, p00, p01, p11, p10,
                        new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
            }
        }

        // Cap the nose and the tail so the hull is closed.
        CapSection(st, Fuselage[0], true);
        CapSection(st, Fuselage[^1], false);

        st.GenerateNormals();
        st.GenerateTangents();
        return st.Commit();
    }

    private static void CapSection(SurfaceTool st, Station station, bool forward)
    {
        Vector3 centre = new(0, station.CentreY, NoseZ + station.S);
        for (int j = 0; j < Segments; j++)
        {
            float t0 = Mathf.Tau * j / Segments, t1 = Mathf.Tau * (j + 1) / Segments;
            Vector3 a = SectionPoint(station, t0), b = SectionPoint(station, t1);
            if (forward) AddTri(st, centre, b, a);
            else AddTri(st, centre, a, b);
        }
    }

    // ----------------------------------------------------------------- canopy

    /// <summary>
    /// The greenhouse: a Huey's most recognisable feature is the wraparound glass and the
    /// chin bubbles the pilots put their feet behind. Built as panels sitting just proud
    /// of the fuselage loft.
    /// </summary>
    private static ArrayMesh BuildCanopy()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh3DPrimitive);

        // Windscreen: from the nose up over the cockpit, both sides.
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 low = new(side * 0.52f, -0.62f, NoseZ + 0.35f);
            Vector3 lowOut = new(side * 1.04f, -0.28f, NoseZ + 0.95f);
            Vector3 topOut = new(side * 0.96f, 0.72f, NoseZ + 1.85f);
            Vector3 top = new(side * 0.30f, 0.86f, NoseZ + 1.30f);
            AddQuadBoth(st, low, lowOut, topOut, top);

            // Chin bubble below the pedals.
            Vector3 c0 = new(side * 0.20f, -1.05f, NoseZ + 0.75f);
            Vector3 c1 = new(side * 0.92f, -0.95f, NoseZ + 1.15f);
            Vector3 c2 = new(side * 0.98f, -0.35f, NoseZ + 1.75f);
            Vector3 c3 = new(side * 0.22f, -0.50f, NoseZ + 1.45f);
            AddQuadBoth(st, c0, c1, c2, c3);

            // Door window.
            Vector3 d0 = new(side * 1.30f, -0.05f, NoseZ + 2.95f);
            Vector3 d1 = new(side * 1.30f, -0.05f, NoseZ + 4.35f);
            Vector3 d2 = new(side * 1.24f, 0.92f, NoseZ + 4.25f);
            Vector3 d3 = new(side * 1.24f, 0.92f, NoseZ + 3.05f);
            AddQuadBoth(st, d0, d1, d2, d3);
        }

        // Roof panel between the windscreens.
        AddQuadBoth(st,
            new Vector3(-0.30f, 0.86f, NoseZ + 1.30f),
            new Vector3(0.30f, 0.86f, NoseZ + 1.30f),
            new Vector3(0.30f, 1.10f, NoseZ + 2.10f),
            new Vector3(-0.30f, 1.10f, NoseZ + 2.10f));

        st.GenerateNormals();
        st.GenerateTangents();
        return st.Commit();
    }

    /// <summary>Sliding cargo doors and their rails - the aircraft's defining feature.</summary>
    private static ArrayMesh BuildDoors()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh3DPrimitive);
        for (int side = -1; side <= 1; side += 2)
        {
            // Door frame outline, standing 3 cm proud of the skin.
            float x = side * 1.40f;
            AddQuadBoth(st,
                new Vector3(x, -1.18f, NoseZ + 4.55f),
                new Vector3(x, -1.18f, NoseZ + 6.35f),
                new Vector3(x, 1.05f, NoseZ + 6.35f),
                new Vector3(x, 1.05f, NoseZ + 4.55f));
        }
        st.GenerateNormals();
        st.GenerateTangents();
        return st.Commit();
    }

    /// <summary>Engine cowling and exhaust on the deck behind the mast.</summary>
    private static ArrayMesh BuildCowling()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh3DPrimitive);

        AddBox(st, new Vector3(0, 1.28f, NoseZ + 6.25f), new Vector3(1.05f, 0.62f, 2.35f));
        // Exhaust stack, canted up and aft the way a turbine's is.
        AddBox(st, new Vector3(0, 1.40f, NoseZ + 7.55f), new Vector3(0.52f, 0.46f, 0.80f));
        // Driveshaft cover running down the spine of the boom.
        AddBox(st, new Vector3(0, 0.86f, NoseZ + 9.60f), new Vector3(0.26f, 0.18f, 3.90f));

        st.GenerateNormals();
        st.GenerateTangents();
        return st.Commit();
    }

    // ------------------------------------------------------------- empennage

    private static ArrayMesh BuildFin()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh3DPrimitive);

        // Swept, tapered fin with a small ventral fin below the boom.
        Vector3 rootLow = new(0, 0.62f, NoseZ + 11.15f);
        Vector3 rootAft = new(0, 0.62f, NoseZ + 12.90f);
        Vector3 tipAft = new(0, 2.05f, NoseZ + 12.75f);
        Vector3 tipFwd = new(0, 2.05f, NoseZ + 12.05f);
        AddThickPanel(st, rootLow, rootAft, tipAft, tipFwd, 0.09f);

        Vector3 vLow = new(0, -0.55f, NoseZ + 12.55f);
        Vector3 vAft = new(0, -0.55f, NoseZ + 12.95f);
        Vector3 vTop2 = new(0, 0.55f, NoseZ + 12.90f);
        Vector3 vTop1 = new(0, 0.55f, NoseZ + 11.95f);
        AddThickPanel(st, vLow, vAft, vTop2, vTop1, 0.07f);

        st.GenerateNormals();
        st.GenerateTangents();
        return st.Commit();
    }

    private static ArrayMesh BuildStabiliser()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh3DPrimitive);
        // Synchronised elevator, well forward on the boom, as on a Huey.
        AddBox(st, new Vector3(0, 0.60f, NoseZ + 9.35f), new Vector3(3.20f, 0.09f, 0.72f));
        st.GenerateNormals();
        st.GenerateTangents();
        return st.Commit();
    }

    // ------------------------------------------------------------------ gear

    private static ArrayMesh BuildSkids()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh3DPrimitive);

        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 1.32f;
            // Skid tube, with the front curled up.
            AddBox(st, new Vector3(x, -1.15f, NoseZ + 4.05f), new Vector3(0.09f, 0.09f, 3.90f));
            AddBox(st, new Vector3(x, -1.02f, NoseZ + 2.00f), new Vector3(0.09f, 0.09f, 0.55f));

            // Cross tubes, angled outward and down from the fuselage.
            for (int i = 0; i < 2; i++)
            {
                float z = NoseZ + (i == 0 ? 2.95f : 5.25f);
                AddBox(st, new Vector3(x * 0.62f, -0.78f, z), new Vector3(1.45f, 0.10f, 0.11f));
            }
        }
        // The cross tubes as a single span under the belly.
        AddBox(st, new Vector3(0, -0.92f, NoseZ + 2.95f), new Vector3(2.70f, 0.11f, 0.12f));
        AddBox(st, new Vector3(0, -0.92f, NoseZ + 5.25f), new Vector3(2.70f, 0.11f, 0.12f));

        st.GenerateNormals();
        st.GenerateTangents();
        return st.Commit();
    }

    private static ArrayMesh BuildMast()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh3DPrimitive);
        AddBox(st, new Vector3(0, 1.85f, NoseZ + 4.35f), new Vector3(0.30f, 1.20f, 0.30f));
        st.GenerateNormals();
        st.GenerateTangents();
        return st.Commit();
    }

    // ----------------------------------------------------------------- rotors

    private static void BuildRotorHub(Node3D parent, Rotorwash.Sim.Airframe af, Materials mats)
    {
        var cfg = af.MainRotor;
        float rotorR = (float)cfg.Radius;

        var hub = new Node3D { Name = "MainRotor", Position = new Vector3(0, 2.35f, -0.05f) };

        // Teetering head: a single spar carrying both blades, plus the stabiliser bar
        // that a Huey has above the head and nothing else does.
        var head = new MeshInstance3D
        {
            Name = "Head",
            Mesh = BoxMesh(new Vector3(0.34f, 0.26f, 1.00f)),
            MaterialOverride = mats.Metal,
        };
        hub.AddChild(head);

        for (int i = 0; i < cfg.NumBlades; i++)
        {
            float a = Mathf.Tau * i / cfg.NumBlades;
            var pivot = new Node3D { Name = $"BladePivot{i}", Rotation = new Vector3(0, a, 0) };

            var blade = new MeshInstance3D
            {
                Name = "Blade",
                Mesh = BladeMesh(rotorR, (float)cfg.Chord),
                MaterialOverride = mats.Dark,
                Position = new Vector3(0, 0, -rotorR * 0.5f - 0.35f),
            };
            pivot.AddChild(blade);
            hub.AddChild(pivot);
        }

        // Stabiliser bar, 90 degrees to the blades.
        var bar = new Node3D { Name = "StabBar", Position = new Vector3(0, 0.22f, 0) };
        bar.AddChild(new MeshInstance3D
        {
            Name = "Bar",
            Mesh = BoxMesh(new Vector3(2.60f, 0.05f, 0.05f)),
            MaterialOverride = mats.Metal,
        });
        hub.AddChild(bar);

        var disc = new MeshInstance3D
        {
            Name = "RotorDisc",
            Mesh = ProceduralProps.RotorDisc(rotorR, 0.10f, 64),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = ProceduralProps.RotorDiscTexture(128, cfg.NumBlades),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.22f, 0.22f, 0.21f),
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        hub.AddChild(disc);

        parent.AddChild(hub);
    }

    private static void BuildTailRotor(Node3D parent, Rotorwash.Sim.Airframe af, Materials mats)
    {
        var cfg = af.TailRotor;
        float tr = (float)cfg.Radius;

        var hub = new Node3D
        {
            Name = "TailRotor",
            Position = new Vector3(0.36f, 1.05f, (float)-cfg.Position.X),
        };

        for (int i = 0; i < cfg.NumBlades; i++)
        {
            float a = Mathf.Tau * i / cfg.NumBlades;
            var pivot = new Node3D { Name = $"TPivot{i}", Rotation = new Vector3(a, 0, 0) };
            pivot.AddChild(new MeshInstance3D
            {
                Name = "TBlade",
                Mesh = BoxMesh(new Vector3(0.05f, tr, (float)cfg.Chord)),
                MaterialOverride = mats.Dark,
                Position = new Vector3(0, tr * 0.5f, 0),
            });
            hub.AddChild(pivot);
        }

        parent.AddChild(hub);
    }

    /// <summary>A rotor blade: tapered in thickness, with a slight tip sweep.</summary>
    private static ArrayMesh BladeMesh(float radius, float chord)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh3DPrimitive);
        float halfC = chord * 0.5f, len = radius - 0.35f;

        // Root end is thicker than the tip; a constant-thickness plank reads as cardboard.
        AddTaperedBox(st,
            new Vector3(0, 0, len * 0.5f), new Vector3(0, 0, -len * 0.5f),
            halfC, 0.075f, halfC * 0.92f, 0.030f);

        st.GenerateNormals();
        st.GenerateTangents();
        return st.Commit();
    }

    // ------------------------------------------------------------- primitives

    private const Godot.Mesh.PrimitiveType Mesh3DPrimitive = Godot.Mesh.PrimitiveType.Triangles;

    private static BoxMesh BoxMesh(Vector3 size) => new() { Size = size };

    private static void AddBox(SurfaceTool st, Vector3 centre, Vector3 size)
    {
        Vector3 h = size * 0.5f;
        Vector3[] c =
        {
            centre + new Vector3(-h.X, -h.Y, -h.Z), centre + new Vector3(h.X, -h.Y, -h.Z),
            centre + new Vector3(h.X, h.Y, -h.Z),   centre + new Vector3(-h.X, h.Y, -h.Z),
            centre + new Vector3(-h.X, -h.Y, h.Z),  centre + new Vector3(h.X, -h.Y, h.Z),
            centre + new Vector3(h.X, h.Y, h.Z),    centre + new Vector3(-h.X, h.Y, h.Z),
        };
        AddQuad(st, c[0], c[3], c[2], c[1]);   // front  (-Z)
        AddQuad(st, c[5], c[6], c[7], c[4]);   // back   (+Z)
        AddQuad(st, c[4], c[7], c[3], c[0]);   // left
        AddQuad(st, c[1], c[2], c[6], c[5]);   // right
        AddQuad(st, c[3], c[7], c[6], c[2]);   // top
        AddQuad(st, c[4], c[0], c[1], c[5]);   // bottom
    }

    private static void AddTaperedBox(SurfaceTool st, Vector3 a, Vector3 b,
                                      float halfWa, float halfHa, float halfWb, float halfHb)
    {
        Vector3[] pa =
        {
            a + new Vector3(-halfWa, -halfHa, 0), a + new Vector3(halfWa, -halfHa, 0),
            a + new Vector3(halfWa, halfHa, 0),   a + new Vector3(-halfWa, halfHa, 0),
        };
        Vector3[] pb =
        {
            b + new Vector3(-halfWb, -halfHb, 0), b + new Vector3(halfWb, -halfHb, 0),
            b + new Vector3(halfWb, halfHb, 0),   b + new Vector3(-halfWb, halfHb, 0),
        };
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            AddQuad(st, pa[i], pa[j], pb[j], pb[i]);
        }
        AddQuad(st, pa[3], pa[2], pa[1], pa[0]);
        AddQuad(st, pb[0], pb[1], pb[2], pb[3]);
    }

    /// <summary>A flat panel given thickness, for fins and stabilisers.</summary>
    private static void AddThickPanel(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float thickness)
    {
        Vector3 offset = new(thickness * 0.5f, 0, 0);
        Vector3[] l = { a - offset, b - offset, c - offset, d - offset };
        Vector3[] r = { a + offset, b + offset, c + offset, d + offset };

        AddQuad(st, l[0], l[1], l[2], l[3]);
        AddQuad(st, r[3], r[2], r[1], r[0]);
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            AddQuad(st, l[i], r[i], r[j], l[j]);
        }
    }

    private static void AddQuad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        => AddQuad(st, a, b, c, d, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));

    /// <summary>
    /// Vertices are passed in outward-normal (counter-clockwise) order, as every geometry
    /// textbook writes them, and emitted REVERSED - because Godot treats clockwise as
    /// front-facing. Measured with --windingtest rather than assumed; getting this wrong
    /// does not hide the model, it silently inverts every normal GenerateNormals derives,
    /// and the whole thing just looks inexplicably dark.
    /// </summary>
    private static void AddQuad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                                Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        st.SetUV(uc); st.AddVertex(c);
        st.SetUV(ub); st.AddVertex(b);
        st.SetUV(ua); st.AddVertex(a);

        st.SetUV(ud); st.AddVertex(d);
        st.SetUV(uc); st.AddVertex(c);
        st.SetUV(ua); st.AddVertex(a);
    }

    private static void AddQuadBoth(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        AddQuad(st, a, b, c, d);
        AddQuad(st, d, c, b, a);
    }

    private static void AddTri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c)
    {
        st.SetUV(new Vector2(1, 1)); st.AddVertex(c);
        st.SetUV(new Vector2(1, 0)); st.AddVertex(b);
        st.SetUV(new Vector2(0, 0)); st.AddVertex(a);
    }
}
