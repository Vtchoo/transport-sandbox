using Godot;
using System;

namespace TransportSandbox;

public sealed record RigidSupports(double Front, double Rear);
/// <summary>A named model part either follows the track or rotates about a body-local hinge.</summary>
public sealed record ArticulatedPartDefinition(string Node, Vector3? Pivot, double Station, double[] AxleStations);
public sealed record ArticulatedPartPose(string Node, Vector3 Position, Vector3 Direction);

public static class VehicleArticulation
{
    public static ArticulatedPartPose[]? Sample(VehicleDefinition definition, Vector3 origin, Basis body,
        TrainPosition position, Func<TrainPosition,double,TrainSample> sample)
    {
        if(definition.ArticulatedParts.Count==0)return null;
        var result=new ArticulatedPartPose[definition.ArticulatedParts.Count];
        for(int i=0;i<result.Length;i++)
        {
            var part=definition.ArticulatedParts[i];
            if(part.Pivot is not { } pivot)
            {
                var point=sample(position,part.Station);
                result[i]=new(part.Node,point.Point,point.Tangent);
                continue;
            }
            var hinge=origin+body*pivot;
            // Minimize sum |hinge + direction*d_i - track_i|² for a fixed hinge.
            // Signed distances also support wheels behind or straddling the pivot.
            var direction=Vector3.Zero;
            foreach(double station in part.AxleStations)
                direction+=(sample(position,station).Point-hinge)*(float)(station-pivot.Z);
            if(direction.LengthSquared()<1e-10f)direction=body.Z;
            result[i]=new(part.Node,hinge,direction.Normalized());
        }
        return result;
    }
}
