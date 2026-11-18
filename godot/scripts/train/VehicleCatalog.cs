using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TransportSandbox;

public sealed class VehicleDefinition
{
    public string DisplayName { get; }
    public string Kind { get; }
    public string Thumbnail { get; }
    public double Width { get; }
    public double Height { get; }
    public double BogieCenters { get; }
    public VehiclePhysics Physics { get; }
    public IReadOnlyList<(double Offset, VehicleDefinition Definition)> Sections { get; }
    public string Id
    {
        get;
    }
    public double Length
    {
        get;
    }
    public IReadOnlyDictionary<int, string> Models
    {
        get;
    }
    public IReadOnlyList<ExhaustOutlet> Exhausts
    {
        get;
    }

    public VehicleDefinition(JsonElement data, IReadOnlyDictionary<int, string>? inheritedModels = null)
    {
        Id = data.GetProperty("id").GetString()!;
        Length = data.GetProperty("length").GetDouble();
        DisplayName = data.GetProperty("name").GetString()!;
        Kind = data.GetProperty("kind").GetString()!;
        Thumbnail = data.TryGetProperty("thumbnail", out var thumb) ? thumb.GetString()! : "";
        Width = data.GetProperty("width").GetDouble();
        Height = data.GetProperty("height").GetDouble();
        BogieCenters = data.GetProperty("bogieCenters").GetDouble();
        var physics = data.GetProperty("physics");
        Physics = new(physics.GetProperty("massKg").GetDouble(), physics.GetProperty("powerW").GetDouble(), physics.GetProperty("tractiveEffortN").GetDouble(), physics.GetProperty("maxSpeedMps").GetDouble());
        var models = new Dictionary<int, string>();
        if (data.TryGetProperty("models", out var modelData))
            foreach (var model in modelData.EnumerateObject()) models.Add(int.Parse(model.Name), model.Value.GetString()!);
        Models = inheritedModels ?? models;
        var sections = new List<(double, VehicleDefinition)>();
        if (data.TryGetProperty("sections", out var sectionData))
            foreach (var section in sectionData.EnumerateArray()) sections.Add((section.GetProperty("offset").GetDouble(), new(section.GetProperty("definition"), Models)));
        Sections = sections;
        var exhausts = new List<ExhaustOutlet>();
        if (data.TryGetProperty("exhausts", out var exhaustData))
        foreach (var outlet in exhaustData.EnumerateArray())
            exhausts.Add(new(JsonData.Vector(outlet.GetProperty("position")),
                outlet.GetProperty("kind").GetString() == "steam",
                outlet.TryGetProperty("idleRate", out var rate) ? rate.GetDouble() : null));
        Exhausts = exhausts;
    }
}

/// <summary>The same gauge-specific model definitions are used by menu and gameplay objects.</summary>
public sealed class VehicleCatalog
{
    public IEnumerable<VehicleDefinition> Definitions => _definitions.Values;
    private readonly Dictionary<string, VehicleDefinition> _definitions = new();
    public VehicleCatalog(JsonElement catalog)
    {
        foreach (var item in catalog.EnumerateArray())
        {
            var definition = new VehicleDefinition(item);
            _definitions.Add(definition.Id, definition);
        }
    }
    public static VehicleCatalog Load() => new(JsonData.Read("res://assets/vehicles/catalog.json"));
    public VehicleDefinition Get(string id) => _definitions.TryGetValue(id, out var definition)
        ? definition : throw new ArgumentException("Unknown vehicle: " + id);
}
