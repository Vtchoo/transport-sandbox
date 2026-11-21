using Godot;
using System;

namespace TransportSandbox;

/// <summary>Application preferences shared by the menu and gameplay audio mixers.</summary>
public sealed class SoundSettings
{
    public string FilePath = "user://audio-settings.cfg";
    public float MasterVolume = 1;
    public float Engines = 1;
    public float Wheels = 1;
    public float Curves = 1;
    public bool Muted;

    public void Load()
    {
        using var file = new ConfigFile();
        if (file.Load(FilePath) != Error.Ok) return;
        float Read(string name) => Math.Clamp((float)file.GetValue("audio", name, 1.0).AsDouble(), 0, 1);
        MasterVolume = Read("master"); Engines = Read("engines"); Wheels = Read("wheels"); Curves = Read("curves");
        Muted = file.GetValue("audio", "muted", false).AsBool();
    }

    public void Save()
    {
        using var file = new ConfigFile();
        file.SetValue("audio", "master", MasterVolume);
        file.SetValue("audio", "engines", Engines);
        file.SetValue("audio", "wheels", Wheels);
        file.SetValue("audio", "curves", Curves);
        file.SetValue("audio", "muted", Muted);
        if (file.Save(FilePath) != Error.Ok) GD.PushWarning("Could not save sound settings.");
    }
}
