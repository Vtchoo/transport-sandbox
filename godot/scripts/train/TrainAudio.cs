using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public readonly record struct TrainAudioVehicle(int Id, string Model, Vector3 Position, double Speed, double Effort, double Radius, double WheelTurns = double.NaN, TrainMotorState MotorState = TrainMotorState.Stopped);

/// <summary>One spatial mixer for the main camera, shared by menu and gameplay. No audio in preview viewports.</summary>
public partial class TrainAudio : Node3D
{
    public const int MaxVehicles = 16;
    public const float AudibleDistance = 350;
    public SoundSettings Settings = new();
    public float MasterVolume { get => Settings.MasterVolume; set => Settings.MasterVolume = value; }
    private readonly Dictionary<string, (AudioStreamWav? Engine, AudioStreamWav? Chuff, AudioStreamWav? Horn)> _models = new();
    private readonly Dictionary<string, AudioStreamWav?> _wheelOverrides = new();
    private readonly Dictionary<string, AudioStreamWav[]> _chuffs = new();
    private readonly Dictionary<string, EngineSoundProfile> _engineProfiles = new();
    private readonly Dictionary<int, Voice> _horns = new();
    private readonly HashSet<int> _presentHorns = new();
    public double DepartureHornChance { get; set; } = .5;
    private readonly Dictionary<int, Voice> _voices = new();
    private readonly HashSet<int> _present = new();
    private readonly List<TrainAudioVehicle> _candidates = new();
    // Audio-only randomness must not consume the simulation's seeded route/world RNG.
    private readonly Random _random = new();
    private AudioStreamWav?[] _rolling = Array.Empty<AudioStreamWav?>();
    private AudioStreamWav? _curve;
    private int _wheelSeed;
    private Vector3 _lastCamera;
    private bool _cameraValid;
    private double _selectionTimer;
    public int ActiveVehicles => _voices.Count;
    internal bool DiagnosticEnabled, DiagnosticDisableDoppler;
    internal long DiagnosticCreates, DiagnosticRemoves, DiagnosticStarts, DiagnosticStops;
    internal float DiagnosticDopplerJump, DiagnosticSourceSpeed;
    internal int DiagnosticFocus;
    internal float DiagnosticHornDoppler = 1, DiagnosticHornJump, DiagnosticListenerSpeed, DiagnosticFocusSpeed;
    internal bool DiagnosticHornActive;
    internal Action<double>? FrameUpdate;
    public override void _Process(double delta) => FrameUpdate?.Invoke(delta);

    private sealed class Voice
    {
        public required string Model;
        public required AudioStreamPlayer3D[] Layers;
        public Vector3 Previous;
        public bool MotionValid;
        public float WheelPitch;
        public float WheelOffset;
        public Node3D? ChuffRoot;
        public AudioStreamPlayer3D[] Chuffs = Array.Empty<AudioStreamPlayer3D>();
        public readonly SteamChuffClock ChuffClock = new();
        public int ChuffIndex;
        public double FallbackTurns;
        public EngineAudioController? Engine;
        public float PreviousDoppler = 1;
    }

