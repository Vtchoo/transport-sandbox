using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public sealed record RailPath(int[] States, double Distance);

/// <summary>Synchronous directed Dijkstra retained as the parity reference. Live trains use RailPathfinder.</summary>
public sealed class RailRouter
{
    private sealed record State(double Length, int[] Next);
    private Dictionary<int, State> _graph = new();
    private readonly Dictionary<(int, int), int[]> _cache = new();
    private readonly Queue<(int, int)> _cacheOrder = new();
    public void Rebuild(TrackNetwork network, TrainPlacement? placement = null)
    {
        var graph = new Dictionary<int, State>();
        foreach (var edge in network.Edges.Values)
        {
            double length;
            if (placement != null) length = placement.Curve(edge).Length;
            else { using var curve = network.Curve(edge); length = curve.GetBakedLength(); }
            foreach (int direction in new[] { -1, 1 })
            {
                int node = direction == 1 ? edge.B : edge.A;
                graph[edge.Id * 2 + (direction == 1 ? 1 : 0)] = new(length, network.Continuations(edge, direction).Select(e => e.Id * 2 + (e.A == node ? 1 : 0)).ToArray());
            }
        }
        _graph = graph;
        _cache.Clear();
        _cacheOrder.Clear();
    }
    public static int StateId(int edge, int direction) => edge * 2 + (direction > 0 ? 1 : 0);
    public static int Direction(int state) => state % 2 == 1 ? 1 : -1;
    /// <summary>Find a target arc position. Reversals are graph transitions only at dead ends.</summary>
    public RailPath? Plan(TrainPosition start, int travelDirection, IReadOnlyDictionary<int, double> targets, bool allowReversals = true)
    {
        int initial = StateId(start.Edge, start.Facing * travelDirection);
        if (!_graph.TryGetValue(initial, out var first) || targets.Count == 0) return null;
        var costs = new Dictionary<int, double>();
        var parents = new Dictionary<int, int?>();
        var heap = new List<(double Cost, int State)>();
        double best = double.PositiveInfinity;
        int? destination = null;
        bool direct = false;
        if (targets.TryGetValue(initial, out double u))
        {
            double distance = (u - start.U) * Direction(initial) * first.Length;
            if (distance >= -.02) { best = Math.Max(0, distance); direct = true; }
        }
        void Offer(int state, double cost, int? parent)
        {
            if (!_graph.ContainsKey(state) || cost >= costs.GetValueOrDefault(state, double.PositiveInfinity)) return;
            costs[state] = cost; parents[state] = parent; Push(heap, (cost, state));
        }
        int[] Exits(int state) => allowReversals && _graph[state].Next.Length == 0 ? new[] { state ^ 1 } : _graph[state].Next;
        double remaining = first.Length * (Direction(initial) > 0 ? 1 - start.U : start.U);
        foreach (int next in Exits(initial)) Offer(next, remaining, null);
        while (heap.Count > 0)
        {
            var (cost, state) = Pop(heap);
            if (cost > costs[state] || cost > best) continue;
            var leg = _graph[state];
            if (targets.TryGetValue(state, out u))
            {
                double distance = cost + leg.Length * (Direction(state) > 0 ? u : 1 - u);
                if (distance < best) { best = distance; destination = state; direct = false; }
            }
            foreach (int next in Exits(state)) Offer(next, cost + leg.Length, state);
        }
        if (direct) return new(new[] { initial }, best);
        if (destination is not { } end) return null;
        var path = new List<int>();
        for (int? state = end; state is { } current; state = parents[current]) path.Add(current);
        path.Reverse(); path.Insert(0, initial);
        return new(path.ToArray(), best);
    }
    public int[] Route(int start, int destinationEdge)
    {
        var key = (start, destinationEdge);
        if (_cache.TryGetValue(key, out var cached))
            return (int[])cached.Clone();
        if (!_graph.ContainsKey(start))
            return Array.Empty<int>();
        var costs = new Dictionary<int, double> { { start, 0 } };
        var parents = new Dictionary<int, int>();
        // Preserve the reference heap's equal-cost ordering for deterministic route ties.
        var heap = new List<(double Cost, int State)> { (0, start) };
        while (heap.Count != 0)
        {
            var (cost, state) = Pop(heap);
            if (cost > costs.GetValueOrDefault(state, double.PositiveInfinity))
                continue;
            if (state / 2 == destinationEdge)
            {
                var path = new List<int> { state };
                while (parents.TryGetValue(state, out int parent))
                {
                    state = parent;
                    path.Add(state);
                }
                path.Reverse();
                var result = path.ToArray();
                if (_cache.Count >= 512)
                    _cache.Remove(_cacheOrder.Dequeue());
                _cache[key] = result;
                _cacheOrder.Enqueue(key);
                return (int[])result.Clone();
            }
            foreach (int neighbor in _graph[state].Next)
            {
                double candidate = cost + _graph[neighbor].Length;
                if (candidate >= costs.GetValueOrDefault(neighbor, double.PositiveInfinity))
                    continue;
                costs[neighbor] = candidate;
                parents[neighbor] = state;
                Push(heap, (candidate, neighbor));
            }
        }
        return Array.Empty<int>();
    }
    private static void Push(List<(double Cost, int State)> heap, (double Cost, int State) value)
    {
        heap.Add(value);
        int index = heap.Count - 1;
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (heap[parent].Cost <= value.Cost)
                break;
            heap[index] = heap[parent];
            index = parent;
        }
        heap[index] = value;
    }
    private static (double Cost, int State) Pop(List<(double Cost, int State)> heap)
    {
        var result = heap[0];
        var tail = heap[^1];
        heap.RemoveAt(heap.Count - 1);
        if (heap.Count == 0)
            return result;
        int index = 0;
        while (index * 2 + 1 < heap.Count)
        {
            int child = index * 2 + 1;
            if (child + 1 < heap.Count && heap[child + 1].Cost < heap[child].Cost)
                child++;
            if (tail.Cost <= heap[child].Cost)
                break;
            heap[index] = heap[child];
            index = child;
        }
        heap[index] = tail;
        return result;
    }
}
