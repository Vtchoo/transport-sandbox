using Godot;
using System;

namespace TransportSandbox;

public partial class GameHUD
{
    private VBoxContainer _graphicsRoot = null!;
    public OptionButton TreeQuality = null!;
    public CheckBox ShadowsEnabled = null!;
    public HSlider RenderDistance = null!, ShadowDistance = null!, ResolutionScale = null!;
    private GraphicsSettings _graphicsDraft = new();
    private GraphicsSettings? _graphicsPrevious;
    private Action _refreshGraphics = () => { };
    public Button ApplyGraphicsButton = null!, KeepGraphicsButton = null!, UndoGraphicsButton = null!;
    public GameWindow GraphicsConfirmation = null!;
    private Label _graphicsCountdown = null!;
    private ulong _graphicsDeadline;
    private void BuildGraphicsMenu()
    {
        _graphicsDraft = GraphicsSettings.Copy();
        var graphics = new Button { Text = "Graphics \u2192", CustomMinimumSize = new(0, 58) };
        UiSkin.Button(graphics);
        _settingsRoot.AddChild(graphics);
        _graphicsRoot = new() { Visible = false };
        _graphicsRoot.AddThemeConstantOverride("separation", 9);
        SettingsWindow.Body.AddChild(_graphicsRoot);
        graphics.Pressed += OpenGraphicsSettings;
        var back = new Button { Text = "\u2190 Settings" };
        UiSkin.Button(back);
        _graphicsRoot.AddChild(back);
        back.Pressed += () => { _graphicsRoot.Hide(); _settingsRoot.Show(); SettingsWindow.RefreshFocus(); graphics.GrabFocus(); };
        UiSkin.Label(_graphicsRoot, "Graphics", 18, "e8eddf");
        var refreshSliders = new System.Collections.Generic.List<Action>();
        HSlider Slider(string title, double min, double max, double step, double initial, Func<double, string> format, Action<double> apply, Func<double> current)
        {
            var label = UiSkin.Label(_graphicsRoot, format(initial), 12, "e8eddf");
            var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = initial, TooltipText = title, CustomMinimumSize = new(0, 24) };
            _graphicsRoot.AddChild(slider);
            refreshSliders.Add(() => { slider.SetValueNoSignal(current()); label.Text = format(current()); });
            slider.ValueChanged += value => { label.Text = format(value); apply(value); };
            return slider;
        }
        RenderDistance = Slider("Terrain render distance", 3, 10, 1, _graphicsDraft.TerrainRings,
            v => $"Terrain distance: {v * TerrainWorld.Chunk / 1000:0.0} km", v => _graphicsDraft.TerrainRings = (int)v, () => _graphicsDraft.TerrainRings);
        UiSkin.Label(_graphicsRoot, "TREE DETAIL", 9, "c0cbbd");
        TreeQuality = new();
        foreach (string name in new[] { "Off", "Low (simple crowns, no trunks)", "Standard", "High (detailed trees farther away)" }) TreeQuality.AddItem(name);
        TreeQuality.Select(_graphicsDraft.TreeDetail);
        UiSkin.Selector(TreeQuality);
        _graphicsRoot.AddChild(TreeQuality);
        TreeQuality.ItemSelected += index => { _graphicsDraft.TreeDetail = (int)index; };
        ShadowsEnabled = new() { Text = "Enable shadows", ButtonPressed = _graphicsDraft.Shadows };
        _graphicsRoot.AddChild(ShadowsEnabled);
        ShadowDistance = Slider("Shadow distance", 100, 2000, 100, _graphicsDraft.ShadowDistance,
            v => $"Shadow distance: {v:0} m", v => _graphicsDraft.ShadowDistance = (float)v, () => _graphicsDraft.ShadowDistance);
        ShadowDistance.Editable = _graphicsDraft.Shadows;
        ShadowsEnabled.Toggled += enabled => { _graphicsDraft.Shadows = enabled; ShadowDistance.Editable = enabled; };
        ResolutionScale = Slider("3D resolution scale", 50, 100, 5, _graphicsDraft.ResolutionScale * 100,
            v => $"3D resolution: {v:0}%", v => _graphicsDraft.ResolutionScale = (float)v / 100, () => _graphicsDraft.ResolutionScale * 100);
        var fxaa = new CheckBox { Text = "Anti-aliasing (FXAA)", ButtonPressed = _graphicsDraft.Fxaa };
        _graphicsRoot.AddChild(fxaa);
        fxaa.Toggled += enabled => { _graphicsDraft.Fxaa = enabled; };
        var restore = new Button { Text = "Restore defaults", CustomMinimumSize = new(0, 36) };
        UiSkin.Button(restore);
        _graphicsRoot.AddChild(restore);
        restore.Pressed += () =>
        {
            _graphicsDraft.CopyFrom(new GraphicsSettings());
            _refreshGraphics();
        };
        UiSkin.Label(_graphicsRoot, "Use Apply to preview changes, then confirm within 15 seconds to keep them. Lower tree detail, shadow distance or resolution to improve performance. Terrain streams after resuming.", 12, "b5c3b1").AutowrapMode = TextServer.AutowrapMode.Word;
        _refreshGraphics = () =>
        {
            foreach (var refresh in refreshSliders) refresh();
            TreeQuality.Select(_graphicsDraft.TreeDetail);
            ShadowsEnabled.SetPressedNoSignal(_graphicsDraft.Shadows);
            ShadowDistance.Editable = _graphicsDraft.Shadows;
            fxaa.SetPressedNoSignal(_graphicsDraft.Fxaa);
        };
        ApplyGraphicsButton = new Button { Text = "Apply", CustomMinimumSize = new(0, 36) };
        UiSkin.Button(ApplyGraphicsButton);
        _graphicsRoot.AddChild(ApplyGraphicsButton);
        GraphicsConfirmation = (SettingsManager ?? Windows).Create("Keep graphics changes?", WindowPlacement.Center,
            new(380, 230), minimum: new(300, 200), modal: true, scrollBody: false);
        _graphicsCountdown = UiSkin.Label(GraphicsConfirmation.Body, "", 14, "e8eddf");
        _graphicsCountdown.AutowrapMode = TextServer.AutowrapMode.Word;
        KeepGraphicsButton = new Button { Text = "Keep changes" };
        UndoGraphicsButton = new Button { Text = "Undo changes" };
        foreach (var button in new[] { KeepGraphicsButton, UndoGraphicsButton })
        {
            UiSkin.Button(button);
            GraphicsConfirmation.Body.AddChild(button);
        }
        ApplyGraphicsButton.Pressed += PreviewGraphics;
        KeepGraphicsButton.Pressed += () =>
        {
            if (_graphicsPrevious == null) return;
            GraphicsSettings.Save();
            _graphicsPrevious = null;
            GraphicsConfirmation.Close();
        };
        UndoGraphicsButton.Pressed += GraphicsConfirmation.Close;
        GraphicsConfirmation.Closed += UndoGraphics;
        SettingsWindow.Closed += () =>
        {
            if (_graphicsPrevious != null) GraphicsConfirmation.Close();
            _graphicsDraft.CopyFrom(GraphicsSettings);
            _refreshGraphics();
        };
    }
    private void PreviewGraphics()
    {
        if (_graphicsPrevious != null) return;
        _graphicsPrevious = GraphicsSettings.Copy();
        GraphicsSettings.CopyFrom(_graphicsDraft);
        ApplyGraphics();
        _graphicsDeadline = Time.GetTicksMsec() + 15000;
        GraphicsConfirmation.Open();
        UndoGraphicsButton.GrabFocus();
        UpdateGraphicsCountdown();
    }
    private void UpdateGraphicsCountdown()
    {
        if (_graphicsPrevious == null) return;
        ulong now = Time.GetTicksMsec();
        if (now >= _graphicsDeadline) { GraphicsConfirmation.Close(); return; }
        _graphicsCountdown.Text = $"Keep these settings? Changes will be undone in {Math.Ceiling((_graphicsDeadline - now) / 1000.0):0} seconds.";
        ulong deadline = _graphicsDeadline;
        GetTree().CreateTimer(.2, processAlways: true, ignoreTimeScale: true).Timeout += () =>
        {
            if (IsInstanceValid(this) && IsInsideTree() && deadline == _graphicsDeadline) UpdateGraphicsCountdown();
        };
    }
    private void UndoGraphics()
    {
        if (_graphicsPrevious != null)
        {
            GraphicsSettings.CopyFrom(_graphicsPrevious);
            _graphicsPrevious = null;
            ApplyGraphics();
        }
        _graphicsDraft.CopyFrom(GraphicsSettings);
        _refreshGraphics();
        SettingsWindow.RefreshFocus();
        ApplyGraphicsButton.GrabFocus();
    }
    public void OpenGraphicsSettings()
    {
        _graphicsDraft.CopyFrom(GraphicsSettings);
        _refreshGraphics();
        OpenSettings();
        _settingsRoot.Hide();
        _graphicsRoot.Show();
        SettingsWindow.RefreshFocus();
        RenderDistance.GrabFocus();
    }
}