    public override void _Ready()
    {
        var config = JsonData.Read("res://assets/audio/trains/sounds.json");
        var profiles = JsonData.Read("res://assets/audio/trains/engine-profiles.json");
        var rolling = config.GetProperty("rolling");
        _rolling = rolling.ValueKind == System.Text.Json.JsonValueKind.Array
            ? rolling.EnumerateArray().Select(path => LoadLoop(path.GetString()!)).Where(stream => stream != null).ToArray()
            : new[] { LoadLoop(rolling.GetString()!) };
        _wheelSeed = _random.Next();
        _curve = LoadLoop(config.GetProperty("curve").GetString()!);
        if (config.TryGetProperty("departureHornChance", out var chance)) DepartureHornChance = Math.Clamp(chance.GetDouble(), 0, 1);
        foreach (var entry in config.GetProperty("vehicles").EnumerateObject())
        {
            _models[entry.Name] = (LoadLoop(entry.Value.GetProperty("engine").GetString()!),
                entry.Value.TryGetProperty("chuff", out var chuff) ? LoadLoop(chuff.GetString()!) : null,
                entry.Value.TryGetProperty("horn", out var horn) ? LoadLoop(horn.GetString()!, loop: false) : null);
            if (entry.Value.TryGetProperty("rolling", out var wheel)) _wheelOverrides[entry.Name] = LoadLoop(wheel.GetString()!);
            if (entry.Value.TryGetProperty("engineProfile", out var profile))
                _engineProfiles[entry.Name] = EngineSoundProfile.Read(profiles.GetProperty(profile.GetString()!));
            if (entry.Value.TryGetProperty("chuffs", out var chuffs))
                _chuffs[entry.Name] = chuffs.EnumerateArray().Select(path => LoadLoop(path.GetString()!, loop: false))
                    .OfType<AudioStreamWav>().ToArray();
        }
    }

    private static AudioStreamWav? LoadLoop(string path, bool loop = true)
    {
        string resource = "res://assets/audio/trains/" + path;
        if (!ResourceLoader.Exists(resource)) { GD.PushWarning("Missing optional train sound: " + resource); return null; }
        var stream = GD.Load<AudioStreamWav>(resource);
        if (stream != null) { stream.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled; stream.LoopBegin = 0; stream.LoopEnd = (int)Math.Round(stream.GetLength() * stream.MixRate); }
        return stream;
    }

    public void ResetMotion()
    {
        _cameraValid = false;
        foreach (var voice in _voices.Values) voice.MotionValid = false;
        foreach (var voice in _horns.Values) voice.MotionValid = false;
    }

    public void Rebase(TrackVector worldShift)
    {
        var shift = worldShift.Float();
        // Spatial mixing runs independently of rendering. Move emitters in the same
        // rebase callback as the listener, before the next audio/physics mix can see
        // a false 512 m separation. Preserve loops, one-shots and velocity history.
        foreach (var child in GetChildren())
            if (child is Node3D spatial) spatial.Position -= shift;
        _lastCamera -= shift;
        foreach (var voice in _voices.Values) voice.Previous -= shift;
        foreach (var voice in _horns.Values) voice.Previous -= shift;
    }

    public bool PlayHorn(TrainAudioVehicle vehicle, Camera3D camera, bool departure = false)
    {
        if (Settings.Muted || MasterVolume <= 0 || Settings.Engines <= 0 ||
            camera.GlobalPosition.DistanceSquaredTo(vehicle.Position) > AudibleDistance * AudibleDistance ||
            _horns.ContainsKey(vehicle.Id) || _horns.Count >= 8 ||
            !_models.TryGetValue(vehicle.Model, out var definition) || definition.Horn == null) return false;
        if (departure && _random.NextDouble() >= DepartureHornChance) return false;
        var player = new AudioStreamPlayer3D { Stream = definition.Horn, UnitSize = 18, MaxDistance = AudibleDistance,
            MaxDb = -6, VolumeDb = Mathf.LinearToDb(.3f * Math.Clamp(MasterVolume, 0, 1) * Math.Clamp(Settings.Engines, 0, 1)),
            Position = vehicle.Position + Vector3.Up };
        AddChild(player);
        var voice = new Voice { Model = vehicle.Model, Layers = new[] { player } };
        // A station horn starts during simulation, before this frame's late mixer.
        // Reuse the emitter's history so its first update has a real velocity too.
        if (_voices.TryGetValue(vehicle.Id, out var engineVoice))
        { voice.Previous = engineVoice.Previous; voice.MotionValid = engineVoice.MotionValid; }
        _horns.Add(vehicle.Id, voice);
        player.Finished += () => { if (_horns.Remove(vehicle.Id)) player.QueueFree(); };
        player.Play();
        return true;
    }

    private void RemoveHorn(int id)
    {
        var player = _horns[id].Layers[0]; player.Stop(); player.QueueFree(); _horns.Remove(id);
    }

