using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public sealed record TrainItem(int Id, VehicleDefinition Definition, int Facing = 1, string? Livery = null);
public sealed class TrainConsist
{
    public int Id;
    public string Name = "";
    public List<TrainItem> Items = new();
    public bool Placed, Running;
    public double Speed;
    public int TravelDirection = 1;
    public TrainOrders Orders = new();
    public bool Stopped => !Running && double.IsFinite(Speed) && Math.Abs(Speed) <= .001;
    public TrainConsist Copy() => new() { Id = Id, Name = Name, Items = new(Items), Placed = Placed, Running = Running, Speed = Speed, TravelDirection = TravelDirection, Orders = Orders.Copy() };
    public double Length => Items.Sum(i => i.Definition.Length) + Math.Max(0, Items.Count - 1) * .12;
    public double[] Offsets
    {
        get
        {
            double front = Length / 2;
            return Items.Select(item => { double offset = front - item.Definition.Length / 2; front -= item.Definition.Length + .12; return offset; }).ToArray();
        }
    }
}
public sealed class TrainBuilder
{
    public readonly List<TrainConsist> Trains = new();
    public TrainConsist? Selected;
    private int _nextTrain = 1, _nextItem = 1;
    public int NextTrainId => _nextTrain;
    public int NextItemId => _nextItem;
    public void RestoreCounters(int nextTrain, int nextItem)
    { _nextTrain = Math.Max(_nextTrain, nextTrain); _nextItem = Math.Max(_nextItem, nextItem); }
    public static int[] Gauges(IEnumerable<VehicleDefinition> definitions)
    {
        var items = definitions.ToArray();
        return items.Length == 0 ? Array.Empty<int>() : items[0].Models.Keys.Order().Where(g => items.All(d => d.Models.ContainsKey(g))).ToArray();
    }
    public static void RequireStopped(TrainConsist train)
    {
        if (!train.Stopped) throw new InvalidOperationException("Train must be stopped first.");
    }
    public TrainConsist Create()
    {
        var train = new TrainConsist { Id = _nextTrain++, Name = $"Train {_nextTrain - 1}" };
        Trains.Add(train);
        return Selected = train;
    }
    public TrainConsist Get(int id) => Trains.FirstOrDefault(t => t.Id == id) ?? throw new InvalidOperationException("Choose an existing train.");
    public TrainItem Add(TrainConsist train, VehicleDefinition definition, int? index = null)
    {
        RequireStopped(train);
        int at = index ?? train.Items.Count;
        if (at < 0 || at > train.Items.Count) throw new InvalidOperationException("Choose a valid insertion position.");
        if (Gauges(train.Items.Select(i => i.Definition).Append(definition)).Length == 0) throw new InvalidOperationException("These vehicles have no compatible gauge in common.");
        var item = new TrainItem(_nextItem++, definition, Livery: definition.ResolveLivery(null));
        train.Items.Insert(at, item);
        Selected = train;
        return item;
    }
    public bool Move(int sourceId, int itemId, int targetId, int index)
    {
        var source = Get(sourceId); var target = Get(targetId);
        RequireStopped(source); RequireStopped(target);
        int from = source.Items.FindIndex(i => i.Id == itemId);
        if (from < 0 || index < 0 || index > target.Items.Count) throw new InvalidOperationException("Choose a valid vehicle and insertion position.");
        if (source == target && (from == index || from + 1 == index)) return false;
        var item = source.Items[from];
        if (source != target && Gauges(target.Items.Select(i => i.Definition).Append(item.Definition)).Length == 0) throw new InvalidOperationException("These vehicles have no compatible gauge in common.");
        source.Items.RemoveAt(from);
        target.Items.Insert(source == target && from < index ? index - 1 : index, item);
        Selected = target;
        return true;
    }
    public void Remove(int trainId, int itemId)
    {
        var train = Get(trainId); RequireStopped(train);
        train.Items.RemoveAll(i => i.Id == itemId); Selected = train;
    }
    public void Flip(int trainId, int itemId)
    {
        var train = Get(trainId); RequireStopped(train);
        int at = train.Items.FindIndex(i => i.Id == itemId);
        if (at >= 0) train.Items[at] = train.Items[at] with { Facing = -train.Items[at].Facing };
        Selected = train;
    }
    public void Merge(TrainConsist target, TrainConsist source, bool front, bool reverse)
    {
        if (target == source || source.Placed || !Trains.Contains(source) || !Trains.Contains(target)) throw new InvalidOperationException("Choose two different trains, with an unplaced incoming train.");
        RequireStopped(target); RequireStopped(source);
        var incoming = reverse ? source.Items.AsEnumerable().Reverse().Select(i => i with { Facing = -i.Facing }).ToList() : source.Items;
        var combined = front ? incoming.Concat(target.Items).ToList() : target.Items.Concat(incoming).ToList();
        if (Gauges(combined.Select(i => i.Definition)).Length == 0) throw new InvalidOperationException("These trains have no compatible gauge in common.");
        target.Items = combined;
        Trains.Remove(source); Selected = target;
    }
    public void Restore(IEnumerable<TrainConsist> trains, int? selected)
    {
        Trains.Clear(); Trains.AddRange(trains.Select(t => t.Copy()));
        Selected = Trains.FirstOrDefault(t => t.Id == selected);
        _nextTrain = Math.Max(_nextTrain, Trains.Select(t => t.Id + 1).DefaultIfEmpty(1).Max());
        _nextItem = Math.Max(_nextItem, Trains.SelectMany(t => t.Items).Select(i => i.Id + 1).DefaultIfEmpty(1).Max());
    }
}
