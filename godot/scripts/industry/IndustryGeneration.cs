using System;
using System.Collections.Generic;

namespace TransportSandbox;

/// <summary>One candidate of each industry type per 4 km region. Worker-safe and independent of discovery order.</summary>
public static class IndustryGeneration
{
    public const double RegionSize = 4096;
    public static IndustryState[] Generate(WorldSettings settings, int rx, int rz)
    {
        var terrain = new Terrain.TerrainSampler(settings.Seed, settings.MountainHeight, settings.HorizontalScale, settings.SeaLevel);
        var result = new List<IndustryState>();
        string[] types = { "coal-mine", "iron-mine", "steel-mill" };
        for (int type = 0; type < types.Length; type++)
        {
            IndustryState? best = null; double bestScore = double.NegativeInfinity;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                double x = (rx + .12 + .76 * (terrain.Hash(rx, rz, 1000 + type * 100 + attempt * 2) / (double)uint.MaxValue)) * RegionSize;
                double z = (rz + .12 + .76 * (terrain.Hash(rx, rz, 1001 + type * 100 + attempt * 2) / (double)uint.MaxValue)) * RegionSize;
                double h = terrain.Height(x, z), low = h, high = h;
                foreach (var (dx, dz) in new[] { (-35, -35), (-35, 35), (35, -35), (35, 35) })
                { double y = terrain.Height(x + dx, z + dz); low = Math.Min(low, y); high = Math.Max(high, y); }
                double relief = high - low;
                if (low < settings.SeaLevel + 5 || relief > (type == 2 ? 7 : 18)) continue;
                if (result.Exists(i => Math.Pow(i.Position.X - x, 2) + Math.Pow(i.Position.Z - z, 2) < 250 * 250)) continue;
                double score = type == 2 ? -relief : Math.Min(h - settings.SeaLevel, 600) * .03 - relief;
                if (score > bestScore)
                { bestScore = score; best = new() { Id = $"{types[type]}:{rx}:{rz}", Type = types[type], Position = new(x, high, z) }; }
            }
            if (best != null) result.Add(best);
        }
        return result.ToArray();
    }
}