    private void UpdateHorn(Voice voice, TrainAudioVehicle vehicle, Vector3 eye, Vector3 cameraVelocity, double delta)
    {
        var velocity = voice.MotionValid ? (vehicle.Position - voice.Previous) / (float)delta : Vector3.Zero;
        if (velocity.Length() > 150) velocity = Vector3.Zero;
        var player = voice.Layers[0];
        player.GlobalPosition = vehicle.Position + Vector3.Up;
        player.PitchScale = DiagnosticDisableDoppler ? 1 : Doppler(vehicle.Position, eye, velocity, cameraVelocity);
        if (DiagnosticEnabled && vehicle.Id == DiagnosticFocus)
        {
            DiagnosticHornActive = true;
            DiagnosticHornDoppler = player.PitchScale;
            DiagnosticHornJump = voice.MotionValid ? Math.Abs(player.PitchScale - voice.PreviousDoppler) : 0;
            DiagnosticFocusSpeed = velocity.Length();
        }
        voice.PreviousDoppler = player.PitchScale;
        float gain = Settings.Muted ? 0 : .3f * Math.Clamp(MasterVolume, 0, 1) * Math.Clamp(Settings.Engines, 0, 1);
        player.VolumeDb = gain <= .0001 ? -80 : Mathf.LinearToDb(gain);
        voice.Previous = vehicle.Position; voice.MotionValid = true;
    }

    // SI units: project relative source/listener velocities onto the line of sight.
    // Clamp artistic pitch shifts and discard teleports instead of producing an audio burst.
    public static float Doppler(Vector3 source, Vector3 listener, Vector3 sourceVelocity, Vector3 listenerVelocity)
    {
        var direction = (listener - source).Normalized();
        if (direction == Vector3.Zero) return 1;
        double numerator = 343 - listenerVelocity.Dot(direction);
        double denominator = 343 - sourceVelocity.Dot(direction);
        return (float)Math.Clamp(numerator / Math.Max(100, denominator), .65, 1.5);
    }

    public static (float Rolling, float Curve, float EnginePitch) Mix(double speed, double effort, double radius)
    {
        speed = Math.Abs(speed);
        float rolling = (float)Math.Clamp(speed / 18, 0, 1);
        float curve = (float)(Math.Clamp((speed - 1.5) / 7, 0, 1) * Math.Clamp((350 - radius) / 300, 0, 1));
        return (rolling, curve, 1);
    }

    public void UpdateSounds(IEnumerable<TrainAudioVehicle> vehicles, Camera3D camera, double delta)
    {
        if (delta <= 0) return;
        if (DiagnosticEnabled) { DiagnosticDopplerJump = 0; DiagnosticSourceSpeed = 0; DiagnosticHornActive = false; DiagnosticHornJump = 0; }
        var eye = camera.GlobalPosition;
        var cameraVelocity = _cameraValid ? (eye - _lastCamera) / (float)delta : Vector3.Zero;
        if (cameraVelocity.Length() > 150) { cameraVelocity = Vector3.Zero; ResetMotion(); }
        if (DiagnosticEnabled) DiagnosticListenerSpeed = cameraVelocity.Length();
        _lastCamera = eye; _cameraValid = true;
        _selectionTimer -= delta;
        bool select = _selectionTimer <= 0;
        _candidates.Clear();
        _present.Clear();
        _presentHorns.Clear();
        foreach (var vehicle in vehicles)
        {
            if (eye.DistanceSquaredTo(vehicle.Position) > AudibleDistance * AudibleDistance) continue;
            if (select) _candidates.Add(vehicle);
            if (_horns.TryGetValue(vehicle.Id, out var horn))
            {
                _presentHorns.Add(vehicle.Id);
                UpdateHorn(horn, vehicle, eye, cameraVelocity, delta);
            }
            if (!_voices.TryGetValue(vehicle.Id, out var voice) || voice.Model != vehicle.Model) continue;
            _present.Add(vehicle.Id);
            UpdateVoice(voice, vehicle, eye, cameraVelocity, delta);
        }
        if (select)
        {
            var nearest = _candidates.OrderBy(v => eye.DistanceSquaredTo(v.Position)).Take(MaxVehicles).ToArray();
            var chosen = nearest.Select(v => v.Id).ToHashSet();
            foreach (var id in _voices.Keys.Where(id => !chosen.Contains(id) || !_present.Contains(id)).ToArray()) RemoveVoice(id);
            foreach (var vehicle in nearest)
                if (!_voices.ContainsKey(vehicle.Id))
                {
                    var voice = CreateVoice(vehicle.Id, vehicle.Model);
                    _voices.Add(vehicle.Id, voice);
                    UpdateVoice(voice, vehicle, eye, cameraVelocity, delta);
                }
            _selectionTimer = .2;
        }
        else foreach (var id in _voices.Keys.Where(id => !_present.Contains(id)).ToArray()) RemoveVoice(id);
        foreach (var id in _horns.Keys.Where(id => !_presentHorns.Contains(id)).ToArray()) RemoveHorn(id);
    }

