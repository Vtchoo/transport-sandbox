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
    public TrackTreeClearance TreeClearance = null!;
    public WindowManager GameWindows = null!;
    public GameHUD HUD = null!;
    public TrainEditor Trains = null!;
    public TrainWindows TrainUI = null!;
    public SignalWindows SignalUI = null!;
    public PauseMenu PauseUI = null!;
    public string HudSettingsFile = "user://game-ui.cfg";
    public SoundSettings SoundSettings = new();
    public JsonElement Catalog;
    public VehicleCatalog Vehicles = null!;
    public int MenuPresetOverride = -1;
    public string MenuSettingsFile = "user://menu-settings.cfg";
    public override void _Ready()
    {
        GetViewport().GuiEmbedSubwindows = true;
        SoundSettings.Load();
        Catalog = JsonData.Read("res://assets/vehicles/catalog.json");
        Vehicles = new(Catalog);
        Camera = new();
        AddChild(Camera);
        AddChild(Interface);
        MenuScene = new()
        {
            Preset = MenuPresetOverride,
            SoundSettings = SoundSettings,
            Catalog = Vehicles
        };
        AddChild(MenuScene);
        MenuUI = new()
        {
            SettingsFile = MenuSettingsFile
        };
        Interface.AddChild(MenuUI);
        MenuUI.StartRequested += settings => CreateGame(MenuUI.WorldName.Text, settings);
        MenuUI.LoadRequested += OpenLoadGames;
        InitializeSaving();
        AddChild(new DisplayShortcuts { Interface = Interface });
    }
    public void ShowSetup() => MenuUI.OpenSetup();
    private void BuildWorldEnvironment() => WorldLighting.AddTo(this, gameplay: true);
    public void StartWorld(bool custom)
    {
        MenuUI.Custom.ButtonPressed = custom;
        StartWithSettings(MenuUI.ChosenSettings());
    }
    public void StartWithSettings(WorldSettings settings)
    {
        Generator = new(settings) { Space = Camera.Space };
        if (WorldDirectory != null) Generator.Backend.Regions = new(WorldDirectory, settings);
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
        TrackNetwork = new() { Space = Camera.Space };
        TreeClearance = new()
        {
            Network = TrackNetwork,
            World = World
        };
        AddChild(TreeClearance);
        TrackRenderer = new()
        {
            Network = TrackNetwork,
            Camera = Camera
        };
        AddChild(TrackRenderer);
        GameWindows = new();
        Interface.AddChild(GameWindows);
        TrackEditor = new()
        {
            Network = TrackNetwork,
            Renderer = TrackRenderer,
            Camera = Camera,
            Generator = Generator,
            Interface = Interface,
            Windows = GameWindows
        };
        AddChild(TrackEditor);
        HUD = new()
        {
            Camera = Camera,
            World = World,
            Editor = TrackEditor,
            Windows = GameWindows,
            PreferencesFile = HudSettingsFile,
            SoundSettings = SoundSettings
        };
        Interface.AddChild(HUD);
        Trains = new()
        {
            HoPhysics = settings.Physics == "ho",
            SoundSettings = SoundSettings,
            WorldSeed = settings.Seed,
            Network = TrackNetwork,
            Catalog = Vehicles,
            Camera = Camera,
            Windows = GameWindows
        };
        AddChild(Trains);
        TrainUI = new()
        {
            Editor = Trains,
            Manager = GameWindows,
            TrackEditor = TrackEditor,
            Interface = Interface
        };
        AddChild(TrainUI);
        SignalUI = new() { Network = TrackNetwork, Interlocking = Trains.Interlocking, Windows = GameWindows,
            Renderer = TrackRenderer.Signals, Camera = Camera, Interface = Interface };
        AddChild(SignalUI);
        TrackEditor.OpenSignal = SignalUI.OpenAt;
        HUD.VehiclesButton.Disabled = HUD.TrainsButton.Disabled = false;
        HUD.VehiclesButton.TooltipText = "Vehicle catalog";
        HUD.VehiclesButton.ToggleMode = true;
        HUD.TrainsButton.TooltipText = "Trains";
        HUD.VehiclesButton.Pressed += TrainUI.ToggleCatalog;
        HUD.TrainsButton.Pressed += TrainUI.OpenFleet;
        HUD.StationsButton.Pressed += TrainUI.StationList.Open;
        TrainUI.CatalogWindow.StateChanged += () => HUD.VehiclesButton.SetPressedNoSignal(TrainUI.CatalogWindow.Visible && !TrainUI.CatalogWindow.IsCollapsed);
        Interface.MoveChild(GameWindows, -1);
        Camera.Loaded = World.HasTerrainAt;
        Camera.GroundHeight = World.GroundHeight;
        Camera.BuildMode = () => TrackEditor.Enabled || Trains.Placing;
        Camera.NavigationBlocked = () => TrackEditor.IsDragging;
        Camera.FloatingOriginEnabled = true;
        HUD.SaveRequested += () => _ = SaveGame();
        PauseUI = new() { SaveGame = SaveGame };
        Interface.AddChild(PauseUI);
        PauseUI.Window.Opened += SignalUI.Overlay.Hide;
        PauseUI.QuitToMenuRequested += ReturnToMenu;
        PauseUI.QuitGameRequested += QuitWithSave;
        HUD.PauseRequested += () => { Camera.ReleaseMouse(); PauseUI.Open(); };
    }
}
