using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TransportSandbox;

public sealed record IndustryDefinition(string Id, string Name, Dictionary<string, double> Inputs, Dictionary<string, double> Outputs,
    Dictionary<string, double> InputStorage, Dictionary<string, double> OutputStorage);
public sealed class IndustryCatalog
{
    public double CatchmentRadiusM { get; }
    public Dictionary<string, double> StationCapacities { get; }
    public Dictionary<string, double> TransferRates { get; }
    public Dictionary<string, IndustryDefinition> Types { get; } = new();
    private static Dictionary<string, double> Amounts(JsonElement data) => data.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble());
    public IndustryCatalog(JsonElement data)
    {
        CatchmentRadiusM = data.GetProperty("catchmentRadiusM").GetDouble();
        StationCapacities = Amounts(data.GetProperty("stationCapacities")); TransferRates = Amounts(data.GetProperty("transferRates"));
        foreach (var item in data.GetProperty("types").EnumerateArray())
        {
            var type = new IndustryDefinition(item.GetProperty("id").GetString()!, item.GetProperty("name").GetString()!,
                Amounts(item.GetProperty("inputs")), Amounts(item.GetProperty("outputs")), Amounts(item.GetProperty("inputStorage")), Amounts(item.GetProperty("outputStorage")));
            foreach (var (cargo, amount) in type.Inputs.Concat(type.Outputs).Concat(type.InputStorage).Concat(type.OutputStorage))
            { CargoTypes.Get(cargo); if (!double.IsFinite(amount) || amount <= 0) throw new ArgumentException("Industry amounts must be positive."); }
            Types.Add(type.Id, type);
        }
    }
    public static IndustryCatalog Load() => new(JsonData.Read("res://assets/industries/types.json"));
}
public sealed class IndustryState
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public TrackVector Position { get; set; }
    public Dictionary<string, double> Inputs { get; set; } = new();
    public Dictionary<string, double> Outputs { get; set; } = new();
}
public sealed class StationCargo
{
    public int Id { get; set; }
    public Dictionary<string, double> Inbound { get; set; } = new();
    public Dictionary<string, double> Outbound { get; set; } = new();
}
public sealed record StationPolyline(int Station, TrackVector[] Points, int Edge = 0);
public sealed record CargoVisitSave(int Train, int Station, int[] Unloaded, Dictionary<int, double> Credit);

