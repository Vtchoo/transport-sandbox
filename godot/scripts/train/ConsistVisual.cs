using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TransportSandbox;

public sealed record ConsistItem(string Id, bool Reversed = false);
public sealed record ConsistDefinition(string Id, int Gauge, IReadOnlyList<ConsistItem> Vehicles)
{
    public static IReadOnlyList<ConsistDefinition> LoadMenuConsists()
    {
        return JsonData.Read("res://assets/menu/consists.json").EnumerateArray().Select(data =>
            new ConsistDefinition(data.GetProperty("id").GetString()!, data.GetProperty("gauge").GetInt32(),
                data.GetProperty("vehicles").EnumerateArray().Select(item =>
                    new ConsistItem(item.GetProperty("id").GetString()!,
                        item.TryGetProperty("reversed", out var reversed) && reversed.GetBoolean())).ToArray())).ToArray();
    }
}

public partial class VehicleVisual : Node3D
{
    public VehicleDefinition Definition
    {
        get;
    }
    public int Gauge
    {
        get;
    }
    public bool Reversed
    {
        get;
    }
    public Node3D Model
    {
        get;
    }
    public VehicleVisual(VehicleDefinition definition, int gauge, bool reversed = false)
    {
        if (!definition.Models.TryGetValue(gauge, out var model))
            throw new ArgumentException($"{definition.Id} is unavailable for {gauge} mm track.");
        Definition = definition;
        Gauge = gauge;
        Reversed = reversed;
        Name = definition.Id;
        // Imported geometry is already in metres. Never normalize or resize catalog models.
        Model = GD.Load<PackedScene>(model).Instantiate<Node3D>();
        AddChild(Model);
        Rotation = new(0, reversed ? Mathf.Pi : 0, 0);
    }
}

/// <summary>Reusable vehicle instances and coupler layout; no menu-specific meshes.</summary>
public partial class ConsistVisual : Node3D
{
    public const double CouplerGap = 0.12;
    public double Length
    {
        get;
    }
    public IReadOnlyList<double> Offsets
    {
        get;
    }
    public IReadOnlyList<VehicleVisual> Vehicles
    {
        get;
    }
    public static ConsistVisual Create(VehicleCatalog catalog, ConsistDefinition consist)
    {
        if (consist.Vehicles.Count == 0)
            throw new ArgumentException("A consist needs at least one vehicle.");
        var definitions = consist.Vehicles.Select(item => catalog.Get(item.Id)).ToArray();
        // Validate the entire consist before creating any scene nodes.
        foreach (var definition in definitions)
            if (!definition.Models.ContainsKey(consist.Gauge))
                throw new ArgumentException($"{definition.Id} is unavailable for {consist.Gauge} mm track.");
        return new ConsistVisual(consist, definitions);
    }
    private ConsistVisual(ConsistDefinition consist, VehicleDefinition[] definitions)
    {
        Length = definitions.Sum(definition => definition.Length) + (definitions.Length - 1) * CouplerGap;
        double front = Length / 2;
        var vehicles = new List<VehicleVisual>();
        var offsets = new List<double>();
        for (int i = 0; i < definitions.Length; i++)
        {
            double offset = front - definitions[i].Length / 2;
            front -= definitions[i].Length + CouplerGap;
            var vehicle = new VehicleVisual(definitions[i], consist.Gauge, consist.Vehicles[i].Reversed)
            {
                Position = new(0, 0, (float)offset)
            };
            AddChild(vehicle);
            vehicles.Add(vehicle);
            offsets.Add(offset);
        }
        Vehicles = vehicles;
        Offsets = offsets;
    }
    public IEnumerable<ExhaustOutlet> WorldExhausts()
    {
        foreach (var vehicle in Vehicles)
            foreach (var outlet in vehicle.Definition.Exhausts)
                yield return outlet with
                {
                    Position = vehicle.GlobalTransform * outlet.Position
                };
    }
}
