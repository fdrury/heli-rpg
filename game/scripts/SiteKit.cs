using System;
using System.Collections.Generic;
using Godot;

namespace Rotorwash;

/// <summary>
/// Assembles a named place out of parts.
///
/// Built to the altitude-banded rule from D-003b, because that rule is what makes a
/// helicopter world affordable: the same site has to pay off three times.
///
///   500 m - SILHOUETTE. A mast, a water tower, a chimney, a silo. Something vertical
///           that says "there is a place here" from across the valley, which is how the
///           player navigates before they have any charts.
///   150 m - OCCUPANCY. Roof states, vehicles, fences, whether the pad is swept. This is
///           where the player decides whether to commit to an approach.
///    15 m - THE LANDING PROBLEM. Wires, clutter, slope, how much clearance there really
///           is between that shed and that mast. Only resolves when you are low.
///
/// Everything is generated from the site id, so a place is always the same place, and
/// nothing needs saving.
/// </summary>
public static class SiteKit
{
    public sealed class Palette
    {
        public Material Concrete = null!;
        public Material Rust = null!;
        public Material Timber = null!;
        public Material Metal = null!;
        public Material Dark = null!;
        public Material Glass = null!;

        public static Palette Build()
        {
            Material M(Color c, float rough, float metal = 0f) => new StandardMaterial3D
            {
                AlbedoColor = c,
                Roughness = rough,
                Metallic = metal,
            };
            return new Palette
            {
                Concrete = M(new Color(0.46f, 0.45f, 0.42f), 0.92f),
                Rust = M(new Color(0.34f, 0.23f, 0.16f), 0.86f, 0.25f),
                Timber = M(new Color(0.30f, 0.26f, 0.21f), 0.88f),
                Metal = M(new Color(0.40f, 0.41f, 0.41f), 0.52f, 0.70f),
                Dark = M(new Color(0.13f, 0.13f, 0.12f), 0.80f),
                Glass = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.10f, 0.13f, 0.14f, 0.55f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    Roughness = 0.18f,
                    MetallicSpecular = 0.8f,
                },
            };
        }
    }

    private static Palette? _palette;
    public static Palette Materials => _palette ??= Palette.Build();

    /// <summary>Build the whole site. Returns a node positioned at the site's ground point.</summary>
    public static Node3D Build(Site site)
    {
        var root = new Node3D { Name = $"Site_{site.Id}_{site.Kind}" };
        var rng = new RandomNumberGenerator { Seed = (ulong)(site.Id * 7919 + 13) };
        Vector3 origin = site.Ground;
        root.Position = origin;

        switch (site.Kind)
        {
            case SiteKind.FuelCache: BuildFuelCache(root, rng, site); break;
            case SiteKind.Settlement: BuildSettlement(root, rng, site); break;
            case SiteKind.Workshop: BuildWorkshop(root, rng, site); break;
            case SiteKind.Wreck: BuildWreck(root, rng, site); break;
            case SiteKind.Relay: BuildRelay(root, rng, site); break;
            case SiteKind.Depot: BuildDepot(root, rng, site); break;
            case SiteKind.Airfield: BuildAirfield(root, rng, site); break;
            case SiteKind.Farmstead: BuildFarmstead(root, rng, site); break;
            default: BuildOverlook(root, rng, site); break;
        }

        return root;
    }

    // ---------------------------------------------------------------- site types

    private static void BuildFuelCache(Node3D root, RandomNumberGenerator rng, Site site)
    {
        // Small, and deliberately hard to see until you are close: the whole point of a
        // fuel cache is that finding it is worth something.
        Pad(root, rng, 9f, 9f, Vector3.Zero);
        for (int i = 0; i < rng.RandiRange(2, 4); i++)
        {
            float a = rng.Randf() * Mathf.Tau, r = rng.RandfRange(5f, 13f);
            Tank(root, rng, Local(root, new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r)),
                 rng.RandfRange(1.4f, 2.1f), rng.RandfRange(2.6f, 4.2f));
        }
        // The silhouette element is small but distinctive: a windpump, visible as a
        // spindle against the sky from a long way off if you know to look.
        Windpump(root, rng, Local(root, new Vector3(rng.RandfRange(-10, 10), 0, rng.RandfRange(-10, 10))));
        Clutter(root, rng, 14f, 6);
    }

    private static void BuildSettlement(Node3D root, RandomNumberGenerator rng, Site site)
    {
        int houses = rng.RandiRange(7, 14);
        // Lay a street and hang the buildings off it. A ring of huts reads as generated;
        // a line of them reads as a place people chose.
        float streetAngle = rng.Randf() * Mathf.Tau;
        Vector3 along = new(Mathf.Cos(streetAngle), 0, Mathf.Sin(streetAngle));
        Vector3 across = new(-along.Z, 0, along.X);

        Road(root, rng, along, 120f, 6.5f);

        for (int i = 0; i < houses; i++)
        {
            float t = rng.RandfRange(-52f, 52f);
            float side = rng.Randf() < 0.5f ? -1 : 1;
            float offset = side * rng.RandfRange(9f, 19f);
            Vector3 p = along * t + across * offset;
            Building(root, rng, Local(root, p), rng.RandfRange(6f, 11f), rng.RandfRange(5f, 9f),
                     rng.RandfRange(3.2f, 6.0f), streetAngle + (side > 0 ? Mathf.Pi : 0),
                     collapsed: rng.Randf() < 0.30f);
        }

        WaterTower(root, rng, Local(root, along * rng.RandfRange(-40, 40) + across * rng.RandfRange(22, 34)));
        Fence(root, rng, 78f, 26);
        Clutter(root, rng, 70f, 22);
        Vehicle(root, rng, Local(root, along * rng.RandfRange(-30, 30) + across * rng.RandfRange(-8, 8)));
    }

    private static void BuildWorkshop(Node3D root, RandomNumberGenerator rng, Site site)
    {
        Building(root, rng, Vector3.Zero, 18f, 12f, 6.5f, rng.Randf() * Mathf.Tau, collapsed: false, shed: true);
        Pad(root, rng, 16f, 16f, new Vector3(18f, 0, 0));
        for (int i = 0; i < 3; i++)
            Vehicle(root, rng, Local(root, new Vector3(rng.RandfRange(-22, 22), 0, rng.RandfRange(-22, 22))));
        Tank(root, rng, Local(root, new Vector3(-14, 0, 8)), 1.8f, 3.6f);
        Clutter(root, rng, 26f, 14);
    }

    private static void BuildWreck(Node3D root, RandomNumberGenerator rng, Site site)
    {
        // Airframes, vehicles, a lorry on its side. The only other aircraft in the world
        // are the ones that did not make it, and they should be a genuine event to find.
        int pieces = rng.RandiRange(3, 7);
        for (int i = 0; i < pieces; i++)
        {
            Vector3 p = Local(root, new Vector3(rng.RandfRange(-14, 14), 0, rng.RandfRange(-14, 14)));
            var box = Box(rng.RandfRange(1.2f, 5.5f), rng.RandfRange(0.8f, 2.4f), rng.RandfRange(1.4f, 7f));
            var mi = new MeshInstance3D { Mesh = box, MaterialOverride = Materials.Rust, Position = p + Vector3.Up * 0.5f };
            mi.Rotation = new Vector3(rng.RandfRange(-0.5f, 0.5f), rng.Randf() * Mathf.Tau, rng.RandfRange(-0.5f, 0.5f));
            root.AddChild(mi);
        }
        Clutter(root, rng, 20f, 12);
    }

    private static void BuildRelay(Node3D root, RandomNumberGenerator rng, Site site)
    {
        Mast(root, rng, Vector3.Zero, rng.RandfRange(38f, 62f));
        Building(root, rng, Local(root, new Vector3(9, 0, 4)), 6f, 5f, 3f, rng.Randf() * Mathf.Tau, false);
        Fence(root, rng, 22f, 14);
        Clutter(root, rng, 20f, 5);
    }

    private static void BuildDepot(Node3D root, RandomNumberGenerator rng, Site site)
    {
        for (int i = 0; i < rng.RandiRange(3, 6); i++)
        {
            float a = Mathf.Tau * i / 6f + rng.RandfRange(-0.3f, 0.3f);
            float r = rng.RandfRange(18f, 46f);
            Building(root, rng, Local(root, new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r)),
                     rng.RandfRange(14f, 26f), rng.RandfRange(10f, 18f), rng.RandfRange(6f, 11f),
                     a, collapsed: rng.Randf() < 0.22f, shed: true);
        }
        Chimney(root, rng, Local(root, new Vector3(rng.RandfRange(-20, 20), 0, rng.RandfRange(-20, 20))),
                rng.RandfRange(26f, 44f));
        for (int i = 0; i < 4; i++)
            Tank(root, rng, Local(root, new Vector3(rng.RandfRange(-40, 40), 0, rng.RandfRange(-40, 40))),
                 rng.RandfRange(3.0f, 5.5f), rng.RandfRange(6f, 11f));
        Fence(root, rng, 88f, 34);
        Clutter(root, rng, 80f, 26);
    }

    private static void BuildAirfield(Node3D root, RandomNumberGenerator rng, Site site)
    {
        float angle = rng.Randf() * Mathf.Tau;
        Runway(root, rng, angle, 620f, 26f);
        Vector3 along = new(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        Vector3 across = new(-along.Z, 0, along.X);

        // Hangars down one side.
        for (int i = 0; i < rng.RandiRange(2, 4); i++)
        {
            Vector3 p = along * (i * 46f - 60f) + across * 44f;
            Building(root, rng, Local(root, p), 34f, 22f, 12f, angle, collapsed: rng.Randf() < 0.3f, shed: true);
        }
        Tower(root, rng, Local(root, across * 52f + along * 70f), 18f);
        for (int i = 0; i < 3; i++)
            Tank(root, rng, Local(root, across * rng.RandfRange(56, 70) + along * rng.RandfRange(-40, 40)), 4.5f, 9f);
        Clutter(root, rng, 130f, 30);
    }

    private static void BuildFarmstead(Node3D root, RandomNumberGenerator rng, Site site)
    {
        float a = rng.Randf() * Mathf.Tau;
        Building(root, rng, Vector3.Zero, 11f, 8f, 5f, a, collapsed: rng.Randf() < 0.4f);
        Building(root, rng, Local(root, new Vector3(14, 0, 6)), 16f, 9f, 6.5f, a, collapsed: rng.Randf() < 0.3f, shed: true);
        Silo(root, rng, Local(root, new Vector3(-9, 0, 11)), rng.RandfRange(9f, 15f));
        Fence(root, rng, 34f, 18);
        Clutter(root, rng, 30f, 10);
    }

    private static void BuildOverlook(Node3D root, RandomNumberGenerator rng, Site site)
    {
        // Nothing to take. A cairn, a bench, a view, and a survey point that fills in the
        // chart - which under a knowledge-based progression is a real reward.
        var cairn = new MeshInstance3D
        {
            Mesh = ProceduralProps.Rock((int)rng.Randi(), 1.1f, 2),
            MaterialOverride = Materials.Concrete,
            Position = Vector3.Up * 0.6f,
        };
        root.AddChild(cairn);
        for (int i = 0; i < 4; i++)
        {
            var r = new MeshInstance3D
            {
                Mesh = ProceduralProps.Rock((int)rng.Randi(), rng.RandfRange(0.4f, 0.8f), 1),
                MaterialOverride = Materials.Concrete,
                Position = Local(root, new Vector3(rng.RandfRange(-4, 4), 0, rng.RandfRange(-4, 4))) + Vector3.Up * 0.3f,
            };
            root.AddChild(r);
        }
        Mast(root, rng, Local(root, new Vector3(3, 0, -3)), 7f);
    }

    // -------------------------------------------------------------------- parts

    /// <summary>Snap a local offset onto the terrain, relative to the site origin.</summary>
    private static Vector3 Local(Node3D root, Vector3 offset)
    {
        Vector3 world = root.Position + offset;
        float h = WorldHeight.At(world.X, world.Z);
        return new Vector3(offset.X, h - root.Position.Y, offset.Z);
    }

    private static BoxMesh Box(float x, float y, float z) => new() { Size = new Vector3(x, y, z) };

    private static void Add(Node3D root, Mesh mesh, Material mat, Vector3 pos, float yaw = 0, Vector3 rot = default)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos };
        mi.Rotation = rot == default ? new Vector3(0, yaw, 0) : rot;
        root.AddChild(mi);
    }

    private static void Building(Node3D root, RandomNumberGenerator rng, Vector3 at,
                                 float w, float d, float h, float yaw, bool collapsed, bool shed = false)
    {
        Material wall = shed
            ? (rng.Randf() < 0.6f ? Materials.Rust : Materials.Metal)
            : (rng.Randf() < 0.5f ? Materials.Concrete : Materials.Timber);

        if (collapsed)
        {
            // A collapsed building is a different SILHOUETTE, which is the point: from
            // 150 m the player can tell at a glance which places still have roofs.
            h *= rng.RandfRange(0.25f, 0.55f);
            Add(root, Box(w, h, d), wall, at + Vector3.Up * (h * 0.5f), yaw);
            for (int i = 0; i < 5; i++)
            {
                var slab = Box(rng.RandfRange(1.5f, 4f), 0.25f, rng.RandfRange(1.5f, 4f));
                Add(root, slab, Materials.Concrete,
                    at + new Vector3(rng.RandfRange(-w, w) * 0.7f, 0.2f, rng.RandfRange(-d, d) * 0.7f),
                    0, new Vector3(rng.RandfRange(-0.6f, 0.6f), rng.Randf() * Mathf.Tau, rng.RandfRange(-0.6f, 0.6f)));
            }
            return;
        }

        Add(root, Box(w, h, d), wall, at + Vector3.Up * (h * 0.5f), yaw);

        // Roof: flat for sheds, pitched for houses. The pitch is most of what tells you
        // from the air whether you are looking at somewhere people lived or worked.
        if (shed)
        {
            Add(root, Box(w * 1.06f, 0.3f, d * 1.06f), Materials.Rust, at + Vector3.Up * (h + 0.15f), yaw);
        }
        else
        {
            float roofH = rng.RandfRange(1.4f, 2.6f);
            var roof = new PrismMesh { Size = new Vector3(w * 1.08f, roofH, d * 1.08f) };
            Add(root, roof, Materials.Dark, at + Vector3.Up * (h + roofH * 0.5f), yaw);
        }

        // Window band. Cheap, and it is the difference between a box and a building.
        int windows = Mathf.Max(1, (int)(w / 2.4f));
        for (int i = 0; i < windows; i++)
        {
            if (rng.Randf() < 0.25f) continue;      // boarded or bricked up
            float t = (i + 0.5f) / windows - 0.5f;
            Vector3 local = new(t * w * 0.82f, h * 0.55f, d * 0.5f + 0.06f);
            Vector3 rotated = local.Rotated(Vector3.Up, yaw);
            Add(root, Box(1.0f, 1.1f, 0.05f), Materials.Glass, at + rotated, yaw);
        }
    }

    private static void Tank(Node3D root, RandomNumberGenerator rng, Vector3 at, float radius, float height)
    {
        var body = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = 12 };
        Add(root, body, Materials.Rust, at + Vector3.Up * (height * 0.5f), rng.Randf() * Mathf.Tau);
        var cap = new CylinderMesh { TopRadius = radius * 1.06f, BottomRadius = radius * 1.06f, Height = 0.25f, RadialSegments = 12 };
        Add(root, cap, Materials.Metal, at + Vector3.Up * (height + 0.1f));
    }

    private static void Silo(Node3D root, RandomNumberGenerator rng, Vector3 at, float height)
    {
        float r = height * 0.19f;
        Add(root, new CylinderMesh { TopRadius = r, BottomRadius = r, Height = height, RadialSegments = 14 },
            Materials.Concrete, at + Vector3.Up * (height * 0.5f));
        Add(root, new SphereMesh { Radius = r * 1.04f, Height = r * 1.3f, RadialSegments = 14, Rings = 5 },
            Materials.Metal, at + Vector3.Up * (height + r * 0.3f));
    }

    private static void WaterTower(Node3D root, RandomNumberGenerator rng, Vector3 at)
    {
        float legH = rng.RandfRange(9f, 15f);
        float r = rng.RandfRange(2.4f, 3.6f);
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Tau * i / 4f + Mathf.Pi / 4f;
            Vector3 foot = new(Mathf.Cos(a) * r * 0.9f, 0, Mathf.Sin(a) * r * 0.9f);
            Add(root, Box(0.22f, legH, 0.22f), Materials.Metal, at + foot + Vector3.Up * (legH * 0.5f));
        }
        Add(root, new CylinderMesh { TopRadius = r, BottomRadius = r * 0.85f, Height = 4.2f, RadialSegments = 12 },
            Materials.Rust, at + Vector3.Up * (legH + 2.1f));
        Add(root, new CylinderMesh { TopRadius = 0.1f, BottomRadius = r * 0.8f, Height = 1.4f, RadialSegments = 12 },
            Materials.Rust, at + Vector3.Up * (legH + 4.6f));
    }

    private static void Mast(Node3D root, RandomNumberGenerator rng, Vector3 at, float height)
    {
        // A lattice mast, built as a taper of four legs. Reads as a spindle from 5 km,
        // which is exactly the job.
        int sections = Mathf.Max(3, (int)(height / 8f));
        for (int s = 0; s < sections; s++)
        {
            float t0 = (float)s / sections, t1 = (float)(s + 1) / sections;
            float r0 = Mathf.Lerp(height * 0.055f, height * 0.016f, t0);
            float r1 = Mathf.Lerp(height * 0.055f, height * 0.016f, t1);
            float y0 = height * t0, y1 = height * t1;
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Tau * i / 4f + Mathf.Pi / 4f;
                Vector3 p0 = at + new Vector3(Mathf.Cos(a) * r0, y0, Mathf.Sin(a) * r0);
                Vector3 p1 = at + new Vector3(Mathf.Cos(a) * r1, y1, Mathf.Sin(a) * r1);
                Strut(root, p0, p1, 0.10f, Materials.Metal);
            }
            // One cross brace per section is enough to read as a lattice.
            float ab = Mathf.Tau * (s % 4) / 4f + Mathf.Pi / 4f;
            Strut(root, at + new Vector3(Mathf.Cos(ab) * r0, y0, Mathf.Sin(ab) * r0),
                       at + new Vector3(Mathf.Cos(ab + Mathf.Pi * 0.5f) * r1, y1, Mathf.Sin(ab + Mathf.Pi * 0.5f) * r1),
                       0.07f, Materials.Metal);
        }
        Add(root, Box(0.5f, 0.5f, 0.5f), Materials.Dark, at + Vector3.Up * (height + 0.3f));
    }

    private static void Chimney(Node3D root, RandomNumberGenerator rng, Vector3 at, float height)
    {
        Add(root, new CylinderMesh
        {
            TopRadius = height * 0.045f,
            BottomRadius = height * 0.085f,
            Height = height,
            RadialSegments = 14,
        }, Materials.Rust, at + Vector3.Up * (height * 0.5f));
    }

    private static void Tower(Node3D root, RandomNumberGenerator rng, Vector3 at, float height)
    {
        Add(root, Box(5f, height, 5f), Materials.Concrete, at + Vector3.Up * (height * 0.5f));
        Add(root, Box(6.4f, 2.6f, 6.4f), Materials.Glass, at + Vector3.Up * (height + 1.3f));
        Add(root, Box(7f, 0.3f, 7f), Materials.Dark, at + Vector3.Up * (height + 2.8f));
    }

    private static void Windpump(Node3D root, RandomNumberGenerator rng, Vector3 at)
    {
        float h = rng.RandfRange(6f, 9f);
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Tau * i / 4f + Mathf.Pi / 4f;
            Vector3 foot = new(Mathf.Cos(a) * 0.9f, 0, Mathf.Sin(a) * 0.9f);
            Strut(root, at + foot, at + Vector3.Up * h, 0.08f, Materials.Metal);
        }
        var fan = new Node3D { Position = at + Vector3.Up * h };
        for (int i = 0; i < 8; i++)
        {
            float a = Mathf.Tau * i / 8f;
            var blade = new MeshInstance3D
            {
                Mesh = Box(0.10f, 1.5f, 0.35f),
                MaterialOverride = Materials.Metal,
                Position = new Vector3(0, Mathf.Cos(a) * 0.85f, Mathf.Sin(a) * 0.85f),
                Rotation = new Vector3(a, 0, 0),
            };
            fan.AddChild(blade);
        }
        root.AddChild(fan);
    }

    private static void Strut(Node3D root, Vector3 a, Vector3 b, float thickness, Material mat)
    {
        Vector3 mid = (a + b) * 0.5f;
        float len = a.DistanceTo(b);
        if (len < 0.01f) return;
        var mi = new MeshInstance3D
        {
            Mesh = Box(thickness, len, thickness),
            MaterialOverride = mat,
            Position = mid,
        };
        // Point the box along the strut.
        Vector3 dir = (b - a).Normalized();
        if (Mathf.Abs(dir.Dot(Vector3.Up)) < 0.999f)
        {
            Vector3 axis = Vector3.Up.Cross(dir).Normalized();
            mi.Rotation = new Quaternion(axis, Mathf.Acos(Mathf.Clamp(Vector3.Up.Dot(dir), -1, 1))).GetEuler();
        }
        root.AddChild(mi);
    }

    private static void Pad(Node3D root, RandomNumberGenerator rng, float w, float d, Vector3 at)
    {
        Add(root, Box(w, 0.18f, d), Materials.Concrete, at + Vector3.Up * 0.09f, rng.Randf() * Mathf.Tau);
    }

    private static void Road(Node3D root, RandomNumberGenerator rng, Vector3 along, float length, float width)
    {
        // A linear feature, which is the strongest thing a world can give a pilot at
        // 500 m: roads are how you navigate before you have charts.
        int segments = Mathf.Max(4, (int)(length / 18f));
        for (int i = 0; i < segments; i++)
        {
            float t = (i + 0.5f) / segments - 0.5f;
            Vector3 p = along * (t * length);
            Vector3 local = Local(root, p);
            Add(root, Box(width, 0.12f, length / segments * 1.04f), Materials.Dark,
                local + Vector3.Up * 0.06f, Mathf.Atan2(along.X, along.Z));
        }
    }

    private static void Runway(Node3D root, RandomNumberGenerator rng, float angle, float length, float width)
    {
        Vector3 along = new(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        int segments = Mathf.Max(8, (int)(length / 30f));
        for (int i = 0; i < segments; i++)
        {
            float t = (i + 0.5f) / segments - 0.5f;
            Vector3 local = Local(root, along * (t * length));
            Add(root, Box(width, 0.2f, length / segments * 1.03f), Materials.Concrete,
                local + Vector3.Up * 0.1f, Mathf.Atan2(along.X, along.Z));
        }
    }

    private static void Fence(Node3D root, RandomNumberGenerator rng, float radius, int posts)
    {
        for (int i = 0; i < posts; i++)
        {
            float a = Mathf.Tau * i / posts;
            Vector3 p = Local(root, new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius));
            if (rng.Randf() < 0.25f) continue;      // fences fall down
            Add(root, Box(0.12f, rng.RandfRange(1.4f, 2.2f), 0.12f), Materials.Timber, p + Vector3.Up * 0.9f);
        }
    }

    private static void Vehicle(Node3D root, RandomNumberGenerator rng, Vector3 at)
    {
        float yaw = rng.Randf() * Mathf.Tau;
        Add(root, Box(2.0f, 1.0f, 4.6f), Materials.Rust, at + Vector3.Up * 0.75f, yaw);
        Add(root, Box(1.8f, 0.9f, 2.0f), Materials.Rust, at + Vector3.Up * 1.65f, yaw);
        for (int i = 0; i < 4; i++)
        {
            if (rng.Randf() < 0.3f) continue;       // wheels get taken
            float sx = (i % 2 == 0 ? -1 : 1) * 0.95f;
            float sz = (i < 2 ? -1 : 1) * 1.5f;
            Vector3 local = new Vector3(sx, 0.35f, sz).Rotated(Vector3.Up, yaw);
            Add(root, new CylinderMesh { TopRadius = 0.36f, BottomRadius = 0.36f, Height = 0.26f, RadialSegments = 8 },
                Materials.Dark, at + local, 0, new Vector3(0, yaw, Mathf.Pi * 0.5f));
        }
    }

    private static void Clutter(Node3D root, RandomNumberGenerator rng, float radius, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float a = rng.Randf() * Mathf.Tau, r = Mathf.Sqrt(rng.Randf()) * radius;
            Vector3 p = Local(root, new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
            var mesh = Box(rng.RandfRange(0.4f, 1.6f), rng.RandfRange(0.3f, 1.0f), rng.RandfRange(0.4f, 1.8f));
            Add(root, mesh, rng.Randf() < 0.5f ? Materials.Rust : Materials.Timber,
                p + Vector3.Up * 0.3f,
                rng.Randf() * Mathf.Tau);
        }
    }
}
