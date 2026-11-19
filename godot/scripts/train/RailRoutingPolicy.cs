using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TransportSandbox;

/// <summary>Immutable station permissions; equal order sets can share destination fields safely.</summary>
public sealed class RailRoutingPolicy : IEquatable<RailRoutingPolicy>
{
    public const double UnorderedStationPenalty = 10000; // Equivalent metres, once per station entry.
    public static readonly RailRoutingPolicy Empty = new(Array.Empty<int>());
    private readonly FrozenSet<int> _stations;
    private readonly string _key;
    private RailRoutingPolicy(int[] stations)
    {
        _stations = stations.ToFrozenSet();
        _key = string.Join(",", stations.Select(id => id.ToString(CultureInfo.InvariantCulture)));
    }
    public static RailRoutingPolicy Create(IEnumerable<int> stations)
    {
        var ids = stations.Where(id => id > 0).Distinct().Order().ToArray();
        return ids.Length == 0 ? Empty : new(ids);
    }
    public bool Allows(int station) => _stations.Contains(station);
    public bool Equals(RailRoutingPolicy? other) => other != null && _key == other._key;
    public override bool Equals(object? other) => other is RailRoutingPolicy policy && Equals(policy);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_key);
}
