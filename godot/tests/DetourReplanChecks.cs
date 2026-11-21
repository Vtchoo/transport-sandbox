using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class DetourReplanChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        await Scenario(check, frame, parent, false);
        await Scenario(check, frame, parent, true);
    }
    private static async Task Scenario(Action<bool, string> check, Func<Task> frame, Node parent, bool blockedShortcut)
    {
        var network = new TrackNetwork();
        var nodes = new[] { 0, 200, 350, 500, 650, 850, 1250, 3650 }.Select(z => network.AddNode(new(0, 0, z))).ToArray();
        var main = Enumerable.Range(0, 7).Select(i => network.ConnectNodes(nodes[i], nodes[i + 1])!).ToArray();
        var p = network.AddNode(new(180, 0, 350)); var middle = network.AddNode(new(130, 0, 425));
        var q = network.AddNode(new(80, 0, 500)); var continuation = network.AddNode(new(80, 0, 700));
        var exit = network.AddNode(new(80, 0, 775));
        var entrance = network.ConnectNodes(nodes[1], p)!;
        network.ConnectNodes(p, middle); network.ConnectNodes(middle, q);
        var rejoin = network.ConnectNodes(q, nodes[4])!;
        var stayParallel = network.ConnectNodes(q, continuation)!;
        network.ConnectNodes(continuation, exit);
        var lateJoin = network.ConnectNodes(exit, nodes[5])!;
        var crossingA = network.AddNode(new(-20, 0, 750)); var crossingB = network.AddNode(new(20, 0, 750));
        network.ConnectNodes(crossingA, crossingB);
        network.SetSignal(nodes[1].Id, 1, RailSignalKind.Path);
        network.SetSignal(nodes[4].Id, 1, RailSignalKind.Normal);
        network.SetSignal(nodes[5].Id, 1, RailSignalKind.Normal);
        network.SetSignal(continuation.Id, 1, RailSignalKind.Normal);
        network.SetSignal(exit.Id, 1, RailSignalKind.Normal);
        network.AddStation(main[6], 1);
        var camera = new WorldCamera { Position = new(300, 400, 500) };
        parent.AddChild(camera); camera.SetProcess(false); camera.LookAt(new(0, 0, 400));
        var layer = new CanvasLayer(); parent.AddChild(layer);
        var windows = new WindowManager(); layer.AddChild(windows);
        var editor = new TrainEditor { Network = network, Camera = camera, Catalog = VehicleCatalog.Load(), Windows = windows };
        parent.AddChild(editor); editor.SetProcess(false);
        try
        {
            int Place(float z, float x = 0)
            {
                editor.AddVehicle("atsf-f7a"); int id = editor.Builder.Selected!.Id;
                editor.PlacementBlockOverride = () => true; editor.StartPlacement(id);
                editor.RefreshPreview(camera.UnprojectPosition(new(x, 0, z)));
                check(editor.Valid && editor.CommitPreview(), "Destination detour fixture places a compatible train");
                return id;
            }
            int id = Place(60); Place(425);
            if (blockedShortcut) Place(737, 80);
            var train = editor.Builder.Get(id); var parked = editor.Parked.Single(p => p.Id == id);
            train.Orders.Add(TrainOrderKind.Station, main[6].Station); editor.OrdersChanged(id); editor.ToggleRunning(id);
            bool initialMain = false, stayedParallel = false, usedOldRejoin = false, checkedReplan = false;
            long searchesAtDetour = -1;
            for (int i = 0; i < 4000; i++)
            {
                editor.AdvanceMovement(.1);
                if (editor.RoutingPending) await Task.Delay(1);
                if (editor.DetourReplanAttempts > 0 && searchesAtDetour < 0) searchesAtDetour = editor.RoutingStats.Searches;
                if (parked.Journey?.Reachable == true && editor.Detours.Successes == 0)
                {
                    var leading = parked.Route!.Copy().Sample(parked.Position, train.Length / 2).Position;
                    initialMain |= parked.Route.Inspect(leading, 1, parked.Journey.TargetPosition, out _).Any(l => l.Edge == main[2].Id);
                }
                stayedParallel |= parked.Position.Edge == stayParallel.Id;
                usedOldRejoin |= parked.Position.Edge == rejoin.Id;
                if (!checkedReplan && editor.DetourReplans > 0)
                {
                    checkedReplan = true;
                    var leading = parked.Route!.Copy().Sample(parked.Position, train.Length / 2).Position;
                    var chosen = parked.Route.Inspect(leading, 1, parked.Journey!.TargetPosition, out _);
                    check(chosen.Any(l => l.Edge == entrance.Id) && chosen.Any(l => l.Edge == stayParallel.Id)
                        && chosen.Any(l => l.Edge == lateJoin.Id) && chosen.All(l => l.Edge != rejoin.Id),
                        "Destination optimization preserves the accepted detour entrance and removes its unnecessary early rejoin");
                    check(chosen.All(l => !l.Detour) && parked.Route.Save().DetourLegs!.Length == 0,
                        "A successful destination replan becomes the normal cyan route, including the accepted entrance, in memory and saves");
                }
                if (parked.Position.Edge == main[6].Id && Math.Abs(train.Speed) < .001) break;
            }
            var front = parked.Route!.Sample(parked.Position, train.Length / 2);
            GD.Print($"Detour replan fixture (shortcut occupied={blockedShortcut}): initial main={initialMain}, parallel={stayedParallel}, old rejoin={usedOldRejoin}, optimized={checkedReplan}, detours={editor.Detours.Successes}, attempts={editor.DetourReplanAttempts}, replans={editor.DetourReplans}, edge={parked.Position.Edge}, front={front.Point}, speed={train.Speed}, supported={parked.Supported}, status={train.Orders.Status}");
            if (blockedShortcut)
                check(initialMain && !checkedReplan && !stayedParallel && usedOldRejoin && parked.Supported,
                    "A cheaper but occupied continuation keeps the safe accepted detour instead of undoing it or stopping for replanning");
            else
                check(initialMain && checkedReplan && stayedParallel && !usedOldRejoin && parked.Supported,
                    "A train initially routed along the main line takes a passing detour and keeps the cheaper parallel continuation");
            check(Math.Abs(front.Point.Z - 3650) < .15 && Math.Abs(train.Speed) < .001,
                "Replanning from the detour entrance preserves the exact station stop distance");
            check(editor.Detours.Successes == 1 && editor.DetourReplanAttempts == 1 && editor.DetourReplans == (blockedShortcut ? 0 : 1)
                && searchesAtDetour > 0 && editor.RoutingStats.Searches == searchesAtDetour,
                "One accepted detour triggers one destination check using the existing shared field, without repeated searches");
        }
        finally { editor.Free(); layer.Free(); camera.Free(); }
        await frame();
    }
}
