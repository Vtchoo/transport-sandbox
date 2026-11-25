using System;

namespace TransportSandbox;

/// <summary>A grounded first-person player in absolute metre coordinates.</summary>
public sealed class WalkingCharacter
{
    public const double EyeHeight = 1.7, WalkSpeed = 4.5, RunSpeed = 8;
    public TrackVector Feet;
    public double VerticalSpeed;

    public TrackVector Eyes => Feet + new TrackVector(0, EyeHeight, 0);

    public void Update(double forward, double right, double yaw, bool run, double delta,
        Func<double, double, double> ground, Func<double, double, bool> loaded, double seaLevel)
    {
        double dt = Math.Clamp(delta, 0, .05);
        double length = Math.Sqrt(forward * forward + right * right);
        if (length > 1) { forward /= length; right /= length; }
        double distance = (run ? RunSpeed : WalkSpeed) * dt;
        double dx = (right * Math.Cos(yaw) - forward * Math.Sin(yaw)) * distance;
        double dz = (-right * Math.Sin(yaw) - forward * Math.Cos(yaw)) * distance;
        // Sliding along each axis keeps the player moving beside steep terrain.
        Move(dx, 0); Move(0, dz);
        double floor = ground(Feet.X, Feet.Z);
        VerticalSpeed -= 9.81 * dt;
        double y = Feet.Y + VerticalSpeed * dt;
        if (y <= floor) { y = floor; VerticalSpeed = 0; }
        Feet = Feet with { Y = y };

        void Move(double x, double z)
        {
            if (x == 0 && z == 0 || !loaded(Feet.X + x, Feet.Z + z)) return;
            double next = ground(Feet.X + x, Feet.Z + z);
            double current = ground(Feet.X, Feet.Z);
            // No swimming or climbing cliff faces in this first walking mode.
            if (next < seaLevel || next - current > Math.Sqrt(x * x + z * z) * 1.2 + .03) return;
            Feet += new TrackVector(x, 0, z);
            if (next > Feet.Y && next - Feet.Y <= .4) Feet = Feet with { Y = next };
        }
    }
}
