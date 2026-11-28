using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class BigBoyChecks
{
    public static async Task Run(Action<bool,string> check,Node parent,Func<Task> frame)
    {
        GenericRunningGear(check);
        var definition=VehicleCatalog.Load().Get(BigBoyModel.Id);
        check(definition.Models.Keys.SequenceEqual(new[]{1435}),"Big Boy is standard gauge only");
        check(definition.Sections.Count==2 && definition.Sections[0].Definition.ArticulatedParts.Count(p=>p.Pivot!=null)==1,"Engine and tender share one purchased item, with a configured hinged front engine");
        check(Math.Abs(definition.Physics.MassKg-539548.123)<1 && definition.Sections.All(s=>s.Definition.Physics.MassKg==0),"Operating mass is counted once, including the loaded tender");
        var stage=new Node3D();parent.AddChild(stage);
        var visual=new PosedVehicleVisual(definition,1435);stage.AddChild(visual);
        var instances=new TrainInstances();stage.AddChild(instances);
        try
        {
            var model=visual.Vehicle.Model;
            Node3D Part(string name)=>model.FindChild(name,true,false) as Node3D ?? throw new Exception(name);
            var wheelsets=model.FindChildren("motion_*","Node3D",true,false).Cast<Node3D>().Where(n=>n.HasMeta("wheel_radius")).ToArray();
            check(wheelsets.Length==19,"4-8-8-4 engine plus seven-axle centipede tender has 19 axles");
            check(wheelsets.Count(n=>n.Name.ToString().StartsWith("motion_driver_"))==8,"Two engines each have four powered axles");
            visual.Apply(PosedVehicleVisual.Straight(definition,Vector3.Zero,Vector3.Back));
            var rear=Part("section_0_body");var front=Part("motion_front_engine");
            check(Part("rear_engine").GetParent()==rear,"Rear engine is a distinct component fixed to the boiler body");
            check(front.GlobalPosition.DistanceTo(rear.GlobalTransform*new Vector3(0,0,BigBoyModel.Hinge))<.001,"Front engine is anchored at its real hinge");
            var initial=Part("motion_driver_front_0").Transform;
            visual.AdvanceRunningGear(1.1);
            check(!Part("motion_driver_front_0").Transform.IsEqualApprox(initial),"Travel animates the wheels");
            check(!Part("motion_driver_front_0").Basis.IsEqualApprox(Part("motion_driver_rear_0").Basis),"Front and rear engines preserve independent crank phases");
            var moved=Part("motion_driver_front_0").Transform;visual.AdvanceRunningGear(0);
            check(Part("motion_driver_front_0").Transform.IsEqualApprox(moved),"Stopped running gear stays stopped");
            visual.AdvanceRunningGear(-1.1);
            check(Part("motion_driver_front_0").Transform.IsEqualApprox(initial),"Reversing signed travel returns to the original crank pose");
            // Reproduce the tight curve that exposed incorrect whole-engine body alignment.
            const double radius=30;
            var network=new TrackNetwork();var a=network.AddNode(new(0,0,-200),1435);var b=network.AddNode(new(0,0,200),1435);var edge=network.ConnectNodes(a,b)!;
            using var placement=new TrainPlacement(network);
            using var genericData=System.Text.Json.JsonDocument.Parse("""
                {"id":"generic-articulated-test","name":"Test chassis","kind":"locomotive",
                 "length":12,"width":2,"height":3,"bogieCenters":4,
                 "physics":{"massKg":10000,"powerW":100000,"tractiveEffortN":10000,"maxSpeedMps":20},
                 "models":{"1435":"unused-test-scene"},"bodySupports":{"front":2,"rear":-1},
                 "articulatedParts":[
                   {"node":"unrelated_rear_frame","pivot":[0,0,1],"axleStations":[-3,-2]},
                   {"node":"unrelated_front_frame","pivot":[0,0,-1],"axleStations":[2,3]},
                   {"node":"unrelated_truck","station":5}]}
                """);
            var generic=new VehicleDefinition(genericData.RootElement);
            var genericStraight=placement.Pose(generic,new(edge.Id,.5));
            check(genericStraight.BodyCenter.DistanceTo(Vector3.Zero)<.001,"Generic asymmetric body supports preserve the authored origin");
            check(genericStraight.Front.DistanceTo(new Vector3(0,0,2))<.001 && genericStraight.Rear.DistanceTo(new Vector3(0,0,-1))<.001,
                "Asymmetric supports preserve actual bogie positions independently of the authored body origin");
            check(genericStraight.ArticulatedParts!.All(p=>p.Direction.Dot(Vector3.Back)>.999),"Generic hinges support wheels ahead of or behind a pivot without reversing the part");
            TrainSample Arc(TrainPosition position,double distance)
            {
                double station=(position.U-.5)*400+distance*position.Facing,angle=station/radius;
                return new(new((float)(radius*(1-Math.Cos(angle))),0,(float)(radius*Math.Sin(angle))),
                    new Vector3((float)Math.Sin(angle),0,(float)Math.Cos(angle))*position.Facing,
                    new(edge.Id,.5+station/400,position.Facing));
            }
            var pose=placement.Pose(definition,new(edge.Id,.5),sample:Arc);visual.Apply(pose);
            var genericCurve=placement.Pose(generic,new(edge.Id,.5),sample:Arc);
            check(genericCurve.ArticulatedParts![0].Position.DistanceTo(genericCurve.BodyCenter+genericCurve.Basis*new Vector3(0,0,1))<.001,
                "An unrelated vehicle uses its configured pivot on curved track");
            check(genericCurve.ArticulatedParts[2].Position.DistanceTo(Arc(new(edge.Id,.5),5).Point)<.001,
                "An unrelated named truck uses its configured track station");
            double RailError(Vector3 point) => Math.Abs(new Vector2(point.X-(float)radius,point.Z).Length()-radius);
            foreach(var axle in wheelsets.Where(n=>n.Name.ToString().StartsWith("motion_driver_")))
                check(RailError(axle.GlobalPosition)<.15,"Powered axle stays within the rigid wheelbase tolerance on a tight curve: "+axle.Name);
            check(rear.GlobalBasis.X.Dot(Part("motion_driver_rear_0").GlobalBasis.X)>.9999,"Rear driver axle axes stay aligned with the boiler");
            check(front.GlobalPosition.DistanceTo(rear.GlobalTransform*new Vector3(0,0,BigBoyModel.Hinge))<.001,"Hinge remains attached on curved track");
            check(front.GlobalBasis.Z.Dot(rear.GlobalBasis.Z)<.9999,"Front engine swivels independently on a curve");
            check(Part("section_1_body").GlobalBasis.Z.Dot(rear.GlobalBasis.Z)<.999,"Tender follows its own curved pose");
            var gear=pose.Sections[0].ArticulatedParts!;
            check(Part("motion_pilot").GlobalPosition.DistanceTo(gear[1].Position)<.001,"Leading truck follows its sampled track location");
            var shifted=TrainEditor.ShiftPose(pose,new TrackVector(512,0,0),new WorldSpace());
            check(shifted.Sections[0].ArticulatedParts![1].Position.DistanceTo(gear[1].Position+new Vector3(512,0,0))<.001,"Origin shifts retain articulated truck and engine targets");
            var blend=TrainEditor.BlendPose(pose,shifted,.5f);
            check(blend.Sections[0].ArticulatedParts![0].Position.DistanceTo(gear[0].Position+new Vector3(256,0,0))<.001,"Render interpolation blends articulation with the rest of the train");
            var backwards=placement.Pose(definition,new(edge.Id,.5),-1,Arc);visual.Apply(backwards);
            check(front.GlobalPosition.DistanceTo(rear.GlobalTransform*new Vector3(0,0,BigBoyModel.Hinge))<.001,"Reversed placement retains the front engine hinge");
            var frontClock=new SteamChuffClock();var rearClock=new SteamChuffClock();int beats=0;
            for(int i=0;i<=100;i++) beats+=frontClock.Advance(i/100d+BigBoyModel.FrontEnginePhase)+rearClock.Advance(i/100d);
            check(beats==8,"Two two-cylinder engines produce eight total exhaust beats per driver revolution");
            visual.Apply(PosedVehicleVisual.Straight(definition,Vector3.Zero,Vector3.Back));
            var meshes=model.FindChildren("*","MeshInstance3D",true,false).Cast<MeshInstance3D>().ToArray();
            var bounds=new Aabb();bool first=true;
            foreach(var mesh in meshes) for(int s=0;s<mesh.Mesh.GetSurfaceCount();s++)
                foreach(var vertex in mesh.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                { var point=mesh.GlobalTransform*vertex;bounds=first?new(point,Vector3.Zero):bounds.Expand(point);first=false; }
            GD.Print($"Big Boy bounds: {bounds}, meshes: {meshes.Length}");
            check(bounds.Position.Y>=-.01 && bounds.End.Y<=definition.Height+.08,"Wheels meet rail level and height stays within the catalog envelope");
            check(bounds.Size.Z<=definition.Length+.15 && bounds.Size.Z>definition.Length-1,"Engine and tender retain metre-scale overall length");
            instances.Sync(new[]{visual});check(instances.BatchCount>10 && meshes.All(m=>!m.Visible),"Articulated body and animated parts use the shared instancer");
            visual.AdvanceRunningGear(.3);instances.Sync(new[]{visual});
            var audio=new TrainAudio();stage.AddChild(audio);
            WorldLighting.AddTo(stage);
            var camera=new Camera3D{Current=true,Position=new(-31,14,34),Fov=43,Far=300};stage.AddChild(camera);camera.LookAt(new(0,2,0));
            check(audio.PlayHorn(new(9001,definition.Id,Vector3.Zero,0,0,double.PositiveInfinity),camera),"Big Boy has a working steam whistle");audio.Settings.Muted=true;
            stage.AddChild(new MeshInstance3D{Mesh=new PlaneMesh{Size=new(150,150)},Position=new(0,-.04f,0),MaterialOverride=new StandardMaterial3D{AlbedoColor=new("748367"),Roughness=1}});
            for(int i=0;i<5;i++)await frame();
            if(DisplayServer.GetName()!="headless")
            {
                await parent.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using(var image=parent.GetViewport().GetTexture().GetImage())image.SavePng("res://.godot/big-boy-preview.png");
                visual.Apply(pose);instances.Sync(new[]{visual});camera.Position=new(-25,24,29);camera.LookAt(new(0,1,0));
                // Visible reference rails make a body which merely swivels but
                // leaves its drivers off the railway obvious in review captures.
                var railMaterial=new StandardMaterial3D{AlbedoColor=new("373e42"),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};
                var rails=new ImmediateMesh();rails.SurfaceBegin(Mesh.PrimitiveType.Lines,railMaterial);
                Vector3 Rail(double station,int side)
                {
                    double angle=station/radius;
                    return new((float)(radius*(1-Math.Cos(angle))+side*.7175*Math.Cos(angle)),.04f,
                        (float)(radius*Math.Sin(angle)-side*.7175*Math.Sin(angle)));
                }
                foreach(int side in new[]{-1,1})for(int j=0;j<400;j++)
                {rails.SurfaceAddVertex(Rail(-50+j*.25,side));rails.SurfaceAddVertex(Rail(-50+(j+1)*.25,side));}
                rails.SurfaceEnd();stage.AddChild(new MeshInstance3D{Mesh=rails});
                for(int i=0;i<4;i++)await frame();
                await parent.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var curve=parent.GetViewport().GetTexture().GetImage();curve.SavePng("res://.godot/big-boy-curve.png");
            }
        }
        finally{stage.Free();await frame();}
    }
    private static void GenericRunningGear(Action<bool,string> check)
    {
        var root=new Node3D();
        try
        {
            check(DistanceRunningGear.TryCreate(root)==null,"Models without mechanism metadata keep their existing animation path");
            var wheel=new Node3D{Name="unrelated_axle",Position=new(1,2,3)};root.AddChild(wheel);
            wheel.SetMeta("wheel_radius",.5);wheel.SetMeta("wheel_phase",.1);
            var link=new Node3D{Name="unrelated_link"};var slider=new Node3D{Name="unrelated_slider"};
            root.AddChild(link);root.AddChild(slider);
            foreach(var (node,kind) in new[]{(link,"connecting"),(slider,"crosshead")})
            {
                node.SetMeta("rod_kind",kind);node.SetMeta("crank_origin",new Vector3(0,.5f,-1));
                node.SetMeta("driver_radius",.5);node.SetMeta("crank_radius",.1);
                node.SetMeta("rod_length",.8);node.SetMeta("crank_phase",.07);node.SetMeta("slider_direction",-1);
            }
            var gear=DistanceRunningGear.TryCreate(root)!;var start=wheel.Transform;
            check(gear.WheelCount==1&&gear.RodCount==2,"Arbitrary node names select generic wheel/rod animation through metadata");
            gear.ApplyDistance(.25);
            var expected=new Basis(Vector3.Right,(float)-(.25/.5+Math.Tau*.1));
            check(wheel.Basis.IsEqualApprox(expected)&&wheel.Position==new Vector3(1,2,3),"An unrelated wheel uses its own radius, phase and pivot position");
            check(Math.Abs(link.Basis.Z.Length()-.8)<.0001 && (link.Position+link.Basis.Z/2).DistanceTo(slider.Position)<.0001,
                "Generic slider-crank preserves rod length with a reversed slide direction");
            var still=link.Transform;gear.ApplyDistance(.25);
            check(link.Transform.IsEqualApprox(still),"Distance-driven rods do not animate while travel is unchanged");
            gear.ApplyDistance(0);check(wheel.Transform.IsEqualApprox(start),"Generic wheel rotation reverses back to its initial phase");
        }
        finally{root.Free();}
    }
}
