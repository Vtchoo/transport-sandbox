using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox.Terrain;

/// <summary>Changed old/new path coverage, with exact cache/mesh decisions resolved on a worker.</summary>
public sealed class EarthworkChanges
{
    private readonly record struct Bounds(double MinX, double MaxX, double MinZ, double MaxZ);
    private readonly Bounds[] _bounds;
    private readonly Dictionary<Vector2I, bool> _resolved = new();
    public EarthworkChanges(IEnumerable<EarthPoint[]> changedPaths, double influenceWidth = 0)
    {
        // Covers shoulders, tree clearance, mesh normal samples, and the coarse
        // chunk-circle test used to choose adaptive versus natural mesh topology.
        double padding = TerrainWorld.Chunk * Math.Sqrt(.5) + 34;
        _bounds = changedPaths.Where(p => p.Length > 0).Select(path =>
        {
            double width = Math.Max(influenceWidth, path.Max(p => Math.Max(p.Left, p.Right)));
            return new Bounds(path.Min(p => p.X) - width - padding, path.Max(p => p.X) + width + padding,
                path.Min(p => p.Z) - width - padding, path.Max(p => p.Z) + width + padding);
        }).ToArray();
    }
    private bool Nearby(Vector2I chunk)
    {
        double x = chunk.X * 512.0, z = chunk.Y * 512.0;
        foreach (var b in _bounds)
            if (b.MinX <= x + 512 && b.MaxX >= x && b.MinZ <= z + 512 && b.MaxZ >= z) return true;
        return false;
    }
    public bool Affects(Vector2I chunk) => _resolved.TryGetValue(chunk, out bool value) ? value : Nearby(chunk);
    public void Resolve(IEnumerable<Vector2I> candidates, EarthworkField? before, EarthworkField after, Func<double, double, double> natural)
    {
        foreach (var chunk in candidates)
        {
            bool affected = Nearby(chunk);
            if (affected)
            {
                bool sameSignature = (before?.ChunkSignature(chunk.X, chunk.Y) ?? "") == after.ChunkSignature(chunk.X, chunk.Y);
                // A distant path can change the initial adaptive-mesh decision even
                // when no shoulder reaches the chunk. Preserve that topology too.
                affected = !sameSignature || (before?.AffectsBounds(chunk.X * 512.0, chunk.Y * 512.0, 512, natural) ?? false)
                    != after.AffectsBounds(chunk.X * 512.0, chunk.Y * 512.0, 512, natural);
            }
            _resolved[chunk] = affected;
        }
    }
}
