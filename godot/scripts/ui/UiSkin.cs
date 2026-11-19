using Godot;

namespace TransportSandbox;

public static class UiSkin
{
    public static Theme CreateTheme()
    {
        var theme = new Theme { DefaultFont = new SystemFont { FontNames = new[] { "Inter", "Segoe UI", "sans-serif" } }, DefaultFontSize = 12 };
        theme.SetColor("font_color", "Label", new("e8eddf"));
        return theme;
    }
    public static StyleBoxFlat Box(string color, string border = "00000000", int radius = 4, int horizontal = 12, int vertical = 8)
    {
        var style = new StyleBoxFlat { BgColor = new(color), BorderColor = new(border), ContentMarginLeft = horizontal, ContentMarginRight = horizontal, ContentMarginTop = vertical, ContentMarginBottom = vertical };
        style.SetCornerRadiusAll(radius);
        style.SetBorderWidthAll(border == "00000000" ? 0 : 1);
        return style;
    }
    public static Texture2D Icon(string name) => GD.Load<Texture2D>($"res://assets/ui/icons/{name}.svg");
    public static void Button(Button button, bool action = false)
    {
        button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        button.AddThemeFontSizeOverride("font_size", action ? 12 : 11);
        button.AddThemeConstantOverride("h_separation", 10);
        button.AddThemeStyleboxOverride("normal", Box(action ? "ffffff08" : "1c302bc9", action ? "00000000" : "e5ede344", action ? 5 : 4, action ? 20 : 13, action ? 11 : 10));
        button.AddThemeStyleboxOverride("hover", Box(action ? "62775866" : "324b3ae8", action ? "becd9c" : "e5ede344", action ? 5 : 4, action ? 20 : 13, action ? 11 : 10));
        button.AddThemeStyleboxOverride("pressed", Box("62775899", "becd9c", action ? 5 : 4, action ? 20 : 13, action ? 11 : 10));
        button.AddThemeStyleboxOverride("disabled", Box("ffffff04", "00000000", action ? 5 : 4, action ? 20 : 13, action ? 11 : 10));
        button.AddThemeStyleboxOverride("focus", Box("00000000", "ebbc75", 4, 0, 0));
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(state, new("edf1e6"));
        foreach (string state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" })
            button.AddThemeColorOverride(state, new("ccddad"));
        button.AddThemeColorOverride("font_disabled_color", new("edf1e680"));
        button.AddThemeColorOverride("icon_disabled_color", new("ccddad80"));
    }
    public static void TrainRunButton(Button button, bool running)
    {
        string background = running ? "754435" : "446343";
        string border = running ? "d99b79" : "adc490";
        foreach (string state in new[] { "normal", "hover", "pressed" })
            button.AddThemeStyleboxOverride(state, Box(background, border, 4, 13, 10));
        foreach (string state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" })
            button.AddThemeColorOverride(state, new("edffdb"));
    }
    public static Label Label(Node parent, string text, int size = 12, string color = "c6d7bc")
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new(color));
        parent.AddChild(label);
        return label;
    }
    public static void Selector(OptionButton selector)
    {
        selector.CustomMinimumSize = new(0, 36);
        foreach (string state in new[] { "normal", "hover", "pressed" })
            selector.AddThemeStyleboxOverride(state, Box("e8eddf", "a8b9a5"));
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
            selector.AddThemeColorOverride(state, new("20392f"));
        // OptionButton uses radio-checkable popup items. Keep the single-selection
        // behavior, but show the selected value with a tick instead of a radio dot.
        var popup = selector.GetPopup();
        Texture2D Marker(bool selected)
        {
            using var image = new Image();
            image.LoadSvgFromString("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" viewBox=\"0 0 16 16\">" +
                (selected ? "<path d=\"M3 8l3 3 7-7\" fill=\"none\" stroke=\"#ccddad\" stroke-width=\"2\"/>" : "") + "</svg>");
            return ImageTexture.CreateFromImage(image);
        }
        popup.AddThemeIconOverride("radio_checked", Marker(true));
        popup.AddThemeIconOverride("radio_unchecked", Marker(false));
    }
}
