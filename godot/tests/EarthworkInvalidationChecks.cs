using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using TransportSandbox.Terrain;

namespace TransportSandbox.Tests;

public static class EarthworkInvalidationChecks
{
    public static void Run(Action<bool, string> check)
    {
        double Ground(double x, double z) => 20;
        EarthPoint[] Prepare(params EarthPoint[] path) => EarthworkField.Prepare(path, Ground, 1000);
        var old = Prepare(new(500, 100, -150), new(520, 100, 200), new(550, 100, 800));
        var distant = Prepare(new(10000, 24, 10000), new(10300, 24, 10000));
        var before = new EarthworkField(new[] { old, distant });
        var candidates = new List<Vector2I>();
        for (int x = -3; x <= 5; x++) for (int z = -3; z <= 5; z++) candidates.Add(new(x, z));
        foreach (bool deletion in new[] { false, true })
        {
            var moved = deletion ? Array.Empty<EarthPoint>() : Prepare(new(1900, -220, 200), new(2200, -220, 750));
            var after = new EarthworkField(new[] { moved, distant });
            var changes = new EarthworkChanges(new[] { old, moved }); changes.Resolve(candidates, before, after, Ground);
            int missed = 0, changed = 0;
            foreach (var key in candidates)
            {
                bool different = before.AffectsBounds(key.X * 512.0, key.Y * 512.0, 512, Ground)
                    != after.AffectsBounds(key.X * 512.0, key.Y * 512.0, 512, Ground);
                for (int x = -1; x <= 513; x += 32)
                    for (int z = -1; z <= 513; z += 32)
                    {
                        double wx = key.X * 512.0 + x, wz = key.Y * 512.0 + z;
                        different |= before.Height(wx, wz, 20) != after.Height(wx, wz, 20)
                            || before.ClearsTree(wx, wz, 20) != after.ClearsTree(wx, wz, 20);
                    }
                if (different) { changed++; if (!changes.Affects(key)) missed++; }
            }
            check(changed > 0 && missed == 0, "Local invalidation covers old/new terrain, tree clearance and adaptive topology for wide cuts, fills and deletions");
            check(!changes.Affects(new(19, 19)), "Unrelated track chunks are excluded even though they contain earthworks");
            // Keys that arrived while the worker ran use conservative coverage, not a stale candidate list.
            var arriving = new EarthworkChanges(new[] { old, moved });
            check(arriving.Affects(new(0, 0)), "Newly loaded chunks still invalidate old path coverage after a camera move");
        }
        var unchanged = new EarthworkChanges(Array.Empty<EarthPoint[]>());
        unchanged.Resolve(candidates, before, before, Ground);
        check(candidates.All(key => !unchanged.Affects(key)), "Identical fields produce no invalidated chunks");
        var narrow = Prepare(new(0, 20, -5000), new(0, 20, 5000));
        var wide = Prepare(new(100, 1000, -5000), new(100, 1000, 5000));
        var masked = new EarthworkField(new[] { narrow, wide }); var exposed = new EarthworkField(new[] { wide });
        var shadowChunk = new Vector2I(-3, 0);
        var shadowChange = new EarthworkChanges(new[] { narrow }, Math.Max(masked.InfluenceWidth, exposed.InfluenceWidth));
        shadowChange.Resolve(new[] { shadowChunk }, masked, exposed, Ground);
        check(masked.Height(-1200, 0, 20) != exposed.Height(-1200, 0, 20) && shadowChange.Affects(shadowChunk)
            && masked.ChunkSignature(-3, 0) != exposed.ChunkSignature(-3, 0),
            "Deleting a narrow path invalidates the distant wide-embankment area it previously masked, including disk cache identity");
    }
}
