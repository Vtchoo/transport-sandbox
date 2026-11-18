using Godot;
using System;

namespace TransportSandbox;

public partial class TrackEditor : Node
{
    public TrackNetwork Network = null!;
    public TrackRenderer Renderer = null!;
    public WorldCamera Camera = null!;
    public TerrainGenerator Generator = null!;
    public CanvasLayer Interface = null!;
    public GameWindow Toolbar = null!;
    public WindowManager Windows = null!;
    public event Action<bool>? EnabledChanged;
    public HBoxContainer NodeMenu = null!, SegmentMenu = null!;
    public Label Status = null!;
    public OptionButton GaugePicker = null!;
    private VBoxContainer _trackOptions = null!, _stationOptions = null!;
    private Button _trackTab = null!, _stationTab = null!;
    public string Mode = "track";
    public bool Enabled, Grid;
    public int Gauge = 1435, Source, SourceSide = 1, HoverNode;
    public float EndAngle;
    public TrackHit? HoverSegment, PendingSplit;
    private string _confirmation = "";
    private sealed class DragState
    {
        public int Id; public Vector3 Point; public Vector2 Mouse; public TrackEdge? Edge; public bool Active;
    }
    private DragState? _drag;
    private MeshInstance3D? _splitMarker;
    public override void _Ready()
    {
        BuildUI();
        SetEnabled(false);
    }
    private static Button Button(Node parent, string text, Action callback, string tooltip = "")
    {
        var button = new Button { Text = text, TooltipText = tooltip };
        UiSkin.Button(button);
        button.Pressed += callback;
        parent.AddChild(button);
        return button;
    }
    private void BuildUI()
    {
        Toolbar = Windows.Create("Build track", new(24, 85, 330, 460), new(280, 240));
        Toolbar.Opened += () => ApplyEnabled(true);
        Toolbar.Closed += () => ApplyEnabled(false);
        Toolbar.Minimized += () => ApplyEnabled(false);
        var box = Toolbar.Body;
        var tabs = new HBoxContainer();
        box.AddChild(tabs);
        _trackTab = Button(tabs, "Track", () => SetMode("track"), "Track");
        _stationTab = Button(tabs, "Stations", () => SetMode("station"), "Stations");
        var group = new ButtonGroup();
        foreach (var tab in new[] { _trackTab, _stationTab })
        {
            tab.ToggleMode = true;
            tab.ButtonGroup = group;
            tab.CustomMinimumSize = new(130, 36);
            tab.AddThemeConstantOverride("icon_max_width", 18);
        }
        _trackTab.Icon = UiSkin.Icon("track");
        _trackTab.SetPressedNoSignal(true);
        _stationTab.Icon = UiSkin.Icon("station");
        _trackOptions = new();
        box.AddChild(_trackOptions);
        UiSkin.Label(_trackOptions, "TRACK GAUGE", 9, "c0cbbd");
        GaugePicker = new();
        UiSkin.Selector(GaugePicker);
        foreach (int mm in TrackNetwork.Gauges)
            GaugePicker.AddItem($"{mm} mm", mm);
        GaugePicker.ItemSelected += index => { Gauge = GaugePicker.GetItemId((int)index); Cancel(); };
        _trackOptions.AddChild(GaugePicker);
        _stationOptions = new()
        {
            Visible = false
        };
        box.AddChild(_stationOptions);
        UiSkin.Label(_stationOptions, "STATION TYPE", 9, "c0cbbd");
        var stationType = new OptionButton();
        stationType.AddItem("Simple platform");
        UiSkin.Selector(stationType);
        _stationOptions.AddChild(stationType);
        Status = new()
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new(0, 0),
            Text = "Click to place the first node. Esc ends construction."
        };
        box.AddChild(Status);
        var actions = new HBoxContainer();
        box.AddChild(actions);
        Button(actions, "Cancel (Esc)", Cancel);
        Button(actions, "Grid (G)", ToggleGrid);
        NodeMenu = new();
        Interface.AddChild(NodeMenu);
        SegmentMenu = new();
        Interface.AddChild(SegmentMenu);
    }
    public void SetEnabled(bool value)
    {
        ApplyEnabled(value);
        if (value)
            Toolbar.Open();
        else
            Toolbar.Close();
    }
    private void ApplyEnabled(bool value)
    {
        bool changed = Enabled != value;
        Enabled = value;
        Renderer.Markers.Visible = value && Mode == "track";
        if (!value)
        {
            Cancel();
            NodeMenu.Hide();
            SegmentMenu.Hide();
        }
        if (changed)
            EnabledChanged?.Invoke(value);
    }
    public void SetMode(string value)
    {
        Cancel();
        Mode = value;
        _trackOptions.Visible = Mode == "track";
        _stationOptions.Visible = Mode == "station";
        _trackTab.SetPressedNoSignal(Mode == "track");
        _stationTab.SetPressedNoSignal(Mode == "station");
        Renderer.Markers.Visible = Enabled && Mode == "track";
        Status.Text = Mode == "station" ? "Click a segment to place a platform on the mouse's side." : "Click to place the first node. Esc ends construction.";
    }
    public void ToggleGrid()
    {
        Grid = !Grid;
        Status.Text = $"World grid {(Grid ? "on" : "off")} (5 m).";
    }
    public void Cancel()
    {
        Source = 0;
        EndAngle = 0;
        PendingSplit = null;
        _confirmation = "";
        if (IsInstanceValid(Renderer.Preview))
            Renderer.Preview.Hide();
        if (IsInstanceValid(Status))
            Status.Text = "Click to place a node. Shift: straight alignment. Z/X: end direction.";
    }
    public override void _Process(double delta)
    {
        if (!Enabled || Windows.HasModal)
            return;
        if (IsInstanceValid(_splitMarker))
            _splitMarker!.Hide();
        if (_drag != null)
            return;
        var mouse = GetViewport().GetMousePosition();
        if (GetViewport().GuiGetHoveredControl() != null)
        {
            Renderer.Preview.Visible = false;
            return;
        }
        int node = PickNode(mouse);
        var hit = Renderer.Closest(Camera, mouse);
        if (node != HoverNode)
        {
            HoverNode = node;
            _confirmation = "";
            RebuildNodeMenu();
        }
        if (PendingSplit != null)
            HoverSegment = PendingSplit;
        else if (hit?.Edge.Id != HoverSegment?.Edge.Id)
        {
            HoverSegment = hit;
            _confirmation = "";
            RebuildSegmentMenu();
        }
        else
            HoverSegment = hit;
        UpdateMenuPositions();
        if (Source != 0 && Network.Nodes.ContainsKey(Source))
        {
            var end = PlacementPoint();
            Network.Nodes.TryGetValue(node, out var snap);
            if (snap != null && snap.Id != Source)
                end = snap.Position;
            Renderer.ShowPreview(PreviewControls(end, snap), Network.Nodes[Source].Gauge);
        }
        else if (hit != null && Mode == "track" && node == 0)
        {
            Renderer.Preview.Hide();
            ShowSplitMarker(PendingSplit?.Point ?? hit.Point);
        }
        else
            Renderer.Preview.Hide();
    }
    public int PickNode(Vector2 mouse)
    {
        int found = 0;
        float best = 15;
        foreach (var node in Network.Nodes.Values)
        {
            if (Camera.IsPositionBehind(node.Position) || Camera.Position.DistanceTo(node.Position) > 800)
                continue;
            float distance = Camera.UnprojectPosition(node.Position).DistanceTo(mouse);
            if (distance < best)
            {
                best = distance;
                found = node.Id;
            }
        }
        return found;
    }
    private (TrackEdge Edge, int Node, Vector3 Point)? PickHandle(Vector2 mouse)
    {
        if (!Network.Nodes.ContainsKey(Renderer.Selected))
            return null;
        foreach (var edge in Network.Incident(Renderer.Selected))
        {
            var controls = Network.Controls(edge);
            var point = controls[edge.A == Renderer.Selected ? 1 : 2];
            if (!Camera.IsPositionBehind(point) && Camera.UnprojectPosition(point).DistanceTo(mouse) < 12)
                return (edge, Renderer.Selected, point);
        }
        return null;
    }
    public Vector3 PlacementPoint()
    {
        var point = Camera.GroundPoint(Generator) + Vector3.Up * 0.2f;
        if (Grid)
        {
            point.X = Mathf.Snapped(point.X, 5);
            point.Z = Mathf.Snapped(point.Z, 5);
            point.Y = (float)Generator.Height(point.X, point.Z) + 0.2f;
        }
        if (Source != 0 && Input.IsPhysicalKeyPressed(Key.Shift))
        {
            var start = Network.Nodes[Source];
            point = start.Position + start.Axis * (point - start.Position).Dot(start.Axis);
        }
        return point;
    }
    public Vector3[] PreviewControls(Vector3 point, TrackNode? target = null)
    {
        var start = Network.Nodes[Source];
        var chord = point - start.Position;
        float length = chord.Length() / 3;
        bool locked = start.Group != 0 || Network.Incident(Source).Count != 0;
        var initial = locked ? start.Axis * SourceSide : chord.Normalized();
        var endDirection = chord.Normalized().Rotated(Vector3.Up, EndAngle);
        if (target != null && (target.Group != 0 || Network.Incident(target.Id).Count != 0))
            endDirection = target.Axis * (target.Axis.Dot(chord) >= 0 ? 1 : -1);
        return new[] { start.Position, start.Position + initial * length, point - endDirection * length, point };
    }
    public bool Place(Vector3 point, int targetId = 0)
    {
        if (Source == 0)
        {
            if (targetId != 0)
                BeginFrom(targetId, 1);
            else
            {
                var node = Network.AddNode(point, Gauge);
                Source = node.Id;
                Renderer.Selected = Source;
                Network.NotifyChanged();
            }
            return true;
        }
        if (!Network.Nodes.TryGetValue(Source, out var start))
        {
            Cancel();
            return false;
        }
        if (point.DistanceTo(start.Position) < 1)
            return false;
        bool created = targetId == 0;
        var target = created ? Network.AddNode(point, start.Gauge) : Network.Nodes[targetId];
        var endDirection = (point - start.Position).Normalized().Rotated(Vector3.Up, EndAngle);
        var edge = Network.ConnectNodes(start, target, SourceSide, endDirection);
        if (edge == null)
        {
            if (created)
                Network.Nodes.Remove(target.Id);
            Status.Text = "Cannot connect: check gauge, duplicate tracks and station-facing sides.";
            return false;
        }
        Source = target.Id;
        SourceSide = -edge.SideB;
        Renderer.Selected = Source;
        EndAngle = 0;
        Status.Text = "Click the next node to continue. Esc ends construction.";
        return true;
    }
    public void BeginFrom(int id, int side)
    {
        if (!Network.Nodes.TryGetValue(id, out var node) || !Network.CanExtend(node, side))
            return;
        Source = id;
        SourceSide = side;
        Gauge = node.Gauge;
        GaugePicker.Select(Array.IndexOf(TrackNetwork.Gauges, Gauge));
        Renderer.Selected = id;
        Renderer.UpdateMarkers();
        PendingSplit = null;
        _confirmation = "";
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (!Enabled || Windows.HasModal)
            return;
        if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.Keycode)
            {
                case Key.Escape:
                    Cancel();
                    break;
                case Key.G:
                    ToggleGrid();
                    break;
                case Key.Z:
                    EndAngle -= Mathf.DegToRad(5);
                    break;
                case Key.X:
                    EndAngle += Mathf.DegToRad(5);
                    break;
                default:
                    return;
            }
            GetViewport().SetInputAsHandled();
        }
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse)
            return;
        if (Mode == "station")
        {
            var hit = Renderer.Closest(Camera, mouse.Position, 35);
            if (hit != null)
            {
                var position = Camera.GroundPoint(Generator);
                int side = (position - hit.Point).Dot(TrackNetwork.Right(Network.Tangent(hit.Edge, hit.T))) >= 0 ? 1 : -1;
                Status.Text = Network.AddStation(hit.Edge, side) ? $"Station {hit.Edge.Station}: platform added." : "No platform here: junction segment or side already occupied.";
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        int node = PickNode(mouse.Position);
        var handle = PickHandle(mouse.Position);
        if (Source != 0)
            Place(node != 0 ? Network.Nodes[node].Position : PlacementPoint(), node);
        else if (handle is { } h)
            StartDrag(h.Node, h.Point, mouse.Position, h.Edge);
        else if (node != 0)
        {
            Renderer.Selected = node;
            Renderer.UpdateMarkers();
            StartDrag(node, Network.Nodes[node].Position, mouse.Position);
        }
        else
        {
            var hit = Renderer.Closest(Camera, mouse.Position);
            if (hit != null)
            {
                PendingSplit = hit;
                HoverSegment = hit;
                RebuildSegmentMenu();
                UpdateMenuPositions();
            }
            else
                Place(PlacementPoint());
        }
        GetViewport().SetInputAsHandled();
    }
    private void StartDrag(int id, Vector3 point, Vector2 mouse, TrackEdge? edge = null) => _drag = new() { Id = id, Point = point, Mouse = mouse, Edge = edge };
    public override void _Input(InputEvent input)
    {
        if (_drag == null)
            return;
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            _drag = null;
            Renderer.Dragging = false;
            Renderer.Dirty = true;
            GetViewport().SetInputAsHandled();
        }
        else if (input is InputEventMouseMotion motion)
        {
            if (!_drag.Active && motion.Position.DistanceTo(_drag.Mouse) < 4)
                return;
            _drag.Active = true;
            Renderer.Dragging = true;
            var original = _drag.Point;
            var ray = Camera.ProjectRayOrigin(motion.Position);
            var direction = Camera.ProjectRayNormal(motion.Position);
            var point = original;
            bool vertical = Input.IsPhysicalKeyPressed(Key.Shift);
            var normal = vertical ? new Vector3(Camera.Position.X - original.X, 0, Camera.Position.Z - original.Z).Normalized() : Vector3.Up;
            var plane = new Plane(normal, normal.Dot(original));
            var intersection = plane.IntersectsRay(ray, direction);
            var start = plane.IntersectsRay(Camera.ProjectRayOrigin(_drag.Mouse), Camera.ProjectRayNormal(_drag.Mouse));
            if (intersection is { } end && start is { } begin)
            {
                if (vertical)
                    point.Y += end.Y - begin.Y;
                else
                    point += end - begin;
            }
            if (!vertical && Grid)
            {
                point.X = Mathf.Snapped(point.X, 5);
                point.Z = Mathf.Snapped(point.Z, 5);
            }
            if (_drag.Edge == null)
                Network.MoveNode(Network.Nodes[_drag.Id], point);
            else
                Network.SetHandle(_drag.Edge, _drag.Id, point);
            GetViewport().SetInputAsHandled();
        }
    }
    private static void ClearButtons(HBoxContainer menu)
    {
        foreach (var child in menu.GetChildren())
        {
            menu.RemoveChild(child);
            child.QueueFree();
        }
    }
    private void RebuildNodeMenu()
    {
        ClearButtons(NodeMenu);
        if (!Network.Nodes.TryGetValue(HoverNode, out var node))
            return;
        int id = HoverNode;
        foreach (int side in new[] { -1, 1 })
            if (Network.CanExtend(node, side))
                Button(NodeMenu, side == -1 ? "+ ←" : "+ →", () => BeginFrom(id, side), "Continue track");
        Button(NodeMenu, "∥−", () => Parallel(id, -1), "Add parallel node on the left");
        Button(NodeMenu, "∥+", () => Parallel(id, 1), "Add parallel node on the right");
        if (Network.CanMerge(id))
            Button(NodeMenu, _confirmation == "merge" ? "Confirm merge" : "Merge", () => Confirm("merge", () => Network.Merge(id)));
        Button(NodeMenu, _confirmation == "node" ? "Confirm delete" : "×", () => Confirm("node", () => Network.DeleteNode(id)), "Delete node and connected segments; click twice");
    }
    public void Parallel(int id, int side)
    {
        int previous = Source;
        var added = Network.AddParallel(Network.Nodes[id], side);
        if (previous != 0)
            Place(added.Position, added.Id);
        else
        {
            Renderer.Selected = added.Id;
            Renderer.UpdateMarkers();
        }
    }
    private void Confirm(string action, Action callback)
    {
        if (_confirmation == action)
        {
            callback();
            _confirmation = "";
            if (!Network.Nodes.ContainsKey(Renderer.Selected))
                Renderer.Selected = 0;
            if (Source != 0 && !Network.Nodes.ContainsKey(Source))
                Cancel();
        }
        else
            _confirmation = action;
        Callable.From(RebuildNodeMenu).CallDeferred();
        Callable.From(RebuildSegmentMenu).CallDeferred();
    }
    private void RebuildSegmentMenu()
    {
        ClearButtons(SegmentMenu);
        if (HoverSegment == null || !Network.Edges.ContainsKey(HoverSegment.Edge.Id))
            return;
        var edge = HoverSegment.Edge;
        if (PendingSplit != null)
        {
            Button(SegmentMenu, "Confirm split", () => { if (PendingSplit != null) { var node = Network.Split(edge, PendingSplit.T); if (node != null) Renderer.Selected = node.Id; } PendingSplit = null; HoverSegment = null; SegmentMenu.Hide(); });
            Button(SegmentMenu, "Cancel", () => { PendingSplit = null; SegmentMenu.Hide(); });
        }
        else
        {
            Button(SegmentMenu, _confirmation == "straight" ? "Confirm straight" : "Straighten", () => Confirm("straight", () => Network.Straighten(edge)));
            Button(SegmentMenu, _confirmation == "edge" ? "Confirm delete" : "×", () => Confirm("edge", () => { Network.Edges.Remove(edge.Id); Network.NotifyChanged(); }), "Delete segment; click twice");
        }
    }
    private void UpdateMenuPositions()
    {
        NodeMenu.Visible = Mode == "track" && HoverNode != 0 && Network.Nodes.ContainsKey(HoverNode);
        if (NodeMenu.Visible)
        {
            var node = Network.Nodes[HoverNode];
            NodeMenu.Visible = Camera.Position.DistanceTo(node.Position) < 500 && !Camera.IsPositionBehind(node.Position);
            NodeMenu.Position = Camera.UnprojectPosition(node.Position) + new Vector2(12, -40);
        }
        SegmentMenu.Visible = Mode == "track" && HoverSegment != null && Source == 0 && HoverNode == 0;
        if (SegmentMenu.Visible)
        {
            var point = HoverSegment!.Point;
            SegmentMenu.Visible = Camera.Position.DistanceTo(point) < 500 && !Camera.IsPositionBehind(point);
            SegmentMenu.Position = Camera.UnprojectPosition(point) + new Vector2(12, -36);
        }
    }
    private void ShowSplitMarker(Vector3 point)
    {
        if (!IsInstanceValid(_splitMarker))
        {
            var mat = TrackRenderer.Material(new(0.9f, 0.7f, 0.15f, 0.5f));
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            _splitMarker = new()
            {
                Mesh = new SphereMesh { Radius = 0.7f, Height = 1.4f },
                MaterialOverride = mat
            };
            Renderer.AddChild(_splitMarker);
        }
        _splitMarker!.Position = point;
        _splitMarker.Visible = true;
    }
}
