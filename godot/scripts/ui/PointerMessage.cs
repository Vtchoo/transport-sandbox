using Godot;

namespace TransportSandbox;

/// <summary>Non-interactive placement hints and short warnings near the mouse.</summary>
public partial class PointerMessage : PanelContainer
{
    public Label TextLabel = null!;
    private double _remaining;
    private bool? _valid;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 100;
        AddThemeStyleboxOverride("panel", UiSkin.Box("593e36f5", "c58e80", 5, 12, 8));
        TextLabel = new Label { MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(250, 0) };
        TextLabel.AddThemeFontSizeOverride("font_size", 11);
        AddChild(TextLabel);
        Hide();
    }
    public void Display(string message, bool valid = false, double seconds = 4.5)
    {
        if (TextLabel.Text != message)
            TextLabel.Text = message;
        _remaining = seconds;
        if (_valid != valid)
        {
            _valid = valid;
            AddThemeStyleboxOverride("panel", UiSkin.Box(valid ? "284e3ef5" : "593e36f5", valid ? "9ee4c5" : "c58e80", 5, 12, 8));
        }
        Show();
        Reposition();
    }
    private void Reposition()
    {
        Size = GetCombinedMinimumSize();
        var mouse = GetViewport().GetMousePosition();
        var screen = GetViewportRect().Size;
        var point = mouse + new Vector2(16, 20);
        if (point.Y + Size.Y > screen.Y - 8)
            point.Y = mouse.Y - Size.Y - 12;
        Position = point.Clamp(Vector2.One * 8, (screen - Size - Vector2.One * 8).Max(Vector2.One * 8));
    }
    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        _remaining -= delta;
        if (_remaining <= 0)
            Hide();
    }
}
