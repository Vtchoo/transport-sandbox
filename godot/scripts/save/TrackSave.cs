using System.Linq;

namespace TransportSandbox;

public sealed partial class TrackNetwork
{
    public void Capture(GameSave save)
    {
        save.NextNode = _nextNode; save.NextEdge = _nextEdge; save.NextGroup = _nextGroup; save.NextStation = _nextStation;
        save.Nodes = Nodes.Values.OrderBy(n => n.Id).Select(n => new SavedNode(n.Id, n.Gauge, n.Group, n.WorldPosition, TrackVector.From(n.Axis))).ToArray();
        save.Edges = Edges.Values.OrderBy(e => e.Id).Select(e => e.Clone()).ToArray();
        save.Groups = Groups.OrderBy(g => g.Key).Select(g => new SavedGroup(g.Key, g.Value.Anchor, g.Value.WorldOrigin, TrackVector.From(g.Value.Axis), new(g.Value.Offsets))).ToArray();
        save.Signals = Signals.Values.Select(s => new SavedSignal(s.Key.Node, s.Key.Side, s.Kind)).ToArray();
    }
    public void Restore(GameSave save)
    {
        Nodes.Clear(); Edges.Clear(); Groups.Clear(); Signals.Clear();
        foreach (var n in save.Nodes) Nodes.Add(n.Id, new() { Id = n.Id, Gauge = n.Gauge, Group = n.Group, Space = Space, WorldPosition = n.Position, Axis = n.Axis.Float() });
        foreach (var e in save.Edges) Edges.Add(e.Id, e.Clone());
        foreach (var g in save.Groups) Groups.Add(g.Id, new() { Anchor = g.Anchor, Space = Space, WorldOrigin = g.Origin, Axis = g.Axis.Float(), Offsets = new(g.Offsets) });
        foreach (var s in save.Signals) Signals.Add(new(s.Node, s.Side), new(new(s.Node, s.Side), s.Kind));
        _nextNode = System.Math.Max(save.NextNode, Nodes.Keys.DefaultIfEmpty(0).Max() + 1); _nextEdge = System.Math.Max(save.NextEdge, Edges.Keys.DefaultIfEmpty(0).Max() + 1);
        _nextGroup = System.Math.Max(save.NextGroup, Groups.Keys.DefaultIfEmpty(0).Max() + 1); _nextStation = System.Math.Max(save.NextStation, Edges.Values.Select(e => e.Station).DefaultIfEmpty(0).Max() + 1);
        NotifyChanged();
    }
}
