using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

namespace TransportSandbox;

/// <summary>Gameplay HUD and its view/settings windows. Game systems own the actual state.</summary>
public partial class GameHUD : Control
{
    public event Action? MainMenuRequested;
    public WorldCamera Camera = null!;
    public TerrainWorld World = null!;
    public TrackEditor Editor = null!;
    public WindowManager Windows = null!;
    public string PreferencesFile = "user://game-ui.cfg";
    public PanelContainer ActionBar = null!, DebugPanel = null!;
    public Button BuildButton = null!, VehiclesButton = null!, TrainsButton = null!, CameraButton = null!, ViewButton = null!, SettingsButton = null!, MenuButton = null!;
    public GameWindow ViewWindow = null!, SettingsWindow = null!;
    public CheckBox Contours = null!;
    public OptionButton ContourInterval = null!, CameraMode = null!;
    public Label Hint = null!, ContourLegend = null!;
    private HBoxContainer _top = null!;
    private HBoxContainer _brand = null!;
    private Label _debug = null!;
    private VBoxContainer _settingsRoot = null!, _gameplayRoot = null!;
    private static readonly int[] Intervals = { 5, 10, 20, 50, 100 };
    private double _debugElapsed;
    private readonly Dictionary<Button, string> _topLabels = new();
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = UiSkin.CreateTheme();
        _brand = new()
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        _brand.AddThemeConstantOverride("separation", 12);
        AddChild(_brand);
        var emblem = new PanelContainer { CustomMinimumSize = new(29, 32), MouseFilter = MouseFilterEnum.Ignore };
        emblem.AddThemeStyleboxOverride("panel", UiSkin.Box("00000000", "8baf9b66", 5, 3, 3));
        _brand.AddChild(emblem);
        emblem.AddChild(new TextureRect { Texture = UiSkin.Icon("trains"), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new(22, 24), MouseFilter = MouseFilterEnum.Ignore });
        var brand = UiSkin.Label(_brand, "TRANSPORT SANDBOX", 12, "e8eddf");
        brand.MouseFilter = MouseFilterEnum.Ignore;
        brand.VerticalAlignment = VerticalAlignment.Center;
        brand.AddThemeFontOverride("font", new FontVariation { BaseFont = new SystemFont { FontNames = new[] { "Inter", "Segoe UI", "sans-serif" }, FontWeight = 600 }, SpacingGlyph = 2 });
        brand.AddThemeColorOverride("font_shadow_color", new("182c2b"));
        brand.AddThemeConstantOverride("shadow_offset_y", 1);
        brand.AddThemeConstantOverride("shadow_outline_size", 4);
        _top = new();
        _top.AddThemeConstantOverride("separation", 7);
        AddChild(_top);
        Button Top(string text, string icon, Action action)
        {
            var button = new Button { Text = text, Icon = UiSkin.Icon(icon), ExpandIcon = false };
            button.AddThemeConstantOverride("icon_max_width", 16);
            UiSkin.Button(button);
            button.Pressed += action;
            _top.AddChild(button);
            _topLabels[button] = text;
            return button;
        }
        CameraButton = Top("Overlook · C", "camera", () => Camera.SetFreeMode(!Camera.FreeMode));
        CameraButton.TooltipText = "Switch camera mode (C)";
        ViewButton = Top("View", "view", () => Toggle(ViewWindow));
        SettingsButton = Top("Settings", "settings", OpenSettings);
        var share = Top("Copy world seed", "copy", () => { });
        share.Pressed += () =>
        {
            DisplayServer.ClipboardSet(World.Generator.SeedText);
            _topLabels[share] = "Seed copied";
            LayoutHUD();
            GetTree().CreateTimer(2).Timeout += () => { if (IsInstanceValid(this)) { _topLabels[share] = "Copy world seed"; LayoutHUD(); } };
        };
        MenuButton = Top("Main menu", "home", () => MainMenuRequested?.Invoke());
        ActionBar = new();
        var barStyle = UiSkin.Box("1c302bef", "b2c6b455", 9, 7, 7);
        barStyle.ShadowColor = new("07151055");
        barStyle.ShadowSize = 12;
        ActionBar.AddThemeStyleboxOverride("panel", barStyle);
        AddChild(ActionBar);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 7);
        ActionBar.AddChild(actions);
        Button ActionButton(string text, string icon)
        {
            var button = new Button { Text = text, Icon = UiSkin.Icon(icon), ExpandIcon = false, CustomMinimumSize = new(0, 44) };
            button.AddThemeConstantOverride("icon_max_width", 22);
            UiSkin.Button(button, true);
            actions.AddChild(button);
            return button;
        }
        BuildButton = ActionButton("", "fence");
        BuildButton.IconAlignment = HorizontalAlignment.Center;
        BuildButton.TooltipText = "Build track";
        BuildButton.ToggleMode = true;
        BuildButton.Toggled += Editor.SetEnabled;
        VehiclesButton = ActionButton("Vehicles", "vehicles");
        VehiclesButton.Disabled = true;
        VehiclesButton.TooltipText = "Vehicle purchasing is not ported yet.";
        TrainsButton = ActionButton("Trains", "trains");
        TrainsButton.Disabled = true;
        TrainsButton.TooltipText = "Train windows are not ported yet.";
        Editor.EnabledChanged += SynchronizeBuild;
        Hint = UiSkin.Label(this, "", 9, "edf1e6");
        Hint.MouseFilter = MouseFilterEnum.Ignore;
        Hint.HorizontalAlignment = HorizontalAlignment.Center;
        Hint.AddThemeColorOverride("font_shadow_color", new("12241d"));
        Hint.AddThemeConstantOverride("shadow_offset_y", 1);
        Hint.AddThemeConstantOverride("shadow_outline_size", 3);
        DebugPanel = new()
        {
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore
        };
        DebugPanel.AddThemeStyleboxOverride("panel", UiSkin.Box("07110dbb", "00000000", 0, 6, 0));
        AddChild(DebugPanel);
        _debug = new()
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        _debug.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Consolas", "monospace" } });
        _debug.AddThemeFontSizeOverride("font_size", 12);
        DebugPanel.AddChild(_debug);
        BuildViewWindow();
        BuildSettingsWindow();
        LoadPreferences();
        Camera.ModeChanged += RefreshCamera;
        Windows.ModalChanged += BlockCamera;
        GetViewport().SizeChanged += LayoutHUD;
        RefreshCamera();
        SynchronizeBuild(Editor.Enabled);
        LayoutHUD();
    }
    private static void Toggle(GameWindow window)
    {
        if (window.Visible)
            window.Close();
        else
            window.Open();
    }
    private void BlockCamera(bool blocked) => Camera.InputBlocked = blocked;
    private void SynchronizeBuild(bool enabled) => BuildButton.SetPressedNoSignal(enabled);
    private void BuildViewWindow()
    {
        ViewWindow = Windows.Create("View options", WindowPlacement.TopRight, new Vector2(300, 260), new(0, 116));
        Contours = new()
        {
            Text = "Show terrain contours"
        };
        Contours.AddThemeFontSizeOverride("font_size", 14);
        ViewWindow.Body.AddChild(Contours);
        UiSkin.Label(ViewWindow.Body, "ELEVATION INTERVAL", 9, "c0cbbd");
        ContourInterval = new();
        foreach (int interval in Intervals)
            ContourInterval.AddItem($"{interval} metres", interval);
        ContourInterval.Select(1);
        UiSkin.Selector(ContourInterval);
        ViewWindow.Body.AddChild(ContourInterval);
        ContourLegend = UiSkin.Label(ViewWindow.Body, "", 11, "b5c3b1");
        ContourLegend.AutowrapMode = TextServer.AutowrapMode.Word;
        Contours.Toggled += _ => ApplyView();
        ContourInterval.ItemSelected += _ => ApplyView();
    }
    private void ApplyView()
    {
        int interval = Intervals[ContourInterval.Selected];
        World.ShowContours(Contours.ButtonPressed);
        World.SetContourInterval(interval);
        ContourLegend.Text = $"Bold contours every {interval * 5} m, measured from sea level. Trees are hidden while contours are shown.";
        SavePreferences();
    }
    private void BuildSettingsWindow()
    {
        SettingsWindow = Windows.Create("Settings", WindowPlacement.Center, new Vector2(440, 360), minimum: new(300, 240), modal: true);
        _settingsRoot = new();
        SettingsWindow.Body.AddChild(_settingsRoot);
        var gameplay = new Button { Text = "Gameplay  →", CustomMinimumSize = new(0, 58) };
        UiSkin.Button(gameplay);
        _settingsRoot.AddChild(gameplay);
        gameplay.Pressed += () => { _settingsRoot.Hide(); _gameplayRoot.Show(); SettingsWindow.RefreshFocus(); CameraMode.GrabFocus(); };
        _gameplayRoot = new()
        {
            Visible = false
        };
        _gameplayRoot.AddThemeConstantOverride("separation", 12);
        SettingsWindow.Body.AddChild(_gameplayRoot);
        var back = new Button { Text = "← Settings" };
        UiSkin.Button(back);
        _gameplayRoot.AddChild(back);
        back.Pressed += () => { _gameplayRoot.Hide(); _settingsRoot.Show(); SettingsWindow.RefreshFocus(); gameplay.GrabFocus(); };
        UiSkin.Label(_gameplayRoot, "Gameplay", 18, "e8eddf");
        UiSkin.Label(_gameplayRoot, "CAMERA MODE", 9, "c0cbbd");
        CameraMode = new();
        CameraMode.AddItem("Overlook (default)");
        CameraMode.AddItem("Free camera");
        UiSkin.Selector(CameraMode);
        _gameplayRoot.AddChild(CameraMode);
        CameraMode.ItemSelected += index => Camera.SetFreeMode(index == 1);
        UiSkin.Label(_gameplayRoot, "Overlook pans and orbits above the terrain. Free camera lets you fly and look around independently.", 12, "b5c3b1").AutowrapMode = TextServer.AutowrapMode.Word;
        UiSkin.Label(_gameplayRoot, "Press C during gameplay to switch. Your preference is saved automatically.", 11, "b5c3b1").AutowrapMode = TextServer.AutowrapMode.Word;
    }
    public void OpenSettings()
    {
        Editor.Cancel();
        _settingsRoot.Show();
        _gameplayRoot.Hide();
        SettingsWindow.Open();
    }
    private void RefreshCamera()
    {
        _topLabels[CameraButton] = Camera.FreeMode ? "Free camera · C" : "Overlook · C";
        CameraMode.Select(Camera.FreeMode ? 1 : 0);
        Hint.Text = Camera.FreeMode ? "WASD: move · Right drag: look · Q / E: down / up · Shift: faster · C: overlook · F3: debug" : "WASD: pan · Right drag: pan · Middle drag: orbit / tilt · Scroll: zoom · Shift: faster · C: free camera · F3: debug";
        SavePreferences();
        LayoutHUD();
    }
    public void LayoutHUD()
    {
        var viewport = GetViewportRect().Size;
        bool compact = viewport.X <= 700;
        foreach (var (button, text) in new[] { (VehiclesButton, "Vehicles"), (TrainsButton, "Trains") })
        {
            button.Text = viewport.X < 460 ? "" : text;
            button.IconAlignment = viewport.X < 460 ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            if (viewport.X < 460 && !button.Disabled)
                button.TooltipText = text;
        }
        ActionBar.Size = ActionBar.GetCombinedMinimumSize();
        ActionBar.Position = new((viewport.X - ActionBar.Size.X) / 2, viewport.Y - (compact ? 65 : 46) - ActionBar.Size.Y);
        _brand.Position = new(viewport.X * .03f, 22);
        foreach (var (button, text) in _topLabels)
        {
            button.Text = compact ? "" : text;
            button.IconAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            button.TooltipText = text;
        }
        _top.Size = _top.GetCombinedMinimumSize();
        _top.Position = new(Math.Max(12, viewport.X - viewport.X * .03f - _top.Size.X), 85);
        Hint.Position = new(12, viewport.Y - 35);
        Hint.Size = new(Math.Max(0, viewport.X - 24), 18);
        Hint.ClipText = true;
        DebugPanel.Position = new(compact ? 12 : viewport.X * .03f, compact ? 132 : 80);
        DebugPanel.Size = DebugPanel.GetCombinedMinimumSize();
    }
    public override void _Process(double delta)
    {
        if (!DebugPanel.Visible)
            return;
        _debugElapsed += delta;
        if (_debugElapsed < .25)
            return;
        _debugElapsed = 0;
        var p = Camera.Position;
        _debug.Text = $"TRANSPORT SANDBOX · DEBUG · F3 TO HIDE\n{(Camera.FreeMode ? "FREE CAMERA" : "OVERLOOK CAMERA")}\nSeed: {World.Generator.SeedText}\nX {p.X:F1}  Y {p.Y:F1}  Z {p.Z:F1}\nChunks: {World.Chunks.Count} / {World.Wanted.Count}\nTerrain workers: {World.WorkerLimit}   FPS: {Engine.GetFramesPerSecond()}";
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 } && GetViewport().GuiGetFocusOwner() is not LineEdit && !Windows.HasModal)
        {
            DebugPanel.Visible = !DebugPanel.Visible;
            _debugElapsed = 1;
            _Process(0);
            GetViewport().SetInputAsHandled();
        }
    }
    private void LoadPreferences()
    {
        using var file = new ConfigFile();
        if (file.Load(PreferencesFile) == Error.Ok)
        {
            Contours.SetPressedNoSignal(file.GetValue("view", "contours", false).AsBool());
            int index = Array.IndexOf(Intervals, file.GetValue("view", "interval", 10).AsInt32());
            ContourInterval.Select(index >= 0 ? index : 1);
            Camera.SetFreeMode(file.GetValue("camera", "free", false).AsBool());
        }
        ApplyView();
    }
    private void SavePreferences()
    {
        using var file = new ConfigFile();
        file.SetValue("view", "contours", Contours.ButtonPressed);
        file.SetValue("view", "interval", Intervals[ContourInterval.Selected]);
        file.SetValue("camera", "free", Camera.FreeMode);
        file.Save(PreferencesFile);
    }
    public override void _ExitTree()
    {
        Camera.ModeChanged -= RefreshCamera;
        Editor.EnabledChanged -= SynchronizeBuild;
        Windows.ModalChanged -= BlockCamera;
        GetViewport().SizeChanged -= LayoutHUD;
    }
}
