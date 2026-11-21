using Godot;

namespace TransportSandbox;

/// <summary>Display shortcuts remain available while the interface is hidden or the game is paused.</summary>
public partial class DisplayShortcuts : Node
{
    public CanvasLayer Interface = null!;
    private Window.ModeEnum _restoreMode = Window.ModeEnum.Windowed;
    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
        var code = key.Keycode == Key.None ? key.PhysicalKeycode : key.Keycode;
        if (code == Key.F1)
        {
            Interface.Visible = !Interface.Visible;
            if (!Interface.Visible) GetViewport().GuiReleaseFocus();
            GetViewport().SetInputAsHandled();
        }
        else if (code == Key.F11)
        {
            var window = GetWindow();
            window.Mode = NextWindowMode(window.Mode);
            GetViewport().SetInputAsHandled();
        }
        else if (code == Key.Escape && !Interface.Visible)
        {
            // Menus opened with Escape must remain accessible after hiding the interface.
            Interface.Show();
        }
    }

    internal Window.ModeEnum NextWindowMode(Window.ModeEnum current)
    {
        if (current is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen) return _restoreMode;
        _restoreMode = current == Window.ModeEnum.Maximized ? current : Window.ModeEnum.Windowed;
        return Window.ModeEnum.Fullscreen;
    }
}
