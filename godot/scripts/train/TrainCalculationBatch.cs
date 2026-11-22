using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox;

/// <summary>Bounded synchronous worker batches. No scene access, reservations or work surviving a step.</summary>
internal static class TrainCalculationBatch
{
    internal static readonly int WorkerLimit = Math.Clamp(System.Environment.ProcessorCount - 2, 1, 4);
    public static void Run<T>(IReadOnlyList<T> jobs, Action<T> calculate, bool parallel)
    {
        int workers = Math.Min(WorkerLimit, jobs.Count);
        if (!parallel || jobs.Count < 4 || workers < 2)
        {
            for (int i = 0; i < jobs.Count; i++) calculate(jobs[i]);
            return;
        }
        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, batch =>
        {
            for (int i = batch; i < jobs.Count; i += workers) calculate(jobs[i]);
        });
    }
}

internal sealed class MovementWork
{
    public readonly ParkedTrain Parked;
    public readonly TrainConsist Train;
    public TrainRoute Route;
    public readonly VehiclePhysics[] Definitions;
    private readonly double[] _offsets;
    private readonly TrainPerformance _performance;
    public double MaximumSpeed => _performance.MaxSpeed;
    private readonly TrainPosition _position;
    private readonly bool _ho;
    public readonly int Direction;
    public readonly double Length;
    public double Grade, Braking, Lookahead, Clearance, Free;
    public double CurveLimit = double.PositiveInfinity;
    public bool ReverseAtSignal;
    public (bool Hold, double StopDistance) Command;
    public InvalidOperationException? Error;

    public MovementWork(ParkedTrain parked, TrainConsist train, TrainRoute route, bool ho, double catalogMax)
    {
        Parked = parked; Train = train; Route = route; _position = parked.Position;
        Definitions = train.Items.Select(item => item.Physics).ToArray();
        _offsets = train.Offsets; _performance = TrainPhysics.Performance(Definitions, ho, catalogMax);
        Direction = train.TravelDirection; Length = train.Length; _ho = ho;
    }
    public void CalculateGrade()
    {
        if (Error != null) return;
        try
        {
            double weighted = 0;
            for (int i = 0; i < Definitions.Length; i++) weighted += Definitions[i].MassKg * Route.Sample(_position, _offsets[i]).Tangent.Y * Direction;
            Grade = weighted / _performance.Mass;
        }
        catch (InvalidOperationException error) { Error = error; }
    }
    public void CalculateCurveLimit()
    {
        if (Error != null) return;
        try
        {
            double scan = Math.Min(Lookahead, Clearance);
            for (double d = -Length / 2; d <= Length / 2 + scan; d += 5)
            {
                var location = Route.Sample(_position, Direction * d).Position;
                double limit = TrainPhysics.CurveLimit(Route.TurnRadius(location), _ho);
                CurveLimit = Math.Min(CurveLimit, Math.Sqrt(limit * limit + 2 * Braking * Math.Max(0, d - Length / 2)));
            }
        }
        catch (InvalidOperationException error) { Error = error; }
    }
}
