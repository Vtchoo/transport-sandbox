using Godot;
using System.Text.Json;

namespace TransportSandbox;

public static class JsonData
{
    public static JsonElement Read(string path)
    {
        using var document = JsonDocument.Parse(FileAccess.GetFileAsString(path));
        return document.RootElement.Clone();
    }
    public static Vector3 Vector(JsonElement data) => new(data[0].GetSingle(), data[1].GetSingle(), data[2].GetSingle());
    public static WorldSettings Settings(JsonElement data) => new(data.GetProperty("seed").GetString()!, data.GetProperty("mountainHeight").GetDouble(), data.GetProperty("horizontalScale").GetDouble(), data.GetProperty("seaLevel").GetDouble(), data.TryGetProperty("physics", out var physics) ? physics.GetString()! : "ho");
}
