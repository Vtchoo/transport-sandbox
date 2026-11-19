using Godot;
using System;
using System.Collections.Generic;

namespace TransportSandbox;

public partial class WorldCamera : Camera3D
{
    public event Action? ManuallyPanned;
    public event Action? ModeChanged;
    public readonly CameraRig Rig = new();
    public WorldSpace Space = new();
    public bool FloatingOriginEnabled;
    public Func<double, double, bool> Loaded = (_, _) => true;
    public Func<double, double, double>? GroundHeight;
    public Func<bool> BuildMode = () => false;
    public Func<bool> NavigationBlocked = () => false;
    private bool _inputBlocked;
    public bool InputBlocked
    {
        get => _inputBlocked; set
        {
            _inputBlocked = value;
            if (value)
                ReleaseMouse();
        }
    }
    public bool FreeMode => Rig.Free;
    public bool Enabled;
    public Vector3 Target
    {
        get => Space.ToLocal(Rig.Target); set => Rig.Target = Space.ToWorld(value);
    }
    public TrackVector WorldPosition => Position == _published ? Rig.Position : Space.ToWorld(Position);
    public TrackVector WorldTarget => Rig.Target;
    public float Distance
    {
        get => (float)Rig.Distance; set => Rig.Distance = value;
    }
    public float Yaw
    {
        get => (float)Rig.Yaw; set => Rig.Yaw = value;
    }
    public float Pitch
    {
        get => (float)-Rig.Pitch; set => Rig.Pitch = -value;
    }
    public Node3D? Following;
    public TerrainGenerator? Ground;
    private Node3D? _lastFollowing;
    private Vector3 _published, _publishedRotation;
    private MouseButton _drag;
    private bool _orbitDrag;
    private double Height(double x, double z) => Ground == null ? 0 : Math.Max(Ground.SeaLevel, GroundHeight?.Invoke(x, z) ?? Ground.HeightWorld(x, z));
    public override void _Ready()
    {
        Space.Rebased += Rebased;
        Far = 14000;
        Near = .15f;
        Fov = 50;
        Current = true;
    }
    private void Rebased(TrackVector worldShift)
    {
        if (Position != _published)
            Rig.Position = Space.Origin - worldShift + TrackVector.From(Position);
        Position = Space.ToLocal(Rig.Position);
        _published = Position;
    }
    public void ResetWorld(TerrainGenerator generator, float x = 600, float z = 900)
    {
        Ground = generator;
        Following = _lastFollowing = null;
        Rig.Free = false;
        Rig.Reset(x, z, Height);
        Fov = 65;
        Publish();
    }
    private void ImportPose()
    {
        if (Position != _published)
            Rig.Position = Space.ToWorld(Position);
        if (Rotation != _publishedRotation)
        {
            Rig.Pitch = Rotation.X;
            Rig.Yaw = Rotation.Y;
        }
    }
    private void Publish()
    {
        if (FloatingOriginEnabled)
            Space.Rebase(Rig.Position);
        Position = Space.ToLocal(Rig.Position);
        _published = Position;
        Rotation = new((float)Rig.Pitch, (float)Rig.Yaw, 0);
        _publishedRotation = Rotation;
    }
    public void ReleaseMouse()
    {
        _drag = MouseButton.None;
        if (Input.MouseMode == Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }
    public override void _ExitTree()
    {
        Space.Rebased -= Rebased;
        ReleaseMouse();
    }
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut)
            ReleaseMouse();
    }
    private void StopFollowing()
    {
        Following = _lastFollowing = null;
        Rig.Following = false;
        ManuallyPanned?.Invoke();
    }
    public override void _Process(double delta)
    {
        if (!Enabled)
            return;
        ImportPose();
        if (BuildMode() && Input.MouseMode == Input.MouseModeEnum.Captured)
            ReleaseMouse();
        var keys = new HashSet<string>();
        if (!InputBlocked && !NavigationBlocked())
        {
            if (GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit or OptionButton))
                foreach (var (key, name) in new[] { (Key.W, "KeyW"), (Key.A, "KeyA"), (Key.S, "KeyS"), (Key.D, "KeyD"), (Key.Q, "KeyQ"), (Key.E, "KeyE"), (Key.Space, "Space"), (Key.Up, "ArrowUp"), (Key.Down, "ArrowDown"), (Key.Left, "ArrowLeft"), (Key.Right, "ArrowRight"), (Key.Shift, "ShiftLeft") })
                    if (Input.IsPhysicalKeyPressed(key))
                        keys.Add(name);
            bool wasFollowing = IsInstanceValid(Following) || Rig.Following;
            Rig.Following = wasFollowing;
            Rig.Update(keys, Math.Clamp(delta, 0, .05), Height, Loaded);
            if (wasFollowing && !Rig.Following)
                StopFollowing();
        }
        if (IsInstanceValid(Following))
        {
            var point = Space.ToWorld(Following!.GlobalPosition);
            if (_lastFollowing != Following)
            {
                Rig.Follow(point, Height);
                _lastFollowing = Following;
            }
            else
                Rig.UpdateFollow(point, Height);
        }
        else
        {
            _lastFollowing = null;
            Rig.Following = false;
        }
        Publish();
    }
    public void SetFreeMode(bool free)
    {
        if (FreeMode == free)
            return;
        ImportPose();
        Rig.SetMode(free, Height);
        if (!free)
            ReleaseMouse();
        Publish();
        ModeChanged?.Invoke();
    }
    public void Look(double dx, double dy)
    {
        ImportPose();
        Rig.Look(dx, dy, Height);
        Publish();
    }
    public void Pan(double dx, double dy)
    {
        ImportPose();
        if (dx != 0 || dy != 0)
            StopFollowing();
        Rig.Pan(dx, dy, Height);
        Publish();
    }
    public void Scroll(double pixels)
    {
        ImportPose();
        Rig.Scroll(pixels, Height);
        Publish();
    }
    public override void _Input(InputEvent input)
    {
        if (!Enabled || InputBlocked || Input.MouseMode != Input.MouseModeEnum.Captured)
            return;
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            ReleaseMouse();
            GetViewport().SetInputAsHandled();
        }
        else if (input is InputEventMouseMotion motion && FreeMode)
        {
            Look(motion.Relative.X, motion.Relative.Y);
            GetViewport().SetInputAsHandled();
        }
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (!Enabled || InputBlocked)
            return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.C })
        {
            SetFreeMode(!FreeMode);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (input is InputEventMouseButton mouse)
        {
            if (mouse.Pressed && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                Scroll((mouse.ButtonIndex == MouseButton.WheelUp ? -100 : 100) * mouse.Factor);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (!mouse.Pressed && mouse.ButtonIndex == _drag)
            {
                _drag = MouseButton.None;
                return;
            }
            if (!mouse.Pressed || mouse.ButtonIndex is not (MouseButton.Left or MouseButton.Middle or MouseButton.Right))
                return;
            if (FreeMode && mouse.ButtonIndex != (BuildMode() ? MouseButton.Right : MouseButton.Left))
                return;
            if (FreeMode && !BuildMode())
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
                return;
            }
            _drag = mouse.ButtonIndex;
            _orbitDrag = FreeMode || _drag == MouseButton.Middle;
        }
        if (input is InputEventMouseMotion motion && _drag != MouseButton.None)
        {
            if (!Input.IsMouseButtonPressed(_drag))
            {
                _drag = MouseButton.None;
                return;
            }
            if (_orbitDrag)
                Look(motion.Relative.X, motion.Relative.Y);
            else
                Pan(motion.Relative.X, motion.Relative.Y);
        }
    }
    public Vector3 GroundPoint(TerrainGenerator generator)
    {
        var mouse = GetViewport().GetMousePosition();
        var origin = ProjectRayOrigin(mouse);
        var direction = ProjectRayNormal(mouse);
        float previous = 0, step = 1, travel = 0;
        while (travel < 12000)
        {
            var point = origin + direction * travel;
            if (point.Y <= generator.Height(point.X, point.Z))
            {
                float low = previous, high = travel;
                for (int i = 0; i < 16; i++)
                {
                    float middle = (low + high) * 0.5f;
                    var p = origin + direction * middle;
                    if (p.Y > generator.Height(p.X, p.Z))
                        low = middle;
                    else
                        high = middle;
                }
                return origin + direction * ((low + high) * 0.5f);
            }
            previous = travel;
            travel += step;
            step = Math.Min(60, step * 1.15f);
        }
        return origin + direction * 1000;
    }
}
