using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class PauseMenuChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        var gameplay = new Node(); parent.AddChild(gameplay);
        var menu = new PauseMenu(); parent.AddChild(menu);
        try
        {
            check(menu.Window.Body.GetChildren().OfType<Button>().Select(b => b.Text)
                .SequenceEqual(new[] { "Resume", "Save game", "Quit to menu", "Quit game" }), "Pause menu exposes the four requested actions in order");
            check(!menu.Window.Collapsible && !menu.Window.Minimizable && !menu.IsOpen, "Pause menu starts hidden without collapse/minimize controls");
            menu.Open();
            check(parent.GetTree().Paused && !gameplay.CanProcess() && menu.CanProcess() && menu.ResumeButton.CanProcess(),
                "Pause freezes gameplay while its menu continues processing input");
            var completion = new TaskCompletionSource<bool>();
            bool savedWhilePaused = false;
            menu.SaveGame = () => { savedWhilePaused = parent.GetTree().Paused; return completion.Task; };
            var saving = menu.Save();
            check(savedWhilePaused && menu.Busy && menu.ResumeButton.Disabled && menu.QuitGameButton.Disabled, "Saving stays paused and disables conflicting actions");
            menu.Window.Close();
            check(menu.IsOpen && parent.GetTree().Paused, "Closing or pressing Escape during a write cannot resume the simulation");
            completion.SetResult(true); check(await saving && !menu.Busy && menu.IsOpen && parent.GetTree().Paused, "Successful saving keeps the pause menu open");
            menu.SaveGame = () => Task.FromResult(false);
            check(!await menu.Save() && !menu.Busy && menu.IsOpen && parent.GetTree().Paused, "A failed save leaves a usable pause menu and a paused session");
            int toMenu = 0, quit = 0;
            menu.QuitToMenuRequested += () => toMenu++; menu.QuitGameRequested += () => quit++;
            menu.QuitToMenuButton.EmitSignal(Button.SignalName.Pressed); menu.QuitGameButton.EmitSignal(Button.SignalName.Pressed);
            check(toMenu == 1 && quit == 1, "Quit buttons dispatch distinct save-and-exit actions");
            menu.ResumeButton.EmitSignal(Button.SignalName.Pressed);
            check(!menu.IsOpen && !parent.GetTree().Paused && gameplay.CanProcess(), "Resume restores gameplay processing");
            menu.Open();
            menu.Window.Manager._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            check(!menu.IsOpen && !parent.GetTree().Paused, "Escape closes the pause menu and resumes");
            menu.Open();
        }
        finally { menu.Free(); gameplay.Free(); }
        check(!parent.GetTree().Paused, "Removing the pause menu cannot leave a replacement scene paused");
        await frame();
    }
}
