using Godot;
using System;
using System.Linq;

namespace TransportSandbox;

/// <summary>Metadata-driven wheel rotation and slider-crank linkages, advanced only by signed travel in metres.</summary>
public sealed class DistanceRunningGear
{
    private sealed record Wheel(Node3D Node, Vector3 Origin, double Radius, double Phase, Vector3 Axis, Basis Rest);
    private sealed record Rod(Node3D Node, string Kind, Vector3 Crank, double DriverRadius, double Phase, float Throw, float Length, float SlideDirection);
    private readonly Wheel[] _wheels;
    private readonly Rod[] _rods;
    public int WheelCount => _wheels.Length;
    public int RodCount => _rods.Length;
    private static double Number(Node node,string key,double fallback) => node.HasMeta(key)?node.GetMeta(key).AsDouble():fallback;
    public static DistanceRunningGear? TryCreate(Node3D model)
    {
        var nodes=model.FindChildren("*","Node3D",true,false).Cast<Node3D>().ToArray();
        if(!nodes.Any(n=>n.HasMeta("wheel_radius")||n.HasMeta("rod_kind")))return null;
        return new(nodes);
    }
    private DistanceRunningGear(Node3D[] nodes)
    {
        _wheels=nodes.Where(n=>n.HasMeta("wheel_radius")).Select(n=>
        {
            double radius=Number(n,"wheel_radius",0),phase=Number(n,"wheel_phase",0);
            var axis=n.HasMeta("wheel_axis")?n.GetMeta("wheel_axis").AsVector3():Vector3.Right;
            var rest=n.HasMeta("wheel_rest_rotation")?n.GetMeta("wheel_rest_rotation").AsVector3():Vector3.Zero;
            if(!double.IsFinite(radius)||radius<=0||!double.IsFinite(phase)||!axis.IsFinite()||axis.LengthSquared()<1e-8||!rest.IsFinite())
                throw new ArgumentException("Invalid wheel animation metadata: "+n.Name);
            return new Wheel(n,n.Position,radius,phase,axis.Normalized(),Basis.FromEuler(rest));
        }).ToArray();
        _rods=nodes.Where(n=>n.HasMeta("rod_kind")).Select(n=>
        {
            string kind=n.GetMeta("rod_kind").AsString();
            var crank=n.GetMeta("crank_origin").AsVector3();
            double radius=Number(n,"driver_radius",0),phase=Number(n,"crank_phase",0),stroke=Number(n,"crank_radius",0),length=Number(n,"rod_length",0),direction=Number(n,"slider_direction",1);
            if(kind is not ("coupling" or "connecting" or "crosshead")||!crank.IsFinite()||!double.IsFinite(radius)||radius<=0||!double.IsFinite(phase)||!double.IsFinite(stroke)||stroke<=0||
                !double.IsFinite(length)||length<=stroke||direction is not (1 or -1))throw new ArgumentException("Invalid rod animation metadata: "+n.Name);
            return new Rod(n,kind,crank,radius,phase,(float)stroke,(float)length,(float)direction);
        }).ToArray();
        ApplyDistance(0);
    }
    public void ApplyDistance(double distance)
    {
        if(!double.IsFinite(distance))throw new ArgumentException("Running gear travel must be finite.");
        foreach(var wheel in _wheels)
        {
            double angle=distance/wheel.Radius+Math.Tau*wheel.Phase;
            wheel.Node.Position=wheel.Origin;
            wheel.Node.Basis=new Basis(wheel.Axis,(float)-Math.IEEERemainder(angle,Math.Tau))*wheel.Rest;
        }
        for(int i=0;i<_rods.Length;i++)
        {
            var rod=_rods[i];double angle=distance/rod.DriverRadius+Math.Tau*rod.Phase;
            float dy=rod.Throw*(float)Math.Sin(angle),dz=rod.Throw*(float)Math.Cos(angle);
            var crank=rod.Crank+new Vector3(0,dy,dz);
            var cross=new Vector3(crank.X,rod.Crank.Y,crank.Z+rod.SlideDirection*Mathf.Sqrt(rod.Length*rod.Length-dy*dy));
            if(rod.Kind=="coupling")rod.Node.Position=crank;
            else if(rod.Kind=="crosshead")rod.Node.Position=cross;
            else
            {
                var delta=cross-crank;rod.Node.Position=(cross+crank)/2;
                rod.Node.Basis=TrackRenderer.Frame(delta)*Basis.FromScale(new(1,1,delta.Length()));
            }
        }
    }
}
