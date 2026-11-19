using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TransportSandbox.Terrain;

namespace TransportSandbox;

/// <summary>Retains uploaded trees across terrain LODs; prepares immutable data off-thread and budgets scene work.</summary>
public sealed class TreeStreamer : IDisposable
{
    private sealed class Entry
    {
        public Node3D? Holder;
        public TreeChunk? Visual;
        public TreeData[] Natural = Array.Empty<TreeData>();
        public PreparedTrees? NaturalPrepared, Ready;
        public EarthworkField? Field;
        public long Version, VisualVersion, Used;
        public bool Dirty;
        public long Bytes;
    }
    private sealed record Work(Vector2I Key, long Version, Task<PreparedTrees> Task);
    private readonly Dictionary<Vector2I, Entry> _entries = new();
    private readonly List<Work> _jobs = new();
    private readonly Queue<TreeChunk> _retired = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Node3D _cacheRoot = new() { Name = "TreeCache", Visible = false };
    private readonly TerrainSampler _sampler;
    private long _clock, _version;
    public long BudgetBytes = 32L * 1024 * 1024;
    public int MaxOperationsPerFrame = 2;
    public double FrameBudgetMs = 2;
    public long Uploads
    {
        get; private set;
    }
    public long Reuses
    {
        get; private set;
    }
    public double LastPumpMs
    {
        get; private set;
    }
    public int LastOperations
    {
        get; private set;
    }
    public int Pending => _jobs.Count + _retired.Count + _entries.Values.Count(e => e.Holder != null && (e.Dirty || e.Ready != null));
    public long CachedBytes => _entries.Values.Where(e => e.Holder == null).Sum(e => e.Bytes);
    public event Action<Vector2I, TreeData[]>? Committed;
    public TreeStreamer(Node parent, TerrainSampler sampler)
    {
        _sampler = sampler;
        parent.AddChild(_cacheRoot);
    }
    public void Activate(Vector2I key, Node3D holder, TreeData[] natural, PreparedTrees? prepared, EarthworkField? field, bool hidden)
    {
        if (!_entries.TryGetValue(key, out var entry))
            _entries[key] = entry = new();
        bool same = ReferenceEquals(entry.Natural, natural) || entry.Natural.AsSpan().SequenceEqual(natural);
        bool changed = !same || !ReferenceEquals(entry.Field, field);
        entry.Holder = holder;
        entry.Used = ++_clock;
        if (changed)
        {
            entry.Version = ++_version;
            entry.Natural = natural;
            entry.NaturalPrepared = prepared;
            entry.Bytes = prepared?.EstimatedBytes ?? natural.LongLength * 28;
            entry.Field = field;
            entry.Dirty = true;
            entry.Ready = null;
        }
        // Terrain holders can be replaced before clearance work or an upload finishes.
        // Transfer the last committed visual immediately; replace it only when ready.
        if (entry.Visual != null)
        {
            Attach(entry.Visual, holder);
            entry.Visual.Visible = !hidden;
            if (!changed && entry.VisualVersion == entry.Version)
                Reuses++;
            Committed?.Invoke(key, entry.Visual.Source);
        }
        if (entry.Dirty && field == null && prepared != null)
        {
            entry.Ready = prepared;
            entry.Dirty = false;
        }
        // Empty far-LOD data is handled by Deactivate, never replacing cached natural trees.
    }
    public void Deactivate(Vector2I key)
    {
        if (!_entries.TryGetValue(key, out var entry))
            return;
        entry.Holder = null;
        entry.Used = ++_clock;
        if (entry.Ready != null || entry.Visual == null)
            entry.Dirty = true;
        entry.Ready = null;
        if (entry.Visual != null)
        {
            Attach(entry.Visual, _cacheRoot);
            entry.Visual.Hide();
        }
        Trim();
    }
    private static void Attach(Node node, Node parent)
    {
        if (node.GetParent() == parent)
            return;
        node.GetParent()?.RemoveChild(node);
        // Cached siblings receive generated names; restore the public path on reattachment.
        if (node is TreeChunk)
            node.Name = "Trees";
        parent.AddChild(node);
    }
    private void Trim()
    {
        long bytes = CachedBytes;
        var inactive = _entries.Where(p => p.Value.Holder == null).OrderBy(p => p.Value.Used).ToArray();
        int count = inactive.Length;
        foreach (var (key, entry) in inactive)
        {
            if (bytes <= BudgetBytes && count <= 128)
                break;
            _entries.Remove(key);
            bytes -= entry.Bytes;
            count--;
            if (entry.Visual != null)
                _retired.Enqueue(entry.Visual);
        }
    }
    public void Pump(bool hidden)
    {
        var timer = Stopwatch.StartNew();
        LastOperations = 0;
        foreach (var work in _jobs.Where(j => j.Task.IsCompleted).ToArray())
        {
            _jobs.Remove(work);
            if (work.Task.IsFaulted)
            {
                GD.PushError(work.Task.Exception!.ToString());
                continue;
            }
            if (work.Task.IsCanceled || !_entries.TryGetValue(work.Key, out var entry) || entry.Version != work.Version)
                continue;
            if (entry.Holder != null)
            {
                entry.Ready = work.Task.Result;
                entry.Dirty = false;
            }
            else
                entry.Dirty = true;
        }
        foreach (var (key, entry) in _entries)
        {
            if (_jobs.Count >= 2)
                break;
            if (entry.Holder == null || !entry.Dirty || _jobs.Any(j => j.Key == key))
                continue;
            var natural = entry.Natural;
            var prepared = entry.NaturalPrepared;
            var field = entry.Field;
            var token = _shutdown.Token;
            entry.Dirty = false;
            _jobs.Add(new(key, entry.Version, Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (field == null)
                    return prepared ?? TreeScenery.Prepare(natural);
                var filtered = natural.Where(tree =>
                {
                    token.ThrowIfCancellationRequested();
                    var point = Vegetation.WorldPosition(_sampler, key.X, key.Y, tree.X, tree.Z);
                    return !field.ClearsTree(point.X, point.Z, tree.Y);
                }).ToArray();
                return prepared != null && filtered.Length == natural.Length ? prepared : TreeScenery.Prepare(filtered);
            }, token)));
        }
        // QueueFree batches destruction at the end of the frame. Submit only bounded batches.
        while (_retired.Count > 0 && LastOperations < MaxOperationsPerFrame && timer.Elapsed.TotalMilliseconds < FrameBudgetMs)
        {
            _retired.Dequeue().QueueFree();
            LastOperations++;
        }
        foreach (var (key, entry) in _entries)
        {
            if (LastOperations >= MaxOperationsPerFrame || timer.Elapsed.TotalMilliseconds >= FrameBudgetMs)
                break;
            if (entry.Holder == null || entry.Ready is not { } ready)
                continue;
            if (entry.Visual != null && entry.Visual.Source.AsSpan().SequenceEqual(ready.Trees))
            {
                Attach(entry.Visual, entry.Holder);
                entry.Visual.Visible = !hidden;
                Reuses++;
            }
            else
            {
                var replacement = TreeScenery.Create(ready);
                replacement.Visible = !hidden;
                if (entry.Visual != null)
                {
                    Attach(entry.Visual, _cacheRoot);
                    entry.Visual.Hide();
                    _retired.Enqueue(entry.Visual);
                }
                entry.Visual = replacement;
                entry.Holder.AddChild(entry.Visual);
                Uploads++;
            }
            entry.Bytes = ready.EstimatedBytes + (ReferenceEquals(ready, entry.NaturalPrepared) ? 0 : entry.NaturalPrepared?.EstimatedBytes ?? entry.Natural.LongLength * 28);
            entry.Ready = null;
            entry.VisualVersion = entry.Version;
            Committed?.Invoke(key, ready.Trees);
            LastOperations++;
        }
        LastPumpMs = timer.Elapsed.TotalMilliseconds;
    }
    public void Dispose()
    {
        _shutdown.Cancel();
        foreach (var job in _jobs)
        {
            try
            {
                job.Task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { GD.PushError(error.ToString()); }
        }
        _jobs.Clear();
        _entries.Clear();
        _retired.Clear();
        _shutdown.Dispose();
        // Nodes belong to the world/cache root and are freed with that scene.
    }
}