/// <summary>Persistent logistics state. No terrain chunks, scene objects or rendering dependencies.</summary>
public sealed class IndustrySimulation
{
    public IndustryCatalog Catalog { get; }
    public readonly Dictionary<string, IndustryState> Industries = new();
    public readonly Dictionary<int, StationCargo> Stations = new();
    public readonly Dictionary<int, List<string>> Connections = new();
    private readonly Dictionary<string, List<int>> _industryStations = new();
    public StationPolyline[] Platforms { get; private set; } = Array.Empty<StationPolyline>();
    private readonly Dictionary<int, (int Station, HashSet<int> Unloaded, Dictionary<int, double> Credit)> _visits = new();
    public IndustrySimulation(IndustryCatalog catalog) => Catalog = catalog;
    public static double Stock(Dictionary<string, double> storage, string cargo) => storage.GetValueOrDefault(cargo);
    public static void AddStock(Dictionary<string, double> storage, string cargo, double amount) => storage[cargo] = Math.Max(0, Stock(storage, cargo) + amount);
    public void Add(IndustryState industry)
    {
        if (!Catalog.Types.ContainsKey(industry.Type)) throw new ArgumentException("Unknown industry: " + industry.Type);
        Industries.Add(industry.Id, industry);
        SetPlatforms(Platforms);
    }
    public void SetPlatforms(StationPolyline[] platforms)
    {
        var currentStations = platforms.Select(p => p.Station).ToHashSet();
        var currentEdges = platforms.Where(p => p.Edge != 0).GroupBy(p => p.Edge).ToDictionary(g => g.Key, g => g.First().Station);
        foreach (var old in Platforms.GroupBy(p => p.Station).Where(g => !currentStations.Contains(g.Key)))
        {
            int replacement = old.Select(p => currentEdges.GetValueOrDefault(p.Edge)).FirstOrDefault(id => id != 0);
            if (replacement == 0 || !Stations.Remove(old.Key, out var source)) continue;
            if (!Stations.TryGetValue(replacement, out var target)) Stations[replacement] = target = new() { Id = replacement };
            foreach (var cargo in source.Inbound) AddStock(target.Inbound, cargo.Key, cargo.Value);
            foreach (var cargo in source.Outbound) AddStock(target.Outbound, cargo.Key, cargo.Value);
        }
        Platforms = platforms; Connections.Clear();
        var groups = platforms.GroupBy(p => p.Station).ToArray();
        double cell = Catalog.CatchmentRadiusM;
        var grid = Industries.Values.GroupBy(i => ((long)Math.Floor(i.Position.X / cell), (long)Math.Floor(i.Position.Z / cell)))
            .ToDictionary(g => g.Key, g => g.Select(i => i.Id).ToArray());
        foreach (var group in groups)
        {
            if (!Stations.ContainsKey(group.Key)) Stations[group.Key] = new() { Id = group.Key };
            var touched = new HashSet<(long, long)>();
            foreach (var line in group)
                for (int n = 1; n < line.Points.Length; n++)
                {
                    var a = line.Points[n - 1]; var b = line.Points[n];
                    for (long x = (long)Math.Floor((Math.Min(a.X, b.X) - cell) / cell); x <= (long)Math.Floor((Math.Max(a.X, b.X) + cell) / cell); x++)
                        for (long z = (long)Math.Floor((Math.Min(a.Z, b.Z) - cell) / cell); z <= (long)Math.Floor((Math.Max(a.Z, b.Z) + cell) / cell); z++) touched.Add((x, z));
                }
            var candidates = touched.Where(grid.ContainsKey).SelectMany(key => grid[key]);
            Connections[group.Key] = candidates.Where(id => group.Any(p => DistanceTo(p.Points, Industries[id].Position) <= cell)).Order(StringComparer.Ordinal).ToList();
        }
        _industryStations.Clear();
        foreach (var (station, ids) in Connections.OrderBy(p => p.Key))
            foreach (string id in ids)
            { if (!_industryStations.TryGetValue(id, out var stations)) _industryStations[id] = stations = new(); stations.Add(station); }
        // Removed stations retain their stock in the save instead of silently destroying cargo.
    }
    public static double DistanceTo(TrackVector[] line, TrackVector point)
    {
        double best = double.PositiveInfinity;
        for (int i = 1; i < line.Length; i++)
        {
            var a = line[i - 1]; var b = line[i];
            double x = b.X - a.X, z = b.Z - a.Z, length = x * x + z * z;
            double t = length > 0 ? Math.Clamp(((point.X - a.X) * x + (point.Z - a.Z) * z) / length, 0, 1) : 0;
            best = Math.Min(best, Math.Sqrt(Math.Pow(point.X - a.X - x * t, 2) + Math.Pow(point.Z - a.Z - z * t, 2)));
        }
        return best;
    }
    public IEnumerable<IndustryState> Connected(int station) => Connections.TryGetValue(station, out var ids) ? ids.Select(id => Industries[id]) : Enumerable.Empty<IndustryState>();
    public void Advance(double seconds)
    {
        using var probe = FrameProbe.Measure("industries.production");
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        foreach (var station in Stations.Values.OrderBy(s => s.Id))
            foreach (var cargo in station.Inbound.Keys.ToArray())
            {
                double budget = Math.Min(Stock(station.Inbound, cargo), Catalog.TransferRates.GetValueOrDefault(cargo) * seconds);
                foreach (var industry in Connected(station.Id))
                {
                    double space = Catalog.Types[industry.Type].InputStorage.GetValueOrDefault(cargo) - Stock(industry.Inputs, cargo);
                    double moved = Math.Max(0, Math.Min(budget, space));
                    AddStock(industry.Inputs, cargo, moved); AddStock(station.Inbound, cargo, -moved); budget -= moved;
                }
            }
        foreach (var industry in Industries.Values.OrderBy(i => i.Id, StringComparer.Ordinal))
        {
            var type = Catalog.Types[industry.Type]; double time = seconds;
            foreach (var (cargo, rate) in type.Inputs) time = Math.Min(time, Stock(industry.Inputs, cargo) / rate);
            foreach (var (cargo, rate) in type.Outputs) time = Math.Min(time, Math.Max(0, type.OutputStorage[cargo] - Stock(industry.Outputs, cargo)) / rate);
            foreach (var (cargo, rate) in type.Inputs) AddStock(industry.Inputs, cargo, -time * rate);
            foreach (var (cargo, rate) in type.Outputs) AddStock(industry.Outputs, cargo, time * rate);
            foreach (var cargo in type.Outputs.Keys)
            {
                double budget = Math.Min(Stock(industry.Outputs, cargo), Catalog.TransferRates.GetValueOrDefault(cargo) * seconds);
                foreach (int id in _industryStations.GetValueOrDefault(industry.Id) ?? Enumerable.Empty<int>())
                {
                    var station = Stations[id];
                    double moved = Math.Min(budget, Math.Max(0, Catalog.StationCapacities.GetValueOrDefault(cargo) - Stock(station.Outbound, cargo)));
                    AddStock(station.Outbound, cargo, moved); AddStock(industry.Outputs, cargo, -moved); budget -= moved;
                }
            }
        }
    }
    public double AcceptingSpace(int station, string cargo)
    {
        if (!Stations.TryGetValue(station, out var storage)) return 0;
        var consumers = Connected(station).Where(i => Catalog.Types[i.Type].Inputs.ContainsKey(cargo)).ToArray();
        if (consumers.Length == 0) return 0;
        double space = consumers.Sum(i => Catalog.Types[i.Type].InputStorage[cargo] - Stock(i.Inputs, cargo));
        double pending = consumers.SelectMany(i => _industryStations.GetValueOrDefault(i.Id) ?? Enumerable.Empty<int>()).Distinct().Sum(id => Stock(Stations[id].Inbound, cargo));
        return Math.Max(0, Math.Min(space - pending, Catalog.StationCapacities.GetValueOrDefault(cargo) - Stock(storage.Inbound, cargo)));
    }
    public void EndVisit(int train) => _visits.Remove(train);
    public void ClearVisits() => _visits.Clear();
    public CargoVisitSave[] SaveVisits() => _visits.Select(p => new CargoVisitSave(p.Key, p.Value.Station, p.Value.Unloaded.ToArray(), new(p.Value.Credit))).ToArray();
    public void RestoreVisits(IEnumerable<CargoVisitSave> visits)
    { _visits.Clear(); foreach (var visit in visits) _visits[visit.Train] = (visit.Station, new(visit.Unloaded), new(visit.Credit)); }
    public bool Service(TrainConsist train, int station, double seconds, Func<int, bool> atPlatform)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || !double.IsFinite(train.Speed) || Math.Abs(train.Speed) > .001 || !Stations.TryGetValue(station, out var storage)) return false;
        if (!_visits.TryGetValue(train.Id, out var visit) || visit.Station != station) _visits[train.Id] = visit = (station, new(), new());
        string? preference = null;
        for (int n = 0; n < train.Orders.Items.Count; n++)
        {
            var order = train.Orders.Items[(train.Orders.Index + n) % train.Orders.Items.Count];
            if (order.Kind == TrainOrderKind.FullLoad) { preference = order.Cargo; break; }
            if (n > 0 && order.Kind is TrainOrderKind.Station or TrainOrderKind.Waypoint) break;
        }
        bool busy = false;
        for (int i = 0; i < train.Items.Count; i++)
        {
            var item = train.Items[i]; if (item.Definition.Cargo is not { } hold || !atPlatform(i)) continue;
            if (item.Cargo is { } load && Connected(station).Any(n => Catalog.Types[n.Type].Inputs.ContainsKey(load.Type)))
            {
                visit.Unloaded.Add(item.Id);
                double rate = Catalog.TransferRates.GetValueOrDefault(load.Type);
                double allowance = rate * seconds;
                if (CargoTypes.Get(load.Type).Discrete)
                { visit.Credit[item.Id] = Math.Min(Math.Max(1, rate), visit.Credit.GetValueOrDefault(item.Id) + allowance); allowance = Math.Floor(visit.Credit[item.Id]); }
                double moved = Math.Min(load.Quantity, Math.Min(AcceptingSpace(station, load.Type), allowance));
                if (CargoTypes.Get(load.Type).Discrete) moved = Math.Floor(moved);
                if (moved > 1e-9)
                {
                    AddStock(storage.Inbound, load.Type, moved);
                    if (CargoTypes.Get(load.Type).Discrete) visit.Credit[item.Id] -= moved;
                    train.Items[i] = item with { Cargo = load.Quantity - moved <= 1e-9 ? null : load with { Quantity = load.Quantity - moved } }; busy = true;
                }
                else if (CargoTypes.Get(load.Type).Discrete && load.Quantity >= 1 && AcceptingSpace(station, load.Type) >= 1 && rate > 0) busy = true;
                continue;
            }
            if (visit.Unloaded.Contains(item.Id)) continue;
            string? cargo = item.Cargo?.Type ?? item.CargoAssignment ?? (preference != null && hold.Accepts(preference) ? preference : null);
            if (cargo == null) cargo = hold.AcceptedTypes.FirstOrDefault(c => Stock(storage.Outbound, c) > 1e-9);
            if (cargo == null || !hold.Accepts(cargo)) continue;
            double quantity = item.Cargo?.Quantity ?? 0;
            double loadingRate = Catalog.TransferRates.GetValueOrDefault(cargo), budget = loadingRate * seconds;
            if (CargoTypes.Get(cargo).Discrete)
            { visit.Credit[item.Id] = Math.Min(Math.Max(1, loadingRate), visit.Credit.GetValueOrDefault(item.Id) + budget); budget = Math.Floor(visit.Credit[item.Id]); }
            double amount = Math.Min(hold.CapacityFor(cargo) - quantity, Math.Min(Stock(storage.Outbound, cargo), budget));
            if (CargoTypes.Get(cargo).Discrete) amount = Math.Floor(amount);
            if (amount > 1e-9)
            { AddStock(storage.Outbound, cargo, -amount); train.Items[i] = item with { Cargo = new(cargo, quantity + amount) }; if (CargoTypes.Get(cargo).Discrete) visit.Credit[item.Id] -= amount; busy = true; }
            else if (CargoTypes.Get(cargo).Discrete && hold.CapacityFor(cargo) - quantity >= 1 && Stock(storage.Outbound, cargo) >= 1 && loadingRate > 0) busy = true;
        }
        return busy;
    }
}
