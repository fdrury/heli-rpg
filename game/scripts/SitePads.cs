using System;
using System.Collections.Generic;
using Godot;

namespace Rotorwash;

/// <summary>
/// Levels the ground where people built something.
///
/// This exists because the core loop test landed a helicopter on a 40 degree slope at a
/// fuel cache. Site placement checked the gradient over six metres and the landing
/// assessment checks it over two and a half, and once the terrain gained fine gullies
/// those two numbers stopped agreeing. Tightening the placement rule would only have made
/// the sites rarer without making them landable.
///
/// The right answer is the obvious one: people who put a fuel tank somewhere levelled the
/// ground first. So every site grades a pad - flat in the middle, blending out to the
/// natural terrain at the edge. Three things fall out of that for free:
///
///   * every named place is landable, which it has to be, because landing is the act the
///     whole game charges for;
///   * a pad reads from the air as a flat patch that does not belong, which is exactly the
///     150 m legibility cue the altitude-banded authoring rule asks for;
///   * the cut-and-fill edge gives each site a visible footprint instead of buildings
///     standing on a hillside.
///
/// Lookups happen millions of times per second from the mesh builder, the collider and the
/// flight model, so the pads are bucketed into a coarse grid and most queries touch nothing.
/// </summary>
public static class SitePads
{
    private readonly record struct Pad(float X, float Z, float Flat, float Blend, float Height);

    private const float CellSize = 512f;

    private static Dictionary<long, List<Pad>>? _grid;
    private static float _maxReach;

    /// <summary>Longest distance any pad can influence. Used to skip work early.</summary>
    public static float MaxReach { get { Ensure(); return _maxReach; } }

    private static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

    private static void Ensure()
    {
        if (_grid is not null) return;
        var grid = new Dictionary<long, List<Pad>>();
        float reach = 0;

        foreach (Site site in WorldMap.Sites)
        {
            // How much ground a place actually clears. A fuel cache levels a patch; an
            // airfield levels a field.
            (float flat, float blend) = site.Kind switch
            {
                SiteKind.Airfield => (150f, 260f),
                SiteKind.Settlement => (85f, 170f),
                SiteKind.Depot => (95f, 165f),
                SiteKind.Workshop => (45f, 90f),
                SiteKind.Farmstead => (38f, 80f),
                SiteKind.FuelCache => (26f, 62f),
                SiteKind.Relay => (22f, 50f),
                SiteKind.Wreck => (14f, 40f),
                _ => (12f, 34f),
            };

            // Base height comes from the RAW terrain: the pads are what define At(), so
            // asking At() here would be circular.
            float h = WorldHeight.RawAt(site.Position.X, site.Position.Y);

            // Sit the pad slightly into the hillside rather than on top of it. Averaging a
            // ring around the centre stops a site perched on a local bump.
            float sum = 0; int n = 0;
            for (int i = 0; i < 8; i++)
            {
                float a = Mathf.Tau * i / 8f;
                sum += WorldHeight.RawAt(site.Position.X + Mathf.Cos(a) * flat * 0.7f,
                                         site.Position.Y + Mathf.Sin(a) * flat * 0.7f);
                n++;
            }
            h = h * 0.35f + sum / n * 0.65f;

            var pad = new Pad(site.Position.X, site.Position.Y, flat, blend, h);
            reach = Mathf.Max(reach, blend);

            int minX = Mathf.FloorToInt((site.Position.X - blend) / CellSize);
            int maxX = Mathf.FloorToInt((site.Position.X + blend) / CellSize);
            int minZ = Mathf.FloorToInt((site.Position.Y - blend) / CellSize);
            int maxZ = Mathf.FloorToInt((site.Position.Y + blend) / CellSize);
            for (int cz = minZ; cz <= maxZ; cz++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    long k = Key(cx, cz);
                    if (!grid.TryGetValue(k, out List<Pad>? list)) grid[k] = list = new List<Pad>();
                    list.Add(pad);
                }
            }
        }

        _maxReach = reach;
        _grid = grid;
    }

    /// <summary>
    /// Apply every pad that reaches this point. Returns the raw height unchanged when
    /// there is nothing nearby, which is almost always.
    /// </summary>
    public static float Apply(float x, float z, float rawHeight)
    {
        Ensure();
        if (!_grid!.TryGetValue(Key(Mathf.FloorToInt(x / CellSize), Mathf.FloorToInt(z / CellSize)),
                                out List<Pad>? pads))
            return rawHeight;

        float h = rawHeight;
        foreach (Pad p in pads)
        {
            float dx = x - p.X, dz = z - p.Z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d >= p.Blend) continue;

            // Fully level inside the flat radius, smoothly returning to the natural ground
            // by the blend radius. Smoothstep rather than linear so there is no crease.
            float t = d <= p.Flat ? 1f
                    : 1f - Mathf.SmoothStep(p.Flat, p.Blend, d);
            h = Mathf.Lerp(h, p.Height, t);
        }
        return h;
    }

    /// <summary>Drop the cache, e.g. after regenerating the world.</summary>
    public static void Invalidate() { _grid = null; }

    public static int PadCount { get { Ensure(); int n = 0; foreach (var l in _grid!.Values) n += l.Count; return n; } }
}
