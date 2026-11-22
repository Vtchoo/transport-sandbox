using System;
using System.Linq;

namespace TransportSandbox;

public partial class TrainEditor
{
    private SavedPose SavePose(VehiclePose p) => new(Network.Space.ToWorld(p.Center), Network.Space.ToWorld(p.Front), Network.Space.ToWorld(p.Rear),
        TrackVector.From(p.FrontDirection), TrackVector.From(p.RearDirection), p.Sections.Select(SavePose).ToArray());
    private VehiclePose LoadPose(SavedPose p) => new(Network.Space.ToLocal(p.Center), Network.Space.ToLocal(p.Front), Network.Space.ToLocal(p.Rear),
        p.FrontDirection.Float(), p.RearDirection.Float(), p.Sections.Select(LoadPose).ToArray());
    public void Capture(GameSave save)
    {
        save.NextTrain = Builder.NextTrainId; save.NextItem = Builder.NextItemId;
        save.Trains = Builder.Trains.Select(t =>
        {
            var parked = Parked.FirstOrDefault(p => p.Id == t.Id);
            var wandering = parked?.Wandering;
            return new SavedTrain
            {
                Id = t.Id, Name = t.Name, Items = t.Items.Select(i => new SavedItem(i.Id, i.Definition.Id, i.Facing, i.Livery, i.Cargo)).ToArray(),
                Running = t.Running, Speed = t.Speed, Direction = t.TravelDirection, Orders = t.Orders.Items.ToArray(),
                OrderIndex = t.Orders.Index, WaitRemaining = t.Orders.WaitRemaining, NextOrder = t.Orders.NextId,
                Position = parked?.Position, Supported = parked?.Supported ?? true, Route = parked == null ? null : RouteFor(parked).Save(),
                Poses = parked?.Poses.Select(SavePose).ToArray() ?? Array.Empty<SavedPose>(),
                Wandering = wandering == null ? null : new(wandering.Direction, wandering.Station, wandering.EndRouteIndex, wandering.EndEdge,
                    wandering.ServicedStation, wandering.ServicedIndex, wandering.Distance, wandering.Servicing)
            };
        }).ToArray();
        save.Reservations = Interlocking.SaveReservations();
    }
    public void Restore(GameSave save)
    {
        CancelPlacement();
        foreach (var p in Parked) RemoveVisuals(p.Visuals);
        Parked.Clear();
        var models = save.Trains.Select(t =>
        {
            var train = new TrainConsist { Id = t.Id, Name = t.Name, Placed = t.Position != null, Running = t.Running, Speed = t.Speed, TravelDirection = t.Direction };
            train.Items.AddRange(t.Items.Select(i =>
            {
                var definition = Catalog.Get(i.Vehicle);
                if (i.Cargo != null && definition.Cargo == null) throw new InvalidOperationException("Saved cargo belongs to a vehicle without a cargo hold.");
                definition.Cargo?.Validate(i.Cargo);
                return new TrainItem(i.Id, definition, i.Facing, definition.ResolveLivery(i.Livery), i.Cargo);
            }));
            train.Orders.Restore(t.Orders, t.OrderIndex, t.WaitRemaining, t.NextOrder); return train;
        }).ToArray();
        Builder.Restore(models, null);
        Builder.RestoreCounters(save.NextTrain, save.NextItem);
        foreach (var saved in save.Trains.Where(t => t.Position != null))
        {
            var train = Builder.Get(saved.Id); var position = saved.Position!.Value;
            var route = saved.Route == null ? new TrainRoute(Placement, position, RouteSeed(train.Id)) : TrainRoute.Restore(Placement, position, saved.Route);
            var poses = saved.Poses.Select(LoadPose).ToArray();
            if (poses.Length != train.Items.Count) throw new InvalidOperationException("Saved train poses do not match its consist");
            int gauge = Network.Edges.TryGetValue(position.Edge, out var edge) ? edge.Gauge : TrainBuilder.Gauges(train.Items.Select(i => i.Definition)).First();
            var parked = new ParkedTrain { Id = train.Id, Position = position, Route = route, Supported = saved.Supported, Poses = poses,
                Visuals = CreateVisuals(train, poses, train.Items.Select(_ => gauge).ToArray()) };
            if (saved.Wandering is { } w) parked.Wandering = new() { Route = route, Revision = _revision, Direction = w.Direction, Station = w.Station,
                EndRouteIndex = w.EndRouteIndex, EndEdge = w.EndEdge, ServicedStation = w.ServicedStation, ServicedIndex = w.ServicedIndex, Distance = w.Distance, Servicing = w.Servicing };
            Parked.Add(parked);
        }
        Interlocking.RestoreReservations(save.Reservations, Parked.Select(p => new SignalTrain(p.Id, RouteFor(p), p.Position, Builder.Get(p.Id).Length, Builder.Get(p.Id).TravelDirection)).ToArray());
        SyncInstances(); Changed?.Invoke(null);
    }
}
