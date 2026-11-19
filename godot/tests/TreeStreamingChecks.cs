using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using TransportSandbox.Terrain;

namespace TransportSandbox.Tests;

public static class TreeStreamingChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        var root = new Node3D();
        parent.AddChild(root);
        var sampler = new TerrainSampler("the-long-way-home", 1800, 1, 0);
        using var stream = new TreeStreamer(root, sampler) { MaxOperationsPerFrame = 1, FrameBudgetMs = 100 };
        var source = TreeStreamingPerformance.Forest(256);
        var prepared = await Task.Run(() => TreeScenery.Prepare(source));
        var holders = Enumerable.Range(0, 4).Select(_ => new Node3D()).ToArray();
        foreach (var holder in holders)
            root.AddChild(holder);
        for (int i = 0; i < holders.Length; i++)
            stream.Activate(new(i, 0), holders[i], source, prepared, null, false);
        stream.Deactivate(new(3, 0));
        stream.Activate(new(3, 0), holders[3], source, prepared, null, false);
        stream.Pump(false);
        check(stream.Uploads == 1 && stream.LastOperations == 1, "Tree uploads obey the per-frame operation budget");
        for (int i = 0; i < 4; i++)
            stream.Pump(false);
        check(holders[3].GetNodeOrNull<TreeChunk>("Trees") != null, "Returning before the first upload still schedules tree creation");
        for (int i = 0; i < holders.Length; i++)
            stream.Deactivate(new(i, 0));
        long reusedUploads = stream.Uploads;
        for (int i = 0; i < holders.Length; i++)
            stream.Activate(new(i, 0), holders[i], source, prepared, null, false);
        check(holders.All(h => h.GetNodeOrNull<TreeChunk>("Trees") != null) && stream.Uploads == reusedUploads, "Multiple cached tree batches restore their public node paths without uploads");
        var first = holders[0].GetNode<TreeChunk>("Trees");
        long uploads = stream.Uploads;
        stream.Deactivate(new(0, 0));
        stream.Activate(new(0, 0), holders[1], source, prepared, null, true);
        check(ReferenceEquals(first.GetParent(), holders[1]) && !first.Visible && stream.Uploads == uploads, "Tree batches reattach without GPU uploads and preserve contour hiding");
        stream.Activate(new(0, 0), holders[0], source, prepared, null, false);
        check(first.Visible && ReferenceEquals(first.GetParent(), holders[0]), "Reusing trees restores visibility and the correct holder");
        var point = Vegetation.WorldPosition(sampler, 0, 0, source[0].X, source[0].Z);
        var field = new EarthworkField(new[] { new[] { new EarthPoint(point.X - 4, 100, point.Z), new EarthPoint(point.X + 4, 100, point.Z) } });
        stream.Activate(new(0, 0), holders[0], source, prepared, field, false);
        check(first.Visible, "Invalidated tree clearance retains the old batch while immutable work runs");
        var replacementHolder = new Node3D();
        root.AddChild(replacementHolder);
        stream.Deactivate(new(0, 0));
        stream.Activate(new(0, 0), replacementHolder, source, prepared, field, false);
        check(first.Visible && ReferenceEquals(first.GetParent(), replacementHolder), "Terrain replacement transfers the previous trees while clearance is pending");
        stream.MaxOperationsPerFrame = 0;
        stream.Pump(false);
        check(first.Visible && ReferenceEquals(replacementHolder.GetNodeOrNull<TreeChunk>("Trees"), first), "Deferred tree uploads keep the last committed mesh visible");
        stream.MaxOperationsPerFrame = 1;
        int turns = 0;
        while (stream.Pending > 0 && turns++ < 1000)
        {
            stream.Pump(false);
            await frame();
        }
        var filtered = replacementHolder.GetNode<TreeChunk>("Trees");
        check(filtered.Visible && (!GodotObject.IsInstanceValid(first) || !first.Visible) && filtered.Source.Length < source.Length && source.Length == 256, "Worker clearance swaps visible trees without changing natural records");
        stream.Activate(new(0, 0), holders[0], source, prepared, null, false);
        stream.Pump(false);
        check(holders[0].GetNode<TreeChunk>("Trees").Source.Length == 256, "Removing track clearance restores all original trees");
        stream.BudgetBytes = 0;
        for (int i = 0; i < holders.Length; i++)
            stream.Deactivate(new(i, 0));
        check(stream.CachedBytes == 0 && stream.Pending > 0, "Evicted tree resources enter the bounded destruction queue");
        stream.Pump(false);
        check(stream.LastOperations <= 1, "Tree destruction obeys the same per-frame operation budget");
        root.QueueFree();
        await frame();
    }
}
