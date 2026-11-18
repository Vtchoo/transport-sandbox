using Godot;

namespace TransportSandbox;

/// <summary>Shared game button styling and text/icon presentation, including drag targets.</summary>
public partial class GameButton : Button
{
    public GameButton()
    {
        UiSkin.Button(this);
    }

    public void SetContent(string text, string icon = "", string tooltip = "")
    {
        Text = text;
        TooltipText = tooltip;
        Icon = icon.Length == 0 ? null : UiSkin.Icon(icon);
        ExpandIcon = true;
        // Centering the icon independently overlays the label in Godot buttons.
        IconAlignment = text.Length == 0 ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        VerticalIconAlignment = VerticalAlignment.Center;
        AddThemeConstantOverride("icon_max_width", 18);
        if (text.Length == 0 && icon.Length > 0)
        {
            CustomMinimumSize = new(40, 40);
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        }
        else
        {
            CustomMinimumSize = Vector2.Zero;
            SizeFlagsHorizontal = SizeFlags.Fill;
        }
    }
}
