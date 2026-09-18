using System;
using System.Collections.Generic;
using Godot;

namespace Rotorwash;

/// <summary>
/// Brings named places into the world as the player approaches, and takes them away again.
///
/// Two ranges, for the same reason the terrain has LODs: a mast or a chimney has to be
/// visible from kilometres away because that is how the player navigates, but the crates
/// and fence posts around its feet only matter when you are close enough to land.
/// Collision is generated later still - the aircraft can only hit what it can reach.
/// </summary>
public sealed partial class SiteStreamer : Node3D
{
    /// <summary>Sites are built when the player is this close.</summary>
    [Export] public float BuildRange { get; set; } = 3200f;

    /// <summary>...and freed when they get this far away. The gap prevents thrashing.</summary>
    [Export] public float ReleaseRange { get; set; } = 3800f;

    /// <summary>Collision shapes are added within this range.</summary>
    [Export] public float CollisionRange { get; set; } = 420f;

    /// <summary>At most this many sites are built per frame.</summary>
    [Export] public int MaxBuildsPerFrame { get; set; } = 1;

    public Node3D? Target { get; set; }

    private readonly Dictionary<int, Node3D> _active = new();
    private readonly HashSet<int> _withCollision = new();
    private readonly Queue<Site> _queue = new();
    private Vector2 _lastRefreshAt = new(float.MaxValue, float.MaxValue);

    /// <summary>The site the aircraft is currently over or beside, if any.</summary>
    public Site? CurrentSite { get; private set; }

    public event Action<Site>? Entered;
    public event Action<Site>? Left;

    public override void _Ready()
    {
        GD.Print($"[world] {WorldMap.Describe()}");
    }

    public override void _Process(double delta)
    {
        if (Target is null) return;
        Vector3 p3 = Target.GlobalPosition;
        var p = new Vector2(p3.X, p3.Z);

        // Only re-plan when the player has actually gone somewhere.
        if (p.DistanceSquaredTo(_lastRefreshAt) > 150f * 150f)
        {
            _lastRefreshAt = p;
            Refresh(p);
        }

        // Build a bounded number per frame. A settlement is a few hundred nodes and
        // building three at once while flying at 60 m is a visible hitch.
        for (int i = 0; i < MaxBuildsPerFrame && _queue.Count > 0; i++)
        {
            Site s = _queue.Dequeue();
            if (_active.ContainsKey(s.Id)) continue;
            if (s.Position.DistanceTo(p) > ReleaseRange) continue;

            Node3D node = SiteKit.Build(s);
            AddChild(node);
            _active[s.Id] = node;
        }

        UpdateCollision(p);
        UpdateCurrentSite(p);
    }

    private void Refresh(Vector2 p)
    {
        // Retire what has gone out of range.
        var stale = new List<int>();
        foreach (var kv in _active)
        {
            Site? site = FindSite(kv.Key);
            if (site is null || site.Position.DistanceTo(p) > ReleaseRange) stale.Add(kv.Key);
        }
        foreach (int id in stale)
        {
            _active[id].QueueFree();
            _active.Remove(id);
            _withCollision.Remove(id);
        }

        // Queue what has come into range, nearest first.
        var wanted = new List<(Site site, float d)>();
        foreach (Site s in WorldMap.Sites)
        {
            float d = s.Position.DistanceTo(p);
            if (d <= BuildRange && !_active.ContainsKey(s.Id)) wanted.Add((s, d));
        }
        wanted.Sort((a, b) => a.d.CompareTo(b.d));

        _queue.Clear();
        foreach (var (site, _) in wanted) _queue.Enqueue(site);
    }

    /// <summary>
    /// Give nearby sites collision. Deferred because a settlement is a hundred boxes and
    /// generating collision for every site in a 3 km radius would cost far more than the
    /// aircraft could ever touch.
    /// </summary>
    private void UpdateCollision(Vector2 p)
    {
        foreach (var kv in _active)
        {
            if (_withCollision.Contains(kv.Key)) continue;
            Site? site = FindSite(kv.Key);
            if (site is null) continue;
            if (site.Position.DistanceTo(p) > CollisionRange) continue;

            AddCollision(kv.Value);
            _withCollision.Add(kv.Key);
        }
    }

    /// <summary>
    /// Wrap every substantial box in the site with a matching collider. Small clutter is
    /// skipped: a crate should not stop a helicopter, and it should not cost a shape.
    /// </summary>
    private static void AddCollision(Node3D siteRoot)
    {
        var body = new StaticBody3D { Name = "Collision" };
        int shapes = 0;

        foreach (Node child in siteRoot.GetChildren())
        {
            if (child is not MeshInstance3D mi) continue;
            Vector3 size = mi.Mesh switch
            {
                BoxMesh b => b.Size,
                CylinderMesh c => new Vector3(c.BottomRadius * 2, c.Height, c.BottomRadius * 2),
                PrismMesh pr => pr.Size,
                _ => Vector3.Zero,
            };
            if (size == Vector3.Zero) continue;
            if (size.X * size.Y * size.Z < 2.0f) continue;     // clutter, not obstacle

            var shape = new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = size },
                Position = mi.Position,
                Rotation = mi.Rotation,
            };
            body.AddChild(shape);
            shapes++;
        }

        if (shapes > 0) siteRoot.AddChild(body);
        else body.QueueFree();
    }

    private void UpdateCurrentSite(Vector2 p)
    {
        Site? found = null;
        foreach (Site s in WorldMap.Sites)
        {
            float d = s.Position.DistanceTo(p);
            // A generous margin: you are "at" a place from a little way out, which is when
            // the player wants to be told its name.
            if (d < s.Radius + 60f) { found = s; break; }
        }

        if (found?.Id != CurrentSite?.Id)
        {
            if (CurrentSite is not null) Left?.Invoke(CurrentSite);
            CurrentSite = found;
            if (found is not null) Entered?.Invoke(found);
        }
    }

    private static Site? FindSite(int id)
    {
        foreach (Site s in WorldMap.Sites) if (s.Id == id) return s;
        return null;
    }

    public int ActiveSites => _active.Count;
    public int QueuedSites => _queue.Count;
}
