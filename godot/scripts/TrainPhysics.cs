using System;
using System.Collections.Generic;

namespace TransportSandbox;

public sealed record VehiclePhysics(double MassKg, double PowerW, double TractiveEffortN, double MaxSpeedMps);
public readonly record struct TrainPerformance(double Mass, double Power, double Traction, double MaxSpeed);
public readonly record struct TrainStep(double Speed, double Distance, double Effort);
public static class TrainPhysics
{
    // Adapted from OpenLoco Vehicle2::update (MIT, third-party/OpenLoco-LICENSE.txt).
    // Copyright (c) 2018 OpenLoco developers. Continuous grade replaces sprite-pitch tables.
    // All state and coefficients use SI: kg, W, m, s. Converted once from the source controller.
    public const double HoMinimumSpeed = 2.2352; // m/s (8.04672 km/h)
    private const double HoMaxAcceleration = 7.210322580645161; // m/s^2
    private const double HoPowerFactor = .6043251158594368; // acceleration per W/kg
    private const double HoGradeFactor = .00005633064516129032; // acceleration per kg * slope sine
    private const double HoDragFactor = .001101062549248678; // acceleration per (m/s)^2
    private const double HoDragSpeedStep = .44704; // m/s; preserve the source drag lookup resolution
    private const double HoBrakeConstant = 2.640564335483871; // m/s^2
    private const double HoBrakeLinear = .5040322580645161; // 1/s
    public const double Gravity = 9.80665;
    private const double Linear = .0001096842564423765;
    public static double CurveLimit(double radius, bool ho) => Math.Sqrt(Math.Max(0, radius) * 0.65 * (ho ? 9 : 1));
    public static double BrakingDeceleration(double grade, bool ho) => ho ? HoBrakeConstant : Math.Max(.05, (.65 + Gravity * grade) / 1.06);
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
        return new(mass, power * (ho ? 1 : .82), traction, power > 0 ? top : 0);
    }
    public static TrainStep Step(IEnumerable<VehiclePhysics> cars, double speed, bool started, double grade, bool ho, double dt, double limit = double.PositiveInfinity, double catalogMax = 75)
    {
        var p = Performance(cars, ho, catalogMax);
        if (p.Mass <= 0 || dt <= 0)
            return new(speed, 0, 0);
        double resistance = p.Mass * Gravity * 0.0015 + p.Mass * Linear * speed + 4 * speed * speed, gravity = p.Mass * Gravity * grade;
        double target = started ? Math.Min(p.MaxSpeed, limit) : 0, next;
        if (ho)
        {
            target = Math.Max(0, target);
            if (speed > target)
                next = Math.Max(target, speed - (HoBrakeLinear * speed + HoBrakeConstant) * dt);
            else if (target <= 0)
                next = 0;
            else
            {
                double drive = HoPowerFactor * p.Power / p.Mass;
                double dragSpeed = Math.Floor(speed / HoDragSpeedStep) * HoDragSpeedStep;
                double acceleration = Math.Min(HoMaxAcceleration,
                    drive - HoGradeFactor * p.Mass * grade - HoDragFactor * dragSpeed * dragSpeed);
                double floor = Math.Min(target, HoMinimumSpeed);
                // Below the floor, start gently even when the uphill force balance is negative.
                double startAcceleration = Math.Min(HoMaxAcceleration, drive);
                next = Math.Min(target, speed < floor ? speed + Math.Max(acceleration, startAcceleration) * dt
                    : Math.Max(speed + acceleration * dt, floor));
            }
        }
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
            effort = ho ? Math.Clamp((target - speed) / Math.Max(target, 1) + .1 + grade * 10, 0, 1)
                : Math.Clamp(((next - speed) / dt * p.Mass * 1.06 + resistance + gravity) / nominal, 0, 1);
        }
        return new(next, (speed + next) * dt / 2, effort);
    }
}
