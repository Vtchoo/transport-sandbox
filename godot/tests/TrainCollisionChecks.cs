using Godot;
using System;
using System.Collections.Generic;

namespace TransportSandbox.Tests;

public static class TrainCollisionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var random = new Random(78239); var catalog = VehicleCatalog.Load();
        var fixtures = new List<(TrainConsist Train, VehiclePose[] Poses)>();
        var index = new TrainCollisionIndex();
        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
        VehiclePose Pose(Vector3 center)
        {
            var forward = new Vector3(Range(-1, 1), Range(-.3f, .3f), Range(-1, 1)).Normalized();
            return new(center, center + forward * 10, center - forward * 10, forward, forward, Array.Empty<VehiclePose>());
        }
        for (int t = 0; t < 8; t++)
        {
            var train = new TrainConsist { Id = t + 1 }; var poses = new VehiclePose[5];
            for (int i = 0; i < poses.Length; i++)
            {
                train.Items.Add(new(i + 1, catalog.Get(i % 2 == 0 ? "atsf-f7a" : "atsf-coach")));
                poses[i] = Pose(new((t - 4) * 64, Range(-10, 10), i * 64 - 128));
            }
            fixtures.Add((train, poses));
        }
        bool Original(Vector3 point, double width, int ignore)
        {
            foreach (var (train, poses) in fixtures)
                if (train.Id != ignore)
                    for (int i = 0; i < poses.Length; i++)
                    {
                        var d = train.Items[i].Definition; var delta = poses[i].Basis.Inverse() * (point - poses[i].BodyCenter);
                        if (Math.Abs(delta.X) < (d.Width + width) / 2 && Math.Abs(delta.Y) < d.Height && Math.Abs(delta.Z) < d.Length / 2 + 5) return true;
                    }
            return false;
        }
        index.Begin(); foreach (var (train, poses) in fixtures) index.Add(train, poses);
        int mismatch = 0;
        for (int phase = 0; phase < 3; phase++)
        {
            for (int i = 0; i < 6000; i++)
            {
                var fixture = fixtures[random.Next(fixtures.Count)]; int car = random.Next(fixture.Poses.Length);
                var pose = fixture.Poses[car]; var d = fixture.Train.Items[car].Definition;
                double width = i % 2 == 0 ? 3 : 7;
                var local = new Vector3(Range(-8, 8), Range(-7, 7), Range(-25, 25));
                // Probe strict envelope edges and immediately adjacent floating-point values too.
                if (i % 4 == 0) local.X = (float)((d.Width + width) / 2) + (i % 3 - 1) * .0001f;
                var point = pose.BodyCenter + pose.Basis * local; int ignore = random.Next(10);
                if (index.Blocked(point, width, ignore) != Original(point, width, ignore)) mismatch++;
            }
            var changed = fixtures[phase];
            for (int i = 0; i < changed.Poses.Length; i++) changed.Poses[i] = Pose(changed.Poses[i].Center + new Vector3(-1024, 40, 64));
            index.Update(changed.Train, changed.Poses);
        }
        check(mismatch == 0, "Spatial collision lookup matches 18,000 brute-force checks across rotations, grades, overpasses, boundaries and in-step movement");
        index.Begin(); index.Add(fixtures[0].Train, fixtures[0].Poses);
        check(!index.Blocked(fixtures[0].Poses[0].BodyCenter, 10, fixtures[0].Train.Id), "A rebuilt index drops removed trains and excludes the querying train");
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) index.Blocked(new(-1024, 40, 64), 3, 0);
        check(GC.GetAllocatedBytesForCurrentThread() == allocated, "Warm spatial point queries allocate no managed memory");
    }
}
