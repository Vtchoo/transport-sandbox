using System;

namespace TransportSandbox;

public sealed class TrainWanderingState
{
    public TrainRoute Route = null!;
    public int Revision, Direction, Station, EndRouteIndex;
    public int ServicedStation, ServicedIndex = int.MinValue;
    public double Distance = double.PositiveInfinity, ScanTimer;
    public bool Servicing;
}

public partial class TrainEditor
{
    /// <summary>Advance station loading/unloading; return true while work remains. No cargo system means idle.</summary>
    public Func<TrainConsist, int, double, bool>? ServiceStation;
    private (bool Hold, double StopDistance) ExecuteWandering(ParkedTrain parked, TrainConsist train, TrainRoute route, double dt, double lookahead)
    {
        parked.Journey = null;
        if (parked.Wandering is not { } state || state.Revision != _revision || state.Direction != train.TravelDirection || state.Route != route)
            parked.Wandering = state = new() { Route = route, Revision = _revision, Direction = train.TravelDirection };
        if (!train.Running) return (true, double.PositiveInfinity);
        if (state.Servicing)
        {
            train.Orders.Status = $"Wandering - working at Station {state.Station}";
            if (ServiceStation?.Invoke(train, state.Station, dt) == true) return (true, 0);
            state.Station = 0; state.Servicing = false; state.Distance = double.PositiveInfinity; state.ScanTimer = 0;
        }
        if (state.Station == 0)
        {
            state.ScanTimer -= dt;
            if (state.ScanTimer <= 0)
            {
                state.ScanTimer = .25;
                var leading = route.Sample(parked.Position, train.TravelDirection * train.Length / 2).Position;
                var stop = route.NextStation(leading, train.TravelDirection, lookahead, state.ServicedStation, state.ServicedIndex);
                if (stop != null)
                { state.Station = stop.Station; state.EndRouteIndex = stop.EndRouteIndex; state.Distance = stop.Distance; }
            }
        }
        if (state.Station != 0 && state.Distance <= .05 && Math.Abs(train.Speed) < .1)
        {
            train.Speed = 0;
            state.ServicedStation = state.Station; state.ServicedIndex = state.EndRouteIndex;
            state.Servicing = true;
            train.Orders.Status = $"Wandering - stopped at Station {state.Station}";
            return (true, 0);
        }
        train.Orders.Status = state.Station == 0 ? "No orders - wandering" : $"Wandering - approach Station {state.Station}";
        return (false, state.Distance);
    }
}
