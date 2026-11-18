using Godot;
using System;

namespace TransportSandbox;

/// <summary>Godot-native drag payloads for catalog vehicles, consist vehicles, insertion gaps and trash.</summary>
public partial class TrainDragButton : GameButton
{
    public Variant Payload;
    public Func<Variant, bool>? Accept;
    public Action<Variant>? Drop;
    public override Variant _GetDragData(Vector2 position)
    {
        if (Payload.VariantType == Variant.Type.Nil) return default;
        SetDragPreview(new Label { Text = Text == "" ? TooltipText : Text, MouseFilter = MouseFilterEnum.Ignore });
        return Payload;
    }
    public override bool _CanDropData(Vector2 position, Variant data) => Accept?.Invoke(data) == true;
    public override void _DropData(Vector2 position, Variant data) => Drop?.Invoke(data);
    public static Godot.Collections.Dictionary Data(string type, int train = 0, int item = 0, string vehicle = "") => new() { ["type"] = type, ["train"] = train, ["item"] = item, ["vehicle"] = vehicle };
    public static Godot.Collections.Dictionary? Read(Variant value) => value.VariantType == Variant.Type.Dictionary ? value.AsGodotDictionary() : null;
}
