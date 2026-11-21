using System;

namespace TransportSandbox.Tests;

public static class EngineAudioChecks
{
    public static void Run(Action<bool, string> check)
    {
        int count = 0;
        foreach (var fixture in JsonData.Read("res://tests/engine-audio-fixtures.json").EnumerateArray())
        {
            var profile = EngineSoundProfile.Read(fixture.GetProperty("profile"));
            var controller = new EngineAudioController(profile);
            bool identical = true;
            foreach (var step in fixture.GetProperty("steps").EnumerateArray())
            {
                var state = Enum.Parse<TrainMotorState>(step.GetProperty("state").GetString()!, ignoreCase: true);
                controller.Advance(step.GetProperty("speedMps").GetDouble(), state, EngineAudioController.TickSeconds);
                identical &= controller.FrequencyHz == step.GetProperty("frequencyHz").GetInt32()
                    && controller.Volume == step.GetProperty("volume").GetInt32();
                count++;
            }
            check(identical, "Original OpenLoco frequency/volume sequence matches SI controller: " + fixture.GetProperty("name").GetString());
            var a = new EngineAudioController(profile); var b = new EngineAudioController(profile);
            for (int i = 0; i < 180; i++) a.Advance(10, TrainMotorState.Accelerating, 1.0 / 60);
            for (int i = 0; i < 432; i++) b.Advance(10, TrainMotorState.Accelerating, 1.0 / 144);
            check(a.FrequencyHz == b.FrequencyHz && a.Volume == b.Volume, "Engine ramps are independent of render frame rate");
            check(float.IsFinite(a.Pitch) && a.Pitch > 0 && a.Gain >= 0, "Engine output is valid for Godot playback");
        }
        check(EngineAudioController.Classify(10, 20, true) == TrainMotorState.Accelerating
            && EngineAudioController.Classify(10, 10, true) == TrainMotorState.Coasting
            && EngineAudioController.Classify(10, 5, true) == TrainMotorState.Braking
            && EngineAudioController.Classify(0, 0, false) == TrainMotorState.Stopped,
            "Actual movement targets classify power, coast, braking and stopped states");
        var profiles = JsonData.Read("res://assets/audio/trains/engine-profiles.json");
        check(EngineSoundProfile.Read(profiles.GetProperty("electric")).Mode == "friction", "TGV uses the Acela speed-driven electric sound profile");
        Godot.GD.Print($"Engine audio parity: {count} source-reference updates and 60/144 Hz timing checks");
    }
}
