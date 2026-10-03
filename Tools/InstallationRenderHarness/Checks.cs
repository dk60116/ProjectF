using System.Reflection;
using ProjectF.Rendering;
using ProjectF.MapObjects;
using UnityEngine;

static class Checks
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static Action Method(object target, string name) => (Action)target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).CreateDelegate(typeof(Action), target);
    static InstallationObject Make(Material material, int id = 60)
    {
        var owner = new InstallationObject { RuntimeMapObjectHandle = new MapObjectHandle(id, 1, 1, id) };
        owner.Add(new MeshRenderer { Materials = new[] { material }, Filter = new MeshFilter { sharedMesh = new Mesh() } });
        return owner;
    }
    static void Main()
    {
        var shader = new Shader();
        shader.Add("_Color", UnityEngine.Rendering.ShaderPropertyType.Color);
        shader.Add("_MainTex", UnityEngine.Rendering.ShaderPropertyType.Texture);
        shader.Add("_Flags", UnityEngine.Rendering.ShaderPropertyType.Int);
        var material = new Material { shader = shader, name = "shared" };
        var host = new InstallationBatchRenderer(); Method(host, "Awake")();
        Action render = Method(host, "LateUpdate");
        var owner = Make(material); InstallationBatchRenderer.Register(owner);
        var body = (MeshRenderer)owner.Renderers[0];
        Check(body.forceRenderingOff && host.RegisteredCount == 1, "registration suppresses the native model once");
        InstallationBatchRenderer.Register(owner);
        Check(host.RegisteredCount == 1, "registration is idempotent");
        Check(!InstallationBatchRenderer.Supports(new ConveyorBelt()) && !InstallationBatchRenderer.Supports(new Pipe())
            && !InstallationBatchRenderer.Supports(new RobotArm()) && !InstallationBatchRenderer.Supports(new Building()),
            "specialized world ownership remains exclusive");
        Check(InstallationBatchRenderer.Supports(new Vehicle()), "vehicle models use shared live-model renderer");
        WorldVisualUpdateManager.Visible.Add(owner); render();
        Check(host.VisibleCount == 1 && host.MatrixCount == 1, "visible model emits one part");
        body.enabled = false; render(); Check(host.MatrixCount == 0, "runtime disabled part emits no matrix");
        body.enabled = true; body.gameObject.activeInHierarchy = false; render();
        Check(host.MatrixCount == 0, "inactive child stays hidden"); body.gameObject.activeInHierarchy = true;
        var added = new MeshRenderer { Materials = new[] { material }, Filter = new MeshFilter { sharedMesh = new Mesh() } };
        owner.Add(added); render();
        Check(host.MatrixCount == 2 && added.forceRenderingOff, "newly created rail/bucket parts are captured and batched");
        owner.RuntimeMapObjectHandle = new MapObjectHandle(60, 1, 2, 60); render();
        Check(host.MatrixCount == 0, "stale presentation handle never submits a reused entity");
        InstallationBatchRenderer.Register(owner); render(); Check(host.MatrixCount == 2, "new generation rebind restores submission");
        MapObjectTickManager.WaitingForWorldLoad = true; render();
        Check(host.MatrixCount == 0 && host.VisibleCount == 0, "world restore suspends model submission");
        MapObjectTickManager.WaitingForWorldLoad = false; render();
        Method(host, "OnDisable")(); Check(!body.forceRenderingOff && !added.forceRenderingOff, "disable restores native presentation");
        Method(host, "OnEnable")(); Check(body.forceRenderingOff, "enable resumes model ownership");
        InstallationBatchRenderer.Unregister(owner); Check(!body.forceRenderingOff && host.RegisteredCount == 0, "pool release restores original draw flags");

        var archetype = new MapObjectArchetype();
        archetype.Nodes.Add(new(){AnimationPath="",ParentIndex=-1,LocalScale=new(1,1,1),LocalRotation=Quaternion.identity});
        archetype.Nodes.Add(new(){AnimationPath="Drill",ParentIndex=0,LocalPosition=new(0,1,0),LocalScale=new(1,1,1),LocalRotation=Quaternion.identity});
        archetype.AnimationClips.Add(new(){LengthSeconds=1,Looping=true,TransformCurveCount=1,TransformCurves=new(){
            new(){Path="Drill",Property="m_LocalPosition.y",Curve=new AnimationCurve(1,-1)}}});
        var animation = InstallationRigidAnimationTemplate.Create(archetype);
        Check(animation != null, "rigid transform curves produce a shared template");
        animation.Evaluate(0.5,true); Check(MathF.Abs(animation.GetMatrix(1).m13)<0.0001f,"curve evaluation changes drill pose");
        animation.Evaluate(1.5,true); Check(MathF.Abs(animation.GetMatrix(1).m13)<0.0001f,"loop phase wraps without per-instance Animator");
        animation.Evaluate(0.5,false); Check(animation.GetMatrix(1).m13==1,"idle pose restores authored transform");
        var miner = new MiningMachine { RuntimeMapObjectHandle = new MapObjectHandle(60, 2, 1, 2),
            BoundItemDefinition = new ItemDefinition { MapObjectArchetype = archetype } };
        var drill = new MeshRenderer { Materials=new[]{material}, Filter=new(){sharedMesh=new Mesh()} };
        drill.transform.name="Drill";drill.transform.parent=miner.transform;miner.Add(drill);
        InstallationBatchRenderer.Register(miner); WorldVisualUpdateManager.Visible.Clear();WorldVisualUpdateManager.Visible.Add(miner);
        Check(InstallationBatchRenderer.TrySetWorkAnimation(miner,true,1),"mining work state binds to shared pose data");
        Check(host.SharedAnimationCount==1,"shared animation ownership is observable");
        Check(miner.SharedAnimation && miner.AnimationRefreshes==1,"registration reconciles native Animator ownership immediately");
        MapObjectTickManager.CurrentSimulationTimeSeconds=0.5;render();
        Check(MathF.Abs(VirtualRenderBatchCollection.LastMatrix.m13)<0.0001f,"miner submits animated shared matrix");
        Check(drill.transform.localToWorldMatrix.m13==0,"shared pose never mutates the gameplay Transform hierarchy");
        WorldVisualUpdateManager.Visible.Clear(); MapObjectTickManager.CurrentSimulationTimeSeconds=0.75;render();
        WorldVisualUpdateManager.Visible.Add(miner);render();
        Check(MathF.Abs(VirtualRenderBatchCollection.LastMatrix.m13+0.5f)<0.0001f,"return to view evaluates current phase without replaying hidden poses");
        InstallationBatchRenderer.TrySetWorkAnimation(miner,true,0.5f);
        MapObjectTickManager.CurrentSimulationTimeSeconds=1;render();
        Check(MathF.Abs(VirtualRenderBatchCollection.LastMatrix.m13+0.75f)<0.0001f,"power ratio change preserves phase and slows subsequent animation");
        InstallationBatchRenderer.TrySetWorkAnimation(miner,false,0);render();
        Check(VirtualRenderBatchCollection.LastMatrix.m13==1,"stopping work submits rest pose");
        host.isActiveAndEnabled=false; Method(host,"OnDisable")();
        Check(!miner.SharedAnimation && !drill.forceRenderingOff,"disabled batch host returns work animation and draw to native presentation");
        host.isActiveAndEnabled=true; Method(host,"OnEnable")();
        Check(miner.SharedAnimation && drill.forceRenderingOff,"reenabled host reclaims shared work animation");
        drill.Materials=new[]{new Material{shader=new Shader{SupportsInstancing=false}}};render();
        Check(!miner.SharedAnimation && host.SharedAnimationCount==0 && !drill.forceRenderingOff,
            "runtime shader fallback resumes native work animation as well as native draw");
        InstallationBatchRenderer.Unregister(miner);WorldVisualUpdateManager.Visible.Clear();
        Check(host.SharedAnimationCount==0,"pool release removes shared animation ownership");

        var iconOwner = Make(material);
        var sprite = new Sprite { name="icon",texture=new Texture(),vertices=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1)},
            uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1)},triangles=new ushort[]{0,1,2}};
        var icon = new SpriteRenderer {sprite=sprite,Materials=new[]{material},color=new Color(0.2f,0.4f,0.6f,1),flipX=true};
        iconOwner.Add(icon);InstallationBatchRenderer.Register(iconOwner);WorldVisualUpdateManager.Visible.Add(iconOwner);render();
        Check(host.MatrixCount==2 && icon.forceRenderingOff,"simple sprite icons join model submission with native draw suppressed");
        icon.enabled=false;render();Check(host.MatrixCount==1,"disabled fluid arrow remains hidden");
        InstallationBatchRenderer.Unregister(iconOwner);WorldVisualUpdateManager.Visible.Clear();
        var unsupported = Make(new Material { shader=new Shader { SupportsInstancing=false } });
        InstallationBatchRenderer.Register(unsupported);WorldVisualUpdateManager.Visible.Add(unsupported);render();
        Check(host.MatrixCount==0 && !unsupported.Renderers[0].forceRenderingOff && host.NativeFallbackPartCount==1,
            "unsupported shader preserves the native renderer instead of dropping the model");
        InstallationBatchRenderer.Unregister(unsupported);WorldVisualUpdateManager.Visible.Clear();

        using (var variants = new InstallationMaterialVariants())
        {
            int color = Shader.PropertyToID("_Color"), flags = Shader.PropertyToID("_Flags"), tex = Shader.PropertyToID("_MainTex");
            body.Global.SetColor(color, new Color(0.2f, 0.3f, 0.4f, 1));
            var a = variants.Resolve(body, material, 0);
            var other = Make(material); var b = (MeshRenderer)other.Renderers[0];
            b.Global.SetColor(color, new Color(0.2f, 0.3f, 0.4f, 1));
            Check(ReferenceEquals(a, variants.Resolve(b, material, 0)) && variants.Count == 1,
                "equal property blocks share a material across entities");
            b.Global.SetColor(color, new Color(0.9f, 0.3f, 0.4f, 1));
            Check(!ReferenceEquals(a, variants.Resolve(b, material, 0)), "different fluid colors remain distinct");
            b.Indexed.SetInt(flags, int.MaxValue);
            var indexMaterial = variants.Resolve(b, material, 0);
            Check(indexMaterial.GetInt(flags) == int.MaxValue, "indexed property block preserves exact integer overrides");
            Check(indexMaterial.GetColor(color).Equals(material.GetColor(color)), "indexed block replaces global block");
            var texture = new Texture(); b.Indexed.Clear(); b.Global.SetTexture(tex, texture);
            Check(ReferenceEquals(variants.Resolve(b, material, 0).GetTexture(tex), texture), "lamp texture override survives batching");
            for (int i = 0; i < 50; i++) variants.Resolve(b, material, 0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) variants.Resolve(b, material, 0);
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "warmed variant resolution allocates no managed memory");
        }
        WorldVisualUpdateManager.Visible.Clear();
        InstallationObject selected = null;
        for (int i = 0; i < 100000; i++)
        {
            var instance = Make(material); InstallationBatchRenderer.Register(instance);
            if (i == 0) selected = instance;
        }
        Check(host.RegisteredCount == 100000, "100000 independent installed model records registered");
        WorldVisualUpdateManager.Visible.Add(selected); render();
        Renderer.Reads = 0;
        for (int i = 0; i < 30; i++) render();
        Check(host.VisibleCount == 1 && host.MatrixCount == 1 && Renderer.Reads == 30,
            "steady camera visits visible model parts, not all 100000 installations");
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) render();
        Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "warmed model submission loop allocates no managed memory");
        Method(host, "OnDestroy")(); Check(!selected.Renderers[0].forceRenderingOff, "host destruction restores model flags");
        Console.WriteLine($"PASS: {checks} installation model lifecycle/material/100000-record checks. Unity boundaries doubled; no GPU or game launched.");
    }
}
