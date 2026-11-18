using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class PlatformChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        var graph = new TrackNetwork();
        var generator = new TerrainGenerator();
        var camera = new WorldCamera();
        parent.AddChild(camera);
        camera.Position = new(130, 130, 180);
        camera.LookAt(new(0, 20, 0), Vector3.Up);
        var renderer = new TrackRenderer { Network = graph, Camera = camera };
        parent.AddChild(renderer);
        renderer.SetProcess(false);
        var layer = new CanvasLayer();
        parent.AddChild(layer);
        var windows = new WindowManager();
        layer.AddChild(windows);
        var editor = new TrackEditor { Network = graph, Renderer = renderer, Camera = camera, Generator = generator, Interface = layer, Windows = windows };
        parent.AddChild(editor);
        editor.SetProcess(false);
        editor.SetEnabled(true);
        editor.SetMode("station");
        try
        {
            var a = graph.AddNode(new(0, 20, -100));
            var b = graph.AddNode(new(0, 20, 100));
            var edge = graph.ConnectNodes(a, b)!;
            Vector2 Side(int sign) => camera.UnprojectPosition(new(sign * 3.5f, 20, 0));
            void Click(int sign) => editor._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Side(sign) });
            editor.RefreshPlatformHover(Side(1));
            var ghost = editor.PlatformPreview;
            check(ghost != null && editor.PlatformSide == 1 && edge.Station == 0 && edge.Platforms.Count == 0, "Raised-track platform preview follows the mouse side without mutating the graph");
            check(ghost?.Geometry.Surfaces.Count == 2 && ghost.Geometry.Surfaces.All(s => s.Name.StartsWith("Platform")) && ghost.Geometry.Sleepers.Count == 0, "Platform ghost contains only full-segment platform geometry");
            editor.RefreshPlatformHover(Side(1));
            check(ReferenceEquals(ghost, editor.PlatformPreview), "Hovering the same segment side reuses the platform preview");
            editor.RefreshPlatformHover(Side(-1));
            check(editor.PlatformSide == -1 && editor.PlatformPreview != null, "Crossing the track flips the preview side at rail height");
            Click(1);
            int station = edge.Station;
            check(station != 0 && edge.Platforms.SequenceEqual(new[] { 1 }), "Click builds a platform along the whole segment");
            editor.RefreshPlatformHover(Side(1));
            check(editor.PlatformPreview == null, "Existing platform side has no duplicate construction preview");
            Click(-1);
            check(edge.Station == station && edge.Platforms.Count == 2, "Opposite platform side shares the same station");
            renderer._Process(0);
            editor.SetEnabled(false);
            editor._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = camera.UnprojectPosition(new(3.5f, 20.45f, 0)) });
            check(editor.Stations.Views.TryGetValue(station, out var view) && view.Visible, "Clicking platform geometry opens its station window outside build mode");
            if (view != null)
            {
                check(view.Body.GetChildren().OfType<Label>().Any(l => l.Text == "Station length: 200.0 m"), "Station length does not double when both platform sides are present");
                view.Close();
            }
            editor.SetEnabled(true);
            editor.SetMode("station");
            editor.StationAction.Select(1);
            editor.RefreshPlatformHover(Side(1));
            var material = editor.PlatformPreview?.GetChild<MeshInstance3D>(0).MaterialOverride as StandardMaterial3D;
            check(material != null && material.AlbedoColor.R > material.AlbedoColor.G, "Removal preview uses the original red highlight");
            Click(1);
            check(edge.Station == station && edge.Platforms.SequenceEqual(new[] { -1 }), "Removing one side preserves the station and opposite platform");
            Click(-1);
            check(edge.Station == 0 && edge.Platforms.Count == 0 && view?.Visible != true, "Removing the last platform clears station membership and closes its window");
            var c = graph.AddNode(new(0, 20, 200));
            var continuation = graph.ConnectNodes(b, c)!;
            graph.AddStation(edge, 1);
            graph.AddStation(continuation, 1);
            check(continuation.Station == edge.Station, "Consecutive platforms attach to one station");
            var pa = graph.AddParallel(a, 1);
            var pb = graph.AddParallel(b, 1);
            check(Math.Abs(pa.Position.X - pb.Position.X) < .001f, "Station spacing is applied once even where two consecutive platform segments meet");
            var parallel = graph.ConnectNodes(pa, pb)!;
            graph.AddStation(parallel, -1);
            check(parallel.Station == edge.Station, "Parallel groups attach platforms to the same station");
            editor.Stations.Open(edge.Station);
            view = editor.Stations.Views[edge.Station];
            check(view.Body.GetChildren().OfType<Label>().Any(l => l.Text == "Station length: 200.0-300.0 m"), "Station window reports usable run lengths separately for parallel tracks: " + string.Join(", ", view.Body.GetChildren().OfType<Label>().Select(l => l.Text)));
            view.Close();
            graph.RemovePlatform(edge, 1);
            graph.RemovePlatform(continuation, 1);
            var branch = graph.AddNode(new(-80, 20, 170));
            graph.ConnectNodes(b, branch, 1);
            editor.StationAction.Select(0);
            editor.RefreshPlatformHover(Side(1));
            check(!graph.CanPlaceStation(edge, 1) && editor.PlatformPreview == null && !graph.AddStation(edge, 1), "Junction segments reject both preview and platform placement");
            editor.SetMode("track");
            check(editor.PlatformPreview == null, "Leaving station mode clears the ghost");
            editor.SetMode("station");
            camera.Position += new Vector3(1000, 0, 0);
            camera.LookAt(Vector3.Zero, Vector3.Up);
            editor.RefreshPlatformHover(camera.UnprojectPosition(graph.Point(parallel, .5f)));
            check(editor.PlatformPreview == null, "Distant tracks do not show platform previews");
            await frame();
        }
        finally { editor.Free(); renderer.Free(); layer.Free(); camera.Free(); generator.Backend.Shutdown(); generator.Backend.Dispose(); }
    }
}