    private Voice CreateVoice(int id, string model)
    {
        if (DiagnosticEnabled) DiagnosticCreates++;
        _models.TryGetValue(model, out var definition);
        // Stable per vehicle across voice culling, with an audio-only random seed for this session.
        var rolling = _wheelOverrides.TryGetValue(model, out var custom) ? custom
            : _rolling.Length > 0 ? _rolling[(HashCode.Combine(_wheelSeed, id) & int.MaxValue) % _rolling.Length] : null;
        var streams = new[] { definition.Engine, rolling, _curve, definition.Chuff };
        var layers = new AudioStreamPlayer3D[4];
        for (int i = 0; i < layers.Length; i++)
        {
            var player = new AudioStreamPlayer3D { Stream = streams[i], UnitSize = 12, MaxDistance = AudibleDistance,
                MaxDb = -6, VolumeDb = -80, PanningStrength = .8f };
            // Doppler is calculated explicitly to survive floating-origin shifts and camera teleports.
            AddChild(player); layers[i] = player;
        }
        var voice = new Voice
        {
            Model = model, Layers = layers,
            WheelPitch = .975f + (float)_random.NextDouble() * .05f,
            WheelOffset = (float)(_random.NextDouble() * (rolling?.GetLength() ?? 0))
        };
        if (_engineProfiles.TryGetValue(model, out var engineProfile)) voice.Engine = new(engineProfile);
        if (_chuffs.TryGetValue(model, out var chuffs) && chuffs.Length > 0)
        {
            voice.ChuffRoot = new() { Name = "SteamChuffs" };
            AddChild(voice.ChuffRoot);
            voice.Chuffs = chuffs.Select(stream =>
            {
                var player = new AudioStreamPlayer3D { Stream = stream, UnitSize = 12, MaxDistance = AudibleDistance,
                    MaxDb = -6, VolumeDb = -80, MaxPolyphony = 2 };
                voice.ChuffRoot.AddChild(player);
                return player;
            }).ToArray();
        }
        return voice;
    }

    private void RemoveVoice(int id)
    {
        if (DiagnosticEnabled) DiagnosticRemoves++;
        foreach (var player in _voices[id].Layers) { player.Stop(); player.QueueFree(); }
        foreach (var player in _voices[id].Chuffs) player.Stop();
        _voices[id].ChuffRoot?.QueueFree();
        _voices.Remove(id);
    }

