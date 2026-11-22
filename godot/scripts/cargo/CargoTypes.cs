using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransportSandbox;

public enum CargoUnit { Tonnes, Litres, Passengers, Items }

/// <summary>Capacity and load quantities use the cargo's own unit; simulation mass remains kilograms.</summary>
public sealed record CargoType(string Id, string Name, CargoUnit Unit, double KilogramsPerUnit)
{
    public bool Discrete => Unit is CargoUnit.Passengers or CargoUnit.Items;
    public string UnitLabel => Unit switch { CargoUnit.Tonnes => "t", CargoUnit.Litres => "L", CargoUnit.Passengers => "passengers", _ => "items" };
    public string FormatQuantity(double quantity) => quantity.ToString(Discrete ? "0" : "0.###", CultureInfo.InvariantCulture);
    public string FormatAmount(double quantity) => FormatQuantity(quantity) + " " + UnitLabel;
    public double MassKg(double quantity) => quantity * KilogramsPerUnit;
}

public static class CargoTypes
{
    public const string CoalId = "coal";
    public const string IronOreId = "iron-ore";
    public const string OilId = "oil";
    public const string PassengersId = "passengers";
    public static readonly CargoType Coal = new(CoalId, "Coal", CargoUnit.Tonnes, 1000);
    public static readonly CargoType IronOre = new(IronOreId, "Iron ore", CargoUnit.Tonnes, 1000);
    // Configurable gameplay mass allowances, not specifications for a particular oil or passenger population.
    public static readonly CargoType Oil = new(OilId, "Oil", CargoUnit.Litres, .85);
    public static readonly CargoType Passengers = new(PassengersId, "Passengers", CargoUnit.Passengers, 75);
    public static IReadOnlyDictionary<string, CargoType> All { get; } = new ReadOnlyDictionary<string, CargoType>(
        new Dictionary<string, CargoType> { [CoalId] = Coal, [IronOreId] = IronOre, [OilId] = Oil, [PassengersId] = Passengers });
    public static CargoType Get(string id) => All.TryGetValue(id, out var cargo) ? cargo : throw new ArgumentException("Unknown cargo: " + id);
}

/// <summary>One cargo type per car at a time. Each capacity uses that cargo's declared unit.</summary>
public sealed class CargoHold
{
    public IReadOnlyDictionary<string, double> Capacities { get; }
    public IEnumerable<string> AcceptedTypes => Capacities.Keys;
    public CargoHold(IEnumerable<KeyValuePair<string, double>> capacitiesByType)
    {
        var capacities = new Dictionary<string, double>();
        foreach (var (id, quantity) in capacitiesByType)
        {
            var type = CargoTypes.Get(id);
            if (!double.IsFinite(quantity) || quantity <= 0 || !double.IsFinite(type.MassKg(quantity)) || (type.Discrete && quantity != Math.Truncate(quantity)))
                throw new ArgumentOutOfRangeException(nameof(capacitiesByType));
            capacities.Add(id, quantity);
        }
        if (capacities.Count == 0) throw new ArgumentException("A cargo hold must accept at least one cargo type.");
        Capacities = new ReadOnlyDictionary<string, double>(capacities);
    }
    public bool Accepts(string id) => Capacities.ContainsKey(id);
    public double CapacityFor(string id) => Capacities.TryGetValue(id, out var quantity) ? quantity : 0;
    public void Validate(CargoLoad? load)
    {
        if (load == null) return;
        if (!Accepts(load.Type)) throw new InvalidOperationException("This car cannot carry " + load.Type + ".");
        var type = CargoTypes.Get(load.Type);
        if (!double.IsFinite(load.Quantity) || load.Quantity <= 0 || load.Quantity > CapacityFor(load.Type) || !double.IsFinite(load.MassKg))
            throw new InvalidOperationException("Cargo quantity must be positive and within the car's capacity.");
        if (type.Discrete && load.Quantity != Math.Truncate(load.Quantity))
            throw new InvalidOperationException("This cargo must use a whole-number quantity.");
    }
}

[JsonConverter(typeof(CargoLoadJsonConverter))]
public sealed record CargoLoad(string Type, double Quantity)
{
    [JsonIgnore] public double MassKg => CargoTypes.Get(Type).MassKg(Quantity);
}

/// <summary>New saves store quantities. Read the original kilogram-only saves without changing their mass.</summary>
public sealed class CargoLoadJsonConverter : JsonConverter<CargoLoad>
{
    public override CargoLoad Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var data = document.RootElement;
        string id = data.GetProperty("Type").GetString() ?? throw new JsonException("Missing cargo type.");
        var type = CargoTypes.Get(id);
        bool quantity = data.TryGetProperty("Quantity", out var amount);
        if (!quantity && !data.TryGetProperty("MassKg", out amount)) throw new JsonException("Missing cargo quantity.");
        double value = JsonSerializer.Deserialize<double>(amount.GetRawText(), options);
        return new(id, quantity ? value : value / type.KilogramsPerUnit);
    }
    public override void Write(Utf8JsonWriter writer, CargoLoad value, JsonSerializerOptions options)
    {
        writer.WriteStartObject(); writer.WriteString("Type", value.Type);
        writer.WriteNumber("Quantity", value.Quantity); writer.WriteEndObject();
    }
}
