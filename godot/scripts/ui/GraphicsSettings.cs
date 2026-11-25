using Godot;
using System;

namespace TransportSandbox;

/// <summary>Local display preferences, deliberately independent of GameSave.</summary>
public sealed class GraphicsSettings
{
    public string FilePath = "user://graphics-settings.cfg";
    public int TerrainRings = 10;
    public int TreeDetail = 2; // Off, low, standard, high.
    public bool Shadows = true;
    public float ShadowDistance = 1000;
    public float ResolutionScale = 1;
    public bool Fxaa;

    public GraphicsSettings Copy() => (GraphicsSettings)MemberwiseClone();
    public void CopyFrom(GraphicsSettings source)
    {
        TerrainRings = source.TerrainRings;
        TreeDetail = source.TreeDetail;
        Shadows = source.Shadows;
        ShadowDistance = source.ShadowDistance;
        ResolutionScale = source.ResolutionScale;
        Fxaa = source.Fxaa;
    }

    public void Load()
    {
        using var file = new ConfigFile();
        if (file.Load(FilePath) != Error.Ok) return;
        TerrainRings = Math.Clamp(file.GetValue("graphics", "terrain_rings", 10).AsInt32(), 3, 10);
        TreeDetail = Math.Clamp(file.GetValue("graphics", "tree_detail", 2).AsInt32(), 0, 3);
        Shadows = file.GetValue("graphics", "shadows", true).AsBool();
        float Read(string key, float fallback, float min, float max)
        {
            float value = (float)file.GetValue("graphics", key, fallback).AsDouble();
            return float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        }
        ShadowDistance = Read("shadow_distance", 1000, 100, 2000);
        ResolutionScale = Read("resolution_scale", 1, .5f, 1);
        Fxaa = file.GetValue("graphics", "fxaa", false).AsBool();
    }
    public void Save()
    {
        using var file = new ConfigFile();
        file.SetValue("graphics", "terrain_rings", TerrainRings);
        file.SetValue("graphics", "tree_detail", TreeDetail);
        file.SetValue("graphics", "shadows", Shadows);
        file.SetValue("graphics", "shadow_distance", ShadowDistance);
        file.SetValue("graphics", "resolution_scale", ResolutionScale);
        file.SetValue("graphics", "fxaa", Fxaa);
        if (file.Save(FilePath) != Error.Ok) GD.PushWarning("Could not save graphics settings.");
    }
    public void Apply(Main game)
    {
        game.GetViewport().Scaling3DScale = ResolutionScale;
        game.GetViewport().ScreenSpaceAA = Fxaa ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        game.World.ApplyGraphics(this);
        foreach (var child in game.GetChildren())
            if (child is DirectionalLight3D sun)
            {
                sun.ShadowEnabled = Shadows;
                sun.DirectionalShadowMaxDistance = ShadowDistance;
            }
    }
}
