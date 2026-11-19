using System;
using System.Linq;

namespace TransportSandbox.Tests;

public static class LongTrainRouteChecks
{
    public static void Run(Action<bool, string> check)
    {
        var network = new TrackNetwork();
        var nodes = Enumerable.Range(0, 51).Select(i => network.AddNode(new(0, 0, i * 100))).ToArray();
        var spans = Enumerable.Range(0, 50).Select(i => network.ConnectNodes(nodes[i], nodes[i + 1])!).ToArray();
        var branchEnd = network.AddNode(new(500, 0, 4200));
        var branch = network.ConnectNodes(nodes[40], branchEnd, 1)!;
        var reverseEnd = network.AddNode(new(-500, 0, 800));
        var reverseBranch = network.ConnectNodes(nodes[10], reverseEnd, -1)!;
        using var placement = new TrainPlacement(network);
        var start = new TrainPosition(spans[0].Id, .5);
        var route = new TrainRoute(placement, start, 3);
        var states = spans.Take(40).Select(edge => RailRouter.StateId(edge.Id, 1)).Append(RailRouter.StateId(branch.Id, 1)).ToArray();
        route.SetPath(start, 1, states); route.Trim(start, 3000);
        var copied = route.Copy();
        check(copied.Sample(start, 4000).Position.Edge == branch.Id, "Copied plans retain a chosen junction more than three kilometres ahead");
        var position = start;
        for (int i = 0; i < 40; i++) { position = route.Sample(position, 100).Position; route.Trim(position, 3000); }
        check(position.Edge == branch.Id && route.PlannedLegCount == states.Length && route.CachedLegCount < states.Length,
            "Long-distance movement follows its full plan while trimming materialized history");
        copied.ClearPlan(start, 1);
        check(copied.PlannedLegCount == 0 && copied.Sample(start, 4000).Position.Edge != branch.Id,
            "Clearing orders removes the persistent plan and restores seeded random junction selection");
        var reverseStart = new TrainPosition(spans[^1].Id, .5);
        var backwards = new TrainRoute(placement, reverseStart, 1);
        var reverseStates = spans.Skip(10).Reverse().Select(edge => RailRouter.StateId(edge.Id, -1)).Append(RailRouter.StateId(reverseBranch.Id, 1)).ToArray();
        backwards.SetPath(reverseStart, -1, reverseStates); backwards.Trim(reverseStart, 3000);
        position = reverseStart;
        for (int i = 0; i < 40; i++) { position = backwards.Sample(position, -100).Position; backwards.Trim(position, 3000); }
        check(position.Edge == reverseBranch.Id && position.RouteIndex < 0 && position.Facing == -1,
            "Negative travel direction retains distant junction choices and per-edge orientation");
    }
}
