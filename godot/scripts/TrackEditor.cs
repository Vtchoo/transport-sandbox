using Godot;
using System;
using System.Linq;

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
    public TrackNodeHoverMenu NodeMenu = null!;
    public TrackSegmentHoverMenu SegmentMenu = null!;
    private TrackEdge? _segmentAction;
    private string _segmentConfirmation = "";
    private readonly System.Collections.Generic.Dictionary<int, Vector3> _midpoints = new();
    public int HoverSide = 1;
    private int _pointerNode;
    public bool BuildingParallel
    {
        get; private set;
    }
    private const float ActionDistance = 600;
    private (int Source, int Side, int Target, Vector3 Point, float Angle)? _parallelGhostKey;
    private bool _parallelGhostValid;
    public Label Status = null!;
    public OptionButton GaugePicker = null!, StationAction = null!, GridPicker = null!;
    public WorldTrackGrid GridOverlay = null!;
    private int _lastGridSpacing = 5;
    public TrackVisual? PlatformPreview
    {
        get; private set;
    }
    public TrackHit? PlatformTarget
    {
        get; private set;
    }
    public int PlatformSide { get; private set; } = 1;
    private (int Edge, int Side, bool Remove)? _platformPreviewKey;
    public StationWindows Stations = null!;
    private VBoxContainer _trackOptions = null!, _stationOptions = null!;
    private Button _trackTab = null!, _stationTab = null!;
    public string Mode = "track";
    public bool Enabled;
    public bool Grid => GridOverlay.Spacing != 0;
    public int Gauge = 1435, Source, SourceSide = 1, HoverNode;
    public float EndAngle;
    public TrackHit? HoverSegment, PendingSplit;
    private string _confirmation = "";
    private sealed class DragState
    {
        public int Id; public Vector3 Point; public Vector2 Mouse; public TrackEdge? Edge; public bool Active, Spacing, Vertical; public float InitialOffset;
    }
    private DragState? _drag;
    public bool IsDragging => _drag != null;
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
        Toolbar = Windows.Create("Build track", WindowPlacement.TopLeft, new Vector2(330, 460), new(0, 61), new(280, 240));
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
        UiSkin.Label(_trackOptions, "WORLD GRID SNAP - G", 9, "c0cbbd");
        GridPicker = new();
        UiSkin.Selector(GridPicker);
        foreach (int spacing in WorldTrackGrid.Spacings)
            GridPicker.AddItem(spacing == 0 ? "Off" : $"{spacing} m", spacing);
        GridPicker.ItemSelected += index => SetGridSpacing(GridPicker.GetItemId((int)index));
        _trackOptions.AddChild(GridPicker);
        GridOverlay = new();
        Renderer.AddChild(GridOverlay);
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
        UiSkin.Label(_stationOptions, "ACTION", 9, "c0cbbd");
        StationAction = new();
        StationAction.AddItem("Build platform");
        StationAction.AddItem("Remove platform");
        UiSkin.Selector(StationAction);
        StationAction.ItemSelected += _ => ClearPlatformPreview();
        _stationOptions.AddChild(StationAction);
        Stations = new()
        {
            Network = Network,
            Windows = Windows,
            Renderer = Renderer
        };
        AddChild(Stations);
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
        NodeMenu.Direction.Pressed += () =>
        {
            if (!Network.Nodes.TryGetValue(HoverNode, out var node))
                return;
            if (node.Gauge != Gauge)
            {
                Status.Text = $"Gauge mismatch. Select {node.Gauge:N0} mm to use this node.";
                return;
            }
            BeginFrom(node.Id, HoverSide);
        };
        NodeMenu.ContinueParallel.Pressed += () => BeginParallelFrom(HoverNode, HoverSide);
        NodeMenu.ParallelLeft.Pressed += () => Parallel(HoverNode, -1);
        NodeMenu.ParallelRight.Pressed += () => Parallel(HoverNode, 1);
        NodeMenu.Delete.Pressed += () => { int id = HoverNode; Confirm("node", () => Network.DeleteNode(id)); };
        NodeMenu.Merge.Pressed += () => { int id = HoverNode; Confirm("merge", () => Network.Merge(id)); };
        Network.Changed += GraphChanged;
        Network.Space.Rebased += Rebased;
        GridOverlay.Space = Network.Space;
        SegmentMenu = new();
        Interface.AddChild(SegmentMenu);
        SegmentMenu.Straighten.Pressed += () => ConfirmSegment("straight");
        SegmentMenu.Delete.Pressed += () => ConfirmSegment("edge");
        SegmentMenu.ConfirmSplit.Pressed += () =>
        {
            var pending = PendingSplit;
            if (pending != null && Network.Edges.ContainsKey(pending.Edge.Id))
            {
                var node = Network.Split(pending.Edge, pending.T);
                if (node != null)
                    Renderer.Selected = node.Id;
            }
            Cancel();
        };
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
        NodeMenu.Hide();
        SegmentMenu.Hide();
        Status.Text = Mode == "station" ? "Click a segment to place a platform on the mouse's side." : "Click to place the first node. Esc ends construction.";
    }
    public void ToggleGrid()
    {
        SetGridSpacing(Grid ? 0 : _lastGridSpacing);
    }
    public void SetGridSpacing(int spacing)
    {
        if (!WorldTrackGrid.Spacings.Contains(spacing))
            return;
        GridOverlay.SetSpacing(spacing);
        GridPicker.Select(Array.IndexOf(WorldTrackGrid.Spacings, spacing));
        if (spacing != 0)
            _lastGridSpacing = spacing;
        _parallelGhostKey = null;
        Status.Text = spacing == 0 ? "World grid snap off." : $"World grid snap - {spacing} m. Existing nodes and Shift straight snap take priority.";
    }
    public void Cancel()
    {
        if (_drag != null)
        {
            _drag = null;
            Renderer.Dragging = Renderer.SpacingDrag = false;
            Renderer.Dirty = true;
        }
        ClearPlatformPreview();
        GridOverlay.Hide();
        Source = 0;
        BuildingParallel = false;
        _parallelGhostKey = null;
        EndAngle = 0;
        PendingSplit = null;
        _segmentAction = null;
        _segmentConfirmation = "";
        _confirmation = "";
        if (IsInstanceValid(SegmentMenu))
            SegmentMenu.Hide();
        if (IsInstanceValid(_splitMarker))
            _splitMarker!.Hide();
        if (IsInstanceValid(Renderer.Preview))
            Renderer.Preview.Hide();
        if (IsInstanceValid(Status))
            Status.Text = Mode == "station" ? "Hover beside an existing track, then click to build a platform." : "Click to place a node. Shift: straight alignment. Z/X: end direction.";
    }
    public override void _Process(double delta)
    {
        if (!Enabled || Windows.HasModal || _drag != null)
        {
            if (_drag != null)
                GridOverlay.ShowAt(Network.Nodes[_drag.Id].Position, Enabled && !Windows.HasModal && Mode == "track");
            else
                GridOverlay.Hide();
            NodeMenu.Hide();
            SegmentMenu.Hide();
            if (IsInstanceValid(_splitMarker))
                _splitMarker!.Hide();
            ClearPlatformPreview();
            if (Windows.HasModal)
                Renderer.Preview.Hide();
            return;
        }
        if (Mode == "station")
        {
            GridOverlay.Hide();
            NodeMenu.Hide();
            SegmentMenu.Hide();
            Renderer.Preview.Hide();
            if (IsInstanceValid(_splitMarker))
                _splitMarker!.Hide();
            if (GetViewport().GuiGetHoveredControl() == null)
                RefreshPlatformHover(GetViewport().GetMousePosition());
            else
                ClearPlatformPreview();
            return;
        }
        if (IsInstanceValid(_splitMarker))
            _splitMarker!.Visible = PendingSplit != null && !Camera.IsPositionBehind(PendingSplit.Point) && Camera.Position.DistanceTo(PendingSplit.Point) <= ActionDistance;
        UpdateMenuPositions();
        var mouse = GetViewport().GetMousePosition();
        if (GetViewport().GuiGetHoveredControl() is { } hovered)
        {
            GridOverlay.Hide();
            Renderer.Preview.Hide();
            if (hovered != NodeMenu && hovered != SegmentMenu && !NodeMenu.IsAncestorOf(hovered) && !SegmentMenu.IsAncestorOf(hovered))
            {
                HoverNode = 0;
                _confirmation = "";
                NodeMenu.Hide();
                SegmentMenu.Hide();
                _segmentAction = null;
                _segmentConfirmation = "";
            }
            return;
        }
        var gridPoint = Grid ? Camera.GroundPoint(Generator) + Vector3.Up * .2f : (Vector3?)null;
        GridOverlay.ShowAt(gridPoint, gridPoint.HasValue && Camera.Position.DistanceTo(gridPoint.Value) <= ActionDistance);
        RefreshHover(mouse);
        int node = _pointerNode;
        var hit = HoverSegment;
        if (Source != 0 && Network.Nodes.ContainsKey(Source))
        {
            var end = PlacementPoint();
            Network.Nodes.TryGetValue(node, out var snap);
            if (snap != null && snap.Id != Source)
                end = snap.Position;
            if (BuildingParallel)
            {
                var key = (Source, SourceSide, snap?.Id ?? 0, end.Snapped(Vector3.One * .1f), EndAngle);
                if (_parallelGhostKey != key)
                {
                    _parallelGhostKey = key;
                    try
                    {
                        Renderer.ShowPreviews(Network.PreviewParallel(Network.Nodes[Source], SourceSide, end, snap, (end - Network.Nodes[Source].Position).Normalized().Rotated(Vector3.Up, EndAngle)).Select(p => p.Controls).ToArray(), Network.Nodes[Source].Gauge);
                        _parallelGhostValid = true;
                    }
                    catch (InvalidOperationException error) { _parallelGhostValid = false; Status.Text = error.Message; }
                }
                Renderer.Preview.Visible = _parallelGhostValid;
            }
            else
                Renderer.ShowPreview(PreviewControls(end, snap), Network.Nodes[Source].Gauge);
        }
        else if (hit != null && Mode == "track" && node == 0 && !Camera.IsPositionBehind(hit.Point) && Camera.Position.DistanceTo(hit.Point) <= ActionDistance)
        {
            Renderer.Preview.Hide();
            ShowSplitMarker(PendingSplit?.Point ?? hit.Point);
        }
        else
            Renderer.Preview.Hide();
    }
    private void GraphChanged()
    {
        _confirmation = "";
        _segmentConfirmation = "";
        _midpoints.Clear();
        ClearPlatformPreview();
        PendingSplit = null;
        _parallelGhostKey = null;
        RebuildSegmentMenu();
        UpdateMenuPositions();
    }
    public override void _ExitTree() { Network.Changed -= GraphChanged; Network.Space.Rebased -= Rebased; }
    private void Rebased(TrackVector worldShift)
    {
        var shift = worldShift.Float();
        _midpoints.Clear(); _parallelGhostKey = null;
        if (_drag != null) _drag.Point -= shift;
        if (PendingSplit is { } split) PendingSplit = split with { Point = split.Point - shift };
        if (HoverSegment is { } hover) HoverSegment = hover with { Point = hover.Point - shift };
        if (PlatformTarget is { } platform) PlatformTarget = platform with { Point = platform.Point - shift };
        if (IsInstanceValid(_splitMarker)) _splitMarker!.Position -= shift;
        if (IsInstanceValid(PlatformPreview)) PlatformPreview!.Position -= shift;
        GridOverlay.Position -= shift;
    }
    internal void RefreshHover(Vector2 mouse)
    {
        int nearest = 0;
        float distance = 100;
        foreach (var candidate in Network.Nodes.Values)
        {
            if (Camera.IsPositionBehind(candidate.Position) || Camera.Position.DistanceTo(candidate.Position) > ActionDistance)
                continue;
            float d = Camera.UnprojectPosition(candidate.Position).DistanceTo(mouse);
            if (d < distance)
            {
                nearest = candidate.Id;
                distance = d;
            }
        }
        if (PendingSplit != null)
            nearest = 0;
        _pointerNode = PickNode(mouse);
        if (nearest != HoverNode)
        {
            HoverNode = nearest;
            _confirmation = "";
        }
        if (nearest != 0)
        {
            var node = Network.Nodes[nearest];
            var center = Camera.UnprojectPosition(node.Position);
            HoverSide = (mouse - center).Dot(TrackNodeHoverMenu.ScreenAxis(Camera, node.Position, node.Axis)) >= 0 ? 1 : -1;
        }
        HoverSegment = PendingSplit ?? Renderer.Closest(Camera, mouse);
        TrackEdge? action = null;
        float best = 60;
        if (PendingSplit == null)
            foreach (var edge in Network.Edges.Values)
            {
                var midpoint = Midpoint(edge);
                if (Camera.IsPositionBehind(midpoint) || Camera.Position.DistanceTo(midpoint) > ActionDistance)
                    continue;
                float d = Camera.UnprojectPosition(midpoint).DistanceTo(mouse);
                if (d < best)
                {
                    best = d;
                    action = edge;
                }
            }
        if (_segmentAction?.Id != action?.Id)
            _segmentConfirmation = "";
        _segmentAction = action;
        RebuildSegmentMenu();
        UpdateMenuPositions();
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
    internal int PickSpacing(Vector2 mouse)
    {
        int found = 0;
        float best = 11;
        foreach (int id in Renderer.SpacingHandleNodes())
        {
            var node = Network.Nodes[id];
            if (Camera.IsPositionBehind(node.Position) || Camera.Position.DistanceTo(node.Position) > ActionDistance)
                continue;
            float distance = Camera.UnprojectPosition(Renderer.SpacingPosition(node)).DistanceTo(mouse);
            if (distance < best)
            {
                found = id;
                best = distance;
            }
        }
        return found;
    }
    public Vector3 PlacementPoint()
    {
        var point = Camera.GroundPoint(Generator) + Vector3.Up * 0.2f;
        return ConstrainPlacementPoint(point, Input.IsPhysicalKeyPressed(Key.Shift));
    }
    internal Vector3 ConstrainPlacementPoint(Vector3 point, bool straight)
    {
        if (Source != 0 && straight)
        {
            var start = Network.Nodes[Source];
            float along = SourceSide * Math.Max(0, (point - start.Position).Dot(start.Axis) * SourceSide);
            return start.Position + start.Axis * along;
        }
        point = GridOverlay.Snap(point);
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
        if (BuildingParallel)
        {
            try
            {
                Network.Nodes.TryGetValue(targetId, out var existing);
                var result = Network.ExtendParallel(start, SourceSide, point, existing, (point - start.Position).Normalized().Rotated(Vector3.Up, EndAngle));
                Source = result.Node.Id;
                SourceSide = result.Side;
                Renderer.Selected = Source;
                EndAngle = 0;
                Status.Text = $"{result.Edges.Length} parallel tracks built. Keep clicking to continue; Esc stops.";
                return true;
            }
            catch (InvalidOperationException error) { Status.Text = error.Message; return false; }
        }
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
        BuildingParallel = false;
        _parallelGhostKey = null;
        Source = id;
        SourceSide = side;
        Gauge = node.Gauge;
        GaugePicker.Select(Array.IndexOf(TrackNetwork.Gauges, Gauge));
        Renderer.Selected = id;
        Renderer.UpdateMarkers();
        PendingSplit = null;
        _segmentAction = null;
        _segmentConfirmation = "";
        _confirmation = "";
        if (IsInstanceValid(SegmentMenu))
            SegmentMenu.Hide();
        if (IsInstanceValid(_splitMarker))
            _splitMarker!.Hide();
    }
    public void BeginParallelFrom(int id, int side)
    {
        if (!Network.Nodes.TryGetValue(id, out var node) || !Network.CanExtendParallel(node, side))
            return;
        BeginFrom(id, side);
        BuildingParallel = true;
        NodeMenu.Hide();
        Status.Text = "Building all parallel tracks. Click to place their next nodes; Esc stops.";
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (Windows.HasModal)
            return;
        if (!Enabled)
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click && Stations.OpenAt(Camera, click.Position))
                GetViewport().SetInputAsHandled();
            return;
        }
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
        if ((Mode != "station" || StationAction.Selected != 1) && Source == 0 && Stations.OpenAt(Camera, mouse.Position))
        {
            ClearPlatformPreview();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (Mode == "station")
        {
            RefreshPlatformHover(mouse.Position);
            var hit = PlatformTarget;
            if (hit == null)
                Status.Text = "Choose an existing track segment.";
            else if (StationAction.Selected == 1)
                Status.Text = Network.RemovePlatform(hit.Edge, PlatformSide) ? "Platform removed." : "This side has no platform.";
            else if (hit.Edge.Platforms.Contains(PlatformSide))
                Stations.Open(hit.Edge.Station);
            else if (Network.AddStation(hit.Edge, PlatformSide))
                Status.Text = $"Platform added to Station {hit.Edge.Station}.";
            else
                Status.Text = "No platforms on junction segments. Split beyond the junction first.";
            GetViewport().SetInputAsHandled();
            return;
        }
        if (PendingSplit != null)
        {
            Cancel();
            GetViewport().SetInputAsHandled();
            return;
        }
        int node = PickNode(mouse.Position);
        var handle = PickHandle(mouse.Position);
        int spacing = PickSpacing(mouse.Position);
        if (Source != 0 && handle == null && spacing == 0 && node != Source)
            Place(node != 0 ? Network.Nodes[node].Position : PlacementPoint(), node);
        else if (spacing != 0)
        {
            Renderer.Selected = spacing;
            Renderer.UpdateMarkers();
            StartDrag(spacing, Renderer.SpacingPosition(Network.Nodes[spacing]), mouse.Position, spacing: true);
        }
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
    private void StartDrag(int id, Vector3 point, Vector2 mouse, TrackEdge? edge = null, bool spacing = false)
    {
        NodeMenu.Hide();
        SegmentMenu.Hide();
        Renderer.Preview.Hide();
        _drag = new()
        {
            Id = id,
            Point = point,
            Mouse = mouse,
            Edge = edge,
            Spacing = spacing,
            Vertical = !spacing && Input.IsPhysicalKeyPressed(Key.Shift),
            InitialOffset = spacing ? Network.Groups[Network.Nodes[id].Group].Offsets[id] : 0
        };
    }
    public override void _Input(InputEvent input)
    {
        if (_drag == null)
            return;
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            _drag = null;
            Renderer.Dragging = false;
            Renderer.SpacingDrag = false;
            Renderer.Dirty = true;
            GetViewport().SetInputAsHandled();
        }
        else if (input is InputEventMouseMotion motion)
        {
            if (!_drag.Active && motion.Position.DistanceTo(_drag.Mouse) < 4)
                return;
            _drag.Active = true;
            Renderer.Dragging = true;
            Renderer.SpacingDrag = _drag.Spacing;
            var node = Network.Nodes[_drag.Id];
            bool vertical = !_drag.Spacing && (motion.ShiftPressed || Input.IsPhysicalKeyPressed(Key.Shift));
            if (_drag.Vertical != vertical)
            {
                _drag.Vertical = vertical;
                _drag.Point = _drag.Edge != null ? Network.Controls(_drag.Edge)[_drag.Edge.A == node.Id ? 1 : 2] : node.Position;
                _drag.Mouse = motion.Position;
                GetViewport().SetInputAsHandled();
                return;
            }
            var point = _drag.Point;
            if (vertical)
            {
                float units = Math.Max(1, Camera.Position.DistanceTo(point)) * 2 * Mathf.Tan(Mathf.DegToRad(Camera.Fov / 2)) / Math.Max(1, GetViewport().GetVisibleRect().Size.Y);
                point.Y += (_drag.Mouse.Y - motion.Position.Y) * units;
            }
            else
            {
                var plane = new Plane(Vector3.Up, point.Y);
                var end = plane.IntersectsRay(Camera.ProjectRayOrigin(motion.Position), Camera.ProjectRayNormal(motion.Position));
                if (end == null)
                    return;
                point = end.Value;
            }
            if (_drag.Spacing)
            {
                var group = Network.Groups[node.Group];
                Network.SetParallelOffset(node, _drag.InitialOffset + (point - _drag.Point).Dot(TrackNetwork.Right(group.Axis)));
                Status.Text = "Parallel spacing - lateral movement only.";
            }
            else if (_drag.Edge != null)
                Network.SetHandle(_drag.Edge, node.Id, point);
            else
                Network.MoveNode(node, vertical ? point : GridOverlay.Snap(point));
            GridOverlay.ShowAt(node.Position, Enabled && Mode == "track");
            GetViewport().SetInputAsHandled();
        }
    }
    private void RebuildNodeMenu() => UpdateMenuPositions();
    public void Parallel(int id, int side)
    {
        if (!Network.Nodes.TryGetValue(id, out var node))
            return;
        int previous = Source;
        var added = Network.AddParallel(node, side);
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
    private Vector3 Midpoint(TrackEdge edge)
    {
        if (!_midpoints.TryGetValue(edge.Id, out var point))
            _midpoints[edge.Id] = point = new TrackCurve(Network.Controls(edge)).PointAt(.5).Float();
        return point;
    }
    private void ConfirmSegment(string action)
    {
        var edge = _segmentAction;
        if (edge == null || !Network.Edges.ContainsKey(edge.Id))
            return;
        if (_segmentConfirmation == action)
        {
            if (action == "straight")
                Network.Straighten(edge);
            else
            {
                Network.Edges.Remove(edge.Id);
                Network.NotifyChanged();
            }
            _segmentAction = null;
            _segmentConfirmation = "";
            SegmentMenu.Hide();
        }
        else
            _segmentConfirmation = action;
        RebuildSegmentMenu();
        UpdateMenuPositions();
    }
    private void RebuildSegmentMenu() => SegmentMenu.SetState(PendingSplit != null, _segmentConfirmation);
    private void UpdateMenuPositions()
    {
        NodeMenu.Visible = Enabled && !Windows.HasModal && _drag == null && PendingSplit == null && Mode == "track" &&
            Network.Nodes.TryGetValue(HoverNode, out var hover) && !Camera.IsPositionBehind(hover.Position) && Camera.Position.DistanceTo(hover.Position) <= ActionDistance;
        if (NodeMenu.Visible)
        {
            var node = Network.Nodes[HoverNode];
            NodeMenu.Direction.Visible = Source == 0 && Network.CanExtend(node, HoverSide);
            NodeMenu.ContinueParallel.Visible = Source == 0 && Network.CanExtendParallel(node, HoverSide);
            NodeMenu.Merge.Visible = Network.CanMerge(node.Id);
            NodeMenu.SetConfirmations(_confirmation == "node", _confirmation == "merge");
            NodeMenu.PositionButtons(Camera, node, HoverSide);
            NodeMenu.Direction.TooltipText = $"Start track from node {node.Id} - {node.Gauge:N0} mm";
            NodeMenu.Direction.Modulate = node.Gauge == Gauge ? Colors.White : new Color("ee9988");
        }
        var target = PendingSplit?.Edge ?? _segmentAction;
        SegmentMenu.Visible = Enabled && !Windows.HasModal && _drag == null && Mode == "track" && target != null && Network.Edges.ContainsKey(target.Id) && Source == 0;
        if (SegmentMenu.Visible)
        {
            var point = PendingSplit?.Point ?? Midpoint(target!);
            SegmentMenu.Visible = Camera.Position.DistanceTo(point) <= ActionDistance && !Camera.IsPositionBehind(point);
            SegmentMenu.PositionButtons(Camera.UnprojectPosition(point), PendingSplit != null);
        }
    }

    private void ClearPlatformPreview()
    {
        PlatformTarget = null;
        _platformPreviewKey = null;
        if (IsInstanceValid(PlatformPreview))
        {
            PlatformPreview!.GetParent()?.RemoveChild(PlatformPreview);
            PlatformPreview.QueueFree();
        }
        PlatformPreview = null;
    }
    internal void RefreshPlatformHover(Vector2 mouse)
    {
        var hit = Renderer.Closest(Camera, mouse, 45);
        if (hit == null || Camera.Position.DistanceTo(hit.Point) > ActionDistance)
        {
            ClearPlatformPreview();
            return;
        }
        var ray = Camera.ProjectRayOrigin(mouse);
        var direction = Camera.ProjectRayNormal(mouse);
        var point = new Plane(Vector3.Up, hit.Point.Y).IntersectsRay(ray, direction);
        int side = point is { } p && (p - hit.Point).Dot(TrackNetwork.Right(Network.Tangent(hit.Edge, hit.T))) < 0 ? -1 : 1;
        bool remove = StationAction.Selected == 1;
        if (remove ? !hit.Edge.Platforms.Contains(side) : !Network.CanPlaceStation(hit.Edge, side))
        {
            ClearPlatformPreview();
            PlatformTarget = hit;
            PlatformSide = side;
            Status.Text = hit.Edge.Platforms.Contains(side) ? $"Station {hit.Edge.Station}: click the platform to open its window." : "No platforms on junction segments. Split beyond the junction first.";
            return;
        }
        var key = (hit.Edge.Id, side, remove);
        if (_platformPreviewKey != key)
        {
            ClearPlatformPreview();
            PlatformPreview = new(new TrackGeometry(Network.Controls(hit.Edge), hit.Edge.Gauge, new[] { side }, preview: true, platformsOnly: true), preview: true);
            if (remove)
                foreach (MeshInstance3D mesh in PlatformPreview.GetChildren())
                    ((StandardMaterial3D)mesh.MaterialOverride).AlbedoColor = new Color("ef7864") { A = .45f };
            Renderer.AddChild(PlatformPreview);
            _platformPreviewKey = key;
        }
        PlatformTarget = hit;
        PlatformSide = side;
        Status.Text = remove ? "Click to remove this platform." : $"Build full-segment platform - {PlatformPreview!.Geometry.Curve.Length:F1} m. Move across the track to change sides.";
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
