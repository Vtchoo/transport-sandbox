using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TransportSandbox;

public sealed class VehicleDefinition
{
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

    public VehicleDefinition(JsonElement data)
    {
        Id = data.GetProperty("id").GetString()!;
        Length = data.GetProperty("length").GetDouble();
        var models = new Dictionary<int, string>();
        foreach (var model in data.GetProperty("models").EnumerateObject())
            models.Add(int.Parse(model.Name), model.Value.GetString()!);
        Models = models;
        var exhausts = new List<ExhaustOutlet>();
        foreach (var outlet in data.GetProperty("exhausts").EnumerateArray())
            exhausts.Add(new(JsonData.Vector(outlet.GetProperty("position")),
                outlet.GetProperty("kind").GetString() == "steam",
                outlet.TryGetProperty("idleRate", out var rate) ? rate.GetDouble() : null));
        Exhausts = exhausts;
    }
}

/// <summary>The same gauge-specific model definitions are used by menu and gameplay objects.</summary>
public sealed class VehicleCatalog
{
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
