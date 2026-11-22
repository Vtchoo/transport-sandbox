using Godot;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TransportSandbox.Tests;

/// <summary>Opt-in float WAV of the game's master bus; captures never run during ordinary gameplay.</summary>
internal sealed class AudioProfileCapture : IDisposable
{
    private readonly AudioEffectCapture _effect = new() { BufferLength = 2 };
    private readonly FileStream _file;
    private readonly int _slot;
    private readonly int _rate = (int)AudioServer.GetMixRate();
    public long Frames { get; private set; }
    public long Discarded => _effect.GetDiscardedFrames();
    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    public AudioProfileCapture(string path)
    {
        _file = File.Create(path); _file.Position = 44;
        _slot = AudioServer.GetBusEffectCount(0); AudioServer.AddBusEffect(0, _effect);
    }
    public void Pump()
    {
        int count = _effect.GetFramesAvailable();
        if (count == 0) return;
        var frames = _effect.GetBuffer(count);
        _file.Write(MemoryMarshal.AsBytes(frames.AsSpan())); Frames += frames.Length;
    }
    public void Dispose()
    {
        Pump(); AudioServer.RemoveBusEffect(0, _slot);
        _file.Position = 0;
        using var header = new BinaryWriter(_file);
        header.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); header.Write((int)(Frames * 8 + 36));
        header.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); header.Write(16); header.Write((short)3); header.Write((short)2);
        header.Write(_rate); header.Write(_rate * 8); header.Write((short)8); header.Write((short)32);
        header.Write(System.Text.Encoding.ASCII.GetBytes("data")); header.Write((int)(Frames * 8));
        _effect.Dispose();
    }
}
