using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public partial class WindowManager : Control
{
    public event Action<bool>? ModalChanged;
    public readonly List<GameWindow> Windows = new();
    public GameWindow? Active
    {
        get; private set;
    }
    public bool HasModal => Windows.Any(window => window.Modal && window.Visible);
    public HBoxContainer Taskbar = null!;
    private ColorRect _shade = null!;
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = UiSkin.CreateTheme();
        _shade = new()
        {
            Color = new(.025f, .09f, .065f, .53f),
            Visible = false
        };
        _shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_shade);
        Taskbar = new()
        {
            Position = new(12, 0)
        };
        Taskbar.AddThemeConstantOverride("separation", 5);
        AddChild(Taskbar);
        GetViewport().SizeChanged += LayoutWindows;
        LayoutWindows();
    }
    public GameWindow Create(string title, Rect2 bounds, Vector2? minimum = null, bool modal = false, bool? collapsible = null, bool? minimizable = null, bool scrollBody = true)
    {
        var window = new GameWindow { WindowTitle = title, Bounds = bounds, MinimumSize = minimum ?? new(270, 200), Modal = modal, Collapsible = collapsible ?? !modal, Minimizable = minimizable ?? !modal, ScrollBody = scrollBody, Manager = this };
        Windows.Add(window);
        AddChild(window);
        MoveChild(Taskbar, -1);
        return window;
    }
    public GameWindow Create(string title, WindowPlacement placement, Vector2 size, Vector2 offset = default,
        Vector2? minimum = null, bool modal = false, bool? collapsible = null, bool? minimizable = null, bool scrollBody = true)
    {
        var window = Create(title, new Rect2(Vector2.Zero, size), minimum, modal, collapsible, minimizable, scrollBody);
        window.Place(placement, offset);
        return window;
    }
    public void Activate(GameWindow window)
    {
        var modal = Windows.LastOrDefault(w => w.Modal && w.Visible);
        if (modal != null && modal != window)
            return;
        Active = window;
        MoveChild(window, -1);
        foreach (var item in Windows)
            item.SetActive(item == window);
        if (window.Modal)
        {
            MoveChild(_shade, GetChildCount() - 1);
            MoveChild(window, -1);
        }
        MoveChild(Taskbar, -1);
    }
    public void Refresh()
    {
        foreach (var child in Taskbar.GetChildren())
        {
            Taskbar.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var window in Windows.Where(w => w.IsMinimized))
        {
            var button = new Button { Text = window.WindowTitle };
            UiSkin.Button(button);
            button.Pressed += window.Open;
            Taskbar.AddChild(button);
        }
        bool modal = HasModal;
        _shade.Visible = modal;
        ModalChanged?.Invoke(modal);
        if (Active != null && !Active.Visible)
        {
            Active = null;
            var next = GetChildren().OfType<GameWindow>().LastOrDefault(w => w.Visible);
            if (next != null)
                Activate(next);
        }
    }
    private void LayoutWindows()
    {
        foreach (var window in Windows)
            window.Constrain();
        Taskbar.Position = new(12, GetViewportRect().Size.Y - 36);
    }
    public override void _Input(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } && Active?.Modal == true)
        {
            Active.Close();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse)
            return;
        foreach (var window in GetChildren().OfType<GameWindow>().Reverse())
            if (window.Visible && window.GetGlobalRect().HasPoint(mouse.Position))
            {
                Activate(window);
                break;
            }
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= LayoutWindows;
}
