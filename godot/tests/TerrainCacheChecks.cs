using Godot;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using TransportSandbox.Terrain;

namespace TransportSandbox.Tests;

public static class TerrainCacheChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        var cache = new TerrainMeshCache { BudgetBytes = 20 };
        CachedTerrainMesh Entry() => new(new ArrayMesh(), Array.Empty<TreeData>(), 10);
        var a = Entry();
        var b = Entry();
        var c = Entry();
        cache.Store(new(new(0, 0), 16, 0), a);
        cache.Store(new(new(1, 0), 16, 0), b);
        check(cache.TryTake(new(new(0, 0), 16, 0), out var taken) && ReferenceEquals(taken, a), "Cache transfers original mesh ownership");
        cache.Store(new(new(0, 0), 16, 0), a);
        cache.Store(new(new(2, 0), 16, 0), c);
        check(!cache.TryTake(new(new(1, 0), 16, 0), out _) && cache.EstimatedBytes == 20, "Cache evicts least recently used mesh within budget");
        cache.AdvanceRevision(1, k => k.X == 0);
        check(!cache.TryTake(new(new(0, 0), 16, 1), out _) && cache.TryTake(new(new(2, 0), 16, 1), out var kept), "Affected LODs invalidate; unrelated meshes retain validity");
        c.Mesh.Dispose();
        cache.Clear();
        var generator = new TerrainGenerator();
        var camera = new Camera3D { Position = new(600, 1000, 600) };
        parent.AddChild(camera);
        var world = new TerrainWorld { Generator = generator, Camera = camera };
        parent.AddChild(world);
        world.SetProcess(false);
        world._Process(0);
        string stage = "initial jobs";
        async Task Drain()
        {
            var clock = Stopwatch.StartNew();
            while (world.ActiveJobs > 0 && clock.Elapsed.TotalSeconds < 15)
            {
                await frame();
                world._Process(0);
            }
            check(world.ActiveJobs == 0, $"Cache test worker queue drains ({stage}, {world.ActiveJobs} active, {world.SubmittedJobs} submitted, {world.Wanted.Count} wanted)");
        }
        async Task Want(int segments)
        {
            stage = $"LOD {segments}";
            world.SetWantedTiles(new[] { new TerrainTile(new(1, 1), segments, 0) });
            world._Process(0);
            await Drain();
        }
        world.SetWantedTiles(Array.Empty<TerrainTile>());
        await Drain();
        world.MeshCache.Clear();
        try
        {
            await Want(16);
            var first = world.Chunks[new(1, 1)].GetChild<MeshInstance3D>(0).Mesh;
            await Want(32);
            long jobs = world.SubmittedJobs;
            await Want(16);
            check(world.SubmittedJobs == jobs && ReferenceEquals(first, world.Chunks[new(1, 1)].GetChild<MeshInstance3D>(0).Mesh), "Returning to a cached LOD reuses the uploaded mesh without generation");
            world.SetWantedTiles(Array.Empty<TerrainTile>());
            await Want(16);
            check(world.SubmittedJobs == jobs, "Returning to unloaded cached terrain avoids worker jobs");
            world.ShowContours(true);
            await Want(32);
            check(world.Chunks[new(1, 1)].GetNodeOrNull<TreeChunk>("Trees") is not { Visible: true }, "Cached terrain respects current contour visibility");
            world.SetEarthworks(new EarthworkField(Array.Empty<EarthPoint[]>()));
            await Want(16);
            check(world.SubmittedJobs == jobs, "Unrelated earthwork revisions preserve cached meshes");
            // Exercise invalidation near the actual ground, rather than spending the
            // timeout excavating an entire mountainous chunk down to a fixed 40 m.
            double height = generator.Sampler.Height(575, 650) + 5;
            var path = EarthworkField.Prepare(new[] { new EarthPoint(550, height, 650), new EarthPoint(600, height, 650) }, generator.Sampler.Height, 1800);
            world.SetEarthworks(new EarthworkField(new[] { path }));
            check(world.MeshCache.Count == 0, "Earthwork edits invalidate every cached LOD for affected chunks");
            // A revision change while this job runs must not cache its older output.
            world._Process(0);
            world.SetEarthworks(new EarthworkField(Array.Empty<EarthPoint[]>()));
            stage = "superseded earthworks";
            await Drain();
            check(!world.MeshCache.TryTake(new(new(1, 1), 32, 0), out _), "Superseded worker output cannot restore stale terrain");
            await Want(64);
            var treeVisual = world.Chunks[new(1, 1)].GetNodeOrNull<TreeChunk>("Trees");
            long generations = generator.Backend.VegetationGenerations, treeUploads = world.Trees.Uploads;
            await Want(128);
            check(generator.Backend.VegetationGenerations == generations, "Changing terrain LOD reuses worker vegetation data instead of recalculating placement");
            check(ReferenceEquals(treeVisual, world.Chunks[new(1, 1)].GetNodeOrNull<TreeChunk>("Trees")) && world.Trees.Uploads == treeUploads, "Near terrain LOD swaps preserve the existing uploaded tree batches");
            world.SetWantedTiles(Array.Empty<TerrainTile>());
            await Want(64);
            check(world.Trees.Uploads == treeUploads && generator.Backend.VegetationGenerations == generations, "Returning to cached terrain restores trees without placement calculations or GPU uploads");
            world.SetWantedTiles(Array.Empty<TerrainTile>());
            world.MeshCache.BudgetBytes = 0;
            check(world.MeshCache.Count == 0 && world.MeshCache.EstimatedBytes == 0, "Cache can release all retained mesh memory");
        }
        finally { world.Free(); camera.Free(); generator.Backend.Dispose(); }
    }
}
