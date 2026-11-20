using Godot;
using System;

namespace TransportSandbox;

public partial class TrackEditor
{
    private VBoxContainer _signalOptions = null!;
    private Button _signalTab = null!;
    public OptionButton SignalType = null!, SignalAction = null!, SignalDirections = null!;
    public SignalKey? SignalTarget { get; private set; }
    public SignalVisual? SignalPreview { get; private set; }
    public SignalVisual? OppositeSignalPreview { get; private set; }
    private (SignalKey Key, RailSignalKind Kind, bool Remove, bool Both)? _signalPreviewKey;
    private void BuildSignalUI(HBoxContainer tabs, VBoxContainer box, ButtonGroup group)
    {
        Renderer.Signals.DirectionVisibility = () => Enabled && Mode == "signal"
            && Toolbar.Visible && !Toolbar.IsCollapsed && !Toolbar.IsMinimized;
        _signalTab = Button(tabs, "", () => SetMode("signal"), "Signals");
        _signalTab.Icon = UiSkin.Icon("signal"); _signalTab.ToggleMode = true; _signalTab.ButtonGroup = group;
        _signalTab.CustomMinimumSize = new(44, 36);
        _signalOptions = new() { Visible = false }; box.AddChild(_signalOptions);
        UiSkin.Label(_signalOptions, "SIGNAL TYPE", 9);
        SignalType = new(); foreach (string type in new[] { "Normal signal", "Chain signal", "Path signal" }) SignalType.AddItem(type);
        UiSkin.Selector(SignalType); _signalOptions.AddChild(SignalType); SignalType.ItemSelected += _ => ClearSignalPreview();
        UiSkin.Label(_signalOptions, "DIRECTIONS", 9);
        SignalDirections = new(); SignalDirections.AddItem("Single direction"); SignalDirections.AddItem("Both directions");
        UiSkin.Selector(SignalDirections); _signalOptions.AddChild(SignalDirections); SignalDirections.ItemSelected += _ => ClearSignalPreview();
        UiSkin.Label(_signalOptions, "ACTION", 9);
        SignalAction = new(); SignalAction.AddItem("Build / replace signal"); SignalAction.AddItem("Remove signal");
        UiSkin.Selector(SignalAction); _signalOptions.AddChild(SignalAction); SignalAction.ItemSelected += _ => ClearSignalPreview();
    }
    private void ClearSignalPreview()
    {
        SignalTarget = null; _signalPreviewKey = null;
        if (IsInstanceValid(SignalPreview)) { SignalPreview!.GetParent()?.RemoveChild(SignalPreview); SignalPreview.QueueFree(); }
        SignalPreview = null;
        if (IsInstanceValid(OppositeSignalPreview)) { OppositeSignalPreview!.GetParent()?.RemoveChild(OppositeSignalPreview); OppositeSignalPreview.QueueFree(); }
        OppositeSignalPreview = null;
    }
    // Adapted from OpenLoco SignalTab::getSide and Common::isPointCloserToNextOrPreviousTile.
    // Copyright (c) 2018 OpenLoco developers. MIT: third-party/OpenLoco-LICENSE.txt.
    // A cursor near the next endpoint selects a signal facing trains arriving from that end.
    internal int SignalApproachSide(TrackNode node, Vector2 mouse)
    {
        var next = Camera.UnprojectPosition(node.Position + node.Axis * 4);
        var previous = Camera.UnprojectPosition(node.Position - node.Axis * 4);
        static float Manhattan(Vector2 a, Vector2 b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        return Manhattan(mouse, next) < Manhattan(mouse, previous) ? -1 : 1;
    }
    internal void RefreshSignalHover(Vector2 mouse)
    {
        TrackNode? nearest = null; float best = 65;
        foreach (var candidate in Network.Nodes.Values)
        {
            if (Camera.IsPositionBehind(candidate.Position) || Camera.Position.DistanceTo(candidate.Position) > ActionDistance || Network.Incident(candidate.Id).Count == 0) continue;
            float distance = Camera.UnprojectPosition(candidate.Position).DistanceTo(mouse);
            if (distance < best) { nearest = candidate; best = distance; }
        }
        if (nearest == null) { ClearSignalPreview(); return; }
        int side = SignalApproachSide(nearest, mouse);
        var key = new SignalKey(nearest.Id, side); bool remove = SignalAction.Selected == 1, both = SignalDirections.Selected == 1;
        var kind = (RailSignalKind)SignalType.Selected;
        if (remove)
        {
            if (!Network.Signals.TryGetValue(key, out var existing) && (!both || !Network.Signals.TryGetValue(new(key.Node, -key.Side), out existing)))
            { ClearSignalPreview(); Status.Text = "No signal for this approach direction."; return; }
            kind = existing.Kind;
        }
        var previewKey = (key, kind, remove, both);
        if (_signalPreviewKey != previewKey)
        {
            ClearSignalPreview(); SignalPreview = new(kind, preview: true, remove: remove);
            Renderer.AddChild(SignalPreview); _signalPreviewKey = previewKey;
            if (both)
            {
                OppositeSignalPreview = new(kind, preview: true, remove: remove);
                Renderer.AddChild(OppositeSignalPreview);
            }
        }
        SignalPreview!.Place(nearest, side); SignalTarget = key;
        OppositeSignalPreview?.Place(nearest, -side);
        Status.Text = remove ? "Click to remove the selected signal direction(s)." : both ? "Click to place both directions at this node."
            : "Click to place a signal. Hover along the track on the approach side to choose its direction.";
    }
    internal bool CommitSignal(Vector2 mouse)
    {
        RefreshSignalHover(mouse);
        if (SignalTarget is not { } key) { Status.Text = "Choose a side of an existing track node."; return false; }
        bool changed = SignalAction.Selected == 1 ? Network.RemoveSignal(key.Node, key.Side) : Network.SetSignal(key.Node, key.Side, (RailSignalKind)SignalType.Selected);
        if (SignalDirections.Selected == 1)
            changed |= SignalAction.Selected == 1 ? Network.RemoveSignal(key.Node, -key.Side) : Network.SetSignal(key.Node, -key.Side, (RailSignalKind)SignalType.Selected);
        ClearSignalPreview(); Status.Text = changed ? "Signal updated." : "This signal is already placed."; return changed;
    }
}
