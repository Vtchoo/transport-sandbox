using Godot;
using System;

namespace TransportSandbox;

public partial class WorldCamera
{
    public bool WalkingMode => _walker != null;
    private WalkingCharacter? _walker;
    private SavedCamera? _beforeWalking;
    private float _beforeWalkingFov;

    private bool TypingOrSelecting()
    {
        var focus = GetViewport().GuiGetFocusOwner();
        return focus is LineEdit or TextEdit || focus is OptionButton select && select.GetPopup().Visible;
    }

    public void SetWalkingMode(bool walking)
    {
        if (WalkingMode == walking || !Enabled || Ground == null) return;
        if (!walking)
        {
            var previous = _beforeWalking!;
            _walker = null; _beforeWalking = null;
            Near = .15f; Fov = _beforeWalkingFov;
            ReleaseMouse(); Restore(previous);
            return;
        }
        ImportPose();
        var saved = Capture();
        var point = FreeMode ? Rig.Position : Rig.Target;
        double height(double x, double z) => GroundHeight?.Invoke(x, z) ?? Ground.HeightWorld(x, z);
        // Find nearby dry land if the overlook target was in a lake/river.
        if (height(point.X, point.Z) < Ground.SeaLevel)
        {
            bool found = false;
            for (double radius = 8; radius <= 256 && !found; radius *= 2)
                for (int i = 0; i < 16 && !found; i++)
                {
                    var candidate = point + new TrackVector(Math.Cos(i * Math.Tau / 16) * radius, 0, Math.Sin(i * Math.Tau / 16) * radius);
                    if (height(candidate.X, candidate.Z) >= Ground.SeaLevel) { point = candidate; found = true; }
                }
            if (!found) return;
        }
        StopFollowing(); _goToFrom = _goToTo = null;
        _beforeWalking = saved; _beforeWalkingFov = Fov;
        _walker = new() { Feet = point with { Y = height(point.X, point.Z) } };
        Rig.Pitch = -.08; Rig.Position = _walker.Eyes;
        Near = .05f; Fov = 75;
        ModeChanged?.Invoke();
        if (!InputBlocked) Input.MouseMode = Input.MouseModeEnum.Captured;
        Publish();
    }

    private void UpdateWalking(double delta)
    {
        if (_walker == null || Ground == null) return;
        bool active = !InputBlocked && !NavigationBlocked() && !TypingOrSelecting()
            && Input.MouseMode == Input.MouseModeEnum.Captured;
        bool down(Key key) => active && Input.IsPhysicalKeyPressed(key);
        _walker.Update((down(Key.W) || down(Key.Up) ? 1 : 0) - (down(Key.S) || down(Key.Down) ? 1 : 0),
            (down(Key.D) || down(Key.Right) ? 1 : 0) - (down(Key.A) || down(Key.Left) ? 1 : 0),
            Rig.Yaw, down(Key.Shift), delta, (x, z) => GroundHeight?.Invoke(x, z) ?? Ground.HeightWorld(x, z), Loaded, Ground.SeaLevel);
        Rig.Position = _walker.Eyes;
        Rig.Target = _walker.Feet;
        Publish();
    }
}
