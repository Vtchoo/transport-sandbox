using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class GridSpacingChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        var graph = new TrackNetwork();
        var generator = new TerrainGenerator();
        var camera = new WorldCamera();
        parent.AddChild(camera);
        camera.Position = new(110, 120, 170);
        camera.LookAt(Vector3.Zero, Vector3.Up);
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
        void Shift(bool pressed) => Input.ParseInputEvent(new InputEventKey { Keycode = Key.Shift, PhysicalKeycode = Key.Shift, Pressed = pressed });
        try
        {
            check(!editor.Grid && editor.GridPicker.ItemCount == 5 && !editor.GridOverlay.Visible, "World grid starts off with the original five choices");
            editor.SetGridSpacing(10);
            editor._UnhandledInput(new InputEventKey { Keycode = Key.G, Pressed = true });
            check(!editor.Grid && editor.GridPicker.Selected == 0, "G disables grid snapping and synchronizes its selector");
            editor._UnhandledInput(new InputEventKey { Keycode = Key.G, Pressed = true });
            check(editor.GridOverlay.Spacing == 10 && editor.GridPicker.GetSelectedId() == 10, "G restores the last selected spacing");
            foreach (int spacing in new[] { 1, 5, 10, 25 })
            {
                editor.SetGridSpacing(spacing);
                var point = editor.GridOverlay.Snap(new(spacing * -1.5f, 7.25f, spacing * 2.5f));
                check(point == new Vector3(-spacing, 7.25f, 3 * spacing), "World snap preserves height and matches JavaScript rounding at positive and negative ties");
            }
            editor.SetGridSpacing(5);
            editor.GridOverlay.ShowAt(new(12, 8, -12), true);
            check(editor.GridOverlay.Visible && editor.GridOverlay.Position == new Vector3(10, 8, -10) && editor.GridOverlay.Rotation == Vector3.Zero, "Grid overlay centers on world-axis snap coordinates without changing height");
            editor.SetMode("station");
            check(!editor.GridOverlay.Visible && editor.GridOverlay.Spacing == 5, "Station tools hide the grid without losing the selected spacing");
            editor.SetMode("track");

            var rawPoint = camera.GroundPoint(generator) + Vector3.Up * .2f;
            var start = graph.AddNode(rawPoint + new Vector3(13, 4, 0));
            start.Axis = Vector3.Right;
            editor.BeginFrom(start.Id, 1);
            Shift(true);
            check(editor.ConstrainPlacementPoint(rawPoint, true).DistanceTo(start.Position) < .001f, "Shift straight snap takes priority over the grid and cannot extend behind the chosen outgoing side");
            Shift(false);
            check(editor.PlacementPoint() == editor.GridOverlay.Snap(rawPoint), "Releasing Shift restores world grid snapping without resampling height");
            editor.Cancel();
            graph.DeleteNode(start.Id);

            var a = graph.AddNode(new(0, 0, -80));
            var b = graph.AddNode(new(0, 0, 80));
            var main = graph.ConnectNodes(a, b)!;
            var pa = graph.AddParallel(a, 1);
            var pb = graph.AddParallel(b, 1);
            var other = graph.ConnectNodes(pa, pb)!;
            renderer.Selected = a.Id;
            renderer._Process(0);
            check(renderer.SpacingHandleNodes().SequenceEqual(new[] { pa.Id }), "Selecting a group anchor exposes its non-anchor spacing handle");
            var handle = renderer.SpacingPosition(pa);
            var screen = camera.UnprojectPosition(handle);
            check(Math.Abs(screen.DistanceTo(camera.UnprojectPosition(pa.Position)) - 25) < 3 && editor.PickSpacing(screen) == pa.Id, "Purple handle is pickable about 25 pixels outward from the node");
            camera.Position *= 1.5f;
            camera.LookAt(Vector3.Zero, Vector3.Up);
            renderer._Process(0);
            handle = renderer.SpacingPosition(pa);
            screen = camera.UnprojectPosition(handle);
            check(Math.Abs(screen.DistanceTo(camera.UnprojectPosition(pa.Position)) - 25) < 3, "Spacing handle remains reachable after camera zoom");
            editor.SetGridSpacing(25);
            var original = pa.Position;
            var anchor = a.Position;
            var offset = graph.Groups[a.Group].Offsets[pa.Id];
            editor._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = screen });
            Shift(true);
            editor._Input(new InputEventMouseMotion { Position = camera.UnprojectPosition(handle + new Vector3(2.3f, 0, 5)), ShiftPressed = true });
            renderer._Process(0);
            check(Math.Abs(graph.Groups[a.Group].Offsets[pa.Id] - offset - 2.3f) < .01f && pa.Position.Y == original.Y && pa.Position.Z == original.Z && a.Position == anchor, "Spacing drag moves only that node laterally and ignores Shift/grid snapping");
            check(renderer.Holders[other.Id].Geometry.Sleepers.Count == 0 && renderer.Holders[main.Id].Geometry.Sleepers.Count > 0, "Spacing drag simplifies only the moved node's connected tracks");
            editor._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            Shift(false);
            renderer._Process(0);
            check(renderer.Holders[other.Id].Geometry.Sleepers.Count > 0, "Releasing the spacing handle restores normal track rendering");
            var group = graph.Groups[a.Group];
            graph.SetParallelOffset(a, 12);
            check(a.Position == anchor && group.Offsets[a.Id] == 0, "Parallel group anchor cannot be moved by spacing adjustment");
            graph.SetParallelOffset(pa, float.NaN);
            check(float.IsFinite(pa.Position.X), "Invalid spacing values cannot corrupt the graph");
            var sibling = graph.AddParallel(a, -1);
            var paPosition = pa.Position;
            var siblingPosition = sibling.Position;
            graph.DeleteNode(a.Id);
            group = graph.Groups[pa.Group];
            check(group.Anchor == pa.Id && group.Offsets[pa.Id] == 0 && pa.Position == paPosition && sibling.Position == siblingPosition, "Deleting the anchor rebases the remaining parallel group without moving its nodes");
            renderer.Selected = sibling.Id;
            renderer.UpdateMarkers();
            check(renderer.SpacingHandleNodes().SequenceEqual(new[] { sibling.Id }), "Selecting a non-anchor exposes only its own spacing handle");
            editor.GridOverlay.ShowAt(Vector3.Zero, true);
            editor.SetEnabled(false);
            check(!editor.GridOverlay.Visible, "Closing the editor hides the world grid");
            await frame();
        }
        finally { Shift(false); editor.Free(); renderer.Free(); layer.Free(); camera.Free(); generator.Backend.Shutdown(); generator.Backend.Dispose(); }
    }
}
