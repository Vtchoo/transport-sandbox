using System;
using System.Collections.Generic;

namespace TransportSandbox;

/// <summary>Numerical port of src/camera.ts. Simulation coordinates and angles stay in double precision.</summary>
public sealed class CameraRig
{
    public bool Free;
    public TrackVector Position, Target;
    public double Yaw = -.5, Pitch = -.72, Distance = 1100, Speed = 60;
    public bool Following;
    private TrackVector _followedPoint;
    private const double MinPitch = -Math.PI / 2 + .08, MaxPitch = -.25;
    private TrackVector Forward() => new(-Math.Sin(Yaw) * Math.Cos(Pitch), Math.Sin(Pitch), -Math.Cos(Yaw) * Math.Cos(Pitch));
    public void Reset(double x, double z, Func<double, double, double> ground)
    {
        Following = false;
        Yaw = -.5;
        Pitch = -.72;
        Distance = 1100;
        Target = new(x, ground(x, z), z);
        Orbit(ground);
        if (Free)
            Pitch = -.15;
    }
    private void Orbit(Func<double, double, double> ground)
    {
        Position = Target + Forward() * -Distance;
        double floor = ground(Position.X, Position.Z) + 12;
        if (Position.Y < floor)
        {
            Target = Target + new TrackVector(0, floor - Position.Y, 0);
            Position = Position with
            {
                Y = floor
            };
        }
    }
    public void SetMode(bool free, Func<double, double, double> ground)
    {
        if (Free == free)
            return;
        Free = free;
        if (free)
            return;
        Pitch = Math.Clamp(Pitch, MinPitch, MaxPitch);
        var direction = Forward();
        double previous = 0, hit = 0;
        for (double t = 20; t <= 3500; t += 20)
        {
            var p = Position + direction * t;
            if (p.Y <= ground(p.X, p.Z))
            {
                double low = previous, high = t;
                for (int i = 0; i < 18; i++)
                {
                    double mid = (low + high) / 2;
                    var sample = Position + direction * mid;
                    if (sample.Y > ground(sample.X, sample.Z))
                        low = mid;
                    else
                        high = mid;
                }
                hit = (low + high) / 2;
                break;
            }
            previous = t;
        }
        Distance = Math.Clamp(hit == 0 ? 1800 : hit, 50, 3500);
        Target = Position + direction * Distance;
        Orbit(ground);
    }
    public void Look(double dx, double dy, Func<double, double, double> ground)
    {
        Yaw -= dx * .003;
        Pitch = Math.Clamp(Pitch - dy * .003, Free ? -Math.PI / 2 + .02 : MinPitch, Free ? Math.PI / 2 - .02 : MaxPitch);
        if (!Free)
            Orbit(ground);
    }
    public void Pan(double dx, double dy, Func<double, double, double> ground)
    {
        if (dx != 0 || dy != 0)
            Following = false;
        double scale = Distance * .0012;
        Target += new TrackVector((-dx * Math.Cos(Yaw) - dy * Math.Sin(Yaw)) * scale, 0, (dx * Math.Sin(Yaw) - dy * Math.Cos(Yaw)) * scale);
        Target = Target with
        {
            Y = ground(Target.X, Target.Z)
        };
        Orbit(ground);
    }
    public void Scroll(double delta, Func<double, double, double> ground)
    {
        double amount = Math.Clamp(delta, -200, 200);
        if (Free)
            Speed = Math.Clamp(Speed * Math.Exp(-amount * .002), 5, 500);
        else
        {
            Distance = Math.Clamp(Distance * Math.Exp(amount * .0015), 50, 3500);
            Orbit(ground);
        }
    }
    public void Update(IReadOnlySet<string> keys, double delta, Func<double, double, double> ground, Func<double, double, bool> loaded)
    {
        bool Has(string key) => keys.Contains(key);
        int Bit(string key) => Has(key) ? 1 : 0;
        if (Has("KeyW") || Has("KeyA") || Has("KeyS") || Has("KeyD") || Has("ArrowUp") || Has("ArrowDown") || Has("ArrowLeft") || Has("ArrowRight") || Free && (Has("KeyQ") || Has("KeyE") || Has("Space")))
            Following = false;
        int forward = (Has("KeyW") || Has("ArrowUp") ? 1 : 0) - (Has("KeyS") || Has("ArrowDown") ? 1 : 0);
        int right = (Has("KeyD") || Has("ArrowRight") ? 1 : 0) - (Has("KeyA") || Has("ArrowLeft") ? 1 : 0);
        int vertical = (Has("KeyE") || Has("Space") ? 1 : 0) - Bit("KeyQ");
        if (!Free)
            Yaw += (Bit("KeyQ") - Bit("KeyE")) * delta;
        var movement = new TrackVector(right * Math.Cos(Yaw) - forward * Math.Sin(Yaw), Free ? vertical : 0, -right * Math.Sin(Yaw) - forward * Math.Cos(Yaw));
        if (movement.LengthSquared > 0)
        {
            double speed = Free ? Speed : Math.Max(30, Distance * .65);
            bool fast = Has("ShiftLeft") || Has("ShiftRight");
            movement = movement.Unit() * (speed * delta * (fast ? Free ? 5 : 2.5 : 1));
            if (loaded(Position.X + movement.X, Position.Z + movement.Z))
            {
                if (Free)
                    Position += movement;
                else
                    Target += movement;
            }
        }
        if (!Free)
        {
            Target = Target with
            {
                Y = Target.Y + (ground(Target.X, Target.Z) - Target.Y) * (1 - Math.Exp(-delta * 7))
            };
            Orbit(ground);
        }
        else
            Position = Position with
            {
                Y = Math.Max(Position.Y, ground(Position.X, Position.Z) + 8)
            };
    }
    public void Follow(TrackVector point, Func<double, double, double> ground)
    {
        Following = true;
        _followedPoint = point;
        Target = point;
        Orbit(ground);
    }
    public void UpdateFollow(TrackVector point, Func<double, double, double> ground)
    {
        if (!Following)
            return;
        if (!Free)
        {
            Target = point;
            Orbit(ground);
        }
        else
        {
            Position += point - _followedPoint;
            Target = point;
            Position = Position with
            {
                Y = Math.Max(Position.Y, ground(Position.X, Position.Z) + 8)
            };
        }
        _followedPoint = point;
    }
}
