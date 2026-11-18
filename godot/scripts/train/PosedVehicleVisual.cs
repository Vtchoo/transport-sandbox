using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

/// <summary>Applies the browser's independent body/bogie/rigid-section poses to named imported parts.</summary>
public partial class PosedVehicleVisual : Node3D
{
    public VehicleDefinition Definition { get; }
    public VehicleVisual Vehicle { get; }
    public VehiclePose? Pose { get; private set; }
    public int Gauge { get; }
    private readonly List<StandardMaterial3D> _ghostMaterials = new();
    private readonly Dictionary<ulong, StandardMaterial3D> _ghostMaterialCache = new();
    public override void _ExitTree()
    {
        foreach (var material in _ghostMaterials) material.Dispose();
        _ghostMaterials.Clear(); _ghostMaterialCache.Clear();
    }
    public PosedVehicleVisual(VehicleDefinition definition, int gauge, bool preview = false)
    {
        Definition = definition; Gauge = gauge;
        Vehicle = new(definition, gauge);
        AddChild(Vehicle);
        if (preview) TintParts(Vehicle);
    }
    private void TintParts(Node node)
    {
        if (node is MeshInstance3D mesh)
            for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
            {
                var original = mesh.GetActiveMaterial(i) as StandardMaterial3D;
                ulong id = original?.GetInstanceId() ?? 0;
                if (!_ghostMaterialCache.TryGetValue(id, out var material))
                {
                    material = original != null ? (StandardMaterial3D)original.Duplicate() : new StandardMaterial3D();
                    material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                    material.AlbedoColor = material.AlbedoColor with { A = Math.Min(material.AlbedoColor.A, .42f) };
                    material.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;
                    material.EmissionEnabled = true;
                    _ghostMaterialCache[id] = material;
                    _ghostMaterials.Add(material);
                }
                mesh.SetSurfaceOverrideMaterial(i, material);
                mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            }
        foreach (var child in node.GetChildren()) TintParts(child);
    }
    public void Tint(bool valid)
    {
        foreach (var material in _ghostMaterials)
        {
            material.Emission = new(valid ? "143d23" : "861817");
            material.EmissionEnergyMultiplier = valid ? .22f : .65f;
        }
    }
    private readonly Dictionary<string, Node3D> _parts = new();
    private Node3D Part(string name)
    {
        if (!_parts.TryGetValue(name, out var part))
            _parts[name] = part = Vehicle.Model.FindChild(name, true, false) as Node3D ?? throw new InvalidOperationException("Missing imported model part: " + name);
        return part;
    }
    public void Apply(VehiclePose pose)
    {
        Pose = pose;
        Position = pose.Center;
        ApplyParts(Definition, pose, "");
    }
    private void ApplyParts(VehicleDefinition definition, VehiclePose pose, string prefix)
    {
        Part(prefix + "body").GlobalTransform = new(pose.Basis, pose.BodyCenter);
        Part(prefix + "bogie_0").GlobalTransform = new(TrackRenderer.Frame(pose.FrontDirection), pose.Front);
        Part(prefix + "bogie_1").GlobalTransform = new(TrackRenderer.Frame(pose.RearDirection), pose.Rear);
        for (int i = 0; i < pose.Sections.Length; i++) ApplyParts(definition.Sections[i].Definition, pose.Sections[i], $"{prefix}section_{i}_");
    }
    public IEnumerable<ExhaustOutlet> Exhausts()
    {
        var body = Part(Definition.Sections.Count == 0 ? "body" : "section_0_body");
        foreach (var outlet in Definition.Exhausts)
        {
            var local = outlet.Position;
            if (Definition.Sections.Count > 0) local.Z -= (float)Definition.Sections[0].Offset;
            yield return outlet with { Position = body.GlobalTransform * local };
        }
    }
    private AnimationPlayer[]? _runningGear;
    private double _wheelPhase;
    public void AdvanceRunningGear(double distance)
    {
        if (_runningGear == null)
        {
            _runningGear = Vehicle.Model.FindChildren("*", "AnimationPlayer", true, false).Cast<AnimationPlayer>().ToArray();
            foreach (var player in _runningGear)
            {
                var name = player.GetAnimationList().FirstOrDefault(n => n.EndsWith("running_gear"));
                if (name != null) { player.Play(name); player.Pause(); }
            }
        }
        _wheelPhase = ((_wheelPhase + distance / (Math.Tau * .65)) % 1 + 1) % 1;
        foreach (var player in _runningGear)
            if (!string.IsNullOrEmpty(player.AssignedAnimation)) player.Seek(_wheelPhase, true);
    }
    public static VehiclePose Straight(VehicleDefinition definition, Vector3 center, Vector3 direction, int facing = 1)
    {
        direction *= facing;
        return new(center, center + direction * (float)(definition.BogieCenters / 2), center - direction * (float)(definition.BogieCenters / 2), direction, direction,
            System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(definition.Sections, s => Straight(s.Definition, center + direction * (float)s.Offset, direction))));
    }
}
