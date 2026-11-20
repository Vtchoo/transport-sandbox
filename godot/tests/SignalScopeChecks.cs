using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class SignalScopeChecks
{
    private static (TrackNode[] Nodes, TrackEdge[] Edges) Line(TrackNetwork network, float x)
    {
        var nodes = Enumerable.Range(0, 9).Select(i => network.AddNode(new(x, 0, i * 100))).ToArray();
        return (nodes, Enumerable.Range(0, 8).Select(i => network.ConnectNodes(nodes[i], nodes[i + 1])!).ToArray());
    }
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        var network = new TrackNetwork(); var a = Line(network, 0);
        network.SetSignal(a.Nodes[1].Id, 1, RailSignalKind.Chain);
        network.SetSignal(a.Nodes[3].Id, 1, RailSignalKind.Chain);
        network.SetSignal(a.Nodes[5].Id, 1, RailSignalKind.Normal);
        network.SetSignal(a.Nodes[7].Id, 1, RailSignalKind.Normal);
        using var placement = new TrainPlacement(network); using var interlocking = new RailInterlocking(network, placement);
        var normal = interlocking.InspectScope(new(a.Nodes[5].Id, 1));
        check(normal.Edges.SequenceEqual(a.Edges.Skip(5).Take(2).Select(e => e.Id)), "Normal signal scope is exactly its following physical block through plain nodes");
        var chain = interlocking.InspectScope(new(a.Nodes[1].Id, 1));
        check(chain.Edges.SequenceEqual(a.Edges.Skip(1).Take(6).Select(e => e.Id)), "Chain scope traverses plain nodes and chain signals through the normal exit's receiving block");
        check(interlocking.ReservationCount == 0 && network.Signals.Values.All(s => s.Aspect == RailSignalAspect.Red), "Scope inspection never changes aspects or acquires reservations");

        var b = Line(network, 20);
        // Same physical block but no legal forward route from a's entry to b's line.
        network.ConnectNodes(b.Nodes[2], a.Nodes[2], side: -1);
        network.SetSignal(a.Nodes[1].Id, 1, RailSignalKind.Path);
        network.SetSignal(a.Nodes[3].Id, 1, RailSignalKind.Normal);
        network.SetSignal(b.Nodes[1].Id, 1, RailSignalKind.Path);
        network.SetSignal(b.Nodes[3].Id, 1, RailSignalKind.Normal);
        var c = Enumerable.Range(0, 5).Select(i => network.AddNode(new(-100 + i * 50, 0, 250))).ToArray();
        var crossing = Enumerable.Range(0, 4).Select(i => network.ConnectNodes(c[i], c[i + 1])!).ToArray();
        network.SetSignal(c[1].Id, 1, RailSignalKind.Path); network.SetSignal(c[3].Id, 1, RailSignalKind.Normal);
        var key = new SignalKey(a.Nodes[1].Id, 1);
        var paths = interlocking.InspectPaths(key);
        check(paths.Selected.Length == 1 && paths.Selected[0].Edges.SequenceEqual(a.Edges.Skip(1).Take(2).Select(e => e.Id)),
            "Selected path enumeration follows signed connections and stops at the next signal");
        check(paths.Others.Any(p => p.Entry == new SignalKey(b.Nodes[1].Id, 1) && !p.Conflict),
            "A disjoint route from another entry in the same block is non-conflicting");
        check(paths.Others.Any(p => p.Entry == new SignalKey(c[1].Id, 1) && p.Conflict) && !paths.Truncated,
            "A geometric crossing from another entry is conflicting even with no shared graph nodes");
        foreach (var node in c) node.Position += Vector3.Up * 8;
        network.NotifyChanged();
        check(!interlocking.InspectPaths(key).Others.Any(p => p.Entry.Node == c[1].Id), "A grade-separated crossing is excluded from conflicting block paths");
        foreach (var node in c) node.Position -= Vector3.Up * 8;
        network.NotifyChanged();
        network.SetSignal(b.Nodes[3].Id, -1, RailSignalKind.Path);
        check(interlocking.InspectPaths(key).Others.Any(p => p.Entry == new SignalKey(b.Nodes[3].Id, -1)
            && p.Conflict && p.Edges.Contains(a.Edges[2].Id)), "A route from another entry sharing the selected route's rails is conflicting");
        var fork = network.AddNode(new(40, 0, 300)); var exit = network.AddNode(new(40, 0, 400)); var beyond = network.AddNode(new(40, 0, 500));
        network.ConnectNodes(a.Nodes[2], fork); network.ConnectNodes(fork, exit); network.ConnectNodes(exit, beyond);
        network.SetSignal(exit.Id, 1, RailSignalKind.Normal);
        var alternatives = interlocking.InspectPaths(key);
        check(alternatives.Selected.Length == 2 && alternatives.Selected.All(p => p.Entry == key && !p.Conflict),
            "All alternative routes from the selected signal remain blue; conflict coloring applies to other entries");

        var camera = new WorldCamera { Position = new(30, 90, 30) }; parent.AddChild(camera); camera.SetProcess(false); camera.LookAt(a.Nodes[1].Position + Vector3.Up * 2);
        var renderer = new SignalRenderer { Network = network }; parent.AddChild(renderer); renderer.SetProcess(false); renderer._Process(0);
        var layer = new CanvasLayer(); parent.AddChild(layer); var windows = new WindowManager(); layer.AddChild(windows);
        var ui = new SignalWindows { Network = network, Interlocking = interlocking, Windows = windows, Renderer = renderer, Camera = camera, Interface = layer };
        parent.AddChild(ui); ui.SetProcess(false);
        try
        {
            var node = a.Nodes[1];
            var head = node.Position + TrackNetwork.SignalRight(node.Axis) * TrackNetwork.SignalOffset(node.Gauge) + Vector3.Up * 3.05f;
            check(renderer.Pick(camera, camera.UnprojectPosition(head)) == key && ui.OpenAt(camera.UnprojectPosition(head)),
                "Clicking the actual signal head opens that directional signal's window");
            ui._Process(0);
            check(ui.Focused == key && ui.Paths != null && ui.Overlay.Visible && ui.Overlay.StrokeCount > 0, "Focused path signal window shows route conflict strokes");
            var originalWindow = ui.Views[key]; var second = new SignalKey(b.Nodes[1].Id, 1);
            ui.Open(second);
            check(ui.Views.Count == 1 && ui.Views[second] == originalWindow && ui.Focused == second && !ui.Views.ContainsKey(key),
                "Opening another signal reuses the only inspector and replaces its highlighted scope");
            ui.Open(key);
            var other = windows.Create("Other tool", WindowPlacement.CenterLeft, new(300, 200)); other.Open(); ui._Process(0);
            check(!ui.Overlay.Visible && ui.Focused == null, "Switching focus to another window hides the signal's scope");
            ui.Open(key); var modal = windows.Create("Modal", WindowPlacement.Center, new(300, 200), modal: true); modal.Open(); ui._Process(0);
            check(!ui.Overlay.Visible, "A modal window suppresses focused signal highlights"); modal.Close();
            ui.Open(key); ui.Views[key].ToggleCollapsed(); ui._Process(0);
            check(!ui.Overlay.Visible, "Collapsing a focused signal window hides its scope");
            ui.Views[key].ToggleCollapsed(); ui.Open(key); ui.Views[key].Minimize(); ui._Process(0);
            check(!ui.Overlay.Visible, "Minimizing a signal window hides its scope");
            ui.Open(key); network.Space.Rebase(new(1024, 0, 1024)); camera.Position -= new Vector3(1024, 0, 1024); ui._Process(0);
            check(ui.Overlay.Visible && ui.Focused == key, "Absolute scope strokes survive a floating-origin change");
            ui._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true }); ui._Process(0);
            check(!ui.Views[key].Visible && !ui.Overlay.Visible, "Escape closes the active signal inspector before opening pause");
            ui.Open(key);
            network.RemoveSignal(key.Node, key.Side); ui._Process(0);
            check(!ui.Views.ContainsKey(key) && !ui.Overlay.Visible, "Deleting the inspected signal closes its window and clears its scope");
        }
        finally { ui.Free(); layer.Free(); renderer.Free(); camera.Free(); }
        await frame();
    }
}
