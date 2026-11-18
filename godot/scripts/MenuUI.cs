using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace TransportSandbox;

public partial class MenuUI : Control
{
    public event Action<WorldSettings>? StartRequested;
    public string SettingsFile = "user://menu-settings.cfg";
    public Control Title = null!;
    public readonly List<Label> TitleLines = new();
    public Button StartButton = null!, Launch = null!, Grip = null!;
    public ColorRect Shade = null!;
    public PanelContainer Dialog = null!, Heading = null!;
    public VBoxContainer Content = null!, Advanced = null!;
    public CheckBox Recommended = null!, Custom = null!;
    public LineEdit SeedInput = null!;
    public HSlider Relief = null!;
    public Label ReliefValue = null!;
    public OptionButton Landform = null!, Physics = null!;
    public Rect2 Bounds = new(0, 100, 460, 610);
    private bool _savedBounds;
    private Control _recommendedSpacer = null!;
    private SystemFont _headingFont = null!, _bodyFont = null!;
    private FontVariation _titleFont = null!;
    private sealed record DragState(Vector2 Pointer, Rect2 Bounds, bool Resize);
    private DragState? _drag;
    private static readonly double[] Scales = { 0.5, 1, 2 };
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _bodyFont = new()
        {
            FontNames = new[] { "Inter", "Segoe UI", "sans-serif" }
        };
        _headingFont = (SystemFont)_bodyFont.Duplicate();
        _headingFont.FontWeight = 600;
        _titleFont = new()
        {
            BaseFont = _headingFont
        };
        Theme = new()
        {
            DefaultFont = _bodyFont,
            DefaultFontSize = 12
        };
        Theme.SetColor("font_color", "Label", new("e8eddf"));
        var overlay = new ColorRect { MouseFilter = MouseFilterEnum.Ignore };
        overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        overlay.Material = ShaderMaterial("shader_type canvas_item; void fragment(){float alpha=mix(0.062745,0.12549,UV.y/0.6); if(UV.y>0.6) alpha=mix(0.12549,0.4,(UV.y-0.6)/0.4); COLOR=vec4(vec3(16.0,27.0,25.0)/255.0,alpha);}");
        AddChild(overlay);
        Title = new()
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(Title);
        var titleGroup = new CanvasGroup { FitMargin = 45, ClearMargin = 45, Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/menu_title.gdshader") } };
        Title.AddChild(titleGroup);
        foreach (string word in new[] { "TRANSPORT", "SANDBOX" })
        {
            var line = new Label { Text = word };
            line.AddThemeFontOverride("font", _titleFont);
            line.AddThemeFontSizeOverride("font_size", 125);
            line.AddThemeColorOverride("font_color", new("fff9e9"));
            titleGroup.AddChild(line);
            TitleLines.Add(line);
        }
        StartButton = Primary("Start game");
        StartButton.AddThemeFontSizeOverride("font_size", 14);
        StartButton.Pressed += OpenSetup;
        AddChild(StartButton);
        BuildDialog();
        LoadPreferences();
        GetViewport().SizeChanged += LayoutMenu;
        LayoutMenu();
    }
    private static ShaderMaterial ShaderMaterial(string code) => new() { Shader = new Shader { Code = code } };
    private static StyleBoxFlat Style(string color, string border = "00000000", int radius = 4)
    {
        var style = new StyleBoxFlat { BgColor = new(color), BorderColor = new(border), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8 };
        style.SetBorderWidthAll(border != "00000000" ? 1 : 0);
        style.SetCornerRadiusAll(radius);
        return style;
    }
    private Button Primary(string text)
    {
        var button = new Button { Text = text, CustomMinimumSize = new(0, 50) };
        button.AddThemeFontOverride("font", _headingFont);
        button.AddThemeStyleboxOverride("normal", Style("eee8d8"));
        button.AddThemeStyleboxOverride("hover", Style("fff9e9"));
        button.AddThemeStyleboxOverride("pressed", Style("e3e6ce"));
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
            button.AddThemeColorOverride(state, new("233b32"));
        return button;
    }
    private static Label Label(string text, Node parent, int size = 12, string color = "c6d7bc")
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new(color));
        parent.AddChild(label);
        return label;
    }
    private static void Gap(Node parent, float height) => parent.AddChild(new Control { CustomMinimumSize = new(0, height) });
    private void BuildDialog()
    {
        Shade = new();
        Shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Shade.Material = ShaderMaterial("shader_type canvas_item; uniform sampler2D screen_texture:hint_screen_texture,filter_linear_mipmap; void fragment(){vec3 blurred=textureLod(screen_texture,SCREEN_UV,2.3).rgb;COLOR=vec4(mix(blurred,vec3(7.0,23.0,17.0)/255.0,0.533333),1.0);}");
        AddChild(Shade);
        Dialog = new();
        var style = Style("233b33fa", "c4b77e", 5);
        style.ContentMarginLeft = 11;
        style.ContentMarginRight = 15;
        style.ContentMarginTop = 10;
        style.ContentMarginBottom = 11;
        style.ShadowColor = new("07151099");
        style.ShadowSize = 28;
        style.ShadowOffset = new(0, 8);
        Dialog.AddThemeStyleboxOverride("panel", style);
        AddChild(Dialog);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        Dialog.AddChild(box);
        var headingSpace = new Control { CustomMinimumSize = new(0, 24) };
        box.AddChild(headingSpace);
        Heading = new()
        {
            Position = new(-11, -10),
            CustomMinimumSize = new(0, 32),
            MouseFilter = MouseFilterEnum.Stop,
            FocusMode = FocusModeEnum.All
        };
        var texture = new GradientTexture2D { Gradient = new Gradient { Colors = new[] { new Color("6a7954"), new Color("4b6245") } }, Width = 1, Height = 32, FillFrom = Vector2.Zero, FillTo = new(0, 1) };
        Heading.AddThemeStyleboxOverride("panel", new StyleBoxTexture { Texture = texture, ContentMarginLeft = 10, ContentMarginRight = 4, ContentMarginTop = 3, ContentMarginBottom = 5 });
        headingSpace.AddChild(Heading);
        Heading.GuiInput += HeadingInput;
        var bar = new HBoxContainer();
        Heading.AddChild(bar);
        var headingLabel = Label("New game", bar, 13, "e8eddf");
        headingLabel.AddThemeFontOverride("font", _headingFont);
        headingLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headingLabel.MouseFilter = MouseFilterEnum.Ignore;
        var close = new Button { Text = "×", CustomMinimumSize = new(25, 24) };
        var closeStyle = Style("203b31", "94a67b66", 2);
        closeStyle.ContentMarginLeft = closeStyle.ContentMarginRight = closeStyle.ContentMarginTop = closeStyle.ContentMarginBottom = 0;
        close.AddThemeStyleboxOverride("normal", closeStyle);
        close.AddThemeFontSizeOverride("font_size", 18);
        close.Pressed += CloseSetup;
        bar.AddChild(close);
        var scroller = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        Content = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        Content.AddThemeConstantOverride("separation", 10);
        box.AddChild(Content);
        Label("Game setup", Content);
        Gap(Content, 4);
        var group = new ButtonGroup();
        Recommended = new()
        {
            Text = "Recommended setup",
            ButtonGroup = group,
            ButtonPressed = true
        };
        Content.AddChild(Recommended);
        var note = Label("Natural terrain, a reproducible world, and responsive HO train behavior.", Content, 11, "a8bca8");
        note.AutowrapMode = TextServer.AutowrapMode.Word;
        Custom = new()
        {
            Text = "Advanced setup",
            ButtonGroup = group
        };
        Content.AddChild(Custom);
        foreach (var radio in new[] { Recommended, Custom })
        {
            radio.AddThemeFontSizeOverride("font_size", 13);
            foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
                radio.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
            radio.AddThemeColorOverride("font_color", new("c0cbbd"));
            radio.AddThemeColorOverride("font_hover_color", new("c0cbbd"));
            radio.AddThemeConstantOverride("h_separation", 9);
            radio.CustomMinimumSize = new(0, 18);
            foreach (bool check in new[] { true, false })
            {
                using var image = new Image();
                string svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"13\" height=\"13\"><circle cx=\"6.5\" cy=\"6.5\" r=\"5.7\" fill=\"{(check ? "#203b31" : "white")}\" stroke=\"{(check ? "#d3c18b" : "#aaa")}\"/>{(check ? "<circle cx=\"6.5\" cy=\"6.5\" r=\"3.5\" fill=\"#d3c18b\"/>" : "")}</svg>";
                image.LoadSvgFromString(svg);
                radio.AddThemeIconOverride(check ? "radio_checked" : "radio_unchecked", ImageTexture.CreateFromImage(image));
            }
        }
        Advanced = new()
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        Advanced.AddThemeConstantOverride("separation", 8);
        Content.AddChild(Advanced);
        Custom.Toggled += value => { Advanced.Visible = value; if (_recommendedSpacer != null) _recommendedSpacer.Visible = !value; Callable.From(TrapFocus).CallDeferred(); };
        Gap(Advanced, 6);
        Label("World and train behavior", Advanced);
        Advanced.AddChild(scroller);
        var fields = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        fields.AddThemeConstantOverride("separation", 8);
        scroller.AddChild(fields);
        Gap(fields, 8);
        Label("WORLD SEED", fields, 9, "c0cbbd");
        var seedRow = new HBoxContainer();
        fields.AddChild(seedRow);
        SeedInput = new()
        {
            MaxLength = 100,
            Text = "the-long-way-home",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new(0, 42)
        };
        SeedInput.AddThemeStyleboxOverride("normal", Style("ffffff08", "b2c6b438"));
        seedRow.AddChild(SeedInput);
        var random = new Button { Text = "↻", TooltipText = "Random seed", CustomMinimumSize = new(48, 42) };
        random.AddThemeStyleboxOverride("normal", Style("ffffff08", "b2c6b438"));
        random.AddThemeFontSizeOverride("font_size", 21);
        random.Pressed += () => SeedInput.Text = Base36(GD.Randi()) + "-" + Base36(GD.Randi());
        seedRow.AddChild(random);
        var reliefRow = new HBoxContainer();
        fields.AddChild(reliefRow);
        Label("MOUNTAIN RELIEF", reliefRow, 9, "c0cbbd").SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ReliefValue = Label("1,800 m", reliefRow, 9, "c0cbbd");
        Relief = new()
        {
            MinValue = 400,
            MaxValue = 3000,
            Step = 100,
            Value = 1800
        };
        var track = Style("ffffff22", "00000000", 3);
        track.ContentMarginTop = track.ContentMarginBottom = 3;
        var fill = (StyleBoxFlat)track.Duplicate();
        fill.BgColor = new("c6c799");
        Relief.AddThemeStyleboxOverride("slider", track);
        Relief.AddThemeStyleboxOverride("grabber_area", fill);
        Relief.AddThemeStyleboxOverride("grabber_area_highlight", fill);
        Relief.ValueChanged += value => ReliefValue.Text = ((int)value).ToString("N0", CultureInfo.InvariantCulture) + " m";
        fields.AddChild(Relief);
        Gap(fields, 0);
        Label("LANDFORM SCALE", fields, 9, "c0cbbd");
        Landform = new();
        foreach (string text in new[] { "Compact · 0.5×", "Natural · 1×", "Expansive · 2×" })
            Landform.AddItem(text);
        Landform.Select(1);
        fields.AddChild(Landform);
        Gap(fields, 0);
        Label("TRAIN PHYSICS", fields, 9, "c0cbbd");
        Physics = new();
        Physics.AddItem("HO · responsive miniature behavior");
        Physics.AddItem("Realistic · full-scale inertia");
        fields.AddChild(Physics);
        foreach (var selector in new[] { Landform, Physics })
        {
            selector.CustomMinimumSize = new(0, 36);
            selector.AddThemeStyleboxOverride("normal", Style("e8eddf", "a8b9a5"));
            selector.AddThemeStyleboxOverride("hover", Style("e8eddf", "a8b9a5"));
            selector.AddThemeColorOverride("font_color", new("20392f"));
            selector.AddThemeColorOverride("font_hover_color", new("20392f"));
        }
        Label("World units remain metres in both modes. HO accelerates and brakes faster, with proportionally compressed top speeds.", fields, 11, "a8bca8").AutowrapMode = TextServer.AutowrapMode.Word;
        _recommendedSpacer = new()
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        Content.AddChild(_recommendedSpacer);
        Launch = Primary("");
        Launch.CustomMinimumSize = new(0, 48);
        Launch.AddThemeStyleboxOverride("normal", Style("d1d7ba"));
        Launch.AddThemeStyleboxOverride("hover", Style("e3e6ce"));
        var launchText = Label("Start game", Launch, 12, "20392f");
        launchText.AddThemeFontOverride("font", _headingFont);
        launchText.Position = new(22, 15);
        launchText.MouseFilter = MouseFilterEnum.Ignore;
        var arrow = Label("↗", Launch, 22, "20392f");
        arrow.MouseFilter = MouseFilterEnum.Ignore;
        Launch.Resized += () => arrow.Position = new(Launch.Size.X - 36, 10);
        Launch.Pressed += Start;
        Content.AddChild(Launch);
        Advanced.Hide();
        Grip = new()
        {
            Text = "◢",
            Flat = true,
            CustomMinimumSize = new(18, 18),
            MouseDefaultCursorShape = CursorShape.Fdiagsize
        };
        Grip.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        Grip.Position = new(-18, -18);
        AddChild(Grip);
        Grip.GuiInput += input => DragStart(input, true);
        Shade.Hide();
        Dialog.Hide();
        Grip.Hide();
    }
    public void LayoutMenu()
    {
        var viewport = GetViewportRect().Size;
        bool compact = viewport.X <= 700;
        float size = (float)Math.Clamp(viewport.X * (compact ? 0.08 : 0.045), compact ? 28 : 32, compact ? 44 : 64), scale = size / 125;
        _titleFont.SpacingGlyph = 10;
        float width = compact ? viewport.X - 2 * Mathf.Clamp(viewport.X * 0.06f, 24, 96) : Math.Min(420, viewport.X * 0.38f);
        int lines = width < _titleFont.GetStringSize("TRANSPORT SANDBOX", HorizontalAlignment.Left, -1, 125).X * scale ? 2 : 1;
        Title.Size = new(width, size * 1.15f * lines);
        TitleLines[0].Text = lines == 2 ? "TRANSPORT" : "TRANSPORT SANDBOX";
        TitleLines[1].Visible = lines == 2;
        for (int i = 0; i < TitleLines.Count; i++)
        {
            TitleLines[i].Scale = Vector2.One * scale;
            TitleLines[i].Position = new(0, i * size * 1.15f - (_titleFont.GetHeight(125) * scale - size * 1.15f) / 2);
        }
        float groupHeight = Title.Size.Y + (compact ? 20 : 28) + 50, left = Mathf.Clamp(viewport.X * 0.06f, 24, 96);
        Title.Position = new(left, compact ? viewport.Y * 0.12f : (viewport.Y - groupHeight) / 2);
        StartButton.Position = Title.Position + new Vector2(0, Title.Size.Y + (compact ? 20 : 28));
        StartButton.Size = new(220, 50);
        if (!_savedBounds)
            Bounds = new(new Vector2((viewport.X - 460) / 2, Bounds.Position.Y), Bounds.Size);
        Constrain();
    }
    public void Constrain()
    {
        var viewport = GetViewportRect().Size;
        var available = new Vector2(Math.Max(1, viewport.X - 16), Math.Max(36, viewport.Y - 113));
        var size = new Vector2(Mathf.Clamp(Bounds.Size.X, Math.Min(300, available.X), available.X), Mathf.Clamp(Bounds.Size.Y, Math.Min(330, available.Y), available.Y));
        Bounds = new(new Vector2(Mathf.Clamp(Bounds.Position.X, 8, Math.Max(8, viewport.X - size.X - 8)), Mathf.Clamp(Bounds.Position.Y, 8, Math.Max(8, viewport.Y - size.Y - 105))), size);
        Dialog.Position = Bounds.Position;
        Dialog.Size = Bounds.Size;
        Heading.Size = new(Bounds.Size.X, 32);
        Grip.Position = Bounds.End - new Vector2(18, 18);
        Grip.Size = new(18, 18);
    }
    public void OpenSetup()
    {
        Shade.Show();
        Dialog.Show();
        Grip.Show();
        StartButton.FocusMode = FocusModeEnum.None;
        (Custom.ButtonPressed ? Custom : Recommended).GrabFocus();
        Callable.From(TrapFocus).CallDeferred();
    }
    public void CloseSetup()
    {
        Dialog.Hide();
        Shade.Hide();
        Grip.Hide();
        StartButton.FocusMode = FocusModeEnum.All;
        StartButton.GrabFocus();
        SavePreferences(false);
    }
    private void TrapFocus()
    {
        var controls = new List<Control>();
        CollectFocus(Dialog, controls);
        for (int i = 0; i < controls.Count; i++)
        {
            controls[i].FocusNext = controls[i].GetPathTo(controls[(i + 1) % controls.Count]);
            controls[i].FocusPrevious = controls[i].GetPathTo(controls[(i - 1 + controls.Count) % controls.Count]);
        }
    }
    private static void CollectFocus(Node node, List<Control> result)
    {
        if (node is Control control && control.IsVisibleInTree() && control.FocusMode != FocusModeEnum.None)
            result.Add(control);
        foreach (var child in node.GetChildren())
            CollectFocus(child, result);
    }
    private void HeadingInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true } key)
        {
            int amount = key.ShiftPressed ? 1 : 10;
            var delta = key.Keycode switch
            {
                Key.Left => new Vector2(-amount, 0),
                Key.Right => new Vector2(amount, 0),
                Key.Up => new Vector2(0, -amount),
                Key.Down => new Vector2(0, amount),
                _ => Vector2.Zero
            };
            Bounds = key.AltPressed ? new(Bounds.Position, Bounds.Size + delta) : new(Bounds.Position + delta, Bounds.Size);
            Constrain();
            SavePreferences(false);
        }
        DragStart(input, false);
    }
    private void DragStart(InputEvent input, bool resize)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse)
            return;
        if (mouse.Pressed)
            _drag = new(GetGlobalMousePosition(), Bounds, resize);
        else
        {
            _drag = null;
            SavePreferences(false);
        }
    }
    public override void _Input(InputEvent input)
    {
        if (Dialog.Visible && input is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            CloseSetup();
            GetViewport().SetInputAsHandled();
        }
        if (_drag == null)
            return;
        if (input is InputEventMouseMotion)
        {
            var delta = GetGlobalMousePosition() - _drag.Pointer;
            Bounds = _drag.Resize ? new(_drag.Bounds.Position, _drag.Bounds.Size + delta) : new(_drag.Bounds.Position + delta, _drag.Bounds.Size);
            Constrain();
            GetViewport().SetInputAsHandled();
        }
        else if (input is InputEventMouseButton { Pressed: false })
        {
            _drag = null;
            SavePreferences(false);
        }
    }
    public WorldSettings ChosenSettings()
    {
        if (!Custom.ButtonPressed)
            return new();
        string seed = SeedInput.Text.Trim();
        return new(seed.Length == 0 ? "the-long-way-home" : seed, Relief.Value, Scales[Landform.Selected], 0, Physics.Selected == 1 ? "realistic" : "ho");
    }
    private void Start()
    {
        var settings = ChosenSettings();
        SeedInput.Text = settings.Seed;
        SavePreferences(true);
        Launch.Disabled = true;
        CloseSetup();
        StartRequested?.Invoke(settings);
    }
    private void LoadPreferences()
    {
        using var config = new ConfigFile();
        if (config.Load(SettingsFile) != Error.Ok)
            return;
        SeedInput.Text = config.GetValue("world", "seed", "the-long-way-home").AsString();
        Relief.Value = config.GetValue("world", "mountainHeight", 1800.0).AsDouble();
        int index = Array.IndexOf(Scales, config.GetValue("world", "horizontalScale", 1.0).AsDouble());
        Landform.Select(index >= 0 ? index : 1);
        Physics.Select(config.GetValue("world", "physics", "ho").AsString() == "realistic" ? 1 : 0);
        if (config.HasSectionKey("window", "bounds"))
        {
            Bounds = config.GetValue("window", "bounds").AsRect2();
            _savedBounds = true;
        }
    }
    public void SavePreferences(bool worldSettings)
    {
        using var config = new ConfigFile();
        config.Load(SettingsFile);
        config.SetValue("window", "bounds", Bounds);
        if (worldSettings)
        {
            var settings = ChosenSettings();
            config.SetValue("world", "seed", settings.Seed);
            config.SetValue("world", "mountainHeight", settings.MountainHeight);
            config.SetValue("world", "horizontalScale", settings.HorizontalScale);
            config.SetValue("world", "seaLevel", settings.SeaLevel);
            config.SetValue("world", "physics", settings.Physics);
        }
        config.Save(SettingsFile);
    }
    private static string Base36(uint value)
    {
        string result = "";
        while (value > 0)
        {
            result = "0123456789abcdefghijklmnopqrstuvwxyz"[(int)(value % 36)] + result;
            value /= 36;
        }
        return result.Length == 0 ? "0" : result;
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= LayoutMenu;
}
