using Godot;
using System;
using System.Linq;

namespace TransportSandbox;

/// <summary>One reusable station list for navigation and route target selection.</summary>
public partial class StationListWindow : Node
{
    public TrackNetwork Network = null!;
    public WindowManager Windows = null!;
    public StationWindows Stations = null!;
    public Func<int, bool>? Choose;
    public GameWindow Window = null!;
    private VBoxContainer _list = null!;
    public override void _Ready()
    {
        Window = Windows.Create("Stations", WindowPlacement.CenterLeft, new(320, 380), minimum: new(240, 200));
        _list = new(); Window.Body.AddChild(_list);
        Network.Changed += Refresh;
        Refresh();
    }
    public void Open() { Refresh(); Window.Open(); }
    public void ActivateStation(int id)
    {
        if (!Network.Edges.Values.Any(edge => edge.Station == id)) return;
        if (Choose?.Invoke(id) != true) Stations.Open(id);
    }
    private void Refresh()
    {
        foreach (var child in _list.GetChildren()) { _list.RemoveChild(child); child.QueueFree(); }
        var stations = Network.Edges.Values.Where(edge => edge.Station != 0).GroupBy(edge => edge.Station).OrderBy(group => group.Key).ToArray();
        if (stations.Length == 0) UiSkin.Label(_list, "No stations built yet.", 11);
        foreach (var station in stations)
        {
            int id = station.Key;
            var button = new GameButton();
            button.SetContent($"Station {id}", "station", "Choose this station or open its window");
            _list.AddChild(button); button.Pressed += () => ActivateStation(id);
        }
    }
    public override void _ExitTree() => Network.Changed -= Refresh;
}
