using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public partial class SignalWindows : Node
{
    public TrackNetwork Network = null!;
    public RailInterlocking Interlocking = null!;
    public WindowManager Windows = null!;
    public SignalRenderer Renderer = null!;
    public Camera3D Camera = null!;
    public Node Interface = null!;
    public readonly Dictionary<SignalKey, GameWindow> Views = new();
    private readonly Dictionary<SignalKey, Label> _labels = new();
    public SignalScopeOverlay Overlay = null!;
    public SignalKey? Focused { get; private set; }
    public SignalScope? Scope { get; private set; }
    public SignalPaths? Paths { get; private set; }
    private bool _dirty = true;
    public override void _Ready()
    {
        Overlay = new() { Network = Network, Camera = Camera }; Interface.AddChild(Overlay);
        Network.Changed += Changed;
    }
    private void Changed() => _dirty = true;
    public void Open(SignalKey key)
    {
        if (!Network.Signals.ContainsKey(key)) return;
        if (!Views.ContainsKey(key) && Views.Count > 0)
        {
            var previous = Views.Single();
            Views.Remove(previous.Key); Views.Add(key, previous.Value);
            var label = _labels[previous.Key]; _labels.Remove(previous.Key); _labels.Add(key, label);
            previous.Value.WindowTitle = $"Signal {key.Node} ({(key.Side > 0 ? "+" : "−")})";
        }
        if (!Views.TryGetValue(key, out var window))
        {
            window = Windows.Create($"Signal {key.Node} ({(key.Side > 0 ? "+" : "−")})", WindowPlacement.CenterRight, new(360, 280));
            Views.Add(key, window);
            var label = UiSkin.Label(window.Body, "", 12); label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _labels.Add(key, label);
            window.StateChanged += Changed;
        }
        window.Open(); _dirty = true; _Process(0);
    }
    public bool OpenAt(Vector2 mouse)
    {
        if (Windows.HasModal || Renderer.Pick(Camera, mouse) is not { } key) return false;
        Open(key); return true;
    }
    public override void _Process(double delta)
    {
        foreach (var key in Views.Keys.Where(k => !Network.Signals.ContainsKey(k)).ToArray())
        { Windows.Unregister(Views[key]); Views.Remove(key); _labels.Remove(key); _dirty = true; }
        var active = Views.FirstOrDefault(p => p.Value == Windows.Active && p.Value.Visible && !p.Value.IsCollapsed);
        SignalKey? focused = active.Value != null && !Windows.HasModal ? active.Key : null;
        if (focused != Focused) { Focused = focused; _dirty = true; }
        if (_dirty)
        {
            _dirty = false; Overlay.Clear(); Scope = null; Paths = null;
            if (Focused is { } key)
            {
                Scope = Interlocking.InspectScope(key);
                if (Network.Signals[key].Kind == RailSignalKind.Path)
                {
                    Paths = Interlocking.InspectPaths(key);
                    Overlay.Add(Paths.Others.Where(p => !p.Conflict).SelectMany(p => p.Edges).Distinct().ToArray(), new("54a6f5"));
                    Overlay.Add(Paths.Others.Where(p => p.Conflict).SelectMany(p => p.Edges).Distinct().ToArray(), new("f05b53"), 7);
                    // Keep a narrow blue center on shared red sections: both routes remain legible.
                    Overlay.Add(Paths.Selected.SelectMany(p => p.Edges).Distinct().ToArray(), new("54a6f5"), 3, false);
                }
                else Overlay.Add(Scope.Edges, new("6cdae1"));
            }
        }
        Overlay.Visible = Focused != null; Overlay.QueueRedraw();
        foreach (var (key, label) in _labels)
        {
            var signal = Network.Signals[key];
            string info = $"{signal.Kind} signal\nState: {signal.Aspect}\nNode: {key.Node} · Direction: {(key.Side > 0 ? "+" : "−")}\n\n";
            if (Focused == key && Scope != null)
            {
                if (Paths != null)
                    info += $"Blue: this signal's routes and non-conflicting routes.\nRed: other entries sharing or crossing this signal's routes.\nShared sections have a blue center and red border.\n\n{Paths.Selected.Length} routes from this signal; {Paths.Others.Length} from other entries."
                        + (Paths.Truncated ? "\nPreview limit reached; some routes are omitted or conservatively marked red." : "")
                        + "\nThis shows track conflicts, not live traffic availability.";
                else info += signal.Kind == RailSignalKind.Normal ? "Following block highlighted." : "Protected chain through to normal exits and their receiving blocks highlighted.";
            }
            if (label.Text != info) label.Text = info;
        }
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (!Windows.HasModal && input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }
            && Views.Values.Contains(Windows.Active))
        { Windows.Active!.Close(); GetViewport().SetInputAsHandled(); }
    }
    public override void _ExitTree()
    {
        Network.Changed -= Changed;
        if (IsInstanceValid(Overlay)) { Overlay.Hide(); Overlay.QueueFree(); }
    }
}
