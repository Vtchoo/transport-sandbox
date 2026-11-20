using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public enum TrainOrderKind { Station, Wait, Waypoint }
public sealed record TrainOrder(int Id, TrainOrderKind Kind, int Target = 0, double Seconds = 0);

/// <summary>Independent order lists; clipboard copies configuration, never execution progress.</summary>
public sealed class TrainOrders
{
    public readonly List<TrainOrder> Items = new();
    public int Index;
    public double WaitRemaining = -1;
    public int Revision { get; private set; }
    public string Status = "No orders - wandering";
    private int _nextId = 1;
    public int NextId => _nextId;
    private int _policyRevision = -1;
    private RailRoutingPolicy _policy = RailRoutingPolicy.Empty;
    public RailRoutingPolicy RoutingPolicy
    {
        get
        {
            if (_policyRevision != Revision)
            {
                _policy = RailRoutingPolicy.Create(Items.Where(order => order.Kind == TrainOrderKind.Station).Select(order => order.Target));
                _policyRevision = Revision;
            }
            return _policy;
        }
    }
    public TrainOrder? Current => Items.Count == 0 ? null : Items[Math.Clamp(Index, 0, Items.Count - 1)];
    public TrainOrders Copy()
    {
        var copy = new TrainOrders { Index = Index, WaitRemaining = WaitRemaining, Revision = Revision, Status = Status, _nextId = _nextId };
        copy.Items.AddRange(Items);
        return copy;
    }
    public void Add(TrainOrderKind kind, int target = 0, double seconds = 0)
    {
        if (kind == TrainOrderKind.Wait && (!double.IsFinite(seconds) || seconds < 0))
            throw new InvalidOperationException("Choose a non-negative wait duration.");
        Items.Add(new(_nextId++, kind, target, seconds));
        Changed();
    }
    public void Append(IEnumerable<TrainOrder> orders)
    {
        var copy = orders.ToArray();
        foreach (var order in copy) Items.Add(order with { Id = _nextId++ });
        if (copy.Length > 0) Changed();
    }
    public void Remove(int id)
    {
        int at = Items.FindIndex(order => order.Id == id);
        if (at < 0) return;
        if (at == Index) WaitRemaining = -1;
        Items.RemoveAt(at);
        if (at < Index) Index--;
        if (Index >= Items.Count) Index = 0;
        Changed();
    }
    public void Move(int id, int before)
    {
        int at = Items.FindIndex(order => order.Id == id);
        if (at < 0 || before < 0 || before > Items.Count || before == at || before == at + 1) return;
        int? current = Current?.Id;
        var order = Items[at]; Items.RemoveAt(at); Items.Insert(before > at ? before - 1 : before, order);
        Index = current is { } active ? Items.FindIndex(item => item.Id == active) : 0;
        Changed();
    }
    public void Advance()
    {
        Index = Items.Count == 0 ? 0 : (Index + 1) % Items.Count;
        WaitRemaining = -1;
    }
    public void Restore(TrainOrder[] items, int index, double waitRemaining, int nextId = 1)
    {
        Items.Clear(); Items.AddRange(items);
        _nextId = Math.Max(nextId, Items.Select(i => i.Id + 1).DefaultIfEmpty(1).Max());
        Index = Items.Count == 0 ? 0 : Math.Clamp(index, 0, Items.Count - 1); WaitRemaining = waitRemaining;
        Changed();
    }
    private void Changed() { Revision++; Status = Items.Count == 0 ? "No orders - wandering" : "Route updated"; }
}

/// <summary>Planning cache belongs to one placed train and is discarded after edits/repositioning.</summary>
public sealed class TrainOrderJourney
{
    public TrainOrder? BlockedOrder;
    public TrainPosition? TargetPosition;
    public int Order, OrdersRevision, NetworkRevision, Direction;
    public long GraphVersion;
    public readonly HashSet<int> Dependencies = new();
    public bool NeedsReplan, HoldForReplan;
    public double Distance = double.PositiveInfinity;
    public bool Reachable, ViaDeadEnd;
}
