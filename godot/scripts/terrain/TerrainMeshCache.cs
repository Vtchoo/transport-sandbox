using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public readonly record struct TerrainMeshKey(Vector2I Chunk, int Segments, long Revision);
public sealed record CachedTerrainMesh(ArrayMesh Mesh, TreeData[] Trees, long EstimatedBytes, PreparedTrees? Prepared = null);

/// <summary>Inactive meshes only. Taking an entry transfers ownership back to the visible world.</summary>
public sealed class TerrainMeshCache
{
    private readonly LinkedList<(TerrainMeshKey Key, CachedTerrainMesh Value)> _lru = new();
    private readonly Dictionary<TerrainMeshKey, LinkedListNode<(TerrainMeshKey Key, CachedTerrainMesh Value)>> _entries = new();
    private long _budget = 128L * 1024 * 1024;
    public long BudgetBytes
    {
        get => _budget; set
        {
            _budget = Math.Max(0, value);
            Trim();
        }
    }
    public int Count => _entries.Count;
    public long EstimatedBytes
    {
        get; private set;
    }
    public long Hits
    {
        get; private set;
    }
    public long Evictions
    {
        get; private set;
    }
    public IEnumerable<Vector2I> Chunks => _entries.Keys.Select(k => k.Chunk).Distinct();
    public void Store(TerrainMeshKey key, CachedTerrainMesh value)
    {
        if (_entries.TryGetValue(key, out var old))
            Remove(old, !ReferenceEquals(old.Value.Value.Mesh, value.Mesh));
        if (value.EstimatedBytes > _budget || _budget == 0)
        {
            value.Mesh.Dispose();
            return;
        }
        _entries[key] = _lru.AddLast((key, value));
        EstimatedBytes += value.EstimatedBytes;
        Trim();
    }
    public bool Contains(TerrainMeshKey key) => _entries.ContainsKey(key);
    public bool TryTake(TerrainMeshKey key, out CachedTerrainMesh value)
    {
        if (!_entries.TryGetValue(key, out var node))
        {
            value = null!;
            return false;
        }
        value = node.Value.Value;
        Remove(node);
        Hits++;
        return true;
    }
    public void AdvanceRevision(long revision, Predicate<Vector2I> affected)
    {
        // Unaffected geometry remains valid across edits; only affected chunks lose all LODs.
        foreach (var node in _lru.ToArray())
        {
            var linked = _entries[node.Key];
            if (affected(node.Key.Chunk))
                Remove(linked, true);
            else
            {
                _entries.Remove(node.Key);
                var key = node.Key with
                {
                    Revision = revision
                };
                linked.Value = (key, node.Value);
                _entries[key] = linked;
            }
        }
    }
    private void Remove(LinkedListNode<(TerrainMeshKey Key, CachedTerrainMesh Value)> node, bool dispose = false)
    {
        _entries.Remove(node.Value.Key);
        EstimatedBytes -= node.Value.Value.EstimatedBytes;
        _lru.Remove(node);
        if (dispose)
            node.Value.Value.Mesh.Dispose();
    }
    private void Trim()
    {
        while ((_entries.Count > 256 || EstimatedBytes > _budget) && _lru.First is { } first)
        {
            Remove(first, true);
            Evictions++;
        }
    }
    public void Clear()
    {
        foreach (var entry in _lru)
            entry.Value.Mesh.Dispose();
        _entries.Clear();
        _lru.Clear();
        EstimatedBytes = 0;
    }
}
