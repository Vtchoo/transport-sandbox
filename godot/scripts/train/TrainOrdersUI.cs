using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public sealed class TrainOrderView
{
    public VBoxContainer Root = null!, List = null!;
    public Label Status = null!;
    public SpinBox Duration = null!;
    public OptionButton Unit = null!, Item = null!;
    public Button Station = null!, Waypoint = null!, Wait = null!, FullLoad = null!, Copy = null!, Paste = null!, Stations = null!;
    public TrainDragButton Trash = null!;
    public readonly Dictionary<int, TrainDragButton> Rows = new();
    public int Revision = -1;
}

public partial class TrainWindows
{
    public StationListWindow StationList = null!;
    public TrainOrderHighlights OrderHighlights = null!;
    private TrainOrder[] _copiedOrders = Array.Empty<TrainOrder>();
    private int? _orderPickTrain;
    private TrainOrderKind _orderPickKind;
    public bool PickingOrder => _orderPickTrain != null;
    private void InitializeOrders()
    {
        StationList = new() { Network = Editor.Network, Windows = Manager, Stations = TrackEditor.Stations, Choose = ChooseStationFromList };
        AddChild(StationList);
        OrderHighlights = new() { Trains = this };
        Interface.AddChild(OrderHighlights);
        Editor.Network.Changed += OrderNetworkChanged;
        TreeExiting += () => Editor.Network.Changed -= OrderNetworkChanged;
    }
    private void OrderNetworkChanged()
    {
        foreach (var view in Views.Values) view.Orders.Revision = -1;
        foreach (var train in Editor.Builder.Trains)
            if (Views.TryGetValue(train.Id, out var view)) RenderOrders(train, view);
    }
    public void OpenRoutes(int id)
    {
        OpenTrain(id, false);
        if (!Views.TryGetValue(id, out var view)) return;
        view.EditTab = false; view.RoutesTab = true;
        Render(Editor.Builder.Get(id), view);
        RefreshCatalog();
    }
    private TrainOrderView BuildOrders(int id, Node parent)
    {
        var ui = new TrainOrderView { Root = new(), List = new() };
        parent.AddChild(ui.Root);
        var add = new HFlowContainer(); ui.Root.AddChild(add);
        ui.Station = Button(add, "Go to station", () => BeginOrderPicking(id, TrainOrderKind.Station), "station");
        ui.Waypoint = Button(add, "Go to waypoint", () => BeginOrderPicking(id, TrainOrderKind.Waypoint), "waypoint");
        var wait = new HBoxContainer(); ui.Root.AddChild(wait);
        ui.Wait = Button(wait, "Wait", () => AddOrder(id, TrainOrderKind.Wait, seconds: ui.Duration.Value * ui.Unit.GetSelectedId()), "wait");
        ui.Duration = new() { MinValue = 0, MaxValue = 1000000, Value = 10, Step = 1, CustomMinimumSize = new(100, 0) };
        wait.AddChild(ui.Duration);
        ui.Unit = new(); UiSkin.Selector(ui.Unit);
        ui.Unit.AddItem("seconds", 1); ui.Unit.AddItem("minutes", 60); ui.Unit.AddItem("hours", 3600); wait.AddChild(ui.Unit);
        var load = new HFlowContainer(); ui.Root.AddChild(load);
        ui.FullLoad = Button(load, "Wait for full load of", () => { }, "fullLoad"); ui.FullLoad.Disabled = true;
        ui.Item = new() { Disabled = true, TooltipText = "Cargo items are not available yet" }; ui.Item.AddItem("Pick item"); UiSkin.Selector(ui.Item); load.AddChild(ui.Item);
        var scroll = new ScrollContainer { CustomMinimumSize = new(0, 110), SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        ui.Root.AddChild(scroll); scroll.AddChild(ui.List); ui.List.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        ui.Trash = new() { CustomMinimumSize = new(40, 40) };
        ui.Trash.SetContent("", "trash", "Drag an order here to delete it"); ui.Root.AddChild(ui.Trash);
        bool OwnOrder(Variant value) => TrainDragButton.Read(value) is { } data && data["type"].AsString() == "order" && data["train"].AsInt32() == id;
        ui.Trash.Accept = OwnOrder;
        ui.Trash.Drop = value => { var data = TrainDragButton.Read(value)!; Editor.Builder.Get(id).Orders.Remove(data["item"].AsInt32()); Editor.OrdersChanged(id); };
        var tools = new HFlowContainer(); ui.Root.AddChild(tools);
        ui.Copy = Button(tools, "Copy route", () => CopyRoute(id), "copy");
        ui.Paste = Button(tools, "Paste route", () => PasteRoute(id), "paste", "Append copied orders to this train");
        ui.Stations = Button(tools, "Stations", () => StationList.Open(), "station");
        ui.Status = UiSkin.Label(ui.Root, "", 11); ui.Status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        UiSkin.Label(ui.Root, "Orders repeat. Drag rows to reorder or to the trash to delete.", 10).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return ui;
    }
    public void AddOrder(int id, TrainOrderKind kind, int target = 0, double seconds = 0)
    {
        Editor.Builder.Get(id).Orders.Add(kind, target, seconds);
        Editor.OrdersChanged(id);
    }
    public void CopyRoute(int id)
    {
        _copiedOrders = Editor.Builder.Get(id).Orders.Items.ToArray();
        foreach (var view in Views.Values) view.Orders.Paste.Disabled = _copiedOrders.Length == 0;
    }
    public void PasteRoute(int id)
    {
        Editor.Builder.Get(id).Orders.Append(_copiedOrders);
        Editor.OrdersChanged(id);
    }
    private string OrderText(TrainOrder order) => order.Kind switch
    {
        TrainOrderKind.Station => $"Go to Station {order.Target}" + (Editor.Network.Edges.Values.Any(edge => edge.Station == order.Target) ? "" : " (missing)"),
        TrainOrderKind.Waypoint => $"Go to Waypoint {order.Target}" + (Editor.Network.Nodes.ContainsKey(order.Target) ? "" : " (missing)"),
        _ => order.Seconds >= 3600 && order.Seconds % 3600 == 0 ? $"Wait {order.Seconds / 3600:g} hours"
            : order.Seconds >= 60 && order.Seconds % 60 == 0 ? $"Wait {order.Seconds / 60:g} minutes" : $"Wait {order.Seconds:g} seconds"
    };
    private void RenderOrders(TrainConsist train, TrainView view)
    {
        var ui = view.Orders;
        if (ui.Revision != train.Orders.Revision)
        {
            ui.Revision = train.Orders.Revision; Clear(ui.List); ui.Rows.Clear();
            if (train.Orders.Items.Count == 0) UiSkin.Label(ui.List, "No orders. Add a destination or wait above.", 11);
            for (int i = 0; i < train.Orders.Items.Count; i++)
            {
                var order = train.Orders.Items[i]; int before = i;
                var row = new TrainDragButton { Text = $"{i + 1}. {OrderText(order)}", ToggleMode = true, Alignment = HorizontalAlignment.Left, ClipText = true,
                    Icon = UiSkin.Icon(order.Kind == TrainOrderKind.Station ? "station" : order.Kind == TrainOrderKind.Waypoint ? "waypoint" : "wait"), IconAlignment = HorizontalAlignment.Left,
                    Payload = TrainDragButton.Data("order", train.Id, order.Id), TooltipText = OrderText(order), CustomMinimumSize = new(0, 34) };
                UiSkin.Button(row); ui.List.AddChild(row); ui.Rows[order.Id] = row;
                row.Accept = data => TrainDragButton.Read(data) is { } d && d["type"].AsString() == "order" && d["train"].AsInt32() == train.Id;
                row.Drop = data => { train.Orders.Move(TrainDragButton.Read(data)!["item"].AsInt32(), before); Editor.OrdersChanged(train.Id); };
            }
            if (train.Orders.Items.Count > 1)
            {
                var end = new TrainDragButton { Text = "Drop here to move to end", CustomMinimumSize = new(0, 28) }; UiSkin.Button(end); ui.List.AddChild(end);
                end.Accept = data => TrainDragButton.Read(data) is { } d && d["type"].AsString() == "order" && d["train"].AsInt32() == train.Id;
                end.Drop = data => { train.Orders.Move(TrainDragButton.Read(data)!["item"].AsInt32(), train.Orders.Items.Count); Editor.OrdersChanged(train.Id); };
            }
        }
        ui.Copy.Disabled = train.Orders.Items.Count == 0; ui.Paste.Disabled = _copiedOrders.Length == 0;
        RefreshOrderStatus(train, view);
    }
    private void RefreshOrderStatus(TrainConsist train, TrainView view)
    {
        view.Orders.Status.Text = train.Orders.Status;
        foreach (var (id, row) in view.Orders.Rows) row.SetPressedNoSignal(train.Orders.Current?.Id == id);
        if (train.Placed) view.Location.Text += "\n" + train.Orders.Status;
    }
    public void BeginOrderPicking(int id, TrainOrderKind kind)
    {
        OpenRoutes(id); Editor.CancelPlacement();
        _orderPickTrain = id; _orderPickKind = kind;
        Editor.Camera.ReleaseMouse();
        TrackEditor.Renderer.Markers.Visible = kind == TrainOrderKind.Waypoint;
    }
    public void CancelOrderPicking()
    {
        if (_orderPickTrain == null) return;
        _orderPickTrain = null;
        if (GodotObject.IsInstanceValid(TrackEditor.Renderer.Markers)) TrackEditor.Renderer.Markers.Visible = TrackEditor.Enabled;
        if (GodotObject.IsInstanceValid(PlacementHint)) PlacementHint.Hide();
    }
    private bool ChooseStationFromList(int station)
    {
        if (_orderPickTrain is not { } id || _orderPickKind != TrainOrderKind.Station) return false;
        CancelOrderPicking(); AddOrder(id, TrainOrderKind.Station, station); return true;
    }
    private int PickOrderTarget(Vector2 mouse)
    {
        if (_orderPickKind == TrainOrderKind.Waypoint) return TrackEditor.PickNode(mouse);
        int station = TrackEditor.Stations.PickAt(Editor.Camera, mouse);
        return station != 0 ? station : TrackEditor.Renderer.Closest(Editor.Camera, mouse)?.Edge.Station ?? 0;
    }
    private void RefreshOrderPicking()
    {
        if (_orderPickTrain is not { } id) return;
        if (!Views.TryGetValue(id, out var view) || !view.Window.Visible || !view.RoutesTab || !Editor.Builder.Trains.Any(train => train.Id == id)) { CancelOrderPicking(); return; }
        if (Manager.HasModal || GetViewport().GuiGetHoveredControl() != null) return;
        int target = PickOrderTarget(GetViewport().GetMousePosition());
        PlacementHint.Display((_orderPickKind == TrainOrderKind.Station ? "Choose a station in the world or Stations list" : "Choose an existing track node as a waypoint") + "\nEsc cancels", target != 0, .2);
    }
    private bool HandleOrderPicking(InputEvent input)
    {
        if (_orderPickTrain is not { } id || Manager.HasModal) return false;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) { CancelOrderPicking(); GetViewport().SetInputAsHandled(); return true; }
        if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return false;
        int target = PickOrderTarget(click.Position);
        if (target != 0) { var kind = _orderPickKind; CancelOrderPicking(); AddOrder(id, kind, target); }
        GetViewport().SetInputAsHandled(); return true;
    }
}
