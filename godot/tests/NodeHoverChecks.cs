using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class NodeHoverChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        var graph = new TrackNetwork();
        var generator = new TerrainGenerator();
        var camera = new WorldCamera();
        parent.AddChild(camera);
        camera.Position = new(120, 100, 160);
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
        try
        {
            var node = graph.AddNode(Vector3.Zero);
            graph.NotifyChanged();
            void Hover(TrackNode n, int side = 1) => editor.RefreshHover(camera.UnprojectPosition(n.Position) + TrackNodeHoverMenu.ScreenAxis(camera, n.Position, n.Axis) * side * .25f);
            Hover(node);
            check(editor.NodeMenu.Visible && editor.HoverSide == 1 && editor.NodeMenu.Direction.Visible, "Hover shows the outgoing tangent action");
            var center = camera.UnprojectPosition(node.Position);
            var plus = editor.NodeMenu.Direction.GetRect().GetCenter();
            check(Math.Abs(plus.DistanceTo(center) - 65) < 1, "Continue button matches original 65 px tangent offset");
            check(Math.Abs(plus.DistanceTo(editor.NodeMenu.Delete.GetRect().GetCenter()) - 32) < 1, "Delete stays close to continue button");
            Hover(node, -1);
            check(editor.HoverSide == -1, "Pointer side chooses negative outgoing direction");
            editor.Gauge = 914;
            editor.NodeMenu.Direction.EmitSignal(Button.SignalName.Pressed);
            check(editor.Source == 0 && editor.Status.Text.Contains("Gauge mismatch"), "Continue action rejects a different selected gauge");
            editor.Gauge = 1435;
            editor.NodeMenu.Direction.EmitSignal(Button.SignalName.Pressed);
            check(editor.Source == node.Id && editor.SourceSide == -1, "Continue action starts construction in hovered direction");
            editor.Cancel();
            editor.NodeMenu.ParallelRight.EmitSignal(Button.SignalName.Pressed);
            Hover(node);
            check(graph.Groups[node.Group].Offsets.Count == 2 && editor.NodeMenu.ContinueParallel.Visible, "Parallel groups expose grouped continuation");
            renderer.ShowPreviews(graph.PreviewParallel(node, 1, new(0, 0, 120)).Select(p => p.Controls).ToArray());
            check(renderer.Preview.GetChildCount() == 2, "Grouped construction previews every parallel track");
            editor.NodeMenu.ContinueParallel.EmitSignal(Button.SignalName.Pressed);
            check(editor.BuildingParallel && editor.Place(new(0, 0, 120)) && graph.Edges.Count == 2, "Grouped continuation builds every parallel track");
            check(editor.BuildingParallel && editor.Place(new(0, 0, 240)) && graph.Edges.Count == 4, "Grouped construction continues until cancelled");
            editor.Cancel();
            check(!editor.BuildingParallel && editor.Source == 0, "Cancel ends grouped construction");
            graph.Nodes.Clear();
            graph.Edges.Clear();
            graph.Groups.Clear();
            graph.NotifyChanged();
            var a = graph.AddNode(new(0, 0, -100));
            var middle = graph.AddNode(Vector3.Zero);
            var b = graph.AddNode(new(0, 0, 100));
            graph.ConnectNodes(a, middle);
            graph.ConnectNodes(middle, b);
            var sibling = graph.AddParallel(middle, 1);
            Hover(middle);
            check(editor.NodeMenu.Merge.Visible, "Two compatible connections expose merge");
            editor.NodeMenu.Merge.EmitSignal(Button.SignalName.Pressed);
            await frame();
            check(graph.Nodes.ContainsKey(middle.Id) && graph.Edges.Count == 2, "Merge waits for confirmation");
            editor.NodeMenu.Merge.EmitSignal(Button.SignalName.Pressed);
            await frame();
            check(!graph.Nodes.ContainsKey(middle.Id) && graph.Nodes.ContainsKey(sibling.Id) && graph.Edges.Count == 1, "Confirmed merge preserves parallel neighbors and joins outer endpoints");
            Hover(a);
            editor.NodeMenu.Delete.EmitSignal(Button.SignalName.Pressed);
            await frame();
            check(graph.Nodes.ContainsKey(a.Id), "Delete waits for confirmation");
            editor.NodeMenu.Delete.EmitSignal(Button.SignalName.Pressed);
            await frame();
            check(!graph.Nodes.ContainsKey(a.Id) && graph.Nodes.ContainsKey(b.Id) && graph.Edges.Count == 0, "Confirmed delete removes connected segments and preserves neighbors");
            graph.Nodes.Clear();
            graph.Groups.Clear();
            graph.NotifyChanged();
            a = graph.AddNode(Vector3.Zero);
            b = graph.AddNode(new(0, 0, 100));
            var edge = graph.ConnectNodes(a, b)!;
            var midpoint = new TrackCurve(graph.Controls(edge)).PointAt(.5).Float();
            var projected = camera.UnprojectPosition(midpoint);
            editor.RefreshHover(projected + new Vector2(35, 0));
            check(editor.SegmentMenu.Visible && editor.SegmentMenu.Straighten.Visible && editor.SegmentMenu.Delete.Visible, "Segment midpoint has a 60 px action hover area independent of rail picking");
            var straightPosition = editor.SegmentMenu.Straighten.GetRect().GetCenter();
            var deletePosition = editor.SegmentMenu.Delete.GetRect().GetCenter();
            check(straightPosition.DistanceTo(projected - new Vector2(0, 35)) < 1 && deletePosition.DistanceTo(projected - new Vector2(0, 71)) < 1, "Segment actions stack above the arc-length midpoint with browser offsets");
            editor.RefreshHover(projected - new Vector2(25, 0));
            check(editor.SegmentMenu.Straighten.GetRect().GetCenter().DistanceTo(straightPosition) < 1, "Segment controls stay fixed while the cursor moves");
            editor.SegmentMenu.Delete.EmitSignal(Button.SignalName.Pressed);
            check(graph.Edges.ContainsKey(edge.Id) && editor.SegmentMenu.Delete.Text == "Confirm delete track", "Segment deletion waits for a second click");
            editor.RefreshHover(new(-1000, -1000));
            editor.RefreshHover(projected);
            check(editor.SegmentMenu.Delete.Text == "Delete track", "Leaving a segment resets its confirmation");
            editor.SegmentMenu.Straighten.EmitSignal(Button.SignalName.Pressed);
            check(editor.SegmentMenu.Straighten.Text == "Confirm straightening", "Straightening exposes explicit confirmation");
            editor.SegmentMenu.Straighten.EmitSignal(Button.SignalName.Pressed);
            check(graph.Edges.ContainsKey(edge.Id) && editor.Source == 0, "Confirmed straightening preserves the segment without starting construction");
            var extraA = graph.AddNode(new(-80, 0, -80));
            var extraB = graph.AddNode(new(-80, 0, 80));
            var extra = graph.ConnectNodes(extraA, extraB)!;
            editor.RefreshHover(camera.UnprojectPosition(new TrackCurve(graph.Controls(extra)).PointAt(.5).Float()));
            editor.SegmentMenu.Delete.EmitSignal(Button.SignalName.Pressed);
            editor.SegmentMenu.Delete.EmitSignal(Button.SignalName.Pressed);
            check(!graph.Edges.ContainsKey(extra.Id) && graph.Nodes.ContainsKey(extraA.Id) && graph.Nodes.ContainsKey(extraB.Id), "Confirmed segment deletion preserves endpoint nodes");
            graph.DeleteNode(extraA.Id);
            graph.DeleteNode(extraB.Id);
            graph.AddStation(edge, 1);
            Hover(a);
            check(!editor.NodeMenu.Direction.Visible, "Station-facing side hides continuation");
            Hover(a, -1);
            check(editor.NodeMenu.Direction.Visible, "Outside-station side allows continuation");
            var splitPoint = graph.Point(edge, .5f);
            editor._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = camera.UnprojectPosition(splitPoint) });
            editor.RefreshHover(camera.UnprojectPosition(splitPoint));
            check(editor.PendingSplit != null && editor.SegmentMenu.Visible && !editor.NodeMenu.Visible, "Pending split confirmation remains reachable near node hover areas");
            check(editor.SegmentMenu.ConfirmSplit.Visible && !editor.SegmentMenu.Delete.Visible && !editor.SegmentMenu.Straighten.Visible, "Pending split shows only its confirmation action");
            int beforeSplit = graph.Edges.Count;
            editor.Cancel();
            check(graph.Edges.Count == beforeSplit && editor.PendingSplit == null, "Cancelling a split preserves the original segment");
            editor._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = camera.UnprojectPosition(splitPoint) });
            editor.SegmentMenu.ConfirmSplit.EmitSignal(Button.SignalName.Pressed);
            check(graph.Edges.Count == beforeSplit + 1 && editor.PendingSplit == null && editor.Source == 0, "Confirm split divides the track without starting construction");
            var modal = windows.Create("Modal", WindowPlacement.Center, new Vector2(300, 240), modal: true);
            modal.Open();
            Hover(a, -1);
            check(!editor.NodeMenu.Visible, "Modal windows suppress hover controls");
            modal.Close();
            camera.Position += new Vector3(1000, 0, 0);
            camera.LookAt(Vector3.Zero, Vector3.Up);
            Hover(a);
            check(!editor.NodeMenu.Visible, "Distant nodes hide hover actions");
            foreach (var preset in Enum.GetValues<WindowPlacement>().Where(p => p != WindowPlacement.Custom))
            {
                var window = windows.Create("Placement", preset, new Vector2(300, 240));
                window.Open();
                var expected = WindowPositions.Resolve(preset, parent.GetViewport().GetVisibleRect().Size, window.Size, Vector2.Zero);
                check(window.Position.DistanceTo(expected) < 1, $"Window preset {preset} positions correctly");
                window.Close();
            }
            modal.Place(WindowPlacement.Center);
            modal.SetBounds(new(42, 51, 300, 240));
            check(modal.Placement == WindowPlacement.Custom, "Manual bounds release automatic placement");
        }
        finally { editor.Free(); renderer.Free(); layer.Free(); camera.Free(); generator.Backend.Shutdown(); generator.Backend.Dispose(); }
    }
    public static void ParallelSourceParity(Action<bool, string> check)
    {
        foreach (var sample in JsonData.Read("res://tests/parallel-fixtures.json").GetProperty("cases").EnumerateArray())
        {
            var graph = new TrackNetwork();
            var source = graph.AddNode(new(600, 0, 900));
            source.Axis = Vector3.Back;
            var other = graph.AddParallel(source, 1);
            graph.SetParallelOffset(other, 4.035f);
            int sign = sample.GetProperty("targetSign").GetInt32(), side = sample.GetProperty("side").GetInt32();
            var point = JsonData.Vector(sample.GetProperty("point"));
            var direction = JsonData.Vector(sample.GetProperty("endDirection"));
            TrackNode? target = null;
            if (sign != 0)
            {
                target = graph.AddNode(point);
                target.Axis = Vector3.Back * sign;
                var sibling = graph.AddParallel(target, 1);
                graph.SetParallelOffset(sibling, 4.035f * sign);
            }
            var plan = graph.PreviewParallel(source, side, point, target, direction);
            var expected = sample.GetProperty("plan");
            check(plan.Length == expected.GetArrayLength(), "Parallel preview count matches browser");
            for (int i = 0; i < plan.Length; i++)
            {
                check(Math.Abs(plan[i].LengthA - expected[i].GetProperty("aLength").GetDouble()) < .001 && Math.Abs(plan[i].LengthB - expected[i].GetProperty("bLength").GetDouble()) < .001, "Offset-curve handle fitting matches browser");
                for (int j = 0; j < 4; j++)
                    check(plan[i].Controls[j].DistanceTo(JsonData.Vector(expected[i].GetProperty("controls")[j])) < .001, "Parallel control points match browser within native coordinate precision");
            }
            int changes = 0;
            graph.Changed += () => changes++;
            var result = graph.ExtendParallel(source, side, point, target, direction);
            check(result.Edges.Length == 2 && changes == 1, "Parallel group commits as one graph update");
            int nodes = graph.Nodes.Count, edges = graph.Edges.Count;
            try
            {
                graph.ExtendParallel(source, side, point, result.Node, direction);
            }
            catch (InvalidOperationException) { }
            check(graph.Nodes.Count == nodes && graph.Edges.Count == edges, "Rejected grouped connections leave graph intact");
        }
    }
}
