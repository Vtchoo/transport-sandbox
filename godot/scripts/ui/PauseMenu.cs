using Godot;
using System;
using System.Threading.Tasks;

namespace TransportSandbox;

/// <summary>Always-processing UI over a paused scene tree; gameplay keeps its own process settings.</summary>
public partial class PauseMenu : Control
{
    public Func<Task<bool>> SaveGame = () => Task.FromResult(false);
    public event Action? QuitToMenuRequested, QuitGameRequested;
    public GameWindow Window = null!;
    public Button ResumeButton = null!, SaveButton = null!, QuitToMenuButton = null!, QuitGameButton = null!;
    public bool IsOpen => IsInstanceValid(Window) && Window.Visible;
    public bool Busy { get; private set; }
    private bool _previousPause;
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        var manager = new WindowManager(); AddChild(manager);
        Window = manager.Create("Paused", WindowPlacement.Center, new(320, 310), minimum: new(280, 280), modal: true,
            collapsible: false, minimizable: false, scrollBody: false);
        Window.Body.AddThemeConstantOverride("separation", 10);
        Button Action(string text, Action action)
        {
            var button = new GameButton { Text = text, CustomMinimumSize = new(0, 44) };
            Window.Body.AddChild(button); button.Pressed += action; return button;
        }
        ResumeButton = Action("Resume", Resume);
        SaveButton = Action("Save game", () => _ = Save());
        QuitToMenuButton = Action("Quit to menu", () => QuitToMenuRequested?.Invoke());
        QuitGameButton = Action("Quit game", () => QuitGameRequested?.Invoke());
        Window.Closed += Closed;
    }
    public void Open()
    {
        if (IsOpen) return;
        _previousPause = GetTree().Paused;
        GetTree().Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Window.Open(); ResumeButton.GrabFocus();
    }
    public void Resume() { if (!Busy) Window.Close(); }
    private void Closed()
    {
        if (Busy) { Window.Open(); return; }
        GetTree().Paused = _previousPause;
    }
    public void SetBusy(bool busy)
    {
        Busy = busy;
        foreach (var button in new[] { ResumeButton, SaveButton, QuitToMenuButton, QuitGameButton }) button.Disabled = busy;
    }
    public async Task<bool> Save()
    {
        if (Busy) return false;
        SetBusy(true);
        try { return await SaveGame(); }
        finally { if (IsInstanceValid(this)) SetBusy(false); }
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (IsOpen && input is InputEventKey { Pressed: true, Echo: false } key && (key.Keycode == Key.F5 || key.PhysicalKeycode == Key.F5))
        { _ = Save(); GetViewport().SetInputAsHandled(); }
    }
    public override void _ExitTree()
    {
        if (IsOpen) GetTree().Paused = _previousPause;
    }
}
