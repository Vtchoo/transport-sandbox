namespace TransportSandbox;

/// <summary>World settings and immutable seeded terrain snapshot.</summary>
public sealed class TerrainGenerator
{
    public WorldSettings Settings
    {
        get;
    }
    public TerrainBackend Backend
    {
        get;
    }
    public string SeedText => Settings.Seed;
    public double SeaLevel => Settings.SeaLevel;
    public uint Seed => Sampler.Seed;
    public Terrain.TerrainSampler Sampler
    {
        get;
    }
    public TerrainGenerator(WorldSettings? settings = null)
    {
        Settings = settings ?? new();
        Sampler = new(Settings.Seed, Settings.MountainHeight, Settings.HorizontalScale, Settings.SeaLevel);
        Backend = new();
        Backend.Configure(Settings.Seed, Settings.MountainHeight, Settings.HorizontalScale, Settings.SeaLevel);
    }
    public double Height(double x, double z) => Sampler.Height(x, z);
}

public sealed record WorldSettings(string Seed = "the-long-way-home", double MountainHeight = 1800,
    double HorizontalScale = 1, double SeaLevel = 0, string Physics = "ho");
