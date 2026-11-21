using Godot;
using System;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class DisplayShortcutChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        var holder = new Node(); parent.AddChild(holder);
        var layer = new CanvasLayer(); holder.AddChild(layer);
        var input = new LineEdit(); layer.AddChild(input);
        var shortcuts = new DisplayShortcuts { Interface = layer }; holder.AddChild(shortcuts);
        bool paused = parent.GetTree().Paused;
        void Press(Key key, bool echo = false, bool down = true) => parent.GetViewport().PushInput(
            new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = down, Echo = echo });
        try
        {
            input.GrabFocus(); await frame();
            check(parent.GetViewport().GuiGetFocusOwner() == input, "Shortcut fixture starts with keyboard focus in a text field");
            Press(Key.F1);
            check(!layer.Visible && !input.IsVisibleInTree() && parent.GetViewport().GuiGetFocusOwner() == null,
                "F1 hides the entire interface and releases hidden text-field focus");
            Press(Key.F1, echo: true); Press(Key.F1, down: false);
            check(!layer.Visible, "Holding or releasing F1 does not toggle the interface again");
            parent.GetTree().Paused = true;
            check(shortcuts.CanProcess() && !holder.CanProcess(), "Display shortcuts remain active when gameplay is paused");
            Press(Key.F1);
            check(layer.Visible, "F1 can restore the interface while paused");
            Press(Key.F1); Press(Key.Escape);
            check(layer.Visible, "Escape reveals the interface before menu handling");
            check(shortcuts.NextWindowMode(Window.ModeEnum.Windowed) == Window.ModeEnum.Fullscreen
                && shortcuts.NextWindowMode(Window.ModeEnum.Fullscreen) == Window.ModeEnum.Windowed,
                "Fullscreen toggle restores windowed mode");
            check(shortcuts.NextWindowMode(Window.ModeEnum.Maximized) == Window.ModeEnum.Fullscreen
                && shortcuts.NextWindowMode(Window.ModeEnum.ExclusiveFullscreen) == Window.ModeEnum.Maximized,
                "Fullscreen toggle restores a previously maximized window and recognizes exclusive fullscreen");
        }
        finally { parent.GetTree().Paused = paused; holder.Free(); }
        await frame();
    }
}
