using Godot;

namespace TransportSandbox;

public partial class TrainWindows
{
    public TrainRouteHighlights DebugHighlights = null!;
    private void InitializeDebug()
    {
        DebugHighlights = new() { Trains = this };
        Interface.AddChild(DebugHighlights);
    }
    public void OpenDebug(int id)
    {
        OpenTrain(id, false);
        if (!Views.TryGetValue(id, out var view)) return;
        view.EditTab = view.RoutesTab = false; view.DebugTab = true;
        Render(Editor.Builder.Get(id), view);
        RefreshCatalog();
    }
    private void BuildDebug(TrainView view)
    {
        view.Debug = new(); view.Window.Body.AddChild(view.Debug);
        UiSkin.Label(view.Debug, "Calculated route", 14);
        UiSkin.Label(view.Debug, "Cyan: active route", 12, "73dce6");
        UiSkin.Label(view.Debug, "Orange: accepted detour sections", 12, "ffad58");
        var help = UiSkin.Label(view.Debug,
            "Detours are merged into the active itinerary. A successful destination replan becomes the normal cyan route. The map shows chosen track ahead, up to the current destination.", 11);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        view.DebugInfo = UiSkin.Label(view.Debug, "Select this tab to inspect the route.", 11);
        view.DebugInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }
}
