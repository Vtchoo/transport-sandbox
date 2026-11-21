using System;
using System.Linq;

namespace TransportSandbox;

/// <summary>Audio-only visit latch; signal stops cannot trigger departure horns.</summary>
public sealed class TrainStationAudioState
{
    private int _approach;
    private bool _departing;
    private bool _initialized;
    public void Initialize(int station, double speed)
    {
        if (_initialized) return;
        _initialized = true;
        // A newly placed or loaded train may already be waiting at a platform.
        if (station != 0 && Math.Abs(speed) < .1) Arrive(station);
    }
    public bool Approach(int station, double distance, double speed)
    {
        if (station == 0 || _departing || _approach == station || Math.Abs(speed) < .5 || distance < 0 || distance > Math.Max(60, Math.Abs(speed) * 5)) return false;
        _approach = station;
        return true;
    }
    public void Arrive(int station) { _approach = station; _departing = true; }
    public bool Depart(double speed)
    {
        if (!_departing || Math.Abs(speed) < .5) return false;
        _departing = false; _approach = 0;
        return true;
    }
}

public partial class TrainEditor
{
    private void StationHorn(ParkedTrain parked, TrainConsist train, bool departure = false)
    {
        // Use the locomotive nearest the leading end, including reverse-running consists.
        var indices = Enumerable.Range(0, Math.Min(train.Items.Count, parked.Poses.Length));
        if (train.TravelDirection < 0) indices = indices.Reverse();
        foreach (int i in indices)
        {
            var item = train.Items[i];
            if (item.Definition.Kind != "locomotive") continue;
            Audio.PlayHorn(new(item.Id, item.Definition.Id, parked.Poses[i].BodyCenter, train.Speed, parked.Effort, double.PositiveInfinity), Camera, departure);
            break;
        }
    }
}
