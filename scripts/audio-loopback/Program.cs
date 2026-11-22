using System.Diagnostics;
using System.Text.Json;
using NAudio.Wave;

// Capture the output endpoint while the supplied game process runs. No microphone input.
if (args.Length < 2) throw new ArgumentException("Usage: AudioLoopback output-prefix executable [arguments...]");
string prefix = Path.GetFullPath(args[0]); Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);
using var capture = new WasapiLoopbackCapture();
using var writer = new WaveFileWriter(prefix + ".wav", capture.WaveFormat);
var packets = new List<object>(); var clock = Stopwatch.StartNew();
DateTime utc = DateTime.UtcNow; long frames = 0;
capture.DataAvailable += (_, e) =>
{
    writer.Write(e.Buffer, 0, e.BytesRecorded);
    frames += e.BytesRecorded / capture.WaveFormat.BlockAlign;
    packets.Add(new { seconds = clock.Elapsed.TotalSeconds, frames, bytes = e.BytesRecorded });
};
var stopped = new TaskCompletionSource();
capture.RecordingStopped += (_, e) => { if (e.Exception != null) stopped.TrySetException(e.Exception); else stopped.TrySetResult(); };
capture.StartRecording();
var start = new ProcessStartInfo(args[1]) { UseShellExecute = false, CreateNoWindow = true };
foreach (string argument in args.Skip(2)) start.ArgumentList.Add(argument);
double launch = clock.Elapsed.TotalSeconds;
using var game = Process.Start(start)!; await game.WaitForExitAsync();
await Task.Delay(250); capture.StopRecording(); await stopped.Task;
File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(new { utc, launch, rate = capture.WaveFormat.SampleRate,
    channels = capture.WaveFormat.Channels, format = capture.WaveFormat.ToString(), frames, game.ExitCode, packets }, new JsonSerializerOptions { WriteIndented = true }));
return game.ExitCode;
