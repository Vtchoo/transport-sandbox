using System;
using System.Text.Json;

namespace TransportSandbox;

public enum TrainMotorState { Stopped, Accelerating, Coasting, Braking }

public sealed class EngineSoundProfile
{
    public string Mode { get; init; } = "simple_motor";
    public int IdleFrequencyHz { get; init; }
    public int IdleVolume { get; init; }
    public double CoastThresholdMps { get; init; }
    public int CoastingFrequencyHz { get; init; }
    public int CoastingVolume { get; init; }
    public int AccelerationFrequencyHz { get; init; }
    public int AccelerationVolume { get; init; }
    public int FrequencyIncreasePerTick { get; init; }
    public int FrequencyDecreasePerTick { get; init; }
    public int VolumeIncreasePerTick { get; init; }
    public int VolumeDecreasePerTick { get; init; }
    public double SpeedFrequencyPerMps { get; init; }
    public double MinSpeedMps { get; init; }
    public int BaseFrequencyHz { get; init; }
    public double SpeedVolumePerMps { get; init; }
    public int BaseVolume { get; init; }
    public int MaxVolume { get; init; }
    public int FirstGearFrequencyHz { get; init; }
    public double MinimumGearSpeedMps { get; init; }
    public double[] GearSpeedsMps { get; init; } = Array.Empty<double>();
    public int[] GearFrequencyOffsetsHz { get; init; } = Array.Empty<int>();
    public static EngineSoundProfile Read(JsonElement data) => JsonSerializer.Deserialize<EngineSoundProfile>(data.GetRawText(),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}

/// <summary>OpenLoco VehicleHead motor/friction updates (MIT, third-party/OpenLoco-LICENSE.txt).
/// Copyright (c) 2018 OpenLoco developers. Parameters converted once to Hz and m/s.
/// Spatial attenuation/Doppler are applied separately by TrainAudio.</summary>
public sealed class EngineAudioController
{
    public const double TickSeconds = .031;
    public readonly EngineSoundProfile Profile;
    public int FrequencyHz { get; private set; }
    public int Volume { get; private set; }
    public float Pitch => FrequencyHz >= 100 ? FrequencyHz / 22000f : 1;
    // OpenLoco's vehicle-volume conversion at maximum pan falloff, without zoom/underground attenuation.
    public float Gain => Volume == 0 ? 0 : (float)Math.Pow(10, ((Volume * 255 / 8) - 8191) / 2000.0);
    private double _elapsed;
    private bool _initialized;
    public EngineAudioController(EngineSoundProfile profile) { Profile = profile; }

    public static TrainMotorState Classify(double speedMps, double targetMps, bool started)
    {
        speedMps = Math.Abs(speedMps);
        targetMps = started ? Math.Max(0, targetMps) : 0;
        if (speedMps > targetMps + 1e-8) return TrainMotorState.Braking;
        if (speedMps <= .001) return TrainMotorState.Stopped;
        return targetMps - speedMps <= .67056 ? TrainMotorState.Coasting : TrainMotorState.Accelerating;
    }

    public void Advance(double speedMps, TrainMotorState state, double delta)
    {
        if (!double.IsFinite(speedMps) || !double.IsFinite(delta) || delta <= 0) return;
        _elapsed += Math.Min(delta, .25);
        while (_elapsed + 1e-10 >= TickSeconds)
        {
            _elapsed -= TickSeconds;
            Tick(Math.Abs(speedMps), state);
        }
    }

    private static int Quantize(double value) => (int)Math.Floor(value + 1e-7);
    private static int Approach(int value, int target, int up, int down) => value > target
        ? Math.Max(target, value - down) : Math.Min(target, value + up);

    private void Tick(double speed, TrainMotorState state)
    {
        var p = Profile;
        if (p.Mode == "friction")
        {
            if (speed < p.MinSpeedMps) { Volume = 0; return; }
            double difference = speed - p.MinSpeedMps;
            FrequencyHz = (p.BaseFrequencyHz + Quantize(difference * p.SpeedFrequencyPerMps)) & 65535;
            Volume = Math.Min(p.MaxVolume, (p.BaseVolume + Quantize(difference * p.SpeedVolumePerMps)) & 255);
            return;
        }
        int frequency = p.IdleFrequencyHz, volume = p.IdleVolume;
        if (p.Mode == "simple_motor")
        {
            if (state == TrainMotorState.Accelerating)
            {
                frequency = (p.AccelerationFrequencyHz + Quantize(speed * p.SpeedFrequencyPerMps)) & 65535;
                volume = p.AccelerationVolume;
            }
            else if (state == TrainMotorState.Coasting && speed >= p.CoastThresholdMps)
            {
                frequency = p.CoastingFrequencyHz; volume = p.CoastingVolume;
            }
        }
        else if (p.Mode == "gearbox_motor")
        {
            bool inGear = state == TrainMotorState.Accelerating;
            if (state == TrainMotorState.Coasting || state == TrainMotorState.Braking)
            {
                volume = p.CoastingVolume;
                inGear = speed >= p.CoastThresholdMps;
            }
            else if (inGear) volume = p.AccelerationVolume;
            if (inGear)
            {
                speed = Math.Max(speed, p.MinimumGearSpeedMps);
                frequency = p.FirstGearFrequencyHz;
                for (int i = 0; i < Math.Min(p.GearSpeedsMps.Length, p.GearFrequencyOffsetsHz.Length); i++)
                {
                    if (speed < p.GearSpeedsMps[i]) break;
                    frequency -= p.GearFrequencyOffsetsHz[i];
                }
                frequency = (frequency + Quantize(speed * p.SpeedFrequencyPerMps)) & 65535;
            }
        }
        else throw new InvalidOperationException("Unknown engine sound mode: " + p.Mode);
        if (!_initialized)
        {
            _initialized = true;
            FrequencyHz = p.IdleFrequencyHz >> 2; Volume = p.IdleVolume >> 1;
            return;
        }
        if (p.Mode == "gearbox_motor" && FrequencyHz > frequency) volume = p.CoastingVolume;
        FrequencyHz = Approach(FrequencyHz, frequency, p.FrequencyIncreasePerTick, p.FrequencyDecreasePerTick);
        Volume = Approach(Volume, volume, p.VolumeIncreasePerTick, p.VolumeDecreasePerTick);
    }
}
