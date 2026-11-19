using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Field = TransportSandbox.RailRoutingGraph.RailDestinationField;

namespace TransportSandbox;

public readonly record struct RailRoutingStats(int Revision, bool BuildingGraph, int ActiveWorkers, int Queued,
    int Cached, long CacheBytes, long Searches, long CacheHits, double LastBuildMs, int IntegratedEdges);

/// <summary>Main-thread scheduler. Workers only read immutable snapshots; stale work is canceled and discarded.</summary>
public sealed class RailPathfinder : IDisposable
{
    private sealed record Cached(Field Field, long Used);
    private sealed record SearchJob(int Revision, Task<Field> Task);
    private readonly record struct Request(RailDestination Destination, RailRoutingPolicy Policy);
    private readonly Dictionary<Request, Cached> _cache = new();
    private readonly Dictionary<Request, SearchJob> _jobs = new();
    private readonly PriorityQueue<(Request Key, int Priority), (int Priority, long Sequence)> _queue = new();
    private readonly Dictionary<Request, int> _queued = new();
    private CancellationTokenSource _cancel = new();
    private readonly List<CancellationTokenSource> _retiredTokens = new();
    private Task<(int Revision, RailRoutingGraph Graph, double Ms)>? _build;
    private RailRoutingEdge[]? _pending;
    private RailRoutingGraph? _graph, _lengthSource;
    private bool _disposed;
    private long _clock, _queueClock, _bytes, _searches, _hits;
    private double _buildMs;
    public int Revision { get; private set; } = -1;
    public int Workers { get; }
    public int MaxQueued { get; }
    public int MaxCached { get; }
    public long CacheBudget { get; }
    public bool Ready => _graph != null;
    public RailRoutingStats Stats => new(Revision, _build != null || _pending != null, _jobs.Count + (_build != null ? 1 : 0), _queued.Count,
        _cache.Count, _bytes, _searches, _hits, _buildMs, _graph?.IntegratedEdges ?? 0);
    public RailPathfinder(int workers = 0, int maxQueued = 128, int maxCached = 64, long cacheBudget = 64L * 1024 * 1024)
    {
        Workers = workers > 0 ? workers : Math.Clamp(Environment.ProcessorCount - 2, 1, 2);
        MaxQueued = Math.Max(1, maxQueued); MaxCached = Math.Max(1, maxCached); CacheBudget = Math.Max(1, cacheBudget);
    }
    public void Invalidate(int revision)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Revision = revision; _cancel.Cancel(); _retiredTokens.Add(_cancel); _cancel = new();
        _lengthSource = _graph ?? _lengthSource;
        _pending = null; _graph = null; _cache.Clear(); _queue.Clear(); _queued.Clear(); _bytes = 0;
    }
    public void SetSnapshot(int revision, RailRoutingEdge[] edges)
    {
        if (revision != Revision) Invalidate(revision);
        // The caller transfers ownership of this array and never mutates it afterwards.
        _pending = edges;
        Pump();
    }
    public bool TryGet(RailDestination destination, out Field? field, bool urgent = false, RailRoutingPolicy? policy = null)
    {
        field = null;
        var key = new Request(destination, policy ?? RailRoutingPolicy.Empty);
        if (_cache.TryGetValue(key, out var cached))
        {
            _cache[key] = cached with { Used = ++_clock }; _hits++; field = cached.Field; return true;
        }
        int priority = urgent ? 0 : 1;
        if (_graph != null && !_jobs.ContainsKey(key) &&
            (_queued.TryGetValue(key, out int existing) ? priority < existing : _queued.Count < MaxQueued))
        {
            _queued[key] = priority;
            _queue.Enqueue((key, priority), (priority, ++_queueClock));
        }
        return false;
    }
    public void Pump()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var (destination, job) in _jobs.ToArray())
        {
            if (!job.Task.IsCompleted) continue;
            _jobs.Remove(destination);
            if (job.Task.IsFaulted) throw new InvalidOperationException("Rail pathfinding worker failed.", job.Task.Exception);
            if (job.Task.IsCanceled || job.Revision != Revision) continue;
            var field = job.Task.Result;
            // Oversized fields can serve callers while retained singly; never accumulate over budget.
            while (_cache.Count > 0 && (_cache.Count >= MaxCached || _bytes + field.Bytes > CacheBudget))
            {
                var oldest = _cache.MinBy(p => p.Value.Used);
                _bytes -= oldest.Value.Field.Bytes; _cache.Remove(oldest.Key);
            }
            _cache[destination] = new(field, ++_clock); _bytes += field.Bytes;
        }
        if (_build?.IsCompleted == true)
        {
            var finished = _build; _build = null;
            if (finished.IsFaulted) throw new InvalidOperationException("Rail graph worker failed.", finished.Exception);
            if (finished.IsCompletedSuccessfully && finished.Result.Revision == Revision)
            { _graph = finished.Result.Graph; _lengthSource = _graph; _buildMs = finished.Result.Ms; }
        }
        if (_build == null && _jobs.Count == 0)
        {
            foreach (var token in _retiredTokens) token.Dispose(); _retiredTokens.Clear();
            if (_pending != null)
            {
                var input = _pending; _pending = null; int revision = Revision; var token = _cancel.Token; var reuse = _lengthSource;
                _build = Task.Run(() =>
                {
                    var timer = Stopwatch.StartNew(); var graph = RailRoutingGraph.Build(input, token, reuse);
                    return (revision, graph, timer.Elapsed.TotalMilliseconds);
                }, token);
            }
        }
        if (_graph == null || _build != null) return;
        while (_jobs.Count < Workers && _queue.TryDequeue(out var request, out _))
        {
            var destination = request.Key;
            if (!_queued.TryGetValue(destination, out int priority) || priority != request.Priority) continue;
            _queued.Remove(destination);
            var graph = _graph; var token = _cancel.Token;
            _jobs.Add(destination, new(Revision, Task.Run(() => graph.Search(destination.Destination, token, destination.Policy), token))); _searches++;
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _cancel.Cancel();
        var tasks = _jobs.Values.Select(j => (Task)j.Task).ToList(); if (_build != null) tasks.Add(_build);
        var tokens = _retiredTokens.Append(_cancel).ToArray();
        // Observe faults and release tokens after workers exit, without blocking scene shutdown.
        _ = Task.WhenAll(tasks).ContinueWith(done => { _ = done.Exception; foreach (var token in tokens) token.Dispose(); }, TaskScheduler.Default);
        _cache.Clear(); _queue.Clear(); _queued.Clear(); _graph = null; _lengthSource = null; _pending = null;
    }
}
