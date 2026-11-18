using Godot;
using System;

namespace TransportSandbox;

public partial class WorldCamera : Camera3D
{
    public event Action? ManuallyPanned;
    public event Action? ModeChanged;
    public bool InputBlocked;
    public Vector3 Target;
    public float Distance = 160, Yaw = -0.65f, Pitch = 0.65f;
    public bool FreeMode, Enabled;
    public Node3D? Following;
    public TerrainGenerator? Ground;

    public void ResetWorld(TerrainGenerator generator, float x = 600, float z = 900)
    {
        Ground = generator;
        Following = null;
        FreeMode = false;
        Yaw = -0.5f;
        Pitch = 0.72f;
        Distance = 1100;
        Fov = 65;
        Target = new(x, (float)Math.Max(generator.SeaLevel, generator.Height(x, z)), z);
        OrbitPosition();
    }
    private Vector3 OrbitDirection() => new(Mathf.Sin(Yaw) * Mathf.Cos(Pitch), Mathf.Sin(Pitch), Mathf.Cos(Yaw) * Mathf.Cos(Pitch));
    private void OrbitPosition()
    {
        Position = Target + OrbitDirection() * Distance;
        if (Ground != null)
        {
            float floor = (float)Math.Max(Ground.SeaLevel, Ground.Height(Position.X, Position.Z)) + 12;
            if (Position.Y < floor)
            {
                Target += Vector3.Up * (floor - Position.Y);
                Position = new(Position.X, floor, Position.Z);
            }
        }
        Basis = Basis.LookingAt(Target - Position, Vector3.Up);
    }
    public override void _Ready()
    {
        Far = 14000;
        Near = 0.15f;
        Fov = 50;
        Current = true;
    }
    private void StopFollowing()
    {
        Following = null;
        ManuallyPanned?.Invoke();
    }
    public override void _Process(double delta)
    {
        if (!Enabled || InputBlocked)
            return;
        float dt = (float)delta;
        var move = Vector3.Zero;
        if (GetViewport().GuiGetFocusOwner() is not LineEdit)
            move = new((Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0), 0,
                (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0));
        if (move != Vector3.Zero)
        {
            StopFollowing();
            float speed = (FreeMode ? 80 : Math.Max(Distance * 0.6f, 10)) * dt;
            if (Input.IsPhysicalKeyPressed(Key.Shift))
                speed *= 3;
            var displacement = new Basis(Vector3.Up, Yaw) * move.Normalized() * speed;
            if (FreeMode)
                Position += displacement;
            else
                Target += displacement;
        }
        if (FreeMode)
        {
            float vertical = (Input.IsPhysicalKeyPressed(Key.E) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Q) ? 1 : 0);
            Position += Vector3.Up * vertical * dt * 70;
            Rotation = new(-Pitch, Yaw, 0);
        }
        else
        {
            if (GodotObject.IsInstanceValid(Following))
                Target = Following!.GlobalPosition;
            else if (Ground != null)
                Target = new(Target.X, Mathf.Lerp(Target.Y,
                (float)Math.Max(Ground.SeaLevel, Ground.Height(Target.X, Target.Z)), 1 - Mathf.Exp(-dt * 7)), Target.Z);
            OrbitPosition();
        }
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (!Enabled || InputBlocked)
            return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.C })
        {
            SetFreeMode(!FreeMode);
        }
        if (input is InputEventMouseMotion motion)
        {
            if (Input.IsMouseButtonPressed(MouseButton.Middle) || (FreeMode && Input.IsMouseButtonPressed(MouseButton.Right)))
            {
                Yaw -= motion.Relative.X * 0.005f;
                Pitch = Mathf.Clamp(Pitch + motion.Relative.Y * 0.005f, 0.08f, 1.48f);
            }
            else if (Input.IsMouseButtonPressed(MouseButton.Right))
            {
                StopFollowing();
                Target += new Basis(Vector3.Up, Yaw) * new Vector3(-motion.Relative.X, 0, -motion.Relative.Y) * Distance * 0.0015f;
            }
        }
        if (input is InputEventMouseButton { Pressed: true } mouse)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp)
                Distance = Math.Max(5, Distance / 1.15f);
            if (mouse.ButtonIndex == MouseButton.WheelDown)
                Distance = Math.Min(4500, Distance * 1.15f);
        }
    }
    public void SetFreeMode(bool free)
    {
        if (FreeMode == free)
            return;
        FreeMode = free;
        if (!FreeMode)
            Target = Position - OrbitDirection() * Distance;
        ModeChanged?.Invoke();
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
