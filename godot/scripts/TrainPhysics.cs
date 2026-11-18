using System;
using System.Collections.Generic;

namespace TransportSandbox;

public sealed record VehiclePhysics(double MassKg, double PowerW, double TractiveEffortN, double MaxSpeedMps);
public readonly record struct TrainPerformance(double Mass, double Power, double Traction, double MaxSpeed);
public readonly record struct TrainStep(double Speed, double Distance, double Effort);
public static class TrainPhysics
{
    public const double Gravity = 9.80665;
    private const double Linear = 0.01 * 4.4482216152605 / (907.18474 * 0.44704);
    public static double CurveLimit(double radius, bool ho) => Math.Sqrt(Math.Max(0, radius) * 0.65 * (ho ? 9 : 1));
    public static double BrakingDeceleration(double grade, bool ho) => ho ? .65 / 1.06 * 9 : Math.Max(.05, (.65 + Gravity * grade) / 1.06);
    public static TrainPerformance Performance(IEnumerable<VehiclePhysics> cars, bool ho, double catalogMax = 75)
    {
        double mass = 0, power = 0, traction = 0, top = double.PositiveInfinity;
        foreach (var car in cars)
        {
            mass += car.MassKg;
            power += car.PowerW;
            if (car.PowerW > 0)
                traction += Math.Min(car.TractiveEffortN, car.MassKg * Gravity * 0.25);
            top = Math.Min(top, car.MaxSpeedMps);
        }
        return new(mass, power * 0.82, traction, power > 0 ? top * (ho ? (120.0 / 3.6) / catalogMax : 1) : 0);
    }
    public static TrainStep Step(IEnumerable<VehiclePhysics> cars, double speed, bool started, double grade, bool ho, double dt, double limit = double.PositiveInfinity, double catalogMax = 75)
    {
        var p = Performance(cars, ho, catalogMax);
        if (p.Mass <= 0 || dt <= 0)
            return new(speed, 0, 0);
        double resistance = p.Mass * Gravity * 0.0015 + p.Mass * Linear * speed + 4 * speed * speed, gravity = p.Mass * Gravity * grade;
        double target = started ? Math.Min(p.MaxSpeed, limit) : 0, next;
        if (ho)
            next = speed + Math.Clamp(target - speed, -0.65 / 1.06 * 9 * dt, p.Traction / (p.Mass * 1.06) * 9 * dt);
        else
        {
            bool braking = !started || speed > target;
            double force = braking ? -p.Mass * 0.65 : Math.Min(p.Traction, p.Power / Math.Max(speed, 0.5));
            next = Math.Max(0, speed + (force - resistance - gravity) / (p.Mass * 1.06) * dt);
            if (started && speed <= target && next > target)
                next = target;
        }
        if (target == 0 && next < 0.001)
            next = 0;
        double effort = 0;
        if (started && target > .001 && speed <= target + (ho ? .001 : 0) && p.Traction > 0 && p.Power > 0)
        {
            double nominal = Math.Min(p.Traction, p.Power / Math.Max(speed, 0.5));
            effort = Math.Clamp(((next - speed) / dt / (ho ? 9 : 1) * p.Mass * 1.06 + resistance + gravity) / nominal, 0, 1);
        }
        return new(next, (speed + next) * dt / 2, effort);
    }
}
