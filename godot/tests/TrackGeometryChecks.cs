using Godot;
using System;
using System.Linq;
using System.Text.Json;

namespace TransportSandbox.Tests;

public static class TrackGeometryChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var reference in JsonData.Read("res://tests/track-geometry-fixtures.json").EnumerateArray())
        {
            string name = reference.GetProperty("name").GetString()!;
            var points = reference.GetProperty("points").EnumerateArray().Select(JsonData.Vector).ToArray();
            bool preview = reference.TryGetProperty("preview", out var p) && p.GetBoolean();
            bool railsOnly = reference.TryGetProperty("railsOnly", out var r) && r.GetBoolean();
            var geometry = new TrackGeometry(points, reference.GetProperty("gauge").GetInt32(), reference.GetProperty("sides").EnumerateArray().Select(s => s.GetInt32()), preview, railsOnly);
            check(Math.Abs(geometry.Curve.Length - reference.GetProperty("length").GetDouble()) < 1e-7, name + ": arc length matches browser");
            check(geometry.Anchor.DistanceTo(JsonData.Vector(reference.GetProperty("anchor"))) < 1e-6, name + ": local origin matches");
            var surfaces = reference.GetProperty("surfaces");
            check(geometry.Surfaces.Count == surfaces.GetArrayLength(), name + ": surface count matches");
            foreach (var source in surfaces.EnumerateArray())
            {
                var surface = geometry.Surfaces.Single(s => s.Name == source.GetProperty("name").GetString());
                var positions = source.GetProperty("positions");
                var normals = source.GetProperty("normals");
                var indices = source.GetProperty("indices");
                check(surface.Vertices.Length * 3 == positions.GetArrayLength() && surface.Indices.Length == indices.GetArrayLength(), name + ": mesh counts match");
                double positionError = 0, normalError = 0;
                for (int i = 0; i < surface.Vertices.Length; i++)
                {
                    var vertex = surface.Vertices[i];
                    var normal = surface.Normals[i];
                    for (int axis = 0; axis < 3; axis++)
                    {
                        positionError = Math.Max(positionError, Math.Abs(vertex[axis] - positions[i * 3 + axis].GetDouble()));
                        normalError = Math.Max(normalError, Math.Abs(normal[axis] - normals[i * 3 + axis].GetDouble()));
                    }
                }
                check(positionError < 1e-5, name + ": every swept vertex matches source (error " + positionError + ")");
                check(normalError < 1e-5, name + ": swept normals match source (error " + normalError + ")");
                bool topology = true;
                for (int i = 0; i < surface.Indices.Length; i += 3)
                    topology &= surface.Indices[i] == indices[i].GetInt32() && surface.Indices[i + 1] == indices[i + 2].GetInt32() && surface.Indices[i + 2] == indices[i + 1].GetInt32();
                check(topology, name + ": topology matches with Godot winding");
            }
            var batches = reference.GetProperty("sleepers");
            check(geometry.Sleepers.Count == batches.GetArrayLength(), name + ": sleeper batches match");
            for (int b = 0; b < geometry.Sleepers.Count; b++)
            {
                var batch = geometry.Sleepers[b];
                var source = batches[b];
                var transforms = source.GetProperty("transforms");
                check(batch.Counts.SequenceEqual(source.GetProperty("counts").EnumerateArray().Select(n => n.GetInt32())), name + ": LOD counts match");
                check(batch.Center.DistanceTo(JsonData.Vector(source.GetProperty("center"))) < 1e-5, name + ": LOD batch center matches");
                double error = 0;
                for (int i = 0; i < batch.Transforms.Length; i++)
                {
                    var actual = batch.Transforms[i];
                    var matrix = transforms[i];
                    Vector3 Column(int column) => new(matrix[column * 4].GetSingle(), matrix[column * 4 + 1].GetSingle(), matrix[column * 4 + 2].GetSingle());
                    error = Math.Max(error, actual.Origin.DistanceTo(Column(3)));
                    error = Math.Max(error, Math.Max(actual.Basis.X.DistanceTo(Column(0)), Math.Max(actual.Basis.Y.DistanceTo(Column(1)), actual.Basis.Z.DistanceTo(Column(2)))));
                    check(actual.Basis.X.Y == 0 && actual.Basis.Y.Y >= 0, name + ": sleepers have no superelevation");
                }
                check(error < 1e-5, name + ": sleeper positions and frames match (error " + error + ")");
            }
            foreach (var lod in reference.GetProperty("lod").EnumerateArray())
                check(geometry.Sleepers[0].Update(lod.GetProperty("distance").GetDouble()) == lod.GetProperty("count").GetInt32(), name + ": sleeper LOD hysteresis matches");
        }
    }
}
