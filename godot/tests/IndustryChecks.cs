using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class IndustryChecks
{
    private static StationPolyline Line(int id, double z, int edge = 0) => new(id, new[] { new TrackVector(0, 0, z), new TrackVector(0, 0, z + 100) }, edge);
    private static IndustryState Site(string id, string type, double x, double z) => new() { Id = id, Type = type, Position = new(x, 10, z) };
    private static void Core(Action<bool, string> check)
    {
        var data = IndustryCatalog.Load(); var sim = new IndustrySimulation(data);
        sim.Add(Site("coal", "coal-mine", 50, 50)); sim.Add(Site("ore", "iron-mine", -50, 50)); sim.Add(Site("mill", "steel-mill", 50, 1050));
        sim.SetPlatforms(new[] { Line(1, 0, 1), Line(2, 1000, 2) });
        check(sim.Connected(1).Count() == 2 && sim.Connected(2).Single().Id == "mill", "Catchment links nearby industries to the correct station");
        sim.Advance(10);
        check(sim.Stations[1].Outbound["coal"] == 10 && sim.Stations[1].Outbound["iron-ore"] == 10 && sim.Industries["mill"].Outputs.GetValueOrDefault("steel") == 0,
            "Mines produce into shared station storage while a mill without inputs stays idle");
        sim.Stations[1].Outbound["coal"] = 70; sim.Stations[1].Outbound["iron-ore"] = 70;
        var vehicles = VehicleCatalog.Load(); var builder = new TrainBuilder(); var train = builder.Create();
        builder.Add(train, vehicles.Get("ge-dash9")); var coal = builder.Add(train, vehicles.Get("open-hopper")); var ore = builder.Add(train, vehicles.Get("open-hopper"));
        builder.AssignCargo(train.Id, coal.Id, "coal"); builder.AssignCargo(train.Id, ore.Id, "iron-ore"); train.Running = true;
        for (int i = 0; i < 36; i++) sim.Service(train, 1, 1, _ => true);
        check(train.Items[1].Cargo == new CargoLoad("coal", 70) && train.Items[2].Cargo == new CargoLoad("iron-ore", 70), "Station loads each assigned hopper to its own capacity");
        check(!sim.Service(train, 1, 1, _ => true) && sim.AcceptingSpace(1, "coal") == 0, "Full trains go idle; mines do not accept deliveries they cannot consume");
        sim.EndVisit(train.Id);
        for (int i = 0; i < 140; i++) { sim.Service(train, 2, .5, _ => true); sim.Advance(.5); }
        double steel = sim.Stations[2].Outbound.GetValueOrDefault("steel") + sim.Industries["mill"].Outputs.GetValueOrDefault("steel");
        double iron = sim.Stations[2].Inbound.GetValueOrDefault("iron-ore") + sim.Industries["mill"].Inputs.GetValueOrDefault("iron-ore");
        double remainingCoal = sim.Stations[2].Inbound.GetValueOrDefault("coal") + sim.Industries["mill"].Inputs.GetValueOrDefault("coal");
        check(train.Items.Skip(1).All(i => i.Cargo == null) && steel > 0, "Started but stationary train unloads and feeds steel production");
        check(Math.Abs(iron + steel / .8 - 70) < 1e-7 && Math.Abs(remainingCoal + steel / .8 * .5 - 70) < 1e-7, "Deliveries, inputs and output recipes conserve cargo without duplication");
        var wide = new IndustrySimulation(data); wide.Add(Site("distant-platform", "coal-mine", 1000, 100));
        wide.SetPlatforms(new[] { Line(5, 0), new StationPolyline(5, new[] { new TrackVector(1000, 0, 0), new TrackVector(1000, 0, 200) }) });
        check(wide.Connected(5).Single().Id == "distant-platform", "Catchment uses every platform rather than the station center");
        wide.Industries["distant-platform"].Outputs["coal"] = 500; wide.Stations[5].Outbound["coal"] = data.StationCapacities["coal"];
        wide.Advance(10); check(wide.Industries["distant-platform"].Outputs["coal"] == 500, "Full industry and station storage pause production");
        wide.SetPlatforms(new[] { Line(5, 0, 11), Line(6, 1000, 12) }); wide.Stations[6].Outbound["coal"] = 7;
        double total = wide.Stations[5].Outbound["coal"] + wide.Stations[6].Outbound["coal"];
        wide.SetPlatforms(new[] { Line(5, 0, 11), Line(5, 1000, 12) });
        check(wide.Stations[5].Outbound["coal"] == total && !wide.Stations.ContainsKey(6), "Merged stations pool cargo without losing the former platform's stock");
        var settings = new WorldSettings("industry-tests", 600);
        var first = IndustryGeneration.Generate(settings, -1, 0); var repeat = IndustryGeneration.Generate(settings, -1, 0);
        check(first.Length > 0 && first.Select(i => (i.Id, i.Type, i.Position)).SequenceEqual(repeat.Select(i => (i.Id, i.Type, i.Position))), "Seeded generation repeats positions and stable IDs in negative regions");
        var another = IndustryGeneration.Generate(settings with { Seed = "other-industry-tests" }, -1, 0);
        check(!first.Select(i => i.Position).SequenceEqual(another.Select(i => i.Position)), "Different seeds change industry locations");
    }
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        Core(check);
        var network = new TrackNetwork(); var generator = new TerrainGenerator() { Space = network.Space };
        var camera = new WorldCamera { Space = network.Space }; parent.AddChild(camera); camera.SetProcess(false);
        camera.Position = new(110, 100, 400); camera.LookAt(new(0, 0, 350));
        var renderer = new TrackRenderer { Network = network, Camera = camera }; parent.AddChild(renderer); renderer.SetProcess(false);
        var layer = new CanvasLayer(); parent.AddChild(layer); var manager = new WindowManager(); layer.AddChild(manager);
        var track = new TrackEditor { Network = network, Renderer = renderer, Camera = camera, Generator = generator, Interface = layer, Windows = manager }; parent.AddChild(track); track.SetProcess(false);
        var trains = new TrainEditor { Network = network, Catalog = VehicleCatalog.Load(), Camera = camera, Windows = manager, SoundSettings = new() { Muted = true } }; parent.AddChild(trains); trains.SetProcess(false);
        var ui = new TrainWindows { Editor = trains, Manager = manager, TrackEditor = track, Interface = layer }; parent.AddChild(ui); ui.SetProcess(false);
        var industries = new IndustryWorld { Generator = generator, Network = network, Camera = camera, TrackEditor = track, Trains = trains, TrainUI = ui, Interface = layer, Windows = manager };
        parent.AddChild(industries); industries.SetProcess(false);
        try
        {
            var mine = industries.Place("coal-mine", new(0, 100, 250));
            industries.OpenList(); industries.OpenIndustry(mine.Id);
            check(industries.ListWindow.Visible && industries.Views[mine.Id].Visible, "Discovered industry list opens individual industry windows");
            var viewport = industries.Views[mine.Id].FindChildren("*", "SubViewport", true, false).Cast<SubViewport>().Single();
            check(viewport.World3D == parent.GetViewport().World3D && viewport.RenderTargetUpdateMode == SubViewport.UpdateMode.Always, "Industry viewport shares the live world with terrain, tracks and trains");
            camera.GoTo(new(100, 0, 350)); check(camera.GoToAnimating, "Nearby Go to animates within 2 km");
            camera.Enabled = true; camera._Process(.6); check(!camera.GoToAnimating, "Nearby camera animation finishes promptly"); camera.Enabled = false;
            camera.GoTo(new(15000, 0, 15000)); check(!camera.GoToAnimating && camera.WorldTarget.X == 15000, "Long-distance Go to jumps immediately");
            industries._Process(.25);
            check(industries.HasWorldVisual(mine.Id) && viewport.RenderTargetUpdateMode == SubViewport.UpdateMode.Always, "Open industry window retains its distant world model for the shared viewport");
            var viewCamera = viewport.GetChildren().OfType<Camera3D>().Single();
            network.Space.Rebase(new(16000, 0, 16000));
            check(viewCamera.Position.IsEqualApprox(network.Space.ToLocal(mine.Position) + new Vector3(86, 65, 95)), "Industry viewport camera follows floating-origin changes");
            network.Space.Rebase(TrackVector.From(Vector3.Zero));
            industries.Views[mine.Id].Close();
            check(!industries.HasWorldVisual(mine.Id) && viewport.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled, "Closing the window releases distant industry geometry and suspends rendering");
            industries.OpenIndustry(mine.Id);
            check(industries.HasWorldVisual(mine.Id), "Reopening a remote industry immediately restores its world model");
            var go = industries.Views[mine.Id].FindChildren("*", "Button", true, false).OfType<GameButton>().Single(b => b.Text == "Go to");
            go.EmitSignal(Button.SignalName.Pressed);
            check(!camera.GoToAnimating && camera.WorldTarget == mine.Position, "Industry viewport Go to button jumps to its actual site");
            for (int i = 0; i < 100 && industries.Regions.Count == 0; i++) { await frame(); industries._Process(.25); }
            check(industries.Regions.Count > 0, "Background exploration commits discovered industry regions");
            var save = new GameSave(); industries.Capture(save);
            var savedIds = save.Industries.Select(i => i.Id).Order().ToArray(); var savedRegions = save.IndustryRegions.Order().ToArray();
            save = JsonSerializer.Deserialize<GameSave>(JsonSerializer.Serialize(save))!; industries.Restore(save);
            check(industries.Simulation.Industries.Keys.Order().SequenceEqual(savedIds) && industries.Regions.Order().SequenceEqual(savedRegions), "Generated discoveries and evaluated regions survive restart without duplication");
            check(industries.Simulation.Industries.ContainsKey(mine.Id) && industries.Place("iron-mine", new(200, 100, 250)).Id != mine.Id, "Industry positions, storage and manual IDs survive a save roundtrip");
            await Movement(check, frame, network, trains, industries, ui);
        }
        finally
        {
            industries.QueueFree(); ui.QueueFree(); trains.QueueFree(); track.QueueFree(); renderer.QueueFree(); layer.QueueFree(); camera.QueueFree();
            generator.Backend.Shutdown(); generator.Backend.Dispose(); await frame(); await frame();
        }
    }
    private static async Task Movement(Action<bool, string> check, Func<Task> frame, TrackNetwork network, TrainEditor trains, IndustryWorld world, TrainWindows ui)
    {
        var a = network.AddNode(new(0, 0, 0)); var b = network.AddNode(new(0, 0, 300)); var c = network.AddNode(new(0, 0, 700)); var d = network.AddNode(new(0, 0, 1000));
        var start = network.ConnectNodes(a, b)!; network.ConnectNodes(b, c); var end = network.ConnectNodes(c, d)!; start.Station = 1; end.Station = 2; network.NotifyChanged();
        world.Simulation.Industries.Clear(); world.Simulation.Add(Site("source", "coal-mine", 100, 100)); world.Simulation.Add(Site("destination", "steel-mill", 100, 900)); world.RefreshConnections();
        world.Simulation.Stations[1].Outbound["coal"] = 70; world.Simulation.Catalog.TransferRates["coal"] = 100;
        var train = trains.Builder.Create(); trains.Builder.Add(train, trains.Catalog.Get("ge-dash9")); var car = trains.Builder.Add(train, trains.Catalog.Get("open-hopper")); trains.Builder.AssignCargo(train.Id, car.Id, "coal");
        // Placement uses the same world-space camera path as gameplay.
        trains.Camera.Position = new(110, 100, 400); trains.Camera.LookAt(new(0, 0, 150));
        trains.StartPlacement(train.Id); trains.RefreshPreview(trains.Camera.UnprojectPosition(new(0, 0, 150)));
        check(trains.Valid && trains.CommitPreview(), "Cargo route train places on its station approach");
        ui.OpenRoutes(train.Id); ui.AddOrder(train.Id, TrainOrderKind.Station, 1);
        var orders = ui.Views[train.Id].Orders;
        check(!orders.FullLoad.Disabled && orders.Item.ItemCount == 2, "Full-load UI offers only this train's compatible cargo types");
        orders.FullLoad.EmitSignal(Button.SignalName.Pressed);
        ui.AddOrder(train.Id, TrainOrderKind.Wait, seconds: 1); ui.AddOrder(train.Id, TrainOrderKind.Station, 2); ui.AddOrder(train.Id, TrainOrderKind.Wait, seconds: 1000); trains.ToggleRunning(train.Id);
        bool loaded = false, delivered = false;
        for (int i = 0; i < 3000; i++)
        {
            trains.AdvanceMovement(.1); world.Simulation.Advance(.1);
            loaded |= train.Items[1].Cargo?.Quantity >= 69.99;
            delivered = world.Simulation.Stations[2].Inbound.GetValueOrDefault("coal") + world.Simulation.Industries["destination"].Inputs.GetValueOrDefault("coal") > 69.99;
            if (delivered) break;
            if (i % 20 == 0) await frame();
        }
        check(loaded && delivered && train.Items[1].Cargo == null, "Station orders stop, fully load, wait, travel and unload through the real train movement loop");
        var snapshot = new GameSave(); trains.Capture(snapshot); world.Capture(snapshot);
        snapshot = JsonSerializer.Deserialize<GameSave>(JsonSerializer.Serialize(snapshot))!;
        trains.Restore(snapshot); world.Restore(snapshot);
        check(trains.Builder.Get(train.Id).Items[1].CargoAssignment == "coal" && trains.Builder.Get(train.Id).Orders.Items.Any(o => o.Kind == TrainOrderKind.FullLoad && o.Cargo == "coal")
            && world.Simulation.Stations[2].Inbound.GetValueOrDefault("coal") + world.Simulation.Industries["destination"].Inputs.GetValueOrDefault("coal") > 69.99,
            "Whole-logistics save restores assignments, full-load orders and station/industry stock");
        var clipboard = train.Orders.Copy(); var other = trains.Builder.Create(); other.Orders.Append(clipboard.Items);
        check(other.Orders.Items.Any(o => o.Kind == TrainOrderKind.FullLoad && o.Cargo == "coal"), "Route copying preserves full-load cargo selection");
    }
}
