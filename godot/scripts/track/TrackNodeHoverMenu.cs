using Godot;

namespace TransportSandbox;

/// <summary>Screen-space node actions; placement follows the original editor's tangent/lateral axes.</summary>
public partial class TrackNodeHoverMenu : Control
{
    public Button Direction = null!, ContinueParallel = null!, Delete = null!, Merge = null!, ParallelLeft = null!, ParallelRight = null!;
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Direction = Action("plus", "Continue track", "284e3e", "dbedb4", true);
        ContinueParallel = Action("parallel", "Continue all parallel tracks", "284e3e", "dbedb4");
        ContinueParallel.Text = "+";
        ContinueParallel.CustomMinimumSize = new(42, 30);
        ContinueParallel.IconAlignment = HorizontalAlignment.Left;
        Delete = Action("close", "Delete node and connected tracks; click twice to confirm", "593e36", "c58e80", true);
        Merge = Action("merge", "Merge two segments; click twice to confirm", "284b50", "b4dae5", true);
        ParallelLeft = Action("parallel", "Add parallel node on the left", "463458", "d7b4f3");
        ParallelRight = Action("parallel", "Add parallel node on the right", "463458", "d7b4f3");
        foreach (var button in new[] { ParallelLeft, ParallelRight })
        {
            button.Text = "+";
            button.CustomMinimumSize = new(42, 30);
            button.IconAlignment = HorizontalAlignment.Left;
        }
        Hide();
    }
    private Button Action(string icon, string tooltip, string background, string border, bool round = false)
    {
        var button = new Button
        {
            Icon = UiSkin.Icon(icon),
            TooltipText = tooltip,
            CustomMinimumSize = new(30, 30),
            ExpandIcon = true,
            IconAlignment = HorizontalAlignment.Center,
            VerticalIconAlignment = VerticalAlignment.Center,
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        button.AddThemeConstantOverride("icon_max_width", 18);
        button.AddThemeConstantOverride("h_separation", 1);
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed" })
            button.AddThemeStyleboxOverride(state, UiSkin.Box(background, border, round ? 15 : 5, 3, 0));
        button.AddThemeStyleboxOverride("focus", UiSkin.Box("00000000", "ebbc75", round ? 15 : 5, 0, 0));
        AddChild(button);
        return button;
    }
    public static Vector2 ScreenAxis(Camera3D camera, Vector3 position, Vector3 axis)
    {
        var delta = camera.UnprojectPosition(position + axis * 10) - camera.UnprojectPosition(position);
        return delta.Length() < .001f ? Vector2.Right : delta.Normalized();
    }
    public void PositionButtons(Camera3D camera, TrackNode node, int side)
    {
        var center = camera.UnprojectPosition(node.Position);
        var axis = ScreenAxis(camera, node.Position, node.Axis);
        var perpendicular = new Vector2(axis.Y, -axis.X);
        var direction = center + axis * side * 65;
        Place(Direction, direction);
        Place(ContinueParallel, direction + perpendicular * 46);
        Place(Delete, direction - perpendicular * 32);
        Place(Merge, direction + perpendicular * 82);
        var right = ScreenAxis(camera, node.Position, TrackNetwork.Right(node.Axis));
        Place(ParallelLeft, center - right * 75);
        Place(ParallelRight, center + right * 75);
    }
    private void Place(Button button, Vector2 center)
    {
        button.Size = button.GetCombinedMinimumSize();
        var viewport = GetViewportRect().Size;
        button.Position = (center - button.Size / 2).Clamp(Vector2.One * 4, viewport - button.Size - Vector2.One * 4);
    }
    public void SetConfirmations(bool delete, bool merge)
    {
        Delete.Icon = UiSkin.Icon(delete ? "check" : "close");
        Merge.Icon = UiSkin.Icon(merge ? "check" : "merge");
        Delete.TooltipText = delete ? "Confirm delete: connected tracks are removed, neighboring nodes remain" : "Delete node and connected tracks; click twice to confirm";
        Merge.TooltipText = merge ? "Confirm merge: keep outer endpoint handles" : "Merge two segments; click twice to confirm";
    }
}
