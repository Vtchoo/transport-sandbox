using Godot;
using System;
using System.Linq;

namespace TransportSandbox.Tests;

public static class SignalVisualChecks
{
    public static void Run(Action<bool, string> check, Node root)
    {
        var network = new TrackNetwork();
        var renderer = new SignalRenderer { Network = network, DirectionVisibility = () => true };
        root.AddChild(renderer);
        try
        {
            foreach (var kind in Enum.GetValues<RailSignalKind>())
            {
                var node = network.AddNode(new((int)kind * 30, 10, 100));
                var end = network.AddNode(new((int)kind * 30, 10, 200));
                network.ConnectNodes(node, end);
                network.SetSignal(node.Id, 1, kind);
                network.SetSignal(node.Id, -1, kind);
            }
            renderer._Process(0);
            renderer.SetProcess(false);
            var visuals = renderer.GetChildren().OfType<SignalVisual>().ToArray();
            var positions = visuals.Select(v => network.Space.ToWorld(v.GlobalPosition)).ToArray();
            var arrows = visuals.Select(v => network.Space.ToWorld(v.DirectionArrow.GlobalPosition)).ToArray();
            check(visuals.Length == 6, "All signal kinds and both directions have persistent visuals");
            foreach (var eye in new[] { new TrackVector(512, 0, 512), new TrackVector(-512, 0, -512), new TrackVector(0, 0, 0) })
            {
                // No renderer process tick follows the shift: this is the late-camera
                // ordering that previously left signals in the old coordinates for a frame.
                network.Space.Rebase(eye);
                check(visuals.Select((v, i) => (network.Space.ToWorld(v.GlobalPosition) - positions[i]).Length < .001).All(x => x),
                    "Signal poles and heads rebase synchronously before another process tick");
                check(visuals.Select((v, i) => (network.Space.ToWorld(v.DirectionArrow.GlobalPosition) - arrows[i]).Length < .001).All(x => x),
                    "Signal direction arrows retain their world placement during origin shifts");
                check(renderer.GetChildren().OfType<SignalVisual>().SequenceEqual(visuals), "Rebasing retains signal visual objects");
            }
        }
        finally { renderer.Free(); }
        network.Space.Rebase(new(1024, 0, 1024)); // Freed renderers must unsubscribe.
    }
}
