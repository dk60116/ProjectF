using System.Globalization;
using System.Text.RegularExpressions;
using ProjectF.Rendering;
using UnityEngine;

static class Checks
{
    static int checks;
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
    static float Value(string text, string name) => float.Parse(Regex.Match(text,
        @"(?m)^\s*" + name + @":(?:\r?\n\s+serializedVersion: \d+\r?\n\s+minMaxState: \d+\r?\n\s+scalar:)?\s*([-\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
    static ParticleSystem ReadFlame(string path)
    {
        string prefab = File.ReadAllText(path);
        string gameObject = Regex.Match(prefab, @"(?s)--- !u!1 &(\d+)\r?\nGameObject:(?:(?!--- !u!).)*m_Name: FlameMain").Groups[1].Value;
        string particle = Regex.Matches(prefab, @"(?s)--- !u!198 &.*?(?=--- !u!)").Select(m => m.Value)
            .Single(s => s.Contains("m_GameObject: {fileID: " + gameObject + "}"));
        string emission = particle[particle.IndexOf("  EmissionModule:")..particle.IndexOf("  SizeModule:")];
        string burst = emission[emission.IndexOf("    m_Bursts:")..];
        var ps = new ParticleSystem();
        ps.main.duration = Value(particle,"lengthInSec"); ps.main.startLifetime = Value(particle,"startLifetime");
        ps.main.startSize3D = Value(particle,"size3D") != 0;
        ps.main.startSizeX = Value(particle,"startSize"); ps.main.startSizeY = Value(particle,"startSizeY"); ps.main.startSizeZ = Value(particle,"startSizeZ");
        ps.main.maxParticles = (int)Value(particle,"maxNumParticles");
        ps.emission.rateOverTime = Value(emission,"rateOverTime");
        ps.emission.Bursts = new[] { new ParticleSystem.Burst { time=Value(burst,"time"), count=Value(burst,"countCurve"),
            cycleCount=(int)Value(burst,"cycleCount"), repeatInterval=Value(burst,"repeatInterval"), probability=Value(burst,"probability") } };
        ps.textureSheetAnimation.enabled=true; ps.textureSheetAnimation.numTilesX=(int)Value(particle,"tilesX");
        ps.textureSheetAnimation.numTilesY=(int)Value(particle,"tilesY"); ps.textureSheetAnimation.fps=Value(particle,"fps");
        ps.textureSheetAnimation.timeMode=(ParticleSystemAnimationTimeMode)(int)Value(particle,"timeMode");
        ps.Renderer.renderMode = ParticleSystemRenderMode.VerticalBillboard; ps.Renderer.pivot=new(0,.5f,0);
        return ps;
    }
    static int Draw(ProductionEffectTemplate template,double phase,VirtualRenderBatchCollection batches)
    {
        batches.Items.Clear(); template.Append(new ProductionFacilityInstance { AnimationPhase=phase },Matrix4x4.identity,new Camera(),batches);
        return batches.Items.Count;
    }
    static void Main(string[] args)
    {
        foreach (string relative in new[] { "Funance/Furnace.prefab", "Steel Funance/Steel Furnace.prefab" })
        {
            var source=ReadFlame(Path.Combine(args[0],"FactorioProject/Assets/MapObject/InputOutputModule",relative));
            Check(source.emission.rateOverTime.constantMax==0 && source.emission.burstCount==1,"Real flame uses bursts only");
            using var template=new ProductionEffectTemplate(source,new Transform()); var batches=new VirtualRenderBatchCollection();
            foreach (double time in new[] {0d,.1,.479,.48,.9,1,10,1000000})
                Check(Draw(template,time,batches)>0,"Flame persists at " + time);
            Draw(template,.1,batches); var item=batches.Items[0];
            Check(Math.Abs(item.Matrix.lossyScale.x-.3)<.001 && Math.Abs(item.Matrix.lossyScale.y-.5)<.001,"Separate XY size preserved");
            Check(item.Matrix.Rotation.Upright,"Vertical flame stays upright");
            Check(item.Key.Mesh.vertices[0].y == 0 && item.Key.Mesh.vertices[2].y == 1,"Flame pivot keeps base at emitter");
            Check(Math.Abs(item.Key.Mesh.uv[0].x-5f/7)<.001,"56 FPS sheet frame respected");
            Check(item.Key.Material.enableInstancing,"Batched material enabled");
            var facility=new ProductionFacilityInstance(); var camera=new Camera();
            for (int i=0;i<500;i++) { facility.AnimationPhase=i/60d; batches.Items.Clear(); template.Append(facility,Matrix4x4.identity,camera,batches); }
            long allocated=GC.GetAllocatedBytesForCurrentThread();
            for (int i=0;i<500;i++) { facility.AnimationPhase=i/60d; batches.Items.Clear(); template.Append(facility,Matrix4x4.identity,camera,batches); }
            Check(GC.GetAllocatedBytesForCurrentThread()==allocated,"Warm effect frame loop allocates zero bytes");
        }
        var ps=new ParticleSystem(); ps.emission.Bursts=new[] {new ParticleSystem.Burst {count=50,cycleCount=1,probability=1}};
        ps.main.maxParticles=3; ps.main.loop=false;
        using (var t=new ProductionEffectTemplate(ps,new Transform()))
        {
            var b=new VirtualRenderBatchCollection(); Check(Draw(t,.2,b)==3,"Burst particle cap");
            Check(Draw(t,2,b)==0,"Nonlooping burst expires");
        }
        ps.emission.Bursts[0].probability=0;
        using (var t=new ProductionEffectTemplate(ps,new Transform())) Check(Draw(t,.2,new())==0,"Probability zero omitted");
        ps.emission.Bursts=Array.Empty<ParticleSystem.Burst>(); ps.emission.rateOverTime=6; ps.main.loop=true;
        using (var t=new ProductionEffectTemplate(ps,new Transform())) Check(Draw(t,.2,new())>0,"Continuous emission preserved");
        ps.Renderer.enabled=false;
        using (var t=new ProductionEffectTemplate(ps,new Transform())) Check(Draw(t,.2,new())==0,"Disabled renderer excluded");
        ProductionEffectTemplate.GetBurstLoopRange(1000000,1,.48f,true,0,0,out long first,out long last);
        Check(last-first<=3,"Saved phase keeps lifetime bounded loop range");
        Console.WriteLine($"PASS: {checks} production effect checks (real furnace prefab emission; engine drawing doubled)");
    }
}