    private void UpdateVoice(Voice voice, TrainAudioVehicle vehicle, Vector3 eye, Vector3 cameraVelocity, double delta)
    {
        var velocity = voice.MotionValid ? (vehicle.Position - voice.Previous) / (float)delta : Vector3.Zero;
        if (velocity.Length() > 150) velocity = Vector3.Zero;
        float doppler = Doppler(vehicle.Position, eye, velocity, cameraVelocity);
        if (DiagnosticDisableDoppler) doppler = 1;
        if (DiagnosticEnabled)
        {
            DiagnosticDopplerJump = Math.Max(DiagnosticDopplerJump, Math.Abs(doppler - voice.PreviousDoppler));
            DiagnosticSourceSpeed = Math.Max(DiagnosticSourceSpeed, velocity.Length());
        }
        voice.PreviousDoppler = doppler;
        voice.Previous = vehicle.Position; voice.MotionValid = true;
        var mix = Mix(vehicle.Speed, vehicle.Effort, vehicle.Radius);
        float load = (float)Math.Clamp(vehicle.Effort, 0, 1);
        voice.Engine?.Advance(vehicle.Speed, vehicle.MotorState, delta);
        Span<float> gains = stackalloc float[] { .10f + .12f * load, .13f * mix.Rolling, .035f * mix.Curve, .16f * Math.Min(1, (float)Math.Abs(vehicle.Speed) / 2) };
        Span<float> pitches = stackalloc float[] { mix.EnginePitch, .75f + .5f * mix.Rolling, 1, (float)Math.Clamp(Math.Abs(vehicle.Speed) / 4, .35, 3) };
        if (voice.Engine is { } engine) { gains[0] = engine.Gain; pitches[0] = engine.Pitch; }
        pitches[1] *= voice.WheelPitch;
        float smoothing = 1 - MathF.Exp(-(float)delta * 7);
        for (int i = 0; i < voice.Layers.Length; i++)
        {
            var player = voice.Layers[i]; player.GlobalPosition = vehicle.Position + Vector3.Up;
            float category = i == 1 ? Settings.Wheels : i == 2 ? Settings.Curves : Settings.Engines;
            float gain = Settings.Muted ? 0 : gains[i] * Math.Clamp(MasterVolume, 0, 1) * Math.Clamp(category, 0, 1);
            float db = gain <= .0001 ? -80 : Mathf.LinearToDb(gain);
            // Profile engines already apply OpenLoco's tick-based ramps; do not smooth them twice.
            player.VolumeDb = i == 0 && voice.Engine != null ? db : Mathf.Lerp(player.VolumeDb, db, smoothing);
            player.PitchScale = i == 0 && voice.Engine != null ? pitches[i] * doppler : Mathf.Lerp(player.PitchScale, pitches[i] * doppler, smoothing);
            if (gain > .0001 && player.Stream != null && !player.Playing)
            { if (DiagnosticEnabled) DiagnosticStarts++; player.Play(i == 1 ? voice.WheelOffset : 0); }
            else if (gain <= .0001 && player.VolumeDb < -65)
            { if (DiagnosticEnabled && player.Playing) DiagnosticStops++; player.Stop(); }
        }
        UpdateChuffs(voice, vehicle, delta, doppler);
    }

    private void UpdateChuffs(Voice voice, TrainAudioVehicle vehicle, double delta, float doppler)
    {
        if (voice.Chuffs.Length == 0) return;
        voice.FallbackTurns += vehicle.Speed * Math.Min(delta, .1) / (Math.Tau * .65);
        int beats = voice.ChuffClock.Advance(double.IsFinite(vehicle.WheelTurns) ? vehicle.WheelTurns : voice.FallbackTurns);
        float gain = Settings.Muted ? 0 : (.05f + .15f * (float)Math.Clamp(vehicle.Effort, 0, 1))
            * Math.Clamp(MasterVolume, 0, 1) * Math.Clamp(Settings.Engines, 0, 1);
        foreach (var player in voice.Chuffs)
        {
            player.GlobalPosition = vehicle.Position + Vector3.Up * 2;
            player.PitchScale = doppler;
            player.VolumeDb = gain <= .0001 ? -80 : Mathf.LinearToDb(gain);
            if (gain <= .0001) player.Stop();
        }
        for (int i = 0; i < beats; i++)
        {
            var player = voice.Chuffs[voice.ChuffIndex];
            voice.ChuffIndex = (voice.ChuffIndex + 1) % voice.Chuffs.Length;
            if (gain > .0001) player.Play();
        }
    }
}
