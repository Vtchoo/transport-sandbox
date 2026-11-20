using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

/// <summary>Headless movement regressions for station throats with chain entries and normal exits.</summary>
public static class ChainStationChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        foreach (bool doubleApproach in new[] { false, true })
            for (int approach = 0; approach < (doubleApproach ? 2 : 1); approach++)
                for (int destination = 0; destination < 2; destination++)
                    foreach (bool signalsAfterPlatforms in new[] { false, true })
                        await Scenario(check, frame, parent, doubleApproach, approach, destination, signalsAfterPlatforms);
    }

    private static async Task Scenario(Action<bool, string> check, Func<Task> frame, Node parent,
        bool doubleApproach, int approach, int destination, bool signalsAfterPlatforms)
    {
        string label = $"{(doubleApproach ? "2-to-2" : "1-to-2")} station throat, approach {approach + 1}, platform {destination + 1}, normals {(signalsAfterPlatforms ? "after" : "before")} platforms";
        var network = new TrackNetwork();
        int[] distances = { 0, 150, 225, 300, 400, 500, 700, 900, 1000 };
        var a = distances.Select(z => network.AddNode(new(0, 0, z))).ToArray();
        var b = distances.Select(z => network.AddNode(new(5, 0, z))).ToArray();
        var left = Enumerable.Range(0, 8).Select(i => network.ConnectNodes(a[i], a[i + 1])!).ToArray();
        // For the single approach, the second track starts at the far end of the turnout.
        var right = Enumerable.Range(doubleApproach ? 0 : 4, doubleApproach ? 8 : 4)
            .Select(i => network.ConnectNodes(b[i], b[i + 1])!).ToArray();
        network.ConnectNodes(a[3], b[4]);
        if (doubleApproach) network.ConnectNodes(b[3], a[4]);
        network.SetSignal(a[1].Id, 1, RailSignalKind.Chain);
        if (doubleApproach) network.SetSignal(b[1].Id, 1, RailSignalKind.Chain);
        foreach (var nodes in new[] { a, b }) network.SetSignal(nodes[signalsAfterPlatforms ? 7 : 5].Id, 1, RailSignalKind.Normal);
        // Each platform spans two segments; all junction/intermediate nodes are unsignalled.
        foreach (var nodes in new[] { a, b })
            foreach (int i in new[] { 5, 6 })
                check(network.AddStation(network.Incident(nodes[i].Id).Single(edge => edge.B == nodes[i + 1].Id), 1),
                    label + ": platform builds");
        var targetNodes = destination == 0 ? a : b;
        int station = network.Incident(targetNodes[6].Id).First(edge => edge.Station != 0).Station;
        var camera = new WorldCamera { Position = new(180, 400, 450) };
        parent.AddChild(camera); camera.SetProcess(false); camera.LookAt(new(0, 0, 400));
        var layer = new CanvasLayer(); parent.AddChild(layer);
        var windows = new WindowManager(); layer.AddChild(windows);
        var editor = new TrainEditor { Network = network, Camera = camera, Catalog = VehicleCatalog.Load(), Windows = windows };
        parent.AddChild(editor); editor.SetProcess(false);
        try
        {
            editor.AddVehicle("atsf-f7a"); int id = editor.Builder.Selected!.Id;
            check(editor.AddVehicle("atsf-coach", id) && editor.AddVehicle("atsf-coach", id)
                && editor.Builder.Get(id).Items.Count == 3, label + ": builds a three-vehicle consist");
            editor.StartPlacement(id); editor.RefreshPreview(camera.UnprojectPosition(new(approach * 5, 0, 100)));
            bool placed = editor.Valid && editor.CommitPreview();
            check(placed, label + ": F7 and two coaches place before the chain signal");
            if (!placed) return;
            var train = editor.Builder.Get(id); var parked = editor.Parked.Single(p => p.Id == id);
            // Placement builds the block topology; occupancy aspects refresh every twelve updates.
            for (int i = 0; i < 12; i++) editor.Interlocking.Update(Array.Empty<SignalTrain>());
            check(network.Signals.Values.All(signal => signal.Aspect == RailSignalAspect.Green),
                label + ": both normal exits and all chain entries initially show green");
            train.Orders.Add(TrainOrderKind.Station, station);
            train.Orders.Add(TrainOrderKind.Wait, seconds: 60);
            editor.OrdersChanged(id); editor.ToggleRunning(id);
            bool passedChain = false, enteredPlatform = false;
            int reversals = 0;
            for (int tick = 0; tick < 2500 && train.Orders.Index == 0; tick++)
            {
                int before = train.TravelDirection;
                editor.AdvanceMovement(.1);
                if (editor.RoutingPending) await Task.Delay(1);
                if (train.TravelDirection != before) reversals++;
                passedChain |= parked.Poses[0].Center.Z > 160;
                enteredPlatform |= network.Edges[parked.Position.Edge].Station == station;
            }
            string diagnostic = $" [z={parked.Poses[0].Center.Z:F2}, speed={train.Speed:F3}, status={train.Orders.Status}]";
            check(passedChain, label + ": train passes the chain signal" + diagnostic);
            check(enteredPlatform && train.Orders.Index == 1 && Math.Abs(train.Speed) < .001 && parked.Supported,
                label + ": train reaches its ordered platform and begins waiting" + diagnostic);
            var front = parked.Route!.Sample(parked.Position, train.Length / 2);
            check(Math.Abs(front.Point.Z - 900) < .15 && Math.Abs(front.Point.X - destination * 5) < .1,
                label + ": leading coupler stops at the chosen platform's exact end" + diagnostic);
            check(reversals == 0 && editor.Detours.Successes == 0,
                label + ": a clear throat needs neither reversal nor traffic detour");
            GD.Print($"Chain station regression: {label}: {(train.Orders.Index == 1 ? "arrived" : "STALLED")}{diagnostic}");
        }
        finally { editor.Free(); layer.Free(); camera.Free(); }
        await frame();
    }
}
