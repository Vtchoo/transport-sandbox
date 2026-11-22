using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox;

public partial class Main
{
    public string SavesRoot = "user://worlds";
    public string? WorldDirectory { get; private set; }
    public string WorldName { get; private set; } = "New World";
    private Task<bool>? _saveTask;
    private double _autosave;
    private bool _leaving;
    private PointerMessage _saveMessage = null!;
    private GameWindow? _loadWindow;
    private void InitializeSaving()
    {
        _saveMessage = new(); Interface.AddChild(_saveMessage);
        GetTree().AutoAcceptQuit = false;
    }
    public void CreateGame(string name, WorldSettings settings)
    {
        try
        {
            WorldName = string.IsNullOrWhiteSpace(name) ? "New World" : name.Trim();
            WorldDirectory = WorldSaveStore.CreateWorldDirectory(ProjectSettings.GlobalizePath(SavesRoot), WorldName);
            StartWithSettings(settings); _ = SaveGame();
        }
        catch (Exception e) { _saveMessage.Display("Could not create world: " + e.Message); }
    }
    public GameSave CaptureGame()
    {
        var save = new GameSave { Name = WorldName, Settings = Generator.Settings, Camera = Camera.Capture() };
        TrackNetwork.Capture(save); Trains.Capture(save); Industries?.Capture(save); return save;
    }
    public Task<bool> SaveGame()
    {
        if (_saveTask is { IsCompleted: false }) return SaveAfterPending(_saveTask);
        try
        {
            if (Generator == null) return Task.FromResult(true);
            // Capture on the scene thread; compression and durable disk writes use managed workers.
            if (WorldDirectory == null)
            {
                WorldDirectory = WorldSaveStore.CreateWorldDirectory(ProjectSettings.GlobalizePath(SavesRoot), WorldName);
                Generator.Backend.Regions = new(WorldDirectory, Generator.Settings);
            }
            return _saveTask = WriteSnapshot(CaptureGame(), WorldDirectory);
        }
        catch (Exception e) { _saveMessage.Display("Save failed: " + e.Message, false, 10); return Task.FromResult(false); }
    }
    private async Task<bool> SaveAfterPending(Task<bool> pending) { await pending; return await SaveGame(); }
    private async Task<bool> WriteSnapshot(GameSave snapshot, string directory)
    {
        try
        {
            await Task.Run(() => WorldSaveStore.Write(directory, snapshot));
            _autosave = 0;
            if (IsInstanceValid(_saveMessage)) _saveMessage.Display("World saved: " + snapshot.Name, true, 2);
            return true;
        }
        catch (Exception e) { if (IsInstanceValid(_saveMessage)) _saveMessage.Display("Save failed: " + e.Message, false, 10); return false; }
    }
    public void LoadGame(string directory)
    {
        // Parse and validate before replacing the menu. The complete simulation is restored together.
        var save = WorldSaveStore.Read(directory);
        foreach (var item in save.Trains.SelectMany(t => t.Items)) Vehicles.Get(item.Vehicle);
        WorldDirectory = directory; WorldName = save.Name;
        StartWithSettings(save.Settings);
        TrackNetwork.Restore(save); Camera.Restore(save.Camera); Trains.Restore(save); Industries.Restore(save);
        World.InitialReady = () => !TreeClearance.IsPending;
    }
    public void OpenLoadGames()
    {
        if (_loadWindow != null) MenuUI.Windows.Unregister(_loadWindow);
        _loadWindow = MenuUI.Windows.Create("Load game", WindowPlacement.Center, new(480, 400), modal: true);
        string root = ProjectSettings.GlobalizePath(SavesRoot); Directory.CreateDirectory(root);
        var directories = Directory.GetDirectories(root).Where(d => File.Exists(Path.Combine(d, WorldSaveStore.SnapshotFile)))
            .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, WorldSaveStore.SnapshotFile))).ToArray();
        if (directories.Length == 0) UiSkin.Label(_loadWindow.Body, "No saved worlds yet.");
        foreach (string directory in directories)
        {
            string name;
            try { name = WorldSaveStore.Read(directory).Name; }
            catch (Exception e) { UiSkin.Label(_loadWindow.Body, Path.GetFileName(directory) + ": " + e.Message); continue; }
            var button = new Button { Text = name + "  ·  " + File.GetLastWriteTime(Path.Combine(directory, WorldSaveStore.SnapshotFile)).ToString("g") };
            UiSkin.Button(button); _loadWindow.Body.AddChild(button);
            button.Pressed += () => { try { LoadGame(directory); } catch (Exception e) { _saveMessage.Display("Could not load world: " + e.Message, false, 10); } };
        }
        _loadWindow.Open();
    }
    private async void ReturnToMenu()
    {
        if (_leaving) return; _leaving = true;
        PauseUI.SetBusy(true);
        Trains.SetProcess(false); TrackEditor.SetProcess(false);
        // If an autosave was in flight, write a fresh snapshot after it completes.
        if (_saveTask is { IsCompleted: false }) await _saveTask;
        if (await SaveGame()) { GetTree().Paused = false; GetTree().ReloadCurrentScene(); }
        else { _leaving = false; PauseUI.SetBusy(false); Trains.SetProcess(true); TrackEditor.SetProcess(true); }
    }
    public override void _Process(double delta)
    {
        if (Generator == null || _leaving) return;
        _autosave += delta;
        if (_autosave >= 300 && _saveTask is not { IsCompleted: false }) _ = SaveGame();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (Generator != null && input is InputEventKey { Pressed: true, Echo: false } key && (key.Keycode == Key.Escape || key.PhysicalKeycode == Key.Escape))
        { Camera.ReleaseMouse(); PauseUI.Open(); GetViewport().SetInputAsHandled(); return; }
        if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F5 } && Generator != null)
        { _ = SaveGame(); GetViewport().SetInputAsHandled(); }
    }
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) QuitWithSave();
    }
    private async void QuitWithSave()
    {
        if (_leaving) return; _leaving = true;
        if (IsInstanceValid(PauseUI)) PauseUI.SetBusy(true);
        if (Trains != null) Trains.SetProcess(false);
        if (TrackEditor != null) TrackEditor.SetProcess(false);
        if (_saveTask is { IsCompleted: false }) await _saveTask;
        if (await SaveGame()) { GetTree().Paused = false; GetTree().Quit(); }
        else { _leaving = false; if (IsInstanceValid(PauseUI)) PauseUI.SetBusy(false); if (Trains != null) Trains.SetProcess(true); if (TrackEditor != null) TrackEditor.SetProcess(true); }
    }
}
