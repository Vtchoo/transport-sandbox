using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Field = TransportSandbox.RailRoutingGraph.RailDestinationField;

namespace TransportSandbox;

public readonly record struct RailRoutingStats(int Revision, bool BuildingGraph, int ActiveWorkers, int Queued,
    int Cached, long CacheBytes, long Searches, long CacheHits, double LastBuildMs, int IntegratedEdges,
    int Components = 0, int ReusedComponents = 0);

/// <summary>Main-thread scheduler over immutable, independently reusable connected components.</summary>
public sealed class RailPathfinder : IDisposable
{
    private sealed record Cached(Field Field, long Used);
    private sealed record SearchJob(Task<Field> Task, CancellationTokenSource Cancel);
    private readonly record struct Request(RailRoutingGraph Graph, RailDestination Destination, RailRoutingPolicy Policy);
    private readonly Dictionary<Request, Cached> _cache = new();
    private readonly Dictionary<Request, SearchJob> _jobs = new();
    private readonly PriorityQueue<(Request Key, int Priority), (int Priority, long Sequence)> _queue = new();
    private readonly Dictionary<Request, int> _queued = new();
    private readonly Queue<(int From, int To, int[] Edges)> _changes = new();
    private CancellationTokenSource? _buildCancel;
    private Task<(int Revision, RailRoutingSnapshot Snapshot, double Ms)>? _build;
    private RailRoutingEdge[]? _pending;
    private RailRoutingSnapshot? _snapshot;
    private int _publishedRevision = -1, _historyEdges;
    private bool _disposed;
    private long _clock, _queueClock, _bytes, _searches, _hits;
    private double _buildMs;
    public int Revision { get; private set; } = -1;
    public int PublishedRevision => _publishedRevision;
    public int Workers { get; }
    public int MaxQueued { get; }
    public int MaxCached { get; }
    public long CacheBudget { get; }
    public bool Ready => _snapshot != null && _publishedRevision == Revision;
    public RailRoutingStats Stats => new(Revision, _build != null || _pending != null, _jobs.Count + (_build != null ? 1 : 0), _queued.Count,
        _cache.Count, _bytes, _searches, _hits, _buildMs, _snapshot?.IntegratedEdges ?? 0,
        _snapshot?.ComponentCount ?? 0, _snapshot?.ReusedComponents ?? 0);
    public RailPathfinder(int workers = 0, int maxQueued = 128, int maxCached = 64, long cacheBudget = 64L * 1024 * 1024)
    {
        Workers = workers > 0 ? workers : Math.Clamp(Environment.ProcessorCount - 2, 1, 2);
        MaxQueued = Math.Max(1, maxQueued); MaxCached = Math.Max(1, maxCached); CacheBudget = Math.Max(1, cacheBudget);
    }
    public void Invalidate(int revision)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Revision = revision; _buildCancel?.Cancel(); _pending = null;
        // Installed journeys and component caches remain usable until the replacement is published.
    }
    public void SetSnapshot(int revision, RailRoutingEdge[] edges)
    {
        if (revision != Revision) Invalidate(revision);
        // The caller transfers ownership of this array and never mutates it afterwards.
        _pending = edges;
        Pump();
    }
    public long ComponentVersion(int edge) => _snapshot?.ForEdge(edge).Version ?? -1;
    public int ConflictCount(int edge) => _snapshot?.ConflictCount(edge) ?? 0;
    public bool AffectsSince(int revision, HashSet<int> dependencies)
    {
        if (revision == _publishedRevision) return false;
        int cursor = revision;
        foreach (var change in _changes)
        {
            if (change.To <= cursor) continue;
            if (change.From != cursor || dependencies.Overlaps(change.Edges)) return true;
            cursor = change.To;
        }
        return cursor != _publishedRevision; // History evicted: conservatively replan before moving.
    }
    public bool TryGet(RailDestination destination, out Field? field, bool urgent = false, RailRoutingPolicy? policy = null, int startEdge = 0)
    {
        field = null;
        if (!Ready) return false;
        var key = new Request(_snapshot!.ForEdge(startEdge), destination, policy ?? RailRoutingPolicy.Empty);
        if (_cache.TryGetValue(key, out var cached))
        {
            _cache[key] = cached with { Used = ++_clock }; _hits++; field = cached.Field; return true;
        }
        int priority = urgent ? 0 : 1;
        if (!_jobs.ContainsKey(key) &&
            (_queued.TryGetValue(key, out int existing) ? priority < existing : _queued.Count < MaxQueued))
        {
            _queued[key] = priority;
            _queue.Enqueue((key, priority), (priority, ++_queueClock));
        }
        return false;
    }
    private void Publish(RailRoutingSnapshot snapshot, int revision)
    {
        _changes.Enqueue((_publishedRevision, revision, snapshot.ChangedEdges));
        _historyEdges += snapshot.ChangedEdges.Length;
        while (_changes.Count > 32 || _historyEdges > 100000)
            _historyEdges -= _changes.Dequeue().Edges.Length;
        _snapshot = snapshot; _publishedRevision = revision;
        foreach (var key in _cache.Keys.Where(key => !snapshot.Contains(key.Graph)).ToArray())
        { _bytes -= _cache[key].Field.Bytes; _cache.Remove(key); }
        foreach (var (key, job) in _jobs)
            if (!snapshot.Contains(key.Graph)) job.Cancel.Cancel();
        foreach (var key in _queued.Keys.Where(key => !snapshot.Contains(key.Graph)).ToArray()) _queued.Remove(key);
        _queue.Clear();
        foreach (var (key, priority) in _queued) _queue.Enqueue((key, priority), (priority, ++_queueClock));
    }
    public void Pump()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var (key, job) in _jobs.ToArray())
        {
            if (!job.Task.IsCompleted) continue;
            _jobs.Remove(key); job.Cancel.Dispose();
            if (job.Task.IsFaulted) throw new InvalidOperationException("Rail pathfinding worker failed.", job.Task.Exception);
            if (job.Task.IsCanceled || _snapshot?.Contains(key.Graph) != true) continue;
            var field = job.Task.Result;
            while (_cache.Count > 0 && (_cache.Count >= MaxCached || _bytes + field.Bytes > CacheBudget))
            {
                var oldest = _cache.MinBy(p => p.Value.Used);
                _bytes -= oldest.Value.Field.Bytes; _cache.Remove(oldest.Key);
            }
            _cache[key] = new(field, ++_clock); _bytes += field.Bytes;
        }
        if (_build?.IsCompleted == true)
        {
            var finished = _build; _build = null; _buildCancel!.Dispose(); _buildCancel = null;
            if (finished.IsFaulted) throw new InvalidOperationException("Rail graph worker failed.", finished.Exception);
            if (finished.IsCompletedSuccessfully && finished.Result.Revision == Revision)
            { Publish(finished.Result.Snapshot, Revision); _buildMs = finished.Result.Ms; }
        }
        if (_build == null && _pending != null && _jobs.Count < Workers)
        {
            var input = _pending; _pending = null; int revision = Revision;
            _buildCancel = new(); var token = _buildCancel.Token; var reuse = _snapshot;
            _build = Task.Run(() =>
            {
                var timer = Stopwatch.StartNew(); var snapshot = RailRoutingSnapshot.Build(input, reuse, token);
                return (revision, snapshot, timer.Elapsed.TotalMilliseconds);
            }, token);
        }
        if (!Ready || _build != null || _pending != null) return;
        while (_jobs.Count < Workers && _queue.TryDequeue(out var request, out _))
        {
            var key = request.Key;
            if (!_queued.TryGetValue(key, out int priority) || priority != request.Priority) continue;
            _queued.Remove(key);
            var cancel = new CancellationTokenSource(); var token = cancel.Token;
            _jobs.Add(key, new(Task.Run(() => key.Graph.Search(key.Destination, token, key.Policy), token), cancel)); _searches++;
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _buildCancel?.Cancel();
        foreach (var job in _jobs.Values) job.Cancel.Cancel();
        var tasks = _jobs.Values.Select(j => (Task)j.Task).ToList(); if (_build != null) tasks.Add(_build);
        var tokens = _jobs.Values.Select(j => j.Cancel).ToList(); if (_buildCancel != null) tokens.Add(_buildCancel);
        _ = Task.WhenAll(tasks).ContinueWith(done => { _ = done.Exception; foreach (var token in tokens) token.Dispose(); }, TaskScheduler.Default);
        _cache.Clear(); _queue.Clear(); _queued.Clear(); _changes.Clear(); _snapshot = null; _pending = null;
    }
}
