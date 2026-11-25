using Godot;
using System;

namespace TransportSandbox.Tests;

public static class WalkingChecks
{
    public static void Run(Action<bool, string> check, Node parent)
    {
        static double flat(double x, double z) => 10;
        static bool loaded(double x, double z) => true;
        var walker = new WalkingCharacter { Feet = new(0, 10, 0) };
        for (int i = 0; i < 60; i++) walker.Update(1, 0, 0, false, 1.0 / 60, flat, loaded, 0);
        check(Math.Abs(walker.Feet.Z + 4.5) < 1e-8 && walker.Feet.Y == 10 && walker.Eyes.Y == 11.7,
            "Walking advances at 4.5 m/s and holds eyes 1.7 m above terrain");
        walker.Feet = new(0, 10, 0);
        for (int i = 0; i < 60; i++) walker.Update(1, 1, 0, true, 1.0 / 60, flat, loaded, 0);
        check(Math.Abs(Math.Sqrt(walker.Feet.X * walker.Feet.X + walker.Feet.Z * walker.Feet.Z) - 8) < 1e-8,
            "Running is 8 m/s without a diagonal speed boost");
        var before = walker.Feet;
        walker.Update(1, 0, 0, false, .05, flat, (_, _) => false, 0);
        check(walker.Feet == before, "Walking cannot enter unloaded terrain");
        walker.Feet = new(0, 10, 0);
        for (int i = 0; i < 60; i++) walker.Update(0, 1, 0, false, 1.0 / 60, (x, _) => 10 + x * .3, loaded, 0);
        check(Math.Abs(walker.Feet.Y - (10 + walker.Feet.X * .3)) < 1e-8, "Feet stay grounded on an uphill slope");
        walker.Feet = new(0, 10, 0);
        walker.Update(0, 1, 0, false, .05, (x, _) => x > 0 ? 20 : 10, loaded, 0);
        check(walker.Feet.X == 0, "Walking does not climb cliff faces");
        walker.Update(0, 1, 0, false, .05, (x, _) => x > 0 ? -5 : 10, loaded, 0);
        check(walker.Feet.X == 0, "Walking does not enter deep water");
        walker.Feet = new(0, 20, 0);
        for (int i = 0; i < 180; i++) walker.Update(0, 0, 0, false, 1.0 / 60, flat, loaded, 0);
        check(walker.Feet.Y == 10 && walker.VerticalSpeed == 0, "Gravity lands without penetrating the terrain");

        var camera = new WorldCamera { Enabled = true, Ground = new TerrainGenerator(), GroundHeight = flat };
        parent.AddChild(camera); camera.SetProcess(false);
        try
        {
            camera.ResetWorld(camera.Ground, 4096, 6144);
            var original = camera.Capture();
            camera.SetWalkingMode(true);
            check(camera.WalkingMode && Math.Abs(camera.WorldPosition.Y - 11.7) < 1e-6,
                "Walking spawns at the overlook target, at eye level");
            check(camera.Capture().Position == original.Position, "Saving while walking preserves the regular camera pose");
            camera.Look(80, -40);
            check(Math.Abs(camera.WorldPosition.Y - 11.7) < 1e-6, "Mouse look does not orbit away from the player");
            var eyes = camera.WorldPosition;
            camera.Space.Rebase(eyes);
            check((camera.WorldPosition - eyes).Length < .001, "Floating origin preserves the absolute player position");
            camera.SetWalkingMode(false);
            check(!camera.WalkingMode && camera.WorldPosition == original.Position && camera.Rig.Yaw == original.Yaw,
                "Leaving walking restores the exact prior camera pose");
            camera.SetWalkingMode(true); camera.SetFreeMode(true);
            check(!camera.WalkingMode && camera.FreeMode, "Selecting free camera cleanly leaves walking");
            camera.SetWalkingMode(true); camera.InputBlocked = true;
            check(Input.MouseMode != Input.MouseModeEnum.Captured, "Modal UI releases captured player input");
            camera.SetWalkingMode(false); camera.InputBlocked = false;
            camera._UnhandledInput(new InputEventKey { Keycode = Key.P, Pressed = true });
            check(camera.WalkingMode, "P enters walking mode");
            camera._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            check(Input.MouseMode != Input.MouseModeEnum.Captured, "Escape releases the mouse without leaving walking");
            camera._UnhandledInput(new InputEventKey { Keycode = Key.P, Pressed = true });
            check(!camera.WalkingMode, "P returns to the previous camera");
        }
        finally { camera.Free(); }
    }
}
