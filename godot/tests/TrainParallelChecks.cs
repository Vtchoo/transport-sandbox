using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class TrainParallelChecks
{
    public static void Run(Action<bool, string> check)
    {
        var network = new TrackNetwork();
        var nodes = Enumerable.Range(0, 24).Select(i => network.AddNode(new(30 * MathF.Sin(i * .3f), 10 * MathF.Sin(i * .5f), i * 100))).ToArray();
        var edges = Enumerable.Range(0, 23).Select(i => network.ConnectNodes(nodes[i], nodes[i + 1])!).ToArray();
        using var placement = new TrainPlacement(network);
        var definition = VehicleCatalog.Load().Get("atsf-f7a");
        MovementWork[] Make() => Enumerable.Range(0, 64).Select(i =>
        {
            var train = new TrainConsist { Id = i + 1, TravelDirection = i % 2 == 0 ? 1 : -1 };
            for (int car = 0; car < 4; car++) train.Items.Add(new(car, definition));
            var position = new TrainPosition(edges[8 + i % 5].Id, .4);
            var parked = new ParkedTrain { Id = train.Id, Position = position };
            var route = new TrainRoute(placement, position, (uint)(i + 40));
            return new MovementWork(parked, train, route, true, 75) { Braking = 2, Clearance = 250, Lookahead = 250 };
        }).ToArray();
        var serial = Make(); var parallel = Make();
        // Start workers against a cold shared geometry cache to exercise concurrent inserts.
        TrainCalculationBatch.Run(parallel, w => w.CalculateGrade(), true);
        TrainCalculationBatch.Run(parallel, w => w.CalculateCurveLimit(), true);
        TrainCalculationBatch.Run(serial, w => w.CalculateGrade(), false);
        TrainCalculationBatch.Run(serial, w => w.CalculateCurveLimit(), false);
        check(serial.Zip(parallel).All(pair => pair.First.Error == null && pair.Second.Error == null
            && pair.First.Grade == pair.Second.Grade && pair.First.CurveLimit == pair.Second.CurveLimit
            && pair.First.Route.Save().Random == pair.Second.Route.Save().Random),
            "Parallel grades/curve limits and seeded route choices exactly match serial results for 64 forward/reverse consists");
        var visits = new int[64]; int coordinator = System.Environment.CurrentManagedThreadId, onWorkers = 0;
        TrainCalculationBatch.Run(Enumerable.Range(0, 64).ToArray(), i =>
        {
            Interlocked.Increment(ref visits[i]);
            if (System.Environment.CurrentManagedThreadId != coordinator) Interlocked.Increment(ref onWorkers);
            Thread.SpinWait(20000);
        }, true);
        check(visits.All(count => count == 1), "Worker batches finish every job exactly once before returning");
        check(TrainCalculationBatch.WorkerLimit == 1 || onWorkers > 0, "Parallel batch actually executes calculations on background threads");
        var broken = Make();
        network.Edges.Remove(broken[0].Parked.Position.Edge);
        TrainCalculationBatch.Run(broken, w => w.CalculateGrade(), true);
        check(broken[0].Error is InvalidOperationException, "Broken track errors are returned to the coordinator rather than escaping worker batches");
    }

    public static async Task Integration(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        var network = new TrackNetwork();
        var nodes = Enumerable.Range(0, 31).Select(i => network.AddNode(new(30 * MathF.Sin(i * .3f), 0, i * 120))).ToArray();
        var edges = Enumerable.Range(0, 30).Select(i => network.ConnectNodes(nodes[i], nodes[i + 1])!).ToArray();
        foreach (var node in nodes.Skip(1).SkipLast(1))
        { network.SetSignal(node.Id, 1, RailSignalKind.Normal); network.SetSignal(node.Id, -1, RailSignalKind.Normal); }
        var save = new GameSave(); network.Capture(save);
        var catalog = VehicleCatalog.Load();
        using (var placement = new TrainPlacement(network))
        {
            SavedPose Save(VehiclePose p) => new(TrackVector.From(p.Center), TrackVector.From(p.Front), TrackVector.From(p.Rear),
                TrackVector.From(p.FrontDirection), TrackVector.From(p.RearDirection), p.Sections.Select(Save).ToArray());
            save.Trains = Enumerable.Range(0, 4).Select(i =>
            {
                var train = new TrainConsist { Id = i + 1 };
                train.Items.Add(new(i + 1, catalog.Get("atsf-f7a")));
                var position = new TrainPosition(edges[5 + i * 2].Id, .5);
                var route = new TrainRoute(placement, position, (uint)(i + 1));
                var poses = placement.Poses(train, position, route.Sample);
                return new SavedTrain { Id = train.Id, Running = true, Direction = i == 3 ? -1 : 1, Position = position,
                    Items = new[] { new SavedItem(i + 1, "atsf-f7a", 1) }, Poses = poses.Select(Save).ToArray(), Route = route.Save() };
            }).ToArray();
        }
        var camera = new WorldCamera(); parent.AddChild(camera); camera.SetProcess(false);
        var layer = new CanvasLayer(); parent.AddChild(layer); var windows = new WindowManager(); layer.AddChild(windows);
        TrainEditor Make(bool parallel)
        {
            var graph = new TrackNetwork(); graph.Restore(save);
            var editor = new TrainEditor { Network = graph, Camera = camera, Windows = windows, Catalog = catalog, ParallelCalculations = parallel };
            parent.AddChild(editor); editor.SetProcess(false); editor.Restore(save); return editor;
        }
        var a = Make(false); var b = Make(true);
        try
        {
            bool same = true;
            for (int step = 0; step < 180; step++)
            {
                a.AdvanceMovement(.05); b.AdvanceMovement(.05);
                for (int i = 0; i < a.Parked.Count; i++)
                    same &= a.Parked[i].Position == b.Parked[i].Position
                        && a.Builder.Get(a.Parked[i].Id).Speed == b.Builder.Get(b.Parked[i].Id).Speed;
                same &= a.Interlocking.SaveReservations().Select(l => (l.Train, l.Signal, l.Entered, Edges: string.Join(',', l.Edges.Order())))
                    .SequenceEqual(b.Interlocking.SaveReservations().Select(l => (l.Train, l.Signal, l.Entered, Edges: string.Join(',', l.Edges.Order()))));
            }
            check(same, "Four opposing/following trains retain identical positions, speeds and reservations with parallel calculations over 540 substeps");
            check(a.Parked.Any(p => a.Builder.Get(p.Id).Speed == 0), "Traffic parity fixture exercises a train waiting for conflicting authority");
        }
        finally { a.Free(); b.Free(); layer.Free(); camera.Free(); }
        await frame();
    }
}
