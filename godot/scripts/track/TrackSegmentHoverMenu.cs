using Godot;

namespace TransportSandbox;

/// <summary>Persistent segment actions anchored above the curve, matching the browser editor.</summary>
public partial class TrackSegmentHoverMenu : Control
{
    public Button Straighten = null!, Delete = null!, ConfirmSplit = null!;
    private (bool Split, string Confirmation)? _state;
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Straighten = Action("Straighten");
        Delete = Action("Delete track");
        ConfirmSplit = Action("Confirm split");
        SetState(false, "");
        Hide();
    }
    private Button Action(string text)
    {
        var button = new Button { Text = text, CustomMinimumSize = new(105, 30), MouseDefaultCursorShape = CursorShape.PointingHand };
        button.AddThemeFontSizeOverride("font_size", 11);
        AddChild(button);
        return button;
    }
    private static void Style(Button button, string background, string border, string hover)
    {
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed" })
            button.AddThemeStyleboxOverride(state, UiSkin.Box(state == "hover" ? hover : background, border, 5, 12, 8));
        button.AddThemeStyleboxOverride("focus", UiSkin.Box("00000000", "ebbc75", 5, 0, 0));
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(state, new("edf5df"));
    }
    public void SetState(bool split, string confirmation)
    {
        if (_state == (split, confirmation))
            return;
        _state = (split, confirmation);
        Straighten.Visible = Delete.Visible = !split;
        ConfirmSplit.Visible = split;
        Straighten.Text = confirmation == "straight" ? "Confirm straightening" : "Straighten";
        Delete.Text = confirmation == "edge" ? "Confirm delete track" : "Delete track";
        Straighten.TooltipText = "Straighten this segment; click twice to confirm";
        Delete.TooltipText = "Delete this segment; keep its endpoint nodes";
        ConfirmSplit.TooltipText = "Divide the segment at the preview node. Esc cancels.";
        Style(Straighten, confirmation == "straight" ? "715332" : "284e3ef5", confirmation == "straight" ? "edc579" : "b9d6c3", "38654e");
        Style(Delete, confirmation == "edge" ? "873f32" : "593e36f5", confirmation == "edge" ? "ffc3a8" : "c58e80", "754b40");
        Style(ConfirmSplit, "284e3ef5", "9ee4c5", "38654e");
    }
    public void PositionButtons(Vector2 point, bool split)
    {
        if (split)
        {
            ConfirmSplit.Size = ConfirmSplit.GetCombinedMinimumSize();
            Place(ConfirmSplit, point - new Vector2(0, 30 + ConfirmSplit.Size.Y / 2));
        }
        else
        {
            Place(Straighten, point - new Vector2(0, 35));
            Place(Delete, point - new Vector2(0, 71));
        }
    }
    private void Place(Button button, Vector2 center)
    {
        button.Size = button.GetCombinedMinimumSize();
        button.Position = (center - button.Size / 2).Clamp(Vector2.One * 4, (GetViewportRect().Size - button.Size - Vector2.One * 4).Max(Vector2.One * 4));
    }
}
