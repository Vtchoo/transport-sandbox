using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public enum TrackSupport { Fill, ConcreteBridge }
public sealed record BridgePier(double Along);

public sealed partial class TrackNetwork
{
    /// <summary>Only edges connecting the same pair of parallel groups share support.</summary>
    public TrackEdge[] SupportPeers(TrackEdge edge)
    {
        int a = Nodes[edge.A].Group, b = Nodes[edge.B].Group;
        if (a == 0 || b == 0 || a == b) return new[] { edge };
        bool Matches(TrackEdge candidate)
        {
            int first = Nodes[candidate.A].Group == a ? candidate.A : candidate.B;
            int second = first == candidate.A ? candidate.B : candidate.A;
            if (!Groups.TryGetValue(a, out var ga) || !Groups.TryGetValue(b, out var gb)) return true;
            int rankA = Array.IndexOf(ga.Offsets.OrderBy(p => p.Value).Select(p => p.Key).ToArray(), first);
            int rankB = Array.IndexOf(gb.Offsets.OrderBy(p => p.Value * (ga.Axis.Dot(gb.Axis) < 0 ? -1 : 1)).Select(p => p.Key).ToArray(), second);
            return rankA == rankB;
        }
        if (!Matches(edge)) return new[] { edge }; // Crossovers between lanes remain independent spans.
        return Edges.Values.Where(e => (Nodes[e.A].Group == a && Nodes[e.B].Group == b ||
            Nodes[e.A].Group == b && Nodes[e.B].Group == a) && Matches(e)).OrderBy(e => e.Id).ToArray();
    }
    public TrackSupport InheritedSupport(TrackNode source, int side) => Incident(source.Id)
        .OrderBy(e => SideAt(e, source.Id) == -side ? 0 : 1).ThenBy(e => e.Id).Select(e => e.Support).FirstOrDefault();
    public void SetSupport(TrackEdge edge, TrackSupport support)
    {
        if (!Enum.IsDefined(support)) throw new ArgumentOutOfRangeException(nameof(support));
        var peers = SupportPeers(edge);
        foreach (var peer in peers) peer.Support = support;
        if (support == TrackSupport.ConcreteBridge && peers.Length > 1)
        {
            var owner = peers.FirstOrDefault(e => e.BridgePiers != null) ?? peers[0];
            SetBridgePiers(owner, owner.BridgePiers);
        }
        else NotifyChanged();
    }
    public void SetBridgePiers(TrackEdge edge, IEnumerable<BridgePier>? piers)
    {
        var data = piers?.OrderBy(p => p.Along).ToList();
        if (data?.Any(p => !double.IsFinite(p.Along) || p.Along <= 0 || p.Along >= 1) == true)
            throw new ArgumentOutOfRangeException(nameof(piers));
        foreach (var peer in SupportPeers(edge))
        {
            bool forward = peer.A == edge.A || Nodes[peer.A].Group != 0 && Nodes[peer.A].Group == Nodes[edge.A].Group;
            peer.BridgePiers = data?.Select(p => forward ? p : new BridgePier(1 - p.Along)).OrderBy(p => p.Along).ToList();
        }
        NotifyChanged();
    }
    public static BridgePier[] AutomaticPiers(double length)
    {
        int spans = Math.Max(1, (int)Math.Ceiling(length / 20));
        return Enumerable.Range(1, spans - 1).Select(i => new BridgePier((double)i / spans)).ToArray();
    }
}
