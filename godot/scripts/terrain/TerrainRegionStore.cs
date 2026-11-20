using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TransportSandbox;

/// <summary>Worker-only disk cache. Append-complete records survive interrupted writes; indices are bounded.</summary>
public sealed class TerrainRegionStore
{
    public const int RegionSize = 32;
    private const int Magic = 0x54535247, Version = 1, Complete = 0x43484E4B, MaxPayload = 128 * 1024 * 1024;
    private sealed record Entry(long Offset, int Length, byte[] Fingerprint, byte[] Digest);
    private sealed class Region
    {
        public readonly Dictionary<int, Entry> Entries = new();
        public long End = 8;
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, Region> _regions = new();
    private readonly Queue<string> _lru = new();
    private readonly string _root, _identity;
    public long Hits { get; private set; }
    public long Misses { get; private set; }
    public int CachedRegions { get { lock (_gate) return _regions.Count; } }
    public TerrainRegionStore(string worldDirectory, WorldSettings settings)
    {
        _root = Path.Combine(worldDirectory, "regions");
        // Bump this generator/cache identity when changing terrain/vegetation geometry algorithms.
        _identity = "terrain-v1-mesh-v1-vegetation-v2:" + System.Text.Json.JsonSerializer.Serialize(settings);
        Directory.CreateDirectory(_root);
    }
    private (string Path, int Slot) Address(int x, int z, int lod)
    {
        int rx = (int)Math.Floor(x / 32.0), rz = (int)Math.Floor(z / 32.0);
        int layer = lod switch { 0 => 0, 16 => 1, 32 => 2, 64 => 3, 128 => 4, _ => throw new ArgumentOutOfRangeException(nameof(lod)) };
        return (Path.Combine(_root, $"r.{rx}.{rz}.tsregion"), ((z - rz * 32) * 32 + x - rx * 32) * 5 + layer);
    }
    private byte[] Fingerprint(string signature) => SHA256.HashData(Encoding.UTF8.GetBytes(_identity + ":" + signature));
    private Region Open(string path)
    {
        if (_regions.TryGetValue(path, out var cached)) return cached;
        var region = new Region();
        if (File.Exists(path))
        {
            using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream);
            if (stream.Length < 8 || reader.ReadInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("Unsupported terrain region: " + path);
            while (stream.Length - stream.Position >= 76)
            {
                long start = stream.Position;
                int slot = reader.ReadInt32(), length = reader.ReadInt32();
                if (slot < 0 || slot >= 5120 || length < 0 || length > MaxPayload || stream.Length - stream.Position < 68L + length) break;
                var fingerprint = reader.ReadBytes(32); var digest = reader.ReadBytes(32);
                stream.Position += length;
                if (reader.ReadInt32() != Complete) break;
                region.Entries[slot] = new(start, length, fingerprint, digest); region.End = stream.Position;
            }
        }
        while (_regions.Count >= 4 && _lru.TryDequeue(out var oldest)) _regions.Remove(oldest);
        _regions[path] = region; _lru.Enqueue(path); return region;
    }
    public byte[]? Read(int x, int z, int lod, string signature)
    {
        lock (_gate)
        {
            var address = Address(x, z, lod); var region = Open(address.Path);
            if (!region.Entries.TryGetValue(address.Slot, out var entry) || !entry.Fingerprint.SequenceEqual(Fingerprint(signature)))
            { Misses++; return null; }
            using var stream = File.OpenRead(address.Path); stream.Position = entry.Offset + 72;
            byte[] packed = new byte[entry.Length]; stream.ReadExactly(packed);
            if (!SHA256.HashData(packed).SequenceEqual(entry.Digest)) { Misses++; return null; }
            try
            {
                using var input = new MemoryStream(packed); using var zip = new BrotliStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream(); var buffer = new byte[65536]; int count;
                while ((count = zip.Read(buffer)) > 0)
                { if (output.Length + count > MaxPayload * 2L) throw new InvalidDataException("Terrain record exceeds size limit"); output.Write(buffer, 0, count); }
                Hits++; return output.ToArray();
            }
            catch (InvalidDataException) { Misses++; return null; }
        }
    }
    public void Write(int x, int z, int lod, string signature, byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var zip = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true)) zip.Write(bytes);
        var packed = output.ToArray();
        if (packed.Length > MaxPayload) throw new InvalidDataException("Terrain chunk exceeds size limit");
        lock (_gate)
        {
            var address = Address(x, z, lod); var region = Open(address.Path);
            var fingerprint = Fingerprint(signature); var digest = SHA256.HashData(packed);
            using (var stream = new FileStream(address.Path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                if (stream.Length == 0) { writer.Write(Magic); writer.Write(Version); }
                stream.SetLength(region.End); stream.Position = region.End;
                writer.Write(address.Slot); writer.Write(packed.Length); writer.Write(fingerprint); writer.Write(digest);
                writer.Write(packed); writer.Write(Complete); writer.Flush(); stream.Flush(true);
                region.Entries[address.Slot] = new(region.End, packed.Length, fingerprint, digest); region.End = stream.Position;
            }
            long live = 8 + region.Entries.Values.Sum(e => 76L + e.Length);
            if (region.End > 64L * 1024 * 1024 && region.End > live * 2) Compact(address.Path, region);
        }
    }
    private static void Compact(string path, Region region)
    {
        string temporary = path + ".compact";
        var replaced = new Dictionary<int, Entry>();
        using (var source = File.OpenRead(path))
        using (var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(target))
        {
            writer.Write(Magic); writer.Write(Version);
            foreach (var (slot, entry) in region.Entries.OrderBy(pair => pair.Key))
            {
                replaced[slot] = entry with { Offset = target.Position }; source.Position = entry.Offset;
                byte[] record = new byte[entry.Length + 76]; source.ReadExactly(record); writer.Write(record);
            }
            writer.Flush(); target.Flush(true);
        }
        File.Move(temporary, path, overwrite: true);
        region.Entries.Clear(); foreach (var pair in replaced) region.Entries.Add(pair.Key, pair.Value);
        region.End = new FileInfo(path).Length;
    }
}
