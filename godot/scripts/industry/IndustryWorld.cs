using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox;

/// <summary>Discovery, distance-culling, manual placement and industry windows. Simulation stays loaded.</summary>
public partial class IndustryWorld : Node3D
{
    public TerrainGenerator Generator = null!;
    public TrackNetwork Network = null!;
    public WorldCamera Camera = null!;
    public TrackEditor TrackEditor = null!;
    public TrainEditor Trains = null!;
    public TrainWindows TrainUI = null!;
    public WindowManager Windows = null!;
    public CanvasLayer Interface = null!;
    public IndustrySimulation Simulation = null!;
    public readonly HashSet<string> Regions = new();
    public readonly Dictionary<string, GameWindow> Views = new();
    public GameWindow ListWindow = null!;
    private VBoxContainer _list = null!;
    private readonly Dictionary<string, Label> _details = new();
    private readonly Dictionary<string, Camera3D> _viewCameras = new();
    private readonly Dictionary<string, Node3D> _visuals = new();
    private IndustryOverlay _overlay = null!;
    private PointerMessage _message = null!;
    private Task<IndustryState[]>? _generation;
    private string _pendingRegion = "";
    private int _epoch, _generationEpoch, _nextManual = 1;
    private string? _placing;
    private Node3D? _preview;
    private TrackVector? _previewPoint;
    private double _tick, _refresh;
    private bool _connectionsDirty = true;
    private readonly Dictionary<int, (int Station, TrackVector[] Controls, StationPolyline Line)> _platformCache = new();
    public bool Placing => _placing != null;
    public const float DrawDistance = 3500;
    internal int WorldVisualCount => _visuals.Count;
    internal bool HasWorldVisual(string id) => _visuals.ContainsKey(id);
    public override void _Ready()
    {
        Simulation ??= new(IndustryCatalog.Load());
        Network.Changed += Changed; Network.Space.Rebased += Rebased;
        Trains.ServiceStation = Service;
        Trains.EndCargoVisit = Simulation.EndVisit;
        ListWindow = Windows.Create("Industries", WindowPlacement.Center, new(470, 460));
        var placement = new HFlowContainer(); ListWindow.Body.AddChild(placement);
        foreach (var type in Simulation.Catalog.Types.Values)
        {
            var button = new Button { Text = "+ " + type.Name, TooltipText = "Place an industry manually" }; UiSkin.Button(button); placement.AddChild(button);
            button.Pressed += () => BeginPlacement(type.Id);
        }
        _list = new(); ListWindow.Body.AddChild(_list);
        ListWindow.Opened += RefreshList;
        ListWindow.StateChanged += () => { if (!ListWindow.Visible || ListWindow.IsCollapsed) CancelPlacement(); };
        _overlay = new() { Industries = this }; Interface.AddChild(_overlay);
        _message = new(); Interface.AddChild(_message);
        TrackEditor.EnabledChanged += TrackMode;
        Windows.StackingChanged += ActiveTool;
        RefreshList();
    }
    private void Changed() => _connectionsDirty = true;
    private void Rebased(TrackVector shift)
    {
        foreach (var (id, visual) in _visuals) visual.Position = Network.Space.ToLocal(Simulation.Industries[id].Position);
        if (_previewPoint is { } p && _preview != null) _preview.Position = Network.Space.ToLocal(p);
        foreach (string id in _viewCameras.Keys) UpdateViewCamera(id);
    }
    private void TrackMode(bool enabled) { if (enabled) CancelPlacement(); }
    private void ActiveTool() { if (Placing && Windows.Active != ListWindow) CancelPlacement(); }
    public override void _ExitTree()
    {
        Network.Changed -= Changed; Network.Space.Rebased -= Rebased; TrackEditor.EnabledChanged -= TrackMode;
        Windows.StackingChanged -= ActiveTool;
        Trains.ServiceStation = null; Trains.EndCargoVisit = null;
        if (IsInstanceValid(_overlay)) _overlay.QueueFree();
        if (IsInstanceValid(_message)) _message.QueueFree();
    }
    public void OpenList() { CancelPlacement(); TrackEditor.SetEnabled(false); Trains.CancelPlacement(); TrainUI.CancelOrderPicking(); RefreshList(); ListWindow.Open(); }
    private void RefreshList()
    {
        if (!ListWindow.Visible) return;
        foreach (var child in _list.GetChildren()) { _list.RemoveChild(child); child.QueueFree(); }
        UiSkin.Label(_list, $"{Simulation.Industries.Count} discovered industries", 11);
        foreach (var industry in Simulation.Industries.Values.OrderBy(i => i.Type).ThenBy(i => i.Id, StringComparer.Ordinal))
        {
            var p = industry.Position;
            var button = new Button { Text = $"{Simulation.Catalog.Types[industry.Type].Name}  |  X {p.X:F0}, Z {p.Z:F0}", Alignment = HorizontalAlignment.Left, ClipText = true, TooltipText = industry.Id };
            UiSkin.Button(button); _list.AddChild(button); button.Pressed += () => OpenIndustry(industry.Id);
        }
    }
    public void OpenIndustry(string id)
    {
        if (!Simulation.Industries.TryGetValue(id, out var industry)) return;
        CancelPlacement(); TrackEditor.SetEnabled(false); Trains.CancelPlacement(); TrainUI.CancelOrderPicking();
        if (!Views.TryGetValue(id, out var window))
        {
            window = Windows.Create(Simulation.Catalog.Types[industry.Type].Name, WindowPlacement.Center, new(450, 460)); Views[id] = window;
            // Share the live world, just like placed-train and station viewports.
            var container = new SubViewportContainer { Stretch = true, CustomMinimumSize = new(0, 195), MouseFilter = Control.MouseFilterEnum.Ignore };
            window.Body.AddChild(container);
            var viewport = new SubViewport { Size = new(400, 220), World3D = GetWorld3D(), HandleInputLocally = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled }; container.AddChild(viewport);
            var camera = new Camera3D { Current = true, Fov = 42, Near = .1f, Far = 14000 }; viewport.AddChild(camera);
            _viewCameras[id] = camera;
            UpdateViewCamera(id);
            var tools = new HBoxContainer(); window.Body.AddChild(tools);
            var go = new GameButton(); go.SetContent("Go to", "waypoint", "Move the world camera to this industry"); tools.AddChild(go); go.Pressed += () => Camera.GoTo(industry.Position);
            _details[id] = UiSkin.Label(window.Body, "", 11); _details[id].AutowrapMode = TextServer.AutowrapMode.WordSmart;
            window.StateChanged += () =>
            {
                viewport.RenderTargetUpdateMode = window.Visible && !window.IsCollapsed ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
                UpdateViewCamera(id); SyncVisuals();
            };
        }
        RefreshDetails(id); UpdateViewCamera(id); window.Open();
    }
    private void UpdateViewCamera(string id)
    {
        if (!_viewCameras.TryGetValue(id, out var camera) || !Simulation.Industries.TryGetValue(id, out var industry)) return;
        var location = Network.Space.ToLocal(industry.Position);
        camera.Position = location + new Vector3(86, 65, 95);
        camera.LookAt(location + new Vector3(0, 6, 0), Vector3.Up);
    }
    private void RefreshDetails(string id)
    {
        var industry = Simulation.Industries[id]; var type = Simulation.Catalog.Types[industry.Type];
        string Storage(Dictionary<string, double> values, Dictionary<string, double> capacity) => string.Join("\n", capacity.Select(c => $"{CargoTypes.Get(c.Key).Name}: {CargoTypes.Get(c.Key).FormatQuantity(IndustrySimulation.Stock(values, c.Key))}/{CargoTypes.Get(c.Key).FormatAmount(c.Value)}"));
        bool waiting = type.Inputs.Any(c => IndustrySimulation.Stock(industry.Inputs, c.Key) < c.Value * .25);
        bool full = type.OutputStorage.Any(c => IndustrySimulation.Stock(industry.Outputs, c.Key) >= c.Value - 1e-8);
        string stations = string.Join(", ", Simulation.Connections.Where(c => c.Value.Contains(id)).Select(c => $"Station {c.Key}"));
        _details[id].Text = $"X {industry.Position.X:F0}, Y {industry.Position.Y:F0}, Z {industry.Position.Z:F0}\n" + (waiting ? "Waiting for inputs" : full ? "Output storage full" : "Producing")
            + (type.Inputs.Count > 0 ? "\nInputs:\n" + Storage(industry.Inputs, type.InputStorage) : "") + "\nOutputs:\n" + Storage(industry.Outputs, type.OutputStorage)
            + "\nProduction: " + string.Join(" / ", type.Outputs.Select(c => CargoTypes.Get(c.Key).Name + " " + CargoTypes.Get(c.Key).FormatAmount(c.Value * 60) + "/min"))
            + "\nConnected: " + (stations.Length > 0 ? stations : "no stations within catchment");
    }
    public void RefreshConnections()
    {
        using var probe = FrameProbe.Measure("industries.catchment");
        var platforms = Network.Edges.Values.Where(e => e.Station != 0).OrderBy(e => e.Id).Select(e =>
        {
            var controls = Network.WorldControls(e);
            if (_platformCache.TryGetValue(e.Id, out var cached) && cached.Station == e.Station && cached.Controls.AsSpan().SequenceEqual(controls)) return cached.Line;
            var curve = new TrackCurve(controls); int count = Math.Clamp((int)Math.Ceiling(curve.Length / 8), 1, 4096);
            var line = new StationPolyline(e.Station, Enumerable.Range(0, count + 1).Select(i => curve.PointAt((double)i / count)).ToArray(), e.Id);
            _platformCache[e.Id] = (e.Station, controls, line); return line;
        }).ToArray();
        foreach (int id in _platformCache.Keys.Where(id => !Network.Edges.TryGetValue(id, out var e) || e.Station == 0).ToArray()) _platformCache.Remove(id);
        if (!platforms.SequenceEqual(Simulation.Platforms)) Simulation.SetPlatforms(platforms);
        _connectionsDirty = false;
    }
    public bool Service(TrainConsist train, int station, double seconds)
    {
        if (_connectionsDirty) RefreshConnections();
        var parked = Trains.Parked.FirstOrDefault(p => p.Id == train.Id); if (parked == null) return false;
        var route = parked.Route;
        return Simulation.Service(train, station, seconds, i =>
        {
            try
            {
                var position = route == null ? Trains.Placement.Sample(parked.Position, train.Offsets[i]).Position : route.Sample(parked.Position, train.Offsets[i]).Position;
                return Network.Edges.TryGetValue(position.Edge, out var edge) && edge.Station == station;
            }
            catch (InvalidOperationException) { return false; }
        });
    }
    public override void _Process(double delta)
    {
        if (_connectionsDirty) RefreshConnections();
        _tick += Math.Clamp(delta, 0, 1);
        while (_tick >= .25) { Simulation.Advance(.25); _tick -= .25; }
        _refresh -= delta;
        if (_refresh > 0) return; _refresh = .25;
        Discover(); SyncVisuals();
        foreach (var (id, window) in Views) if (window.Visible && !window.IsCollapsed) RefreshDetails(id);
        if (Placing)
        {
            if (Trains.Placing || TrainUI.PickingOrder) { CancelPlacement(); return; }
            var point = Camera.GroundPoint(Generator); _previewPoint = Network.Space.ToWorld(point);
            if (_preview != null) _preview.Position = point;
        }
    }
    private void Discover()
    {
        if (_generation != null)
        {
            if (!_generation.IsCompleted) return;
            try
            {
                if (_generationEpoch == _epoch)
                {
                    foreach (var state in _generation.GetAwaiter().GetResult())
                        if (!Simulation.Industries.ContainsKey(state.Id)) Simulation.Add(state);
                    Regions.Add(_pendingRegion); RefreshList();
                }
            }
            catch (Exception e) { GD.PushWarning("Industry generation: " + e.Message); }
            _generation = null;
        }
        int x = (int)Math.Floor(Camera.WorldTarget.X / IndustryGeneration.RegionSize), z = (int)Math.Floor(Camera.WorldTarget.Z / IndustryGeneration.RegionSize);
        foreach (var (dx, dz) in new[] { (0, 0), (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1) })
        {
            int rx = x + dx, rz = z + dz; string key = $"{rx}:{rz}"; if (Regions.Contains(key)) continue;
            _pendingRegion = key; _generationEpoch = _epoch;
            var settings = Generator.Settings; _generation = Task.Run(() => IndustryGeneration.Generate(settings, rx, rz)); return;
        }
    }
    private void SyncVisuals()
    {
        var inspected = Views.Where(v => v.Value.Visible && !v.Value.IsCollapsed && Simulation.Industries.ContainsKey(v.Key)).Select(v => v.Key).ToHashSet();
        var nearby = Simulation.Industries.Values.Where(i => (i.Position - Camera.WorldPosition).LengthSquared <= DrawDistance * DrawDistance).Select(i => i.Id).ToHashSet();
        nearby.UnionWith(inspected);
        foreach (var id in _visuals.Keys.Where(id => !nearby.Contains(id)).ToArray())
        { var visual = _visuals[id]; RemoveChild(visual); visual.QueueFree(); _visuals.Remove(id); }
        int budget = 3;
        foreach (string id in nearby.Where(id => !_visuals.ContainsKey(id)).OrderByDescending(inspected.Contains))
        {
            if (!inspected.Contains(id) && budget-- <= 0) break;
            var state = Simulation.Industries[id]; var visual = IndustryGeometry.Create(state.Type);
            visual.Position = Network.Space.ToLocal(state.Position); AddChild(visual); _visuals[id] = visual;
        }
    }
    public void BeginPlacement(string type)
    {
        CancelPlacement(); TrackEditor.SetEnabled(false); Trains.CancelPlacement(); TrainUI.CancelOrderPicking();
        _placing = type; _preview = IndustryGeometry.Create(type); AddChild(_preview);
        _message.Display("Click terrain to place " + Simulation.Catalog.Types[type].Name + ". Esc cancels.", true, 10);
        _refresh = 0;
    }
    public void CancelPlacement()
    { _placing = null; _previewPoint = null; if (_preview != null) { _preview.QueueFree(); _preview = null; } }
    public IndustryState Place(string type, TrackVector position)
    {
        if (position.Y <= Generator.SeaLevel + 1) throw new InvalidOperationException("Place the industry on dry land.");
        if (Simulation.Industries.Values.Any(i => Math.Abs(i.Position.X - position.X) < 80 && Math.Abs(i.Position.Z - position.Z) < 75))
            throw new InvalidOperationException("Another industry occupies this site.");
        var industry = new IndustryState { Id = "manual-" + _nextManual++, Type = type, Position = position };
        Simulation.Add(industry); RefreshList(); SyncVisuals(); return industry;
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (Windows.HasModal) return;
        if (Placing && input is InputEventKey { Pressed: true, Keycode: Key.Escape })
        { CancelPlacement(); GetViewport().SetInputAsHandled(); return; }
        if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;
        if (Placing)
        {
            try { var point = Network.Space.ToWorld(Camera.GroundPoint(Generator)); var state = Place(_placing!, point); CancelPlacement(); OpenIndustry(state.Id); }
            catch (InvalidOperationException e) { _message.Display(e.Message); }
            GetViewport().SetInputAsHandled(); return;
        }
        if (TrackEditor.Enabled || Trains.Placing || TrainUI.PickingOrder) return;
        var ray = Camera.ProjectRayOrigin(click.Position); var direction = Camera.ProjectRayNormal(click.Position);
        string? picked = null; float best = float.PositiveInfinity;
        foreach (var (id, visual) in _visuals)
        {
            var p = new Plane(Vector3.Up, visual.Position.Y + 6).IntersectsRay(ray, direction);
            if (p is { } point && Math.Abs(point.X - visual.Position.X) <= 35 && Math.Abs(point.Z - visual.Position.Z) <= 33 && ray.DistanceSquaredTo(point) < best)
            { best = ray.DistanceSquaredTo(point); picked = id; }
        }
        if (picked != null) { OpenIndustry(picked); GetViewport().SetInputAsHandled(); }
    }
    public void Capture(GameSave save)
    {
        save.Industries = Simulation.Industries.Values.Select(i => new IndustryState { Id = i.Id, Type = i.Type, Position = i.Position, Inputs = new(i.Inputs), Outputs = new(i.Outputs) }).ToArray();
        save.StationCargo = Simulation.Stations.Values.Select(s => new StationCargo { Id = s.Id, Inbound = new(s.Inbound), Outbound = new(s.Outbound) }).ToArray();
        save.IndustryRegions = Regions.ToArray(); save.NextIndustry = _nextManual;
        save.CargoVisits = Simulation.SaveVisits();
    }
    public void Restore(GameSave save)
    {
        _epoch++; CancelPlacement(); Simulation.Industries.Clear(); Simulation.Stations.Clear(); Simulation.ClearVisits(); Simulation.SetPlatforms(Array.Empty<StationPolyline>()); Regions.Clear();
        foreach (var state in save.Industries) Simulation.Add(state);
        foreach (var station in save.StationCargo) Simulation.Stations.Add(station.Id, station);
        foreach (string region in save.IndustryRegions) Regions.Add(region);
        _nextManual = Math.Max(save.NextIndustry, 1); _connectionsDirty = true;
        Simulation.RestoreVisits(save.CargoVisits);
        foreach (var visual in _visuals.Values) visual.QueueFree(); _visuals.Clear(); RefreshConnections(); RefreshList();
    }
}
