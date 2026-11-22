using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class CargoChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        var catalog = VehicleCatalog.Load(); var hopper = catalog.Get("open-hopper");
        var hold = hopper.Cargo!;
        check(CargoTypes.All.Count == 4 && CargoTypes.Get("coal").Name == "Coal" && CargoTypes.Get("iron-ore").Name == "Iron ore", "Coal and iron ore have stable cargo definitions");
        check(hold.CapacityFor("coal") == 70 && hold.CapacityFor("iron-ore") == 70 && !hold.Accepts("passengers"), "Hopper accepts only coal and iron ore at 70 tonnes each");
        var different = new CargoHold(new Dictionary<string, double> { ["coal"] = 70, ["iron-ore"] = 50 });
        different.Validate(new("coal", 60));
        bool Reject(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
        check(Reject(() => different.Validate(new("iron-ore", 60))), "Capacity is enforced independently for each cargo type");
        foreach (double mass in new[] { -1d, 0d, 70.001d, double.NaN, double.PositiveInfinity })
            check(Reject(() => hold.Validate(new("coal", mass))), "Invalid or overloaded cargo quantity is rejected: " + mass);
        check(Reject(() => hold.Validate(new("wood", 1000))), "Unsupported cargo is rejected");
        var liquidAndPeople = new CargoHold(new Dictionary<string, double> { ["oil"] = 40000, ["passengers"] = 80 });
        liquidAndPeople.Validate(new("oil", 12500.5));
        liquidAndPeople.Validate(new("passengers", 80));
        check(new CargoLoad("oil", 1000).MassKg == 850 && new CargoLoad("passengers", 80).MassKg == 6000,
            "Litres and passenger counts convert to physics mass independently of capacity units");
        check(Reject(() => liquidAndPeople.Validate(new("passengers", 1.5))) && Reject(() => liquidAndPeople.Validate(new("passengers", 81))),
            "Passenger loads must be whole numbers within passenger capacity");
        bool fractionalCapacity = false;
        try { _ = new CargoHold(new Dictionary<string, double> { ["passengers"] = 80.5 }); }
        catch (ArgumentOutOfRangeException) { fractionalCapacity = true; }
        check(fractionalCapacity, "Passenger capacities cannot contain fractional people");
        var metadata = JsonNode.Parse(JsonData.Read("res://assets/vehicles/catalog.json").EnumerateArray().First(v => v.GetProperty("id").GetString() == "open-hopper").GetRawText())!;
        metadata["cargo"] = JsonNode.Parse("{\"capacities\":{\"oil\":40000,\"passengers\":80}}");
        var example = new VehicleDefinition(JsonSerializer.SerializeToElement(metadata));
        check(example.CargoSummary == "Oil: 40000 L / Passengers: 80 passengers", "Vehicle capacity labels use each cargo's unit");
        check(hopper.CargoSummary == "Coal: 70 t / Iron ore: 70 t", "Hopper capacities remain in tonnes");
        var legacyLoad = JsonSerializer.Deserialize<CargoLoad>("{\"Type\":\"coal\",\"MassKg\":70000}")!;
        check(legacyLoad.Quantity == 70 && legacyLoad.MassKg == 70000, "Old kilogram-only cargo saves preserve their mass");
        var oil = new CargoLoad("oil", 12500.5);
        string encoded = JsonSerializer.Serialize(oil);
        check(JsonSerializer.Deserialize<CargoLoad>(encoded) == oil && encoded.Contains("Quantity") && !encoded.Contains("MassKg"),
            "New cargo saves preserve native-unit quantities without persisting derived mass");
        metadata["cargo"] = JsonNode.Parse("{\"capacitiesKg\":{\"coal\":70000}}");
        check(new VehicleDefinition(JsonSerializer.SerializeToElement(metadata)).Cargo!.CapacityFor("coal") == 70,
            "Legacy kilogram capacity metadata converts to cargo units");
        var graph = new TrackNetwork();
        var camera = new WorldCamera { Space = graph.Space }; parent.AddChild(camera); camera.SetProcess(false);
        camera.Position = new(110, 100, 400); camera.LookAt(new(0, 0, 350), Vector3.Up);
        var editor = new TrainEditor { Network = graph, Catalog = catalog, Camera = camera }; parent.AddChild(editor); editor.SetProcess(false);
        graph.ConnectNodes(graph.AddNode(Vector3.Zero), graph.AddNode(new(0, 0, 700)));
        try
        {
            editor.AddVehicle("ge-dash9"); int id = editor.Builder.Selected!.Id;
            check(editor.AddVehicle("open-hopper", id), "Hopper appends to the Dash 9 on standard gauge");
            var train = editor.Builder.Get(id); var item = train.Items[1];
            double emptySpeed = TrainPhysics.Step(train.Items.Select(i => i.Physics), 0, true, 0, false, .1).Speed;
            editor.Builder.SetCargo(id, item.Id, new("coal", 70));
            check(train.Items[1].MassKg == 93000 && train.Items[1].Physics.MassKg == 93000, "Payload adds to 23-tonne tare mass");
            check(TrainPhysics.Step(train.Items.Select(i => i.Physics), 0, true, 0, false, .1).Speed < emptySpeed, "Loaded consist accelerates more slowly");
            check(Reject(() => editor.Builder.SetCargo(id, train.Items[0].Id, new("coal", 1000))), "Locomotive without a cargo hold rejects cargo");
            editor.StartPlacement(id); editor.RefreshPreview(camera.UnprojectPosition(new(0, 0, 350)));
            check(editor.Valid && editor.Ghosts.Count == 2 && editor.CommitPreview(), "Locomotive and hopper preview and place as one train");
            var visual = editor.Parked.Single().Visuals[1];
            check(new[] { "body", "bogie_0", "bogie_1" }.All(n => visual.Vehicle.Model.FindChild(n, true, false) != null), "Hopper exposes independently posed body and bogies");
            check(editor.Instances.BatchCount == 6, "Hopper uses three instance batches alongside the locomotive");
            var meshes = visual.Vehicle.Model.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().ToArray();
            check(meshes.Any(m => m.Name == "inside_floor") && meshes.Count(m => m.Name.ToString().StartsWith("discharge_side_")) == 4, "Open hopper has an interior and two discharge bays");
            var save = new GameSave(); editor.Capture(save);
            editor.Restore(JsonSerializer.Deserialize<GameSave>(JsonSerializer.Serialize(save))!);
            var restored = editor.Builder.Get(id);
            check(restored.Items[1].Cargo == new CargoLoad("coal", 70) && restored.Items[1].MassKg == 93000 && restored.Placed, "Saved loaded hopper restores cargo, mass and track placement");
            editor.Builder.SetCargo(id, item.Id, null);
            editor.Builder.SetCargo(id, item.Id, new("iron-ore", 70));
            check(restored.Items[1].Cargo?.Type == "iron-ore", "Empty hopper can be reused for iron ore");
            restored.Running = true;
            check(Reject(() => editor.Builder.SetCargo(id, item.Id, null)), "Cargo cannot be changed while the train is running");
            restored.Running = false;
            var legacy = JsonSerializer.Deserialize<SavedItem>("{\"Id\":1,\"Vehicle\":\"open-hopper\",\"Facing\":1}")!;
            check(legacy.Cargo == null, "Old save items without cargo remain empty");
        }
        finally { editor.QueueFree(); camera.QueueFree(); await frame(); await frame(); }
    }
}
