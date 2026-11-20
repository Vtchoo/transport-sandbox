using Godot;
using System;
using System.IO;

public partial class TerrainBackend
{
    private static byte[] EncodeChunk(ChunkData data)
    {
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        writer.Write(data.Segments); writer.Write(data.Vertices.Length); writer.Write(data.Indices.Length);
        for (int i = 0; i < data.Vertices.Length; i++)
        {
            var v = data.Vertices[i]; var n = data.Normals[i]; var c = data.Colors[i];
            writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); writer.Write(n.X); writer.Write(n.Y); writer.Write(n.Z);
            writer.Write(c.R); writer.Write(c.G); writer.Write(c.B); writer.Write(c.A);
        }
        foreach (int index in data.Indices) writer.Write(index);
        return bytes.ToArray();
    }
    private static ChunkData DecodeChunk(byte[] bytes, long revision)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        int segments = reader.ReadInt32(), count = reader.ReadInt32(), indicesCount = reader.ReadInt32();
        if (count < 0 || indicesCount < 0 || 12L + count * 40L + indicesCount * 4L != bytes.Length)
            throw new InvalidDataException("Invalid terrain mesh record");
        var vertices = new Vector3[count]; var normals = new Vector3[count]; var colors = new Color[count]; var indices = new int[indicesCount];
        for (int i = 0; i < count; i++)
        {
            vertices[i] = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            normals[i] = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            colors[i] = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
        for (int i = 0; i < indices.Length; i++) indices[i] = reader.ReadInt32();
        return new(segments, vertices, normals, colors, indices, Array.Empty<float>(), revision);
    }
}
