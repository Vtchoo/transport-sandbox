using System;
using System.Collections.Generic;
using System.Threading;

namespace TransportSandbox.Terrain;

/// <summary>Original vegetation.ts rules. Stateless global cells, independent of mesh LOD and job order.</summary>
public static class Vegetation
{
    public const int Stride = 7;
    private static double Random(TerrainSampler terrain, int x, int z, uint salt)
    {
        uint h = terrain.Seed ^ unchecked((uint)x * 374761393) ^ unchecked((uint)z * 668265263) ^ salt;
        h = unchecked((h ^ (h >> 13)) * 1274126177);
        return (h ^ (h >> 16)) / 4294967296.0;
    }
    // Recreate pre-Float32 candidate coordinates for clearance at exact width boundaries.
    public static (double X, double Z) WorldPosition(TerrainSampler terrain, int chunkX, int chunkZ, float localX, float localZ)
    {
        int dx = (int)Math.Floor(localX / 8), dz = (int)Math.Floor(localZ / 8);
        int cx = unchecked(chunkX * 64 + dx), cz = unchecked(chunkZ * 64 + dz);
        return (chunkX * 512.0 + (dx + .18 + Random(terrain, cx, cz, 31) * .64) * 8,
            chunkZ * 512.0 + (dz + .18 + Random(terrain, cx, cz, 53) * .64) * 8);
    }
    public static float[] Generate(TerrainSampler terrain, int chunkX, int chunkZ, CancellationToken cancellation = default)
    {
        const int spacing = 8, cells = 512 / spacing;
        var trees = new List<float>();
        double Random(int x, int z, uint salt)
        {
            return Vegetation.Random(terrain, x, z, salt);
        }
        double sea = terrain.SeaLevel;
        for (int dz = 0; dz < cells; dz++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int dx = 0; dx < cells; dx++)
            {
                int cx = unchecked(chunkX * cells + dx), cz = unchecked(chunkZ * cells + dz);
                double localX = (dx + .18 + Random(cx, cz, 31) * .64) * spacing;
                double localZ = (dz + .18 + Random(cx, cz, 53) * .64) * spacing;
                double x = chunkX * 512.0 + localX, z = chunkZ * 512.0 + localZ;
                double forest = terrain.Noise(x / (650 * terrain.HorizontalScale), z / (650 * terrain.HorizontalScale), 307);
                double patch = terrain.Noise(x / 105, z / 105, 311);
                double density = (.025 + .73 * TerrainSampler.Smooth(-.28, .42, forest + patch * .3)) / 4;
                double chance = Random(cx, cz, 79);
                if (chance > density)
                    continue;
                double y = terrain.Height(x, z);
                double shore = TerrainSampler.Smooth(sea + 4, sea + 16, y);
                double treeline = 1 - TerrainSampler.Smooth(sea + 1000, sea + 1450, y + terrain.Noise(x / 400, z / 400, 317) * 90);
                if (chance > density * shore * treeline)
                    continue;
                double sx = terrain.Height(x + 4, z) - terrain.Height(x - 4, z);
                double sz = terrain.Height(x, z + 4) - terrain.Height(x, z - 4);
                double slope = Math.Sqrt(sx * sx + sz * sz) / 8;
                if (chance > density * shore * treeline * (1 - TerrainSampler.Smooth(.35, .8, slope)))
                    continue;
                int species = Random(cx, cz, 101) < .2 + .65 * TerrainSampler.Smooth(sea + 250, sea + 1000, y) ? 1 : 0;
                double height = (species == 1 ? 13 : 9) + Random(cx, cz, 127) * (species == 1 ? 13 : 12);
                double radius = height * (species == 1 ? .19 : .26) * (.85 + Random(cx, cz, 149) * .3);
                trees.AddRange(new[] { (float)localX, (float)y, (float)localZ, (float)height, (float)radius, (float)(Random(cx, cz, 173) * Math.PI * 2), (float)species });
            }
        }
        return trees.ToArray();
    }
}
