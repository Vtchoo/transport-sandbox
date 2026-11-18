using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

/// <summary>Directed-edge Dijkstra. Rebuild on the main thread; each worker owns its router/cache.</summary>
public sealed class RailRouter
{
    private sealed record State(double Length, int[] Next);
    private Dictionary<int, State> _graph = new();
    private readonly Dictionary<(int, int), int[]> _cache = new();
    private readonly Queue<(int, int)> _cacheOrder = new();
    public void Rebuild(TrackNetwork network)
    {
        var graph = new Dictionary<int, State>();
        foreach (var edge in network.Edges.Values)
        {
            using var curve = network.Curve(edge);
            double length = curve.GetBakedLength();
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
