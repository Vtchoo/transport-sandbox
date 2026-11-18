using Godot;
using System.Text.Json;

namespace TransportSandbox;

public partial class Main : Node3D
{
    public WorldCamera Camera = null!;
    public MenuDiorama MenuScene = null!;
    public MenuUI MenuUI = null!;
    public CanvasLayer Interface = new();
    public TerrainWorld World = null!;
    public TerrainGenerator Generator = null!;
    public TrackNetwork TrackNetwork = null!;
    public TrackRenderer TrackRenderer = null!;
    public TrackEditor TrackEditor = null!;
    public JsonElement Catalog;
    public VehicleCatalog Vehicles = null!;
    public int MenuPresetOverride = -1;
    public string MenuSettingsFile = "user://menu-settings.cfg";
    public override void _Ready()
    {
        GetViewport().GuiEmbedSubwindows = true;
        Catalog = JsonData.Read("res://assets/vehicles/catalog.json");
        Vehicles = new(Catalog);
        Camera = new();
        AddChild(Camera);
        AddChild(Interface);
        MenuScene = new()
        {
            Preset = MenuPresetOverride,
            Catalog = Vehicles
        };
        AddChild(MenuScene);
        MenuUI = new()
        {
            SettingsFile = MenuSettingsFile
        };
        Interface.AddChild(MenuUI);
        MenuUI.StartRequested += StartWithSettings;
    }
    public void ShowSetup() => MenuUI.OpenSetup();
    private void BuildWorldEnvironment() => WorldLighting.AddTo(this);
    public void StartWorld(bool custom)
    {
        MenuUI.Custom.ButtonPressed = custom;
        StartWithSettings(MenuUI.ChosenSettings());
    }
    public void StartWithSettings(WorldSettings settings)
    {
        Generator = new(settings);
        Camera.ResetWorld(Generator);
        Camera.Enabled = true;
        RemoveChild(MenuScene);
        MenuScene.QueueFree();
        MenuUI.Hide();
        Camera.Current = true;
        BuildWorldEnvironment();
        World = new()
        {
            Generator = Generator,
            Camera = Camera
        };
        World.SetMeta("physics_mode", settings.Physics);
        AddChild(World);
        TrackNetwork = new();
        TrackRenderer = new()
        {
            Network = TrackNetwork,
            Camera = Camera
        };
        AddChild(TrackRenderer);
        TrackEditor = new()
        {
            Network = TrackNetwork,
            Renderer = TrackRenderer,
            Camera = Camera,
            Generator = Generator,
            Interface = Interface
        };
        AddChild(TrackEditor);
        var actions = new HBoxContainer();
        actions.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        actions.OffsetLeft = -110;
        actions.OffsetRight = 110;
        actions.OffsetTop = -65;
        actions.OffsetBottom = -17;
        Interface.AddChild(actions);
        var build = new Button { Text = "Build track", CustomMinimumSize = new(220, 48), ToggleMode = true };
        build.Toggled += TrackEditor.SetEnabled;
        actions.AddChild(build);
        var panel = new HBoxContainer { Position = new(20, 20) };
        Interface.AddChild(panel);
        var contours = new CheckButton { Text = "Terrain contours" };
        contours.Toggled += World.ShowContours;
        panel.AddChild(contours);
        panel.AddChild(new Label { Text = "  WASD pan · Middle drag orbit · Right drag pan · Wheel zoom · C free camera · Q/E height" });
    }
}
