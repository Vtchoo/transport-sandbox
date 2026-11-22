using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public sealed class TrainView
{
    public GameWindow Window = null!;
    public bool EditTab = true;
    public bool RoutesTab;
    public bool DebugTab;
    public Button DebugButton = null!;
    public VBoxContainer Debug = null!;
    public Label DebugInfo = null!;
    public TrainOrderView Orders = null!;
    public Button RoutesButton = null!;
    public bool? RunStyleRunning;
    public int? Selected;
    public VBoxContainer Edit = null!, Overview = null!;
    public HBoxContainer Consist = null!, Tools = null!;
    public Dictionary<int, Label> CargoLabels = new();
    public Label Specs = null!, OverviewSpecs = null!, Location = null!, Status = null!, SelectedName = null!, Empty = null!;
    public TrainDragButton Trash = null!;
    public VehiclePreview Preview = null!;
    public Button ViewTab = null!, EditButton = null!, Run = null!, Place = null!, Reverse = null!, Follow = null!, Pickup = null!, MoveFront = null!, MoveRear = null!, Flip = null!, Remove = null!;
}
/// <summary>Catalog routing, independent train windows, consist editing and fleet controls from the browser UI.</summary>
public partial class TrainWindows : Node
{
    public TrainEditor Editor = null!;
    public WindowManager Manager = null!;
    public TrackEditor TrackEditor = null!;
    public CanvasLayer Interface = null!;
    public readonly Dictionary<int, TrainView> Views = new();
    public GameWindow CatalogWindow = null!, FleetWindow = null!;
    public VehiclePreview CatalogPreview = null!;
    public PointerMessage WarningBubble = null!, PlacementHint = null!;
    public Label Coupler = null!;
    private Label _catalogContext = null!, _previewName = null!, _empty = null!;
    private VBoxContainer _fleet = null!;
    public readonly Dictionary<string, TrainDragButton> CatalogButtons = new();
    private string? _previewVehicle;
    private string? _hoveredVehicle, _focusedVehicle;
    private bool _refreshing;
    private double _catalogTimer, _viewTimer;
    public int? Following
    {
        get; private set;
    }
    public override void _Ready()
    {
        BuildCatalog();
        InitializeOrders();
        InitializeDebug();
        FleetWindow = Manager.Create("Trains", new Rect2(50, 110, 330, 390), new(270, 200));
        _fleet = new();
        FleetWindow.Body.AddChild(_fleet);
        WarningBubble = new();
        Interface.AddChild(WarningBubble);
        PlacementHint = new();
        Interface.AddChild(PlacementHint);
        Coupler = new Label { Text = "+", MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 90, Visible = false };
        Coupler.AddThemeFontSizeOverride("font_size", 22);
        Coupler.AddThemeColorOverride("font_color", new("9ee4c5"));
        Interface.AddChild(Coupler);
        Editor.Changed += Change;
        Editor.Warning += Warn;
        Editor.MotionChanged += RefreshMotion;
        Editor.Network.Space.Rebased += Rebased;
        Editor.TrainClicked += OpenTrainFromWorld;
        Editor.PlacementStarted += SuspendTrack;
        Manager.StackingChanged += StackChanged;
        Editor.Camera.ManuallyPanned += StopFollowing;
        TrackEditor.EnabledChanged += TrackEnabled;
        Refresh();
    }
    private Button Button(Node parent, string text, Action action, string icon = "", string tooltip = "")
    {
        var button = new GameButton();
        button.SetContent(text, icon, tooltip);
        parent.AddChild(button);
        button.Pressed += action;
        return button;
    }
    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
    private void BuildCatalog()
    {
        CatalogWindow = Manager.Create("Vehicle catalog", new Rect2(24, 85, 350, 590), new(280, 440), scrollBody: false);
        _catalogContext = UiSkin.Label(CatalogWindow.Body, "", 11);
        _catalogContext.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new(0, 100), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        CatalogWindow.Body.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(list);
        foreach (var definition in Editor.Catalog.Definitions)
        {
            var row = new TrainDragButton
            {
                Text = definition.DisplayName + "\n" + FormattableString.Invariant($"{definition.Length:F2} m") + " - " + string.Join(" / ", definition.Models.Keys.Order()) + " mm",
                Icon = GD.Load<Texture2D>(definition.Thumbnail),
                ExpandIcon = true,
                IconAlignment = HorizontalAlignment.Left,
                CustomMinimumSize = new(0, 58),
                Alignment = HorizontalAlignment.Left,
                ClipText = true,
                Payload = TrainDragButton.Data("vehicle", vehicle: definition.Id),
                TooltipText = definition.DisplayName + (definition.Cargo == null ? "" : "\n" + definition.CargoSummary)
            };
            row.AddThemeConstantOverride("icon_max_width", 78);
            UiSkin.Button(row);
            list.AddChild(row);
            row.Pressed += () => CatalogClick(definition.Id);
            row.MouseEntered += () => { _hoveredVehicle = definition.Id; RefreshCatalogPreview(); };
            row.MouseExited += () => { if (_hoveredVehicle == definition.Id) _hoveredVehicle = null; RefreshCatalogPreview(); };
            row.FocusEntered += () => { _focusedVehicle = definition.Id; RefreshCatalogPreview(); };
            row.FocusExited += () => { if (_focusedVehicle == definition.Id) _focusedVehicle = null; RefreshCatalogPreview(); };
            CatalogButtons[definition.Id] = row;
        }
        _empty = UiSkin.Label(CatalogWindow.Body, "No compatible vehicles are available.", 11);
        var previewFrame = new PanelContainer();
        previewFrame.AddThemeStyleboxOverride("panel", UiSkin.Box("304942", "91a58055", 0, 0, 0));
        CatalogWindow.Body.AddChild(previewFrame);
        CatalogPreview = new();
        previewFrame.AddChild(CatalogPreview);
        CatalogPreview.CustomMinimumSize = new(0, 155);
        CatalogPreview.Modulate = new(1, 1, 1, 0);
        _previewName = UiSkin.Label(CatalogWindow.Body, "Hover a vehicle to preview it", 11);
        var help = UiSkin.Label(CatalogWindow.Body, "Edit the frontmost train to append vehicles to its rear. Otherwise, clicking creates a new train.", 10);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }
    private void ClearCatalogHover(string id)
    {
        if (_hoveredVehicle == id)
            _hoveredVehicle = null;
        if (_focusedVehicle == id)
            _focusedVehicle = null;
        RefreshCatalogPreview();
    }
    private void RefreshCatalogPreview()
    {
        _previewVehicle = _hoveredVehicle ?? _focusedVehicle;
        CatalogPreview.Modulate = new(1, 1, 1, _previewVehicle == null ? 0 : 1);
        if (_previewVehicle == null)
        {
            CatalogPreview.Suspend();
            _previewName.Text = "Hover a vehicle to preview it";
        }
        else
        {
            var definition = Editor.Catalog.Get(_previewVehicle);
            _previewName.Text = definition.DisplayName + (definition.Cargo == null ? "" : "\n" + definition.CargoSummary);
        }
    }
    public TrainConsist? Destination()
    {
        var window = Manager.Topmost(w => !w.IsCollapsed && Views.Values.Any(v => v.Window == w));
        if (window == null)
            return null;
        var entry = Views.First(p => p.Value.Window == window);
        return entry.Value.EditTab ? Editor.Builder.Trains.FirstOrDefault(t => t.Id == entry.Key) : null;
    }
    public (int Id, TrainView View)? FocusedTrain()
    {
        var window = Manager.Active;
        if (window == null || !window.Visible || window.IsCollapsed || Manager.HasModal) return null;
        foreach (var (id, view) in Views)
            if (view.Window == window) return (id, view);
        return null;
    }
    public void CatalogClick(string id)
    {
        var target = Destination();
        if (Editor.Allows(Editor.Catalog.Get(id), target))
            Editor.AddVehicle(id, target?.Id);
    }
    public void ToggleCatalog()
    {
        if (CatalogWindow.Visible && !CatalogWindow.IsCollapsed)
            CatalogWindow.Close();
        else
        {
            SuspendTrack();
            CatalogWindow.Open();
        }
    }
    public void OpenFleet() => FleetWindow.Open();
    private void OpenTrainFromWorld(int id) => OpenTrain(id, false);
    public void OpenTrain(int id, bool? edit = null)
    {
        if (!Editor.Builder.Trains.Any(t => t.Id == id))
            return;
        var train = Editor.Builder.Get(id);
        var view = EnsureView(train);
        CancelOrderPicking();
        view.RoutesTab = false;
        view.DebugTab = false;
        view.EditTab = edit ?? (train.Placed ? false : view.EditTab);
        Editor.Select(id);
        SuspendTrack();
        Render(train, view);
        view.Window.Open();
        RefreshCatalog();
    }
    private TrainView EnsureView(TrainConsist train)
    {
        if (Views.TryGetValue(train.Id, out var existing))
            return existing;
        var view = new TrainView { EditTab = !train.Placed };
        Views[train.Id] = view;
        view.Window = Manager.Create(train.Name, new Rect2(390 + (train.Id - 1) % 5 * 28, 85 + (train.Id - 1) % 5 * 26, 550, 510), new(340, 330));
        var tabs = new HBoxContainer();
        view.Window.Body.AddChild(tabs);
        view.ViewTab = Button(tabs, "", () => OpenTrain(train.Id, false), "viewport", "Viewport");
        view.EditButton = Button(tabs, "", () => OpenTrain(train.Id, true), "wrench", "Edit train");
        view.RoutesButton = Button(tabs, "", () => OpenRoutes(train.Id), "routes", "Routes");
        view.DebugButton = Button(tabs, "", () => OpenDebug(train.Id), "debug", "Debug route");
        var group = new ButtonGroup();
        view.ViewTab.ToggleMode = view.EditButton.ToggleMode = view.RoutesButton.ToggleMode = view.DebugButton.ToggleMode = true;
        view.ViewTab.ButtonGroup = view.EditButton.ButtonGroup = view.RoutesButton.ButtonGroup = view.DebugButton.ButtonGroup = group;
        view.Overview = new();
        view.Window.Body.AddChild(view.Overview);
        view.Preview = new();
        view.Overview.AddChild(view.Preview);
        var tools = new HBoxContainer();
        view.Overview.AddChild(tools);
        view.Run = Button(tools, "", () => Editor.ToggleRunning(train.Id), "play", "Start train");
        view.Follow = Button(tools, "", () => ToggleFollow(train.Id), "follow", "Follow train");
        view.Pickup = Button(tools, "", () => Editor.RemoveFromTrack(train.Id), "pickup", "Remove from track");
        view.Location = UiSkin.Label(view.Overview, "", 11);
        view.OverviewSpecs = UiSkin.Label(view.Overview, "", 11);
        view.Edit = new();
        view.Window.Body.AddChild(view.Edit);
        view.Specs = UiSkin.Label(view.Edit, "", 11);
        UiSkin.Label(view.Edit, "FRONT  <                                       >  REAR", 10);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new(0, 198) };
        view.Edit.AddChild(scroll);
        view.Consist = new();
        scroll.AddChild(view.Consist);
        view.Empty = UiSkin.Label(view.Edit, "Add vehicles from the catalog.", 11);
        view.SelectedName = UiSkin.Label(view.Edit, "", 11);
        view.Tools = new();
        view.Edit.AddChild(view.Tools);
        view.MoveFront = Button(view.Tools, "< Move", () => MoveSelected(train.Id, -1));
        view.MoveRear = Button(view.Tools, "Move >", () => MoveSelected(train.Id, 1));
        view.Flip = Button(view.Tools, "Flip", () => Editor.Edit("Flip vehicle", () => { Editor.Builder.Flip(train.Id, view.Selected!.Value); return true; }), "flip");
        view.Remove = Button(view.Tools, "Remove", () => Editor.Edit("Remove vehicle", () => { Editor.Builder.Remove(train.Id, view.Selected!.Value); return true; }), "close");
        var trash = new TrainDragButton();
        trash.SetContent("", "trash", "Drop a vehicle here to delete it");
        view.Edit.AddChild(trash);
        view.Trash = trash;
        trash.Accept = data => TrainDragButton.Read(data) is { } d && d["type"].AsString() == "item" && d["train"].AsInt32() == train.Id;
        trash.Drop = data => { var d = TrainDragButton.Read(data)!; Editor.Edit("Remove vehicle", () => { Editor.Builder.Remove(train.Id, d["item"].AsInt32()); return true; }); };
        view.Place = Button(view.Edit, "", () => { Editor.Select(train.Id); Manager.Activate(view.Window); Editor.StartPlacement(train.Id); }, "download", "Place train on track");
        tools = new();
        view.Edit.AddChild(tools);
        Button(tools, "Add vehicles...", () => CatalogWindow.Open(), "plus");
        view.Reverse = Button(tools, "Reverse preview - R", Editor.ReversePreview, "flip");
        view.Orders = BuildOrders(train.Id, view.Window.Body);
        BuildDebug(view);
        view.Status = UiSkin.Label(view.Window.Body, "", 11);
        view.Status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        view.Window.StateChanged += StackChanged;
        return view;
    }
    private void MoveSelected(int id, int direction)
    {
        var view = Views[id];
        var train = Editor.Builder.Get(id);
        int from = train.Items.FindIndex(i => i.Id == view.Selected);
        if (from < 0)
            return;
        Editor.Edit("Reorder vehicle", () => Editor.Builder.Move(id, view.Selected!.Value, id, from + (direction < 0 ? -1 : 2)));
    }
    public bool DropAt(int train, int index, Variant data)
    {
        var d = TrainDragButton.Read(data);
        if (d == null || !d.ContainsKey("type"))
            return false;
        if (d["type"].AsString() == "vehicle")
            return Editor.AddVehicle(d["vehicle"].AsString(), train, index);
        if (d["type"].AsString() == "item")
            return Editor.Edit("Move vehicle", () => Editor.Builder.Move(d["train"].AsInt32(), d["item"].AsInt32(), train, index));
        return false;
    }
    private void Gap(TrainView view, int train, int at)
    {
        var gap = new TrainDragButton { Text = "+", CustomMinimumSize = new(28, 105), TooltipText = $"Drag a vehicle here to insert at position {at + 1}" };
        UiSkin.Button(gap);
        view.Consist.AddChild(gap);
        gap.Accept = data => TrainDragButton.Read(data) is { } d && d.ContainsKey("type") && d["type"].AsString() is "item" or "vehicle";
        gap.Drop = data => DropAt(train, at, data);
    }
    private void Render(TrainConsist train, TrainView view)
    {
        view.Edit.Visible = view.EditTab && !view.RoutesTab && !view.DebugTab;
        view.Overview.Visible = !view.EditTab && !view.RoutesTab && !view.DebugTab;
        view.Orders.Root.Visible = view.RoutesTab;
        view.Debug.Visible = view.DebugTab;
        view.EditButton.SetPressedNoSignal(view.EditTab);
        view.ViewTab.SetPressedNoSignal(!view.EditTab && !view.RoutesTab && !view.DebugTab);
        view.RoutesButton.SetPressedNoSignal(view.RoutesTab);
        view.DebugButton.SetPressedNoSignal(view.DebugTab);
        RefreshSpecs(train, view);
        view.Empty.Visible = train.Items.Count == 0;
        view.Trash.Visible = train.Items.Count > 0;
        view.Location.Text = !train.Placed ? "In construction - not placed" : Editor.Parked.First(p => p.Id == train.Id).Supported ? "Stopped on track" : "Supporting track changed. The train remains at its last valid location.";
        Clear(view.Consist); view.CargoLabels.Clear();
        Gap(view, train.Id, 0);
        for (int i = 0; i < train.Items.Count; i++)
        {
            var item = train.Items[i];
            var card = new VBoxContainer { CustomMinimumSize = new(135, 0) };
            view.Consist.AddChild(card);
            var button = new TrainDragButton { Payload = TrainDragButton.Data("item", train.Id, item.Id), CustomMinimumSize = new(135, 105), TooltipText = item.Definition.DisplayName };
            UiSkin.Button(button);
            card.AddChild(button);
            button.ToggleMode = true;
            button.SetPressedNoSignal(view.Selected == item.Id);
            var content = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            button.AddChild(content);
            content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            content.OffsetLeft = content.OffsetTop = 6;
            content.OffsetRight = content.OffsetBottom = -6;
            content.AddChild(new TextureRect { Texture = GD.Load<Texture2D>(item.Definition.Thumbnail), CustomMinimumSize = new(0, 46), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, FlipH = item.Facing == -1, MouseFilter = Control.MouseFilterEnum.Ignore });
            var caption = UiSkin.Label(content, $"{i + 1}. {item.Definition.DisplayName}\n{item.Definition.Length:F2} m" + (item.Facing == -1 ? " - reversed" : ""), 10);
            caption.MouseFilter = Control.MouseFilterEnum.Ignore;
            caption.ClipText = true;
            button.Pressed += () => { view.Selected = item.Id; Render(Editor.Builder.Get(train.Id), view); };
            if (item.Definition.Cargo is { } hold)
            {
                string capacity = string.Join("\n", hold.Capacities.Select(c => $"{CargoTypes.Get(c.Key).Name}: {CargoTypes.Get(c.Key).FormatAmount(c.Value)}"));
                var type = item.Cargo == null ? null : CargoTypes.Get(item.Cargo.Type);
                var cargo = UiSkin.Label(card, item.Cargo == null ? capacity : $"{type!.Name}: {type.FormatQuantity(item.Cargo.Quantity)}/{type.FormatAmount(hold.CapacityFor(item.Cargo.Type))}", 10);
                cargo.TooltipText = "Capacity (one cargo type per car)\n" + capacity;
                view.CargoLabels[item.Id] = cargo;
                var assignment = new OptionButton { TooltipText = "Cargo assignment", FitToLongestItem = false };
                UiSkin.Selector(assignment); card.AddChild(assignment); assignment.AddItem("Automatic");
                assignment.SetItemMetadata(0, "");
                foreach (var id in hold.AcceptedTypes)
                { assignment.AddItem(CargoTypes.Get(id).Name); assignment.SetItemMetadata(assignment.ItemCount - 1, id); if (item.CargoAssignment == id) assignment.Select(assignment.ItemCount - 1); }
                assignment.ItemSelected += selected => Editor.SetCargoAssignment(train.Id, item.Id, selected == 0 ? null : assignment.GetItemMetadata((int)selected).AsString());
            }
            if (item.Definition.Liveries.Count > 0)
            {
                var selector = new OptionButton { TooltipText = "Livery", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FitToLongestItem = false };
                UiSkin.Selector(selector);
                card.AddChild(selector);
                for (int variant = 0; variant < item.Definition.Liveries.Count; variant++)
                {
                    var livery = item.Definition.Liveries[variant];
                    selector.AddItem(livery.Name);
                    if (livery.Id == item.Definition.ResolveLivery(item.Livery)) selector.Select(variant);
                }
                selector.ItemSelected += index => Editor.SetLivery(train.Id, item.Id, item.Definition.Liveries[(int)index].Id);
            }
            Gap(view, train.Id, i + 1);
        }
        int selected = train.Items.FindIndex(i => i.Id == view.Selected);
        view.Tools.Visible = view.SelectedName.Visible = selected >= 0;
        view.SelectedName.Text = selected >= 0 ? train.Items[selected].Definition.DisplayName : "";
        view.MoveFront.Disabled = selected <= 0;
        view.MoveRear.Disabled = selected < 0 || selected >= train.Items.Count - 1;
        view.Place.Disabled = train.Placed || train.Items.Count == 0 || Editor.Placing && Editor.Builder.Selected?.Id == train.Id;
        view.Reverse.Disabled = !Editor.Placing || Editor.Builder.Selected?.Id != train.Id;
        view.Pickup.Disabled = view.Follow.Disabled = !train.Placed;
        view.Follow.TooltipText = Following == train.Id ? "Stop following" : "Follow train";
        RefreshRunButton(train, view);
        RenderOrders(train, view);
        view.Status.Text = Editor.Builder.Selected?.Id == train.Id ? Editor.Message : "";
    }
    private static void RefreshRunButton(TrainConsist train, TrainView view)
    {
        view.Run.Icon = UiSkin.Icon(train.Stopped ? "play" : "pause");
        view.Run.TooltipText = train.Running ? "Stop train" : "Start train";
        view.Run.Disabled = !train.Placed;
        if (view.RunStyleRunning != train.Running)
        {
            UiSkin.TrainRunButton(view.Run, train.Running);
            view.RunStyleRunning = train.Running;
        }
    }
    private void RefreshMotion()
    {
        foreach (var (id, view) in Views)
            if (Editor.Builder.Trains.FirstOrDefault(t => t.Id == id) is { } train)
            {
                if (!view.Window.Visible || view.Window.IsCollapsed) continue;
                RefreshSpecs(train, view);
                RefreshRunButton(train, view);
                foreach (var item in train.Items)
                    if (view.CargoLabels.TryGetValue(item.Id, out var label))
                    {
                        if (item.Cargo is { } load) { var type = CargoTypes.Get(load.Type); label.Text = type.Name + ": " + type.FormatQuantity(load.Quantity) + "/" + type.FormatAmount(item.Definition.Cargo!.CapacityFor(load.Type)); }
                        else label.Text = string.Join("\n", item.Definition.Cargo!.Capacities.Select(c => $"{CargoTypes.Get(c.Key).Name}: {CargoTypes.Get(c.Key).FormatAmount(c.Value)}"));
                    }
                if (train.Placed) view.Location.Text = FormattableString.Invariant($"{Math.Abs(train.Speed) * 3.6:F1} km/h - {(train.Running ? "Started" : train.Stopped ? "Stopped" : "Stopping")}");
                RefreshOrderStatus(train, view);
            }
    }
    private static void RefreshSpecs(TrainConsist train, TrainView view)
    {
        view.Specs.Text = FormattableString.Invariant($"{train.Items.Count} vehicles - {train.Length:F2} m - {train.Items.Sum(i => i.MassKg) / 1000:F1} t\nGauge: ") + string.Join(" / ", TrainBuilder.Gauges(train.Items.Select(i => i.Definition))) + " mm";
        view.OverviewSpecs.Text = view.Specs.Text;
    }
    private void Rebased(TrackVector worldShift)
    {
        var shift = worldShift.Float();
        foreach (var view in Views.Values) view.Preview.Rebase(shift);
    }
    private void RefreshCatalog()
    {
        var target = Destination();
        int count = 0;
        foreach (var (id, button) in CatalogButtons)
        {
            button.Visible = Editor.Allows(Editor.Catalog.Get(id), target);
            if (button.Visible)
                count++;
            else if (_previewVehicle == id)
                ClearCatalogHover(id);
        }
        _empty.Visible = count == 0;
        _catalogContext.Text = target == null ? "Click a vehicle to create a new train." : "Compatible vehicles - click to append to the rear of the frontmost train editor.";
    }
    private void Change(string? action)
    {
        Refresh();
        if (action != null && Editor.Builder.Selected is { } train)
            OpenTrain(train.Id, action is "Place train" or "Attach trains" ? false : !train.Placed ? true : Views[train.Id].EditTab);
    }
    public void Refresh()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            foreach (int id in Views.Keys.ToArray())
            if (!Editor.Builder.Trains.Any(t => t.Id == id))
            {
                var view = Views[id];
                Views.Remove(id);
                Manager.Unregister(view.Window);
            }
            foreach (var train in Editor.Builder.Trains)
                Render(train, EnsureView(train));
            Clear(_fleet);
            if (Editor.Builder.Trains.Count == 0)
                UiSkin.Label(_fleet, "No trains yet. Choose a vehicle from the catalog.", 11);
            foreach (var train in Editor.Builder.Trains)
                Button(_fleet, $"{train.Name} - {train.Items.Count} vehicles - {(train.Placed ? "parked" : "construction")}", () => OpenTrain(train.Id, false));
            RefreshCatalog();
        }
        finally { _refreshing = false; }
    }
    private void StackChanged()
    {
        if (_refreshing)
            return;
        if (Editor.Placing && !Views.Values.Any(v => v.Window == Manager.Active && v.EditTab && v.Window.Visible && !v.Window.IsCollapsed))
            Editor.CancelPlacement();
        if (Views.FirstOrDefault(p => p.Value.Window == Manager.Active) is var active && active.Value != null)
            Editor.Select(active.Key);
        foreach (var (id, view) in Views)
        {
            var train = Editor.Builder.Trains.FirstOrDefault(t => t.Id == id);
            if (train == null)
                continue;
            view.Place.Disabled = train.Placed || train.Items.Count == 0 || Editor.Placing && Editor.Builder.Selected?.Id == id;
            view.Reverse.Disabled = !Editor.Placing || Editor.Builder.Selected?.Id != id;
        }
        RefreshCatalog();
    }
    private void Warn(string text) => WarningBubble.Display(text);
    private void SuspendTrack()
    {
        CancelOrderPicking();
        if (TrackEditor.Enabled)
            TrackEditor.SetEnabled(false);
    }
    private void TrackEnabled(bool value)
    {
        if (value)
        {
            Editor.CancelPlacement();
            CancelOrderPicking();
        }
    }
    private void StopFollowing()
    {
        Following = null;
        Editor.Camera.Following = null;
    }
    public void ToggleFollow(int id)
    {
        if (Following == id)
            StopFollowing();
        else if (Editor.Parked.FirstOrDefault(p => p.Id == id) is { } parked)
        {
            Following = id;
            Editor.Camera.ReleaseMouse();
            Editor.Camera.Following = parked.Visuals[0].Vehicle.Model.FindChild("body", true, false) as Node3D;
        }
        Refresh();
    }
    public override void _Process(double delta)
    {
        using var probe = FrameProbe.Measure("train.ui");
        _catalogTimer += delta;
        _viewTimer += delta;
        if (_catalogTimer >= .04)
        {
            _catalogTimer = 0;
            if (_previewVehicle is { } id && CatalogWindow.Visible && !CatalogWindow.IsCollapsed)
                CatalogPreview.Catalog(Editor.Catalog.Get(id));
            else
                CatalogPreview.Suspend();
        }
        if (_viewTimer >= .15)
        {
            _viewTimer = 0;
            foreach (var (id, view) in Views)
                if (!view.EditTab && !view.RoutesTab && !view.DebugTab && view.Window.Visible && !view.Window.IsCollapsed)
                {
                    RefreshRunButton(Editor.Builder.Get(id), view);
                    view.Preview.Train(Editor.Builder.Get(id), Editor.Parked.FirstOrDefault(p => p.Id == id), Editor.Catalog, GetViewport().World3D);
                }
                else
                    view.Preview.Suspend();
        }
        if (Following is { } following)
        {
            var parked = Editor.Parked.FirstOrDefault(p => p.Id == following);
            if (parked == null)
                StopFollowing();
            else
                Editor.Camera.Following = parked.Visuals[0].Vehicle.Model.FindChild("body", true, false) as Node3D;
        }
        bool showHint = Editor.Placing && !Manager.HasModal && GetViewport().GuiGetHoveredControl() == null;
        if (showHint)
            PlacementHint.Display(Editor.Message + "\n" + (Editor.Valid ? "Click to place" : "Choose a track position") + " - R reverse - Esc cancel", Editor.Valid && !Editor.OccupiedBlock, .2);
        else
            PlacementHint.Hide();
        Coupler.Visible = showHint && Editor.AppendTarget != null;
        if (Coupler.Visible && Editor.AppendTarget is { } append)
        {
            try
            {
                Coupler.Position = Editor.Camera.UnprojectPosition(Editor.Placement.Sample(append.Train.Position, (append.Front ? 1 : -1) * Editor.Builder.Get(append.Train.Id).Length / 2).Point) - new Vector2(7, 12);
            }
            catch (InvalidOperationException) { Coupler.Hide(); }
        }
        RefreshOrderPicking();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (HandleOrderPicking(input)) return;
        if (Manager.HasModal || input is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        bool active = Manager.Active == CatalogWindow || Manager.Active == FleetWindow || Views.Values.Any(v => v.Window == Manager.Active);
        if (!active)
            return;
        if (key.Keycode == Key.Escape && !Editor.Placing)
        {
            Manager.Active?.Close();
            GetViewport().SetInputAsHandled();
        }
    }
    public override void _ExitTree()
    {
        if (IsInstanceValid(OrderHighlights)) OrderHighlights.QueueFree();
        if (IsInstanceValid(DebugHighlights)) DebugHighlights.QueueFree();
        CancelOrderPicking();
        Editor.Changed -= Change;
        Editor.Warning -= Warn;
        Editor.MotionChanged -= RefreshMotion;
        Editor.Network.Space.Rebased -= Rebased;
        Editor.TrainClicked -= OpenTrainFromWorld;
        Editor.PlacementStarted -= SuspendTrack;
        Manager.StackingChanged -= StackChanged;
        Editor.Camera.ManuallyPanned -= StopFollowing;
        TrackEditor.EnabledChanged -= TrackEnabled;
    }
}
