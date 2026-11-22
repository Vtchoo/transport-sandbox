using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class TrainAudioChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node root)
    {
        EngineAudioChecks.Run(check);
        FollowingHorn(check, root);
        var source = new Vector3(0, 0, 30);
        var clock = new SteamChuffClock();
        check(clock.Advance(0) == 0 && clock.Advance(.24) == 0 && clock.Advance(.25) == 1 && clock.Advance(1) == 3,
            "Four chuffs follow exact driver quarter-turns");
        check(clock.Advance(1) == 0 && clock.Advance(0) == 4, "Stopped drivers produce no chuffs and reverse travel still produces four per revolution");
        check(clock.Advance(100) == 0 && clock.Advance(100.25) == 1, "Relocation does not replay accumulated exhaust beats");
        check(TrainAudio.Doppler(source, Vector3.Zero, new(0, 0, -20), Vector3.Zero) > 1, "Approaching source raises pitch");
        check(TrainAudio.Doppler(source, Vector3.Zero, new(0, 0, 20), Vector3.Zero) < 1, "Receding source lowers pitch");
        check(TrainAudio.Doppler(source, Vector3.Zero, Vector3.Zero, new(0, 0, 20)) > 1, "Listener motion toward source raises pitch");
        check(Math.Abs(TrainAudio.Doppler(source, Vector3.Zero, new(0, 0, 20), new(0, 0, 20)) - 1) < .00001, "Following at matching velocity leaves pitch unchanged");
        var idle = TrainAudio.Mix(0, 0, 20); var straight = TrainAudio.Mix(20, 1, double.PositiveInfinity); var curve = TrainAudio.Mix(20, 1, 80);
        check(idle.Rolling == 0 && idle.Curve == 0, "Stationary wheels and flange whine are silent");
        check(straight.Curve == 0 && curve.Curve > 0, "Squeal requires a moving train on a tight curve");
        var visit = new TrainStationAudioState();
        check(!visit.Approach(1, 500, 10) && visit.Approach(1, 40, 10) && !visit.Approach(1, 20, 10), "Arrival horn triggers once near the station");
        check(!visit.Depart(10), "An ordinary signal stop does not create a station departure");
        visit.Arrive(1);
        check(!visit.Depart(0) && visit.Depart(1) && !visit.Depart(2), "Departure triggers once after a station stop starts moving");
        check(visit.Approach(1, 40, 10), "Returning to the same station allows a new arrival horn");
        var loadedVisit = new TrainStationAudioState(); loadedVisit.Initialize(2, 0);
        check(loadedVisit.Depart(1), "A train loaded or placed at a station can sound its departure horn");
        var camera = new Camera3D(); root.AddChild(camera);
        var audio = new TrainAudio(); root.AddChild(audio);
        check(audio.MasterVolume == 1, "Train mixer defaults to one hundred percent");
        var vehicles = Enumerable.Range(0, 40).Select(i => new TrainAudioVehicle(i, i % 2 == 0 ? "atsf-f7a" : "drgw-consolidation", new(0, 0, 10 + i), 15, .6, 70, 0)).ToArray();
        audio.UpdateSounds(vehicles, camera, .25);
        check(audio.ActiveVehicles == TrainAudio.MaxVehicles, "Nearby voice budget is bounded");
        var players = audio.GetChildren().OfType<AudioStreamPlayer3D>().ToArray();
        check(players.Where((_, i) => i % 4 == 1).Select(p => p.PitchScale).Distinct().Count() > 1,
            "Identical-speed wheel loops are detuned independently rather than synchronized");
        var wheels = players.Where((_, i) => i % 4 == 1).ToArray();
        check(wheels.All(p => p.Stream.ResourcePath.EndsWith("wheels-freight-1.wav") || p.Stream.ResourcePath.EndsWith("wheels-freight-2.wav")), "All wheels use non-puffing freight recordings");
        var chuffs = audio.GetChildren().OfType<Node3D>().SelectMany(n => n.GetChildren().OfType<AudioStreamPlayer3D>()).ToArray();
        check(chuffs.Length == 32 && chuffs.All(p => p.Stream is AudioStreamWav w && w.LoopMode == AudioStreamWav.LoopModeEnum.Disabled), "Each audible steam locomotive has four extracted one-shot chuffs");
        vehicles = vehicles.Select(v => v with { WheelTurns = .25 }).ToArray();
        audio.UpdateSounds(vehicles, camera, .016);
        check(chuffs.Where((_, i) => i % 4 == 0).All(p => p.Playing) && chuffs.Where((_, i) => i % 4 != 0).All(p => !p.Playing), "First quarter-turn plays only the A chuff");
        vehicles = vehicles.Select(v => v with { WheelTurns = .5 }).ToArray();
        audio.UpdateSounds(vehicles, camera, .016);
        check(chuffs.Where((_, i) => i % 4 == 1).All(p => p.Playing), "Next quarter-turn advances to the B chuff");
        var wheelPaths = wheels.Select(p => p.Stream.ResourcePath).ToArray();
        check(players.Where(p => p.Stream != null).All(p => p.Stream is AudioStreamWav w && !w.Stereo && w.LoopMode == AudioStreamWav.LoopModeEnum.Forward), "Spatial test assets load as mono looping WAV");
        check(players.Count(p => p.Stream != null) > 40, "Steam, diesel, wheel and curve layers are loaded");
        audio.DepartureHornChance = 0;
        check(!audio.PlayHorn(vehicles[0], camera, departure: true), "Zero departure chance suppresses horns");
        audio.DepartureHornChance = 1;
        check(audio.PlayHorn(vehicles[0], camera, departure: true) && !audio.PlayHorn(vehicles[0], camera), "A departure horn plays once without overlapping itself");
        var hornPlayer = audio.GetChildren().OfType<AudioStreamPlayer3D>().Last();
        check(hornPlayer.Stream is AudioStreamWav horn && horn.LoopMode == AudioStreamWav.LoopModeEnum.Disabled, "Horn samples play once even when source objects were looping");
        var allEmitters = players.Concat(chuffs).Append(hornPlayer).ToArray();
        var positions = allEmitters.Select(p => p.GlobalPosition).ToArray();
        var playing = allEmitters.Select(p => p.Playing).ToArray();
        var pitches = allEmitters.Select(p => p.PitchScale).ToArray();
        var shift = new TrackVector(512, 0, -512);
        audio.Rebase(shift); camera.Position -= shift.Float();
        check(allEmitters.Select((p, i) => p.GlobalPosition.DistanceTo(positions[i] - shift.Float()) < .001).All(v => v),
            "Origin shift immediately relocates engine, wheel, curve, steam and horn emitters with the listener");
        check(allEmitters.Select((p, i) => p.Playing == playing[i] && p.PitchScale == pitches[i]).All(v => v),
            "Origin shift preserves playing loops, one-shots and pitch without restarting audio");
        vehicles = vehicles.Select(v => v with { Position = v.Position - shift.Float() }).ToArray();
        audio.UpdateSounds(vehicles, camera, .016);
        check(audio.ActiveVehicles == TrainAudio.MaxVehicles, "Rebased voices remain in range on the following update");
        audio.Rebase(shift * -1); camera.Position += shift.Float();
        vehicles = vehicles.Select(v => v with { Position = v.Position + shift.Float() }).ToArray();
        audio.ResetMotion(); camera.Position = new(0, 0, 20);
        audio.UpdateSounds(vehicles, camera, .016);
        check(players.All(p => float.IsFinite(p.PitchScale) && p.PitchScale > 0), "Motion reset keeps valid pitches");
        audio.Settings.Muted = true;
        audio.UpdateSounds(vehicles, camera, 1);
        check(players.All(p => !p.Playing), "Mute silences already-playing layers");
        check(chuffs.All(p => !p.Playing), "Mute stops steam one-shots as well");
        audio.UpdateSounds(Array.Empty<TrainAudioVehicle>(), camera, .016);
        check(audio.ActiveVehicles == 0 && players.All(p => !p.Playing), "Removing trains stops their sounds immediately");
        check(!hornPlayer.Playing, "Removing its vehicle stops the horn as well");
        await frame();
        camera.Position = Vector3.Zero; audio.ResetMotion();
        audio.UpdateSounds(vehicles, camera, .25);
        var restoredPaths = audio.GetChildren().OfType<AudioStreamPlayer3D>().Where((_, i) => i % 4 == 1).Select(p => p.Stream.ResourcePath);
        check(restoredPaths.SequenceEqual(wheelPaths), "Vehicles retain their chosen wheel recording after voice culling");
        audio.QueueFree(); camera.QueueFree(); await frame(); await frame();
    }
    private static void FollowingHorn(Action<bool, string> check, Node root)
    {
        var camera = new Camera3D(); root.AddChild(camera);
        var audio = new TrainAudio(); root.AddChild(audio);
        try
        {
            var vehicle = new TrainAudioVehicle(9999, "tgv-sud-est-power", new(0, 0, 30), 60, .8, double.PositiveInfinity);
            audio.UpdateSounds(new[] { vehicle }, camera, .02);
            var cadences = new[] { 1.0 / 144, 1.0 / 55, 1.0 / 90, 1.0 / 30 };
            AudioStreamPlayer3D? horn = null;
            for (int i = 0; i < 24; i++)
            {
                double dt = cadences[i % cadences.Length];
                var travel = new Vector3(0, 0, (float)(60 * dt));
                camera.Position += travel; vehicle = vehicle with { Position = vehicle.Position + travel };
                if (i == 0)
                {
                    check(audio.PlayHorn(vehicle, camera), "Moving TGV can begin a station horn with existing emitter motion history");
                    horn = audio.GetChildren().OfType<AudioStreamPlayer3D>().Last();
                }
                if (i == 12)
                {
                    var shift = new TrackVector(512, 0, -512);
                    audio.Rebase(shift); camera.Position -= shift.Float(); vehicle = vehicle with { Position = vehicle.Position - shift.Float() };
                }
                audio.UpdateSounds(new[] { vehicle }, camera, dt);
                check(Math.Abs(horn!.PitchScale - 1) < .0001, "Following horn retains neutral Doppler from its first update across variable frame cadence and origin shifts");
            }
            vehicle = vehicle with { Position = vehicle.Position + new Vector3(0, 0, 1.2f) };
            audio.UpdateSounds(new[] { vehicle }, camera, .02);
            check(horn!.PitchScale < .99, "Stopping the listener retains genuine Doppler for a receding train");
        }
        finally { audio.Free(); camera.Free(); }
    }
}
