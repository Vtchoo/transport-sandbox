using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TransportSandbox;

public static class WorldSaveStore
{
    public const string SnapshotFile = "world.json.gz";
    private static readonly JsonSerializerOptions Options = new()
    { IncludeFields = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    public static string Slug(string name)
    {
        var text = new StringBuilder();
        foreach (char c in name.Trim().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) text.Append(char.ToLowerInvariant(c));
        string slug = Regex.Replace(text.ToString(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length == 0) slug = "world";
        slug = slug[..Math.Min(slug.Length, 80)].TrimEnd('-');
        if (Regex.IsMatch(slug, "^(con|prn|aux|nul|com[0-9]|lpt[0-9])$")) slug = "world-" + slug;
        return slug;
    }
    public static string CreateWorldDirectory(string root, string name)
    {
        Directory.CreateDirectory(root); string slug = Slug(name), path = Path.Combine(root, slug);
        while (Directory.Exists(path) || File.Exists(path)) { slug += "_"; path = Path.Combine(root, slug); }
        Directory.CreateDirectory(path); return path;
    }
    public static void Write(string directory, GameSave save)
    {
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, SnapshotFile), temporary = target + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using (var compressed = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true)) JsonSerializer.Serialize(compressed, save, Options);
            stream.Flush(true);
        }
        if (File.Exists(target)) File.Replace(temporary, target, Path.Combine(directory, "world.previous.json.gz"));
        else File.Move(temporary, target);
    }
    public static GameSave Read(string directory)
    {
        using var input = File.OpenRead(Path.Combine(directory, SnapshotFile)); using var zip = new GZipStream(input, CompressionMode.Decompress);
        var save = JsonSerializer.Deserialize<GameSave>(zip, Options) ?? throw new InvalidDataException("Empty save file");
        if (save.Version != 1) throw new InvalidDataException($"Unsupported save version {save.Version}");
        Validate(save); return save;
    }
    public static void Validate(GameSave save)
    {
        var nodes = save.Nodes.ToDictionary(n => n.Id); var edges = save.Edges.ToDictionary(e => e.Id);
        var groups = save.Groups.ToDictionary(g => g.Id);
        bool Finite(TrackVector v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
        if (!double.IsFinite(save.Settings.MountainHeight) || !double.IsFinite(save.Settings.HorizontalScale) || save.Settings.HorizontalScale <= 0
            || !double.IsFinite(save.Settings.SeaLevel) || save.Settings.Physics is not ("ho" or "realistic"))
            throw new InvalidDataException("Invalid world settings");
        if (nodes.Keys.Any(id => id < 1) || edges.Keys.Any(id => id < 1) || save.Trains.Select(t => t.Id).Distinct().Count() != save.Trains.Length)
            throw new InvalidDataException("Invalid or duplicate object IDs");
        foreach (var node in save.Nodes)
            if (!Finite(node.Position) || !Finite(node.Axis) || node.Axis.LengthSquared < .001 || !TrackNetwork.Gauges.Contains(node.Gauge)
                || node.Group != 0 && !groups.ContainsKey(node.Group)) throw new InvalidDataException("Invalid saved node");
        foreach (var group in save.Groups)
            if (!nodes.ContainsKey(group.Anchor) || !Finite(group.Origin) || !Finite(group.Axis)
                || group.Offsets.Any(p => !nodes.TryGetValue(p.Key, out var node) || node.Group != group.Id || !float.IsFinite(p.Value)))
                throw new InvalidDataException("Invalid saved parallel group");
        foreach (var edge in save.Edges)
            if (!nodes.ContainsKey(edge.A) || !nodes.ContainsKey(edge.B) || edge.A == edge.B || edge.SideA is not (-1 or 1) || edge.SideB is not (-1 or 1))
                throw new InvalidDataException("Invalid saved track connection");
        foreach (var signal in save.Signals)
            if (!nodes.ContainsKey(signal.Node) || signal.Side is not (-1 or 1)) throw new InvalidDataException("Invalid saved signal");
        foreach (var train in save.Trains)
        {
            if (train.Direction is not (-1 or 1) || !double.IsFinite(train.Speed) || train.Items.Any(i => i.Facing is not (-1 or 1)))
                throw new InvalidDataException("Invalid saved train");
            if (train.Position is { } p && (!double.IsFinite(p.U) || p.U < 0 || p.U > 1 || p.Facing is not (-1 or 1)
                || train.Poses.Length != train.Items.Length || train.Supported && !edges.ContainsKey(p.Edge)))
                throw new InvalidDataException("Invalid saved train placement");
            if (train.Route is { } route && (route.PlanStep is not (-1 or 1) || route.Legs.Any(l => l.Facing is not (-1 or 1))
                || route.Legs.Select(l => l.Index).Distinct().Count() != route.Legs.Length)) throw new InvalidDataException("Invalid saved route history");
        }
    }
}
