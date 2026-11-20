using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TransportSandbox.Terrain;

namespace TransportSandbox;

/// <summary>Debounced immutable earthwork snapshots. Workers never touch the graph or scene tree.</summary>
public partial class TrackTreeClearance : Node
{
    public TrackNetwork Network = null!;
    public TerrainWorld World = null!;
    private readonly CancellationTokenSource _shutdown = new();
    private sealed record Prepared(TrackVector[] Controls, EarthPoint[] Points);
    private sealed record Result(long Revision, EarthworkField? Field, EarthworkChanges? Changes, Dictionary<int, Prepared> Cache);
    private Dictionary<int, Prepared> _cache = new();
    private Dictionary<int, Prepared> _applied = new();
    private EarthworkField? _appliedField;
    private Task<Result>? _job;
    private long _revision;
    private double _remaining;
    private bool _dirty;
    public bool IsPending => _dirty || _job != null;
    public override void _Ready()
    {
        Network.Changed += Changed;
        Changed();
    }
    private void Changed()
    {
        _revision++;
        _dirty = true;
        _remaining = .4;
    }
    public static EarthPoint[] SamplePath(Vector3[] controls)
    {
        return SamplePath(controls.Select(TrackVector.From).ToArray());
    }
    public static EarthPoint[] SamplePath(TrackVector[] controls)
    {
        var curve = new TrackCurve(controls);
        int steps = Math.Max(2, (int)Math.Ceiling(curve.Length / 2));
        var points = new EarthPoint[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            var bed = curve.PointAt(t) + curve.FrameAt(t).Up * -.65;
            points[i] = new(bed.X, bed.Y + .65, bed.Z);
        }
        return points;
    }
    public override void _Process(double delta)
    {
        _remaining -= delta;
        if (_job is { IsCompleted: true })
        {
            if (_job.IsCompletedSuccessfully)
            {
                var result = _job.Result;
                _cache = result.Cache;
                if (result.Revision == _revision)
                {
                    if (result.Field != null) World.SetEarthworks(result.Field, result.Changes);
                    _applied = result.Cache;
                    if (result.Field != null) _appliedField = result.Field;
                }
            }
            else if (_job.IsFaulted)
                GD.PushError(_job.Exception!.ToString());
            _job = null;
        }
        if (!_dirty || _remaining > 0 || _job != null)
            return;
        _dirty = false;
        long revision = _revision;
        using var captureProbe = FrameProbe.Measure("earthworks.capture");
        var paths = Network.Edges.Values.Select(e => (e.Id, Controls: Network.WorldControls(e))).ToArray();
        var previous = _cache;
        var applied = _applied; var appliedField = _appliedField;
        var candidates = World.EarthworkCandidates().ToArray();
        var sampler = World.Generator.Sampler;
        var token = _shutdown.Token;
        _job = Task.Run(() =>
        {
            using var jobProbe = EarthworkProbe.Measure("preparation.total");
            var cache = new Dictionary<int, Prepared>();
            foreach (var (id, controls) in paths)
            {
                token.ThrowIfCancellationRequested();
                if (previous.TryGetValue(id, out var old) && old.Controls.SequenceEqual(controls)) cache[id] = old;
                else
                {
                    using var segmentProbe = EarthworkProbe.Measure("preparation.segment", id);
                    cache[id] = new(controls, EarthworkField.Prepare(SamplePath(controls), sampler.Height, sampler.MountainHeight, token));
                }
            }
            // Compare against the last APPLIED snapshot, not a discarded worker result.
            var changed = cache.Where(p => !applied.TryGetValue(p.Key, out var old) || !old.Controls.SequenceEqual(p.Value.Controls)).Select(p => p.Key).ToHashSet();
            changed.UnionWith(applied.Keys.Where(id => !cache.ContainsKey(id)));
            if (changed.Count == 0) return new Result(revision, null, null, cache);
            EarthworkField field;
            using (EarthworkProbe.Measure("preparation.field")) field = new EarthworkField(cache.Values.Select(p => p.Points));
            var changedPaths = changed.SelectMany(id => new[] { applied.GetValueOrDefault(id)?.Points, cache.GetValueOrDefault(id)?.Points })
                .Where(points => points != null).Select(points => points!);
            var changes = new EarthworkChanges(changedPaths, Math.Max(appliedField?.InfluenceWidth ?? 0, field.InfluenceWidth));
            using (EarthworkProbe.Measure("preparation.dirty-chunks")) changes.Resolve(candidates, appliedField, field, sampler.Height);
            return new Result(revision, field, changes, cache);
        }, token);
    }
    public override void _ExitTree()
    {
        Network.Changed -= Changed;
        _shutdown.Cancel();
        try
        {
            _job?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { GD.PushError(error.ToString()); }
        finally { _shutdown.Dispose(); }
    }
}
