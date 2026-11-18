using Godot;
using System;

namespace TransportSandbox;

/// <summary>In-game window chrome. Independent of the operating system's Godot.Window.</summary>
public partial class GameWindow : Control
{
    public event Action? Opened, Closed, Minimized, StateChanged;
    public string WindowTitle = "Window";
    public bool Modal;
    public bool ScrollBody = true;
    private bool _collapsible = true, _minimizable = true;
    public bool Collapsible
    {
        get => _collapsible;
        set
        {
            if (!value && IsCollapsed)
                ToggleCollapsed();
            _collapsible = value;
            if (IsInstanceValid(_collapse))
                _collapse.Visible = value;
        }
    }
    public bool Minimizable
    {
        get => _minimizable;
        set
        {
            _minimizable = value;
            if (IsInstanceValid(_minimize))
                _minimize.Visible = value;
            if (!value && IsMinimized)
                Open();
        }
    }
    public bool IsMinimized
    {
        get; private set;
    }
    public bool IsCollapsed
    {
        get; private set;
    }
    public Vector2 MinimumSize = new(270, 200);
    public Rect2 Bounds = new(40, 85, 330, 300);
    public VBoxContainer Body = null!, Footer = null!;
    public WindowManager Manager = null!;
    private Panel _background = null!;
    private PanelContainer _header = null!;
    private MarginContainer _content = null!;
    private Button _grip = null!, _collapse = null!, _minimize = null!;
    private Vector2? _dragPointer;
    private Rect2 _dragBounds;
    private bool _resizing;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        _background = new()
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        _background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_background);
        var frame = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.AddThemeConstantOverride("separation", 8);
        AddChild(frame);
        _header = new()
        {
            CustomMinimumSize = new(0, 32),
            MouseDefaultCursorShape = CursorShape.Drag,
            FocusMode = FocusModeEnum.All
        };
        frame.AddChild(_header);
        _header.GuiInput += HeaderInput;
        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 3);
        _header.AddChild(titleRow);
        var title = UiSkin.Label(titleRow, WindowTitle, 13, "e8eddf");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.MouseFilter = MouseFilterEnum.Ignore;
        Button Chrome(string icon, string tooltip, Action action)
        {
            var button = new Button { Icon = UiSkin.Icon(icon), TooltipText = tooltip, CustomMinimumSize = new(25, 24), ExpandIcon = true };
            button.AddThemeConstantOverride("icon_max_width", 16);
            button.AddThemeStyleboxOverride("normal", UiSkin.Box("203b31", "94a67b66", 2, 0, 0));
            button.AddThemeStyleboxOverride("hover", UiSkin.Box("66744d", "94a67b66", 2, 0, 0));
            button.AddThemeStyleboxOverride("pressed", UiSkin.Box("4b6245", "c4b77e", 2, 0, 0));
            button.AddThemeStyleboxOverride("hover_pressed", UiSkin.Box("66744d", "c4b77e", 2, 0, 0));
            button.AddThemeStyleboxOverride("focus", UiSkin.Box("00000000", "ebbc75", 2, 0, 0));
            button.Pressed += action;
            titleRow.AddChild(button);
            return button;
        }
        _minimize = Chrome("minimize", "Minimize", Minimize);
        _minimize.Visible = Minimizable;
        _collapse = Chrome("collapse", "Roll up", ToggleCollapsed);
        _collapse.Visible = Collapsible;
        Chrome("close", "Close", Close);
        _content = new()
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        foreach (var (side, amount) in new[] { ("left", 10), ("right", 14), ("top", 0), ("bottom", 10) })
            _content.AddThemeConstantOverride("margin_" + side, amount);
        frame.AddChild(_content);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        if (ScrollBody)
            _content.AddChild(scroll);
        Body = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        Body.AddThemeConstantOverride("separation", 10);
        if (ScrollBody)
            scroll.AddChild(Body);
        else
        {
            _content.AddChild(Body);
            scroll.Free();
            Body.SizeFlagsVertical = SizeFlags.ExpandFill;
        }
        Footer = new();
        Footer.AddThemeConstantOverride("separation", 10);
        var footerMargin = new MarginContainer();
        footerMargin.AddThemeConstantOverride("margin_left", 10);
        footerMargin.AddThemeConstantOverride("margin_right", 14);
        footerMargin.AddThemeConstantOverride("margin_bottom", 11);
        frame.AddChild(footerMargin);
        footerMargin.AddChild(Footer);
        footerMargin.Visible = false;
        Footer.ChildEnteredTree += _ => footerMargin.Show();
        _grip = new()
        {
            Icon = UiSkin.Icon("resize"),
            Flat = true,
            ExpandIcon = true,
            MouseDefaultCursorShape = CursorShape.Fdiagsize
        };
        _grip.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        _grip.OffsetLeft = -18;
        _grip.OffsetTop = -18;
        AddChild(_grip);
        _grip.GuiInput += input => BeginDrag(input, true);
        SetActive(false);
        Constrain();
        Hide();
    }
    public void SetActive(bool active)
    {
        var panel = UiSkin.Box("233b33fa", active ? "c4b77e" : "526b58", 5, 0, 0);
        panel.ShadowColor = new("07151099");
        panel.ShadowSize = active ? 28 : 18;
        panel.ShadowOffset = new(0, 8);
        _background.AddThemeStyleboxOverride("panel", panel);
        if (active)
        {
            var texture = new GradientTexture2D { Gradient = new Gradient { Colors = new[] { new Color("6a7954"), new Color("4b6245") } }, Width = 1, Height = 32, FillFrom = Vector2.Zero, FillTo = new(0, 1) };
            _header.AddThemeStyleboxOverride("panel", new StyleBoxTexture { Texture = texture, ContentMarginLeft = 10, ContentMarginRight = 4, ContentMarginTop = 3, ContentMarginBottom = 5 });
        }
        else
            _header.AddThemeStyleboxOverride("panel", UiSkin.Box("344f40", "102a21", 0, 6, 4));
    }
    public void Open()
    {
        bool wasOpen = Visible && !IsMinimized;
        IsMinimized = false;
        Show();
        Manager.Activate(this);
        Constrain();
        Manager.Refresh();
        if (!wasOpen)
            Opened?.Invoke();
        if (Modal)
        {
            _header.GrabFocus();
            Callable.From(RefreshFocus).CallDeferred();
        }
        StateChanged?.Invoke();
    }
    public void Close()
    {
        if (!Visible && !IsMinimized)
            return;
        _dragPointer = null;
        Hide();
        IsMinimized = false;
        Manager.Refresh();
        Closed?.Invoke();
        StateChanged?.Invoke();
    }
    public void Minimize()
    {
        if (!Minimizable)
            return;
        Hide();
        IsMinimized = true;
        _dragPointer = null;
        Manager.Refresh();
        Minimized?.Invoke();
        StateChanged?.Invoke();
    }
    public void ToggleCollapsed()
    {
        if (!Collapsible)
            return;
        IsCollapsed = !IsCollapsed;
        _content.Visible = !IsCollapsed;
        Footer.GetParent<Control>().Visible = !IsCollapsed && Footer.GetChildCount() > 0;
        _grip.Visible = !IsCollapsed;
        _collapse.Icon = UiSkin.Icon(IsCollapsed ? "expand" : "collapse");
        Constrain();
        StateChanged?.Invoke();
    }
    public void RefreshFocus()
    {
        if (!Modal || !Visible)
            return;
        var controls = new System.Collections.Generic.List<Control>();
        void Collect(Node node)
        {
            if (node is Control control && control.IsVisibleInTree() && control.FocusMode != FocusModeEnum.None)
                controls.Add(control);
            foreach (var child in node.GetChildren())
                Collect(child);
        }
        Collect(this);
        for (int i = 0; i < controls.Count; i++)
        {
            controls[i].FocusNext = controls[i].GetPathTo(controls[(i + 1) % controls.Count]);
            controls[i].FocusPrevious = controls[i].GetPathTo(controls[(i + controls.Count - 1) % controls.Count]);
        }
    }
    public void SetBounds(Rect2 bounds)
    {
        Bounds = bounds;
        Constrain();
    }
    public void Constrain()
    {
        var viewport = GetViewportRect().Size;
        var available = new Vector2(Math.Max(1, viewport.X - 16), Math.Max(36, viewport.Y - 105));
        var size = new Vector2(Mathf.Clamp(Bounds.Size.X, Math.Min(MinimumSize.X, available.X), available.X), Mathf.Clamp(Bounds.Size.Y, Math.Min(MinimumSize.Y, available.Y), available.Y));
        float height = IsCollapsed ? 34 : size.Y;
        var position = new Vector2(Mathf.Clamp(Bounds.Position.X, 8, Math.Max(8, viewport.X - size.X - 8)), Mathf.Clamp(Bounds.Position.Y, 8, Math.Max(8, viewport.Y - height - 105)));
        Bounds = new(position, size);
        Position = position;
        Size = new(size.X, height);
    }
    private void HeaderInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true } key)
        {
            int step = key.ShiftPressed ? 1 : 10;
            var delta = key.Keycode switch
            {
                Key.Left => new Vector2(-step, 0),
                Key.Right => new Vector2(step, 0),
                Key.Up => new Vector2(0, -step),
                Key.Down => new Vector2(0, step),
                _ => Vector2.Zero
            };
            SetBounds(key.AltPressed ? new(Bounds.Position, Bounds.Size + delta) : new(Bounds.Position + delta, Bounds.Size));
        }
        BeginDrag(input, false);
    }
    private void BeginDrag(InputEvent input, bool resize)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse)
            return;
        if (mouse.Pressed)
        {
            Manager.Activate(this);
            _dragPointer = GetGlobalMousePosition();
            _dragBounds = Bounds;
            _resizing = resize;
        }
        else
            _dragPointer = null;
    }
    public override void _Input(InputEvent input)
    {
        if (_dragPointer is not { } pointer)
            return;
        if (input is InputEventMouseMotion)
        {
            var delta = GetGlobalMousePosition() - pointer;
            SetBounds(_resizing ? new(_dragBounds.Position, _dragBounds.Size + delta) : new(_dragBounds.Position + delta, _dragBounds.Size));
            GetViewport().SetInputAsHandled();
        }
        else if (input is InputEventMouseButton { Pressed: false })
            _dragPointer = null;
    }
}
