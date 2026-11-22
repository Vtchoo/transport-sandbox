using Godot;
using System;

namespace TransportSandbox;

/// <summary>Native SubViewport for catalog orbit previews and first-vehicle train views.</summary>
public partial class VehiclePreview : SubViewportContainer
{
    public SubViewport View = null!;
    public Camera3D Camera = null!;
    private Node3D? _draft;
    private bool _shared;
    private double _time;
    private string _key = "";
    private readonly World3D _privateWorld = new();
    public override void _Ready()
    {
        Stretch = true; CustomMinimumSize = new(0, 180); MouseFilter = MouseFilterEnum.Ignore;
        View = new SubViewport { Size = new(384, 216), World3D = _privateWorld, HandleInputLocally = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        AddChild(View);
        Camera = new Camera3D { Current = true, Fov = 42, Far = 14000, Near = .1f };
        View.AddChild(Camera);
    }
    private void PrivateScene()
    {
        if (_draft != null) { _draft.GetParent()?.RemoveChild(_draft); _draft.QueueFree(); }
        if (_shared) View.World3D = _privateWorld;
        _shared = false;
        _draft = new(); View.AddChild(_draft);
        WorldLighting.AddTo(_draft);
        _draft.GetChild<WorldEnvironment>(0).Environment.BackgroundColor = new("304942");
        Camera.MakeCurrent();
    }
    public void Catalog(VehicleDefinition definition)
    {
        string key = "catalog:" + definition.Id;
        if (_key != key)
        {
            _key = key; _time = 0; PrivateScene();
            _draft!.AddChild(new VehicleVisual(definition, System.Linq.Enumerable.Min(definition.Models.Keys)));
        }
        _time += .04;
        float angle = Mathf.Pi / 3 + (float)_time * .35f, elevation = .25f;
        double radius = Math.Sqrt(definition.Width * definition.Width + definition.Length * definition.Length) / 2, halfHeight = definition.Height / 2;
        double vertical = Mathf.DegToRad(21), horizontal = Math.Atan(Math.Tan(vertical) * Math.Max(1, Size.X) / Math.Max(1, Size.Y));
        float distance = (float)(Math.Max(radius / Math.Sin(horizontal), (radius * Math.Sin(elevation) + halfHeight * Math.Cos(elevation)) / Math.Sin(vertical)) * 1.12);
        var center = new Vector3(0, (float)halfHeight, 0);
        Camera.Position = center + new Vector3(Mathf.Sin(angle) * Mathf.Cos(elevation), Mathf.Sin(elevation), Mathf.Cos(angle) * Mathf.Cos(elevation)) * distance;
        Camera.LookAt(center, Vector3.Up); View.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }
    public void Train(TrainConsist train, ParkedTrain? parked, VehicleCatalog catalog, World3D world)
    {
        if (train.Items.Count == 0) { View.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled; return; }
        Vector3 center;
        if (parked != null)
        {
            if (!_shared) { if (_draft != null) { _draft.GetParent()?.RemoveChild(_draft); _draft.QueueFree(); _draft = null; } View.World3D = world; _shared = true; _key = ""; }
            center = parked.Poses[0].BodyCenter;
        }
        else
        {
            string key = string.Join(",", train.Items.ConvertAll(i => $"{i.Id}:{i.Facing}:{i.Definition.Id}:{i.Livery}:{i.Cargo}"));
            if (_key != key || _shared)
            {
                PrivateScene(); _key = key;
                var offsets = train.Offsets;
                for (int i = 0; i < train.Items.Count; i++)
                {
                    var item = train.Items[i];
                    var visual = new VehicleVisual(item.Definition, System.Linq.Enumerable.Min(item.Definition.Models.Keys), item.Facing == -1, item.Livery) { Position = new(0, 0, (float)offsets[i]) };
                    visual.CargoFill?.SetLoad(item.Cargo);
                    _draft!.AddChild(visual);
                }
            }
            center = new(0, 2, (float)train.Offsets[0]);
        }
        float distance = Math.Max(14, (float)train.Items[0].Definition.Length * .75f);
        Camera.Position = center + new Vector3(distance * .95f, distance * .48f, distance * .45f);
        Camera.LookAt(center, Vector3.Up); View.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }
    public void Suspend() => View.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
    public void Rebase(Vector3 shift)
    {
        if (!_shared) return;
        Camera.Position -= shift;
        if (View.RenderTargetUpdateMode != SubViewport.UpdateMode.Disabled) View.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }
}
