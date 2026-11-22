using Godot;
using System;
using System.Linq;

namespace TransportSandbox;

/// <summary>Support properties and individual pier handles for one segment and its matching parallel spans.</summary>
public partial class SegmentEditor : Node
{
    public TrackEditor Editor = null!;
    public GameWindow Window = null!;
    public OptionButton SupportPicker = null!;
    public Button AddPier = null!, DeletePier = null!, ResetPiers = null!;
    public int EdgeId { get; private set; }
    private TrackNetwork Network => Editor.Network;
    private VBoxContainer _pierOptions = null!, _list = null!;
    private Label _summary = null!;
    private Node3D _handles = new();
    private int _selected = -1;
    private bool _dragging;
    private float _dragHeight;
    public override void _Ready()
    {
        Window = Editor.Windows.Create("Edit segment", WindowPlacement.Center, new(340, 390));
        _summary = UiSkin.Label(Window.Body, "", 11);
        UiSkin.Label(Window.Body, "SUPPORT TYPE", 9, "c0cbbd");
        SupportPicker = new(); TrackEditor.FillSupportPicker(SupportPicker); Window.Body.AddChild(SupportPicker);
        SupportPicker.ItemSelected += i => { if (Network.Edges.TryGetValue(EdgeId, out var e)) Network.SetSupport(e, (TrackSupport)i); };
        _pierOptions = new(); Window.Body.AddChild(_pierOptions);
        UiSkin.Label(_pierOptions, "Drag a pier handle along the bridge centreline. Select a pier to delete it.", 11).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var actions = new HBoxContainer(); _pierOptions.AddChild(actions);
        AddPier = new Button { Text = "Add pier" }; UiSkin.Button(AddPier); actions.AddChild(AddPier);
        DeletePier = new GameButton(); ((GameButton)DeletePier).SetContent("", "trash", "Delete selected pier"); actions.AddChild(DeletePier);
        ResetPiers = new Button { Text = "Automatic piers" }; UiSkin.Button(ResetPiers); _pierOptions.AddChild(ResetPiers);
        AddPier.Pressed += Add;
        DeletePier.Pressed += Delete;
        ResetPiers.Pressed += () => { if (Network.Edges.TryGetValue(EdgeId, out var e)) { _selected = -1; Network.SetBridgePiers(e, null); } };
        _list = new(); _pierOptions.AddChild(_list);
        Editor.Renderer.AddChild(_handles);
        Network.Changed += Refresh; Network.Space.Rebased += Rebased;
        Window.StateChanged += () => { _dragging = false; Refresh(); };
    }
    public override void _ExitTree()
    {
        Network.Changed -= Refresh; Network.Space.Rebased -= Rebased;
        if (IsInstanceValid(_handles)) _handles.QueueFree();
    }
    private void Rebased(TrackVector shift) { _dragging = false; Refresh(); }
    public void Open(int edge)
    {
        if (!Network.Edges.ContainsKey(edge)) return;
        Editor.Cancel(); EdgeId = edge; _selected = -1; Refresh(); Window.Open();
    }
    private BridgeLayout Layout() => new(Network, Network.SupportPeers(Network.Edges[EdgeId])[0]);
    private bool Active => Window.Visible && !Window.IsCollapsed && Editor.Windows.Active == Window && !Editor.Windows.HasModal;
    private void Refresh()
    {
        foreach (var child in _handles.GetChildren()) { _handles.RemoveChild(child); child.QueueFree(); }
        if (!Network.Edges.TryGetValue(EdgeId, out var edge)) { Window.Close(); return; }

        _summary.Text = $"Segment {EdgeId}\nLength: {new TrackCurve(Network.WorldControls(edge)).Length:F1} m\n{Network.SupportPeers(edge).Length} matching track segment(s)";
        SupportPicker.Select((int)edge.Support); _pierOptions.Visible = edge.Support == TrackSupport.ConcreteBridge;
        foreach (var child in _list.GetChildren()) { _list.RemoveChild(child); child.QueueFree(); }
        if (edge.Support != TrackSupport.ConcreteBridge) return;
        var layout = Layout(); var piers = layout.Piers;
        for (int i = 0; i < piers.Length; i++)
        {
            int index = i;
            var button = new Button { Text = $"Pier {i + 1}: {piers[i].Along * layout.Curve.Length:F1} m", ToggleMode = true, ButtonPressed = i == _selected };
            UiSkin.Button(button); _list.AddChild(button); button.Pressed += () => { _selected = index; Refresh(); };
            var position = layout.PierPoint(piers[i]) + new TrackVector(0, 2.5, 0);
            var material = TrackRenderer.Material(new(i == _selected ? "ffc45c" : "5acada"));
            material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded; material.NoDepthTest = true;
            _handles.AddChild(new MeshInstance3D { Name = $"PierHandle_{i}", Mesh = new SphereMesh { Radius = .5f, Height = 1 }, MaterialOverride = material,
                Position = Network.Space.ToLocal(position), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        DeletePier.Disabled = _selected < 0 || _selected >= piers.Length;
    }
    public void Add()
    {
        if (!Network.Edges.TryGetValue(EdgeId, out var edge) || edge.Support != TrackSupport.ConcreteBridge) return;
        var layout = Layout(); var piers = layout.Piers.ToList();
        var ends = new[] { 0d }.Concat(piers.Select(p => p.Along)).Append(1).ToArray();
        int largest = Enumerable.Range(0, ends.Length - 1).OrderByDescending(i => ends[i + 1] - ends[i]).First();
        var added = new BridgePier((ends[largest] + ends[largest + 1]) / 2); piers.Add(added);
        piers = piers.OrderBy(p => p.Along).ToList(); _selected = piers.IndexOf(added);
        Network.SetBridgePiers(layout.Edge, piers);
    }
    public void Delete()
    {
        if (!Network.Edges.TryGetValue(EdgeId, out var edge) || edge.Support != TrackSupport.ConcreteBridge) return;
        var layout = Layout(); var piers = layout.Piers.ToList();
        if (_selected < 0 || _selected >= piers.Count) return;
        piers.RemoveAt(_selected); _selected = -1; Network.SetBridgePiers(layout.Edge, piers);
    }
    public override void _Process(double delta)
    {
        _handles.Visible = Active;
        if (!Active) { _dragging = false; return; }
        if (!Network.Edges.TryGetValue(EdgeId, out var edge) || edge.Support != TrackSupport.ConcreteBridge) return;
        foreach (var child in _handles.GetChildren().OfType<Node3D>())
        {
            float distance = Editor.Camera.GlobalPosition.DistanceTo(child.GlobalPosition);
            child.Visible = distance <= 1000 && !Editor.Camera.IsPositionBehind(child.GlobalPosition);
            child.Scale = Vector3.One * Math.Max(.4f, distance * .012f);
        }
    }
    public override void _Input(InputEvent input)
    {
        // A release over window controls may never reach _UnhandledInput.
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) _dragging = false;
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (!Active || !Network.Edges.TryGetValue(EdgeId, out var edge) || edge.Support != TrackSupport.ConcreteBridge) return;
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        {
            if (!click.Pressed) { if (_dragging) { _dragging = false; GetViewport().SetInputAsHandled(); } return; }
            int index = 0;
            foreach (var handle in _handles.GetChildren().OfType<Node3D>())
            {
                if (handle.Visible && Editor.Camera.UnprojectPosition(handle.GlobalPosition).DistanceTo(click.Position) < 13)
                {
                    _selected = index; _dragHeight = handle.GlobalPosition.Y; _dragging = true; Refresh(); GetViewport().SetInputAsHandled(); return;
                }
                index++;
            }
        }
        if (_dragging && input is InputEventMouseMotion motion)
        {
            var camera = Editor.Camera;
            var point = new Plane(Vector3.Up, _dragHeight).IntersectsRay(camera.ProjectRayOrigin(motion.Position), camera.ProjectRayNormal(motion.Position));
            if (point is { } local)
            {
                var layout = Layout(); var piers = layout.Piers.ToList();
                if (_selected >= 0 && _selected < piers.Count)
                {
                    var pier = layout.Closest(Network.Space.ToWorld(local)); piers[_selected] = pier;
                    piers = piers.OrderBy(p => p.Along).ToList(); _selected = piers.IndexOf(pier);
                    Network.SetBridgePiers(layout.Edge, piers);
                }
            }
            GetViewport().SetInputAsHandled();
        }
    }
}
