using Godot;

namespace TransportSandbox;

public enum WindowPlacement
{
    Custom, Center, TopLeft, TopCenter, TopRight, CenterLeft, CenterRight, BottomLeft, BottomCenter, BottomRight
}

public static class WindowPositions
{
    public static Vector2 Resolve(WindowPlacement placement, Vector2 viewport, Vector2 size, Vector2 offset)
    {
        float x = placement switch
        {
            WindowPlacement.TopLeft or WindowPlacement.CenterLeft or WindowPlacement.BottomLeft => 24,
            WindowPlacement.TopRight or WindowPlacement.CenterRight or WindowPlacement.BottomRight => viewport.X - size.X - 24,
            _ => (viewport.X - size.X) / 2
        };
        float y = placement switch
        {
            WindowPlacement.TopLeft or WindowPlacement.TopCenter or WindowPlacement.TopRight => 24,
            WindowPlacement.BottomLeft or WindowPlacement.BottomCenter or WindowPlacement.BottomRight => viewport.Y - size.Y - 110,
            _ => (viewport.Y - size.Y) / 2
        };
        return new Vector2(x, y) + offset;
    }
}
