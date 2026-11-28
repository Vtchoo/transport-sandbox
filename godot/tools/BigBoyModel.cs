using Godot;
using System;

namespace TransportSandbox;

/// <summary>Metre-scale, coal-fired ALCO 4000-class silhouette with independently posed running gear.</summary>
public static class BigBoyModel
{
    public const string Id = "up-big-boy", Path = "res://assets/vehicles/up-big-boy-1435.tscn";
    public const float DriverRadius = .8636f, Hinge = .35f;
    public const double FrontEnginePhase = .17;
    private static StandardMaterial3D Material(string name, string hex, float rough = .65f) => new()
        { ResourceName = name, AlbedoColor = new(hex), Roughness = rough };
    private static MeshInstance3D Mesh(Node3D parent, string name, Mesh mesh, Vector3 at, Material material)
    {
        var node = new MeshInstance3D { Name = name, Mesh = mesh, Position = at, MaterialOverride = material };
        parent.AddChild(node, true); return node;
    }
    private static MeshInstance3D Box(Node3D p, string n, Vector3 size, Vector3 at, Material m) => Mesh(p, n, new BoxMesh { Size = size }, at, m);
    private static MeshInstance3D Cylinder(Node3D p, string n, float radius, float length, Vector3 at, Material m, bool alongTrack = false)
    {
        var node = Mesh(p, n, new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = length, RadialSegments = 20 }, at, m);
        if (alongTrack) node.Rotation = new(Mathf.Pi / 2, 0, 0);
        return node;
    }
    private static Node3D Group(Node3D p, string name, Vector3 at = default)
    { var node = new Node3D { Name = name, Position = at }; p.AddChild(node); return node; }
    private static void Pipe(Node3D p, string n, Vector3 a, Vector3 b, float radius, Material m)
    { var node = Cylinder(p, n, radius, a.DistanceTo(b), (a+b)/2, m); node.Quaternion = new Quaternion(Vector3.Up, (b-a).Normalized()); }
    private static void Letter(Node3D p, string text, float width, float height, Vector3 at, float yaw, Material material)
    {
        var source = new TextMesh { Text = text, Font = ThemeDB.FallbackFont, FontSize = 48, PixelSize = .01f, Depth = 0,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var baked = new ArrayMesh(); baked.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, source.SurfaceGetArrays(0));
        var size = baked.GetAabb().Size; var node = Mesh(p, "lettering", baked, at, material);
        node.Scale = new(width/Math.Max(.001f,size.X), height/Math.Max(.001f,size.Y), 1); node.Rotation = new(0,yaw,0);
    }
    private static void Wheelset(Node3D p, string name, float z, float radius, Material black, Material steel, bool spokes = false, double phase = 0)
    {
        var axle = Group(p, name, new(0,radius,z)); axle.SetMeta("wheel_radius", radius);
        axle.SetMeta("wheel_phase",phase);
        var shaft = Cylinder(axle, "axle", .08f, 1.75f, Vector3.Zero, black); shaft.Rotation = new(0,0,Mathf.Pi/2);
        foreach (int side in new[] {-1,1})
        {
            float x = side * .785f;
            var rim = Mesh(axle,"tire",new TorusMesh { InnerRadius = radius*.84f, OuterRadius = radius, Rings = 20, RingSegments = 6 },new(x,0,0),steel);
            rim.Rotation = new(0,0,Mathf.Pi/2);
            var hub = Cylinder(axle,"hub",radius*.21f,.16f,new(x,0,0),black); hub.Rotation = new(0,0,Mathf.Pi/2);
            if (spokes) for (int i=0;i<12;i++)
            {
                var spoke = Box(axle,"spoke",new(.09f,.055f,radius*1.74f),new(x,0,0),black);
                spoke.Rotation = new(i*Mathf.Pi/12,0,0);
            }
            else { var disc = Cylinder(axle,"disc",radius*.85f,.08f,new(x,0,0),black); disc.Rotation=new(0,0,Mathf.Pi/2); }
            if(spokes)
            {
                float quarter=side==1?0:Mathf.Pi/2;
                var pin=Cylinder(axle,"crankpin",.065f,.19f,new(side*1.08f,.2f*Mathf.Sin(quarter),.2f*Mathf.Cos(quarter)),steel);
                pin.Rotation=new(0,0,Mathf.Pi/2);
                Box(axle,"counterweight",new(.1f,.22f,.32f),new(side*.85f,-.47f*Mathf.Sin(quarter),-.47f*Mathf.Cos(quarter)),black);
            }
        }
    }
    public static Node3D Build()
    {
        StandardMaterial3D black = Material("paint-boiler-black","22262a"), frame = Material("paint-frame-black","151b20");
        StandardMaterial3D graphite = Material("paint-smokebox-graphite","62696b"), steel = Material("metal-stainless","c4c9cb");
        StandardMaterial3D white = Material("decal-fixed","ece8d9"), glass = Material("glass-tinted","263e49"), coal = Material("coal","16181a",1);
        var root = new Node3D { Name = Id };
        // Root placeholders retain the common vehicle contract. Actual sections carry geometry.
        Group(root,"body"); Group(root,"bogie_0"); Group(root,"bogie_1");
        var body=Group(root,"section_0_body",new(0,0,7.1580375f));
        Group(root,"section_0_bogie_0"); Group(root,"section_0_bogie_1");
        // Continue the boiler/firebox crown through the cab's front wall. Keep
        // the smokebox end at +8.2 m; there is no open gap ahead of the cab.
        Cylinder(body,"boiler",1.17f,18.2f,new(0,3.24f,-.9f),black,true);
        Cylinder(body,"smokebox",1.18f,2.5f,new(0,3.24f,9.45f),graphite,true);
        Cylinder(body,"smokebox-door",1.04f,.13f,new(0,3.24f,10.76f),graphite,true);
        Cylinder(body,"door-hub",.19f,.08f,new(0,3.24f,10.86f),steel,true);
        foreach(float z in new[]{-6.8f,-4.9f,-2.7f,-.4f,1.9f,4.2f,6.5f,8.15f})
        { var band=Mesh(body,"boiler-band",new TorusMesh{InnerRadius=1.169f,OuterRadius=1.19f,Rings=28,RingSegments=4},new(0,3.24f,z),graphite);band.Rotation=new(Mathf.Pi/2,0,0); }
        Box(body,"firebox",new(2.65f,2.3f,4.6f),new(0,2.36f,-8.2f),black);
        Box(body,"cab",new(3.25f,2.46f,3.15f),new(0,3.15f,-11.18f),black);
        Box(body,"cab-roof",new(3.38f,.18f,3.4f),new(0,4.48f,-11.18f),black);
        foreach(int side in new[]{-1,1})
        {
            Box(body,"cab-window",new(.04f,.85f,1.45f),new(side*1.635f,3.7f,-10.95f),glass);
            Box(body,"cab-window-divider",new(.06f,.87f,.055f),new(side*1.66f,3.7f,-10.95f),steel);
            Letter(body,"4000",1.25f,.36f,new(side*1.66f,2.71f,-11.12f),side*Mathf.Pi/2,white);
            Box(body,"running-board",new(.4f,.1f,18.4f),new(side*1.32f,2.64f,.9f),black);
            Pipe(body,"handrail",new(side*1.39f,3.02f,-8.3f),new(side*1.39f,3.02f,9.7f),.025f,steel);
            for(int j=0;j<11;j++) Pipe(body,"handrail-post",new(side*1.39f,2.64f,-8.3f+j*1.8f),new(side*1.39f,3.02f,-8.3f+j*1.8f),.018f,graphite);
            Pipe(body,"air-pipe",new(side*1.18f,2.06f,-6.5f),new(side*1.18f,2.06f,8.3f),.07f,frame);
            Cylinder(body,"air-reservoir",.19f,3.9f,new(side*1.29f,2.36f,-5.6f),black,true);
            for(int i=0;i<4;i++) Box(body,"cab-step",new(.36f,.06f,.7f),new(side*1.5f,.74f+i*.3f,-12.1f),steel);
        }
        Cylinder(body,"steam-dome",.52f,.45f,new(0,4.47f,-1.1f),black);
        Cylinder(body,"sand-dome-front",.51f,.35f,new(0,4.42f,3.4f),black);
        Cylinder(body,"sand-dome-rear",.51f,.35f,new(0,4.42f,-4.6f),black);
        // Two outlets on the common smokebox, not a separate stack per engine unit.
        foreach(float z in new[]{8.88f,9.57f}) Cylinder(body,"stack",.31f,.56f,new(0,4.65f,z),graphite);
        Cylinder(body,"bell",.16f,.26f,new(0,4.49f,5.9f),steel);
        var lamp = Material("lamp-white","fff1d0"); lamp.EmissionEnabled=true;lamp.Emission=new("ffedbd");lamp.EmissionEnergyMultiplier=.6f;
        Cylinder(body,"headlight-housing",.23f,.25f,new(0,4.26f,10.62f),frame,true);
        Cylinder(body,"headlight",.18f,.02f,new(0,4.26f,10.76f),lamp,true);
        Letter(body,"4000",.65f,.18f,new(0,3.93f,10.86f),0,white);
        // A separate component with a fixed joint to the boiler, not another bogie.
        var rearEngine=Group(body,"rear_engine");
        Box(rearEngine,"rear-engine-frame",new(1.45f,.38f,7.8f),new(0,.99f,-3.1f),frame);
        var front=Group(body,"motion_front_engine",new(0,0,Hinge));
        Box(front,"front-engine-frame",new(1.45f,.38f,8.1f),new(0,.99f,4.1f),frame);
        BuildEngine(front,"front",new[]{6.9f-Hinge,5.05f-Hinge,3.2f-Hinge,1.35f-Hinge},8.3f-Hinge,black,steel,graphite,FrontEnginePhase);
        BuildEngine(rearEngine,"rear",new[]{-.5f,-2.35f,-4.2f,-6.05f},.9f,black,steel,graphite);
        var pilot=Group(body,"motion_pilot",new(0,0,10.2f));
        Box(pilot,"pilot-frame",new(1.6f,.2f,3.6f),new(0,.76f,0),frame);
        Wheelset(pilot,"motion_wheel_pilot_0",.7f,.4572f,frame,steel); Wheelset(pilot,"motion_wheel_pilot_1",-.7f,.4572f,frame,steel);
        Box(pilot,"pilot-deck",new(3.05f,.16f,1.6f),new(0,1.13f,1.6f),graphite);
        Box(pilot,"pilot-buffer",new(3.2f,.5f,.24f),new(0,.75f,2.6f),frame);
        for(int i=0;i<13;i++) Pipe(pilot,"pilot-bar",new(-1.4f+i*.233f,.4f,2.65f),new(-1.4f+i*.233f,1.02f,2.3f),.027f,steel);
        Coupler(pilot,2.74f,frame);
        var trailing=Group(body,"motion_trailing",new(0,0,-10));
        Box(trailing,"trailing-frame",new(1.7f,.22f,3.3f),new(0,.83f,0),frame);
        Wheelset(trailing,"motion_wheel_trailing_0",.8f,.5334f,frame,steel); Wheelset(trailing,"motion_wheel_trailing_1",-.8f,.5334f,frame,steel);
        var tender=Group(root,"section_1_body",new(0,0,-13.1400005f));
        Group(root,"section_1_bogie_1");
        var tenderTruck=Group(root,"section_1_bogie_0",new(0,0,-8.74f));
        Box(tender,"tender-frame",new(3.2f,.3f,13.7f),new(0,1.16f,0),frame);
        Box(tender,"water-tank",new(3.25f,2.4f,10.6f),new(0,2.5f,-1.25f),black);
        Box(tender,"coal-bunker",new(3.16f,1.95f,4.9f),new(0,2.68f,3.85f),black);
        // Visible, faceted coal rather than an oil tank or covered bunker.
        for(int i=0;i<48;i++)
        {
            float x=((i*37)%101)/100f*2.6f-1.3f,z=1.8f+((i*23)%97)/96f*4;
            var lump=Mesh(tender,"coal",new PrismMesh{Size=new(.52f,.28f,.62f)},new(x,3.67f+.26f*(1-Math.Abs(x)/1.4f),z),coal);lump.Rotation=new(0,i*1.9f,0);
        }
        foreach(int side in new[]{-1,1})
        {
            Letter(tender,"UNION PACIFIC",8.3f,.62f,new(side*1.64f,2.68f,-1.1f),side*Mathf.Pi/2,white);
            for(int i=0;i<6;i++) Box(tender,"rear-ladder-rung",new(.45f,.035f,.06f),new(side*1.12f,1.45f+i*.36f,-6.62f),steel);
            Pipe(tender,"ladder",new(side*1.34f,1.3f,-6.63f),new(side*1.34f,3.65f,-6.63f),.025f,steel);
            Box(tender,"centipede-frame",new(.2f,.3f,7.7f),new(side*1.05f,.8f,-2.5f),frame);
        }
        Box(tenderTruck,"truck-frame",new(2.1f,.25f,2.9f),new(0,.8f,0),frame);
        Wheelset(tenderTruck,"motion_wheel_tender_front_0",.75f,.4572f,frame,steel);
        Wheelset(tenderTruck,"motion_wheel_tender_front_1",-.75f,.4572f,frame,steel);
        for(int i=0;i<5;i++) Wheelset(tender,$"motion_wheel_centipede_{i}",-5.7f+i*1.27f,.4572f,frame,steel);
        Cylinder(tender,"water-hatch",.36f,.13f,new(0,3.76f,-4.5f),graphite);
        Coupler(tender,-6.958f,frame); Letter(tender,"4000",1.6f,.42f,new(0,2.65f,-6.57f),Mathf.Pi,white);
        return root;
    }
    private static void Coupler(Node3D p,float z,Material m)
    { Box(p,"coupler",new(.3f,.23f,.28f),new(0,.88f,z),m); }
    private static void BuildEngine(Node3D p,string name,float[] stations,float cylinder,Material black,Material steel,Material graphite,double phase=0)
    {
        for(int i=0;i<4;i++) Wheelset(p,$"motion_driver_{name}_{i}",stations[i],DriverRadius,black,steel,true,phase);
        foreach(int side in new[]{-1,1})
        {
            Cylinder(p,"cylinder",.36f,1.32f,new(side*1.08f,1.16f,cylinder),graphite,true);
            Cylinder(p,"valve-chest",.17f,1.06f,new(side*1.08f,1.63f,cylinder),black,true);
            Pipe(p,"slide-guide",new(side*1.14f,DriverRadius,cylinder-.5f),new(side*1.14f,DriverRadius,cylinder-2),.045f,steel);
            string suffix=side==1?"right":"left";
            var coupling=Group(p,$"motion_coupling_{name}_{suffix}"); Box(coupling,"coupling-rod",new(.09f,.12f,stations[0]-stations[3]),Vector3.Zero,steel);
            var main=Group(p,$"motion_main_{name}_{suffix}"); Box(main,"main-rod",new(.11f,.12f,1),Vector3.Zero,steel);
            var cross=Group(p,$"motion_crosshead_{name}_{suffix}"); Box(cross,"crosshead",new(.24f,.22f,.34f),Vector3.Zero,steel);
            foreach(var (node,kind) in new[]{(coupling,"coupling"),(main,"connecting"),(cross,"crosshead")})
            {
                node.SetMeta("rod_kind",kind);
                node.SetMeta("crank_origin",new Vector3(side*1.14f,DriverRadius,kind=="coupling"?(stations[0]+stations[3])/2:stations[2]));
                node.SetMeta("crank_radius",.2);node.SetMeta("driver_radius",DriverRadius);
                node.SetMeta("rod_length",5.05);node.SetMeta("crank_phase",phase+(side==1?0:.25));
            }
        }
    }
    public static void Export()
    {
        var root=Build();
        DistanceRunningGear.TryCreate(root)!.ApplyDistance(0);
        void Own(Node node) { foreach(var child in node.GetChildren()) { child.Owner=root;Own(child); } }
        Own(root);var scene=new PackedScene(); var result=scene.Pack(root);
        if(result==Error.Ok) result=ResourceSaver.Save(scene,Path);
        root.Free(); if(result!=Error.Ok) throw new InvalidOperationException(result.ToString());
        GD.Print("Authored coal-fired Big Boy");
    }
}
