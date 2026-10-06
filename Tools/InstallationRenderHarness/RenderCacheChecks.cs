using ProjectF.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

static partial class Checks
{
    static void CheckRenderCaches(InstallationBatchRenderer host, Action render, Material material)
    {
        var owner = Make(material);
        var part = (MeshRenderer)owner.Renderers[0];
        InstallationBatchRenderer.Register(owner);
        WorldVisualUpdateManager.Visible.Add(owner);
        render();
        Check(host.MatrixCacheMisses == 1 && host.BatchKeyRebuilds == 1, "first submission constructs matrix metadata and key");
        int keywords = Shader.KeywordReads;
        Renderer.PropertyBlockReads = 0;
        for (int i = 0; i < 30; i++) render();
        Check(host.MatrixCacheHits == 1 && host.MatrixCacheMisses == 0 && host.BatchKeyRebuilds == 0,
            "unchanged model reuses matrix metadata and batch key");
        Check(Shader.KeywordReads == keywords, "unchanged shared shader performs no further keyword searches");
        Check(Renderer.PropertyBlockReads == 0, "plain model copies no empty property blocks");

        part.transform.localToWorldMatrix = Matrix4x4.TRS(new(1, 0, 0), Quaternion.identity, new(1, 1, 1));
        render();
        Check(host.MatrixCacheMisses == 1 && host.BatchKeyRebuilds == 0 && VirtualRenderBatchCollection.LastMatrix.m03 == 1,
            "movement within cell updates actual matrix and keeps compatible key");
        part.transform.localToWorldMatrix = Matrix4x4.TRS(new(-17, 0, 33), Quaternion.identity, new(-1, 1, 1));
        render();
        var key = VirtualRenderBatchCollection.LastKey;
        Check(host.BatchKeyRebuilds == 1 && key.BatchCellX == -2 && key.BatchCellZ == 2 && key.InvertCulling,
            "cross-cell movement and negative scale update spatial key and winding");
        part.gameObject.layer = 9; part.shadowCastingMode = 2; part.receiveShadows = false; part.renderingLayerMask = 8;
        render(); key = VirtualRenderBatchCollection.LastKey;
        Check(key.Layer == 9 && key.ShadowCastingMode == 2 && !key.ReceiveShadows && key.RenderingLayerMask == 8,
            "runtime render settings invalidate cached key");
        var mesh = new Mesh(); part.Filter.sharedMesh = mesh; render();
        Check(VirtualRenderBatchCollection.LastKey.Mesh == mesh, "runtime mesh replacement invalidates cached key");
        int color = Shader.PropertyToID("_Color");
        part.Global.SetColor(color, new(.25f, .5f, .75f, 1)); render();
        Material tinted = VirtualRenderBatchCollection.LastKey.Material;
        Check(tinted != material && tinted.GetColor(color).Equals(new Color(.25f, .5f, .75f, 1)),
            "property block introduced after warm cache is visible immediately");
        int layoutReads = Shader.PropertyLayoutReads;
        part.Global.SetColor(color, new(.75f, .5f, .25f, 1)); render();
        Check(Shader.PropertyLayoutReads == layoutReads && VirtualRenderBatchCollection.LastKey.Material != tinted,
            "property edits reuse shader metadata and update material");
        part.Global.Clear(); render();
        Check(VirtualRenderBatchCollection.LastKey.Material == material, "cleared property block restores shared material");

        var replacement = new Material { shader = new Shader() };
        part.Materials = new[] { replacement }; render();
        Check(VirtualRenderBatchCollection.LastKey.Material == replacement, "runtime shared material replacement is detected");
        replacement.shader = new Shader { SupportsInstancing = false }; render();
        Check(host.MatrixCount == 0 && host.NativeFallbackPartCount == 1 && !part.forceRenderingOff,
            "same material with unsupported replacement shader restores native renderer");
        replacement.shader = material.shader; render();
        Check(host.MatrixCount == 1 && part.forceRenderingOff, "supported shader replacement resumes cached submission");
        part.Filter.sharedMesh.subMeshCount = 2; part.Materials = new[] { material, replacement }; render();
        Check(host.MatrixCount == 2 && VirtualRenderBatchCollection.LastKey.SubmeshIndex == 1,
            "runtime submesh growth creates independent slots");
        part.Filter.sharedMesh.subMeshCount = 1; render();
        Check(host.MatrixCount == 1, "runtime submesh shrink removes stale slots");

        var sprite = new Sprite { texture = new Texture(), vertices = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1) },
            uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1) }, triangles = new ushort[] { 0, 1, 2 } };
        var icon = new SpriteRenderer { sprite = sprite, Materials = new[] { material } };
        owner.Add(icon); render(); icon.flipX = true; render();
        Check(VirtualRenderBatchCollection.LastKey.InvertCulling, "sprite flip invalidates cached winding");
        icon.color = new(.1f, .2f, .3f, 1); render();
        Check(VirtualRenderBatchCollection.LastKey.Material.GetColor(color).Equals(icon.color), "sprite color changes remain live");
        WorldVisualUpdateManager.Visible.Clear(); render();
        icon.flipX = false; WorldVisualUpdateManager.Visible.Add(owner); render();
        Check(host.MatrixCount == 2 && !VirtualRenderBatchCollection.LastKey.InvertCulling,
            "culled model returns with current sprite state");
        for (int i = 0; i < 50; i++) render();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) render();
        Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "warmed matrix/key/material cache emits without GC allocation");
        InstallationBatchRenderer.Unregister(owner); WorldVisualUpdateManager.Visible.Clear();

        using var variants = new InstallationMaterialVariants();
        part.Global.SetColor(color, new(.2f, .3f, .4f, 1));
        var mutable = new Material { shader = material.shader };
        var before = variants.Resolve(part, mutable, 0);
        var nextShader = new Shader(); nextShader.Add("_Color", ShaderPropertyType.Color); mutable.shader = nextShader;
        var after = variants.Resolve(part, mutable, 0);
        Check(after != before && after.shader == nextShader, "shader replacement cannot reuse a variant of the old shader");
        CheckSnapshotShape(host, render, material);
    }

    static void CheckSnapshotShape(InstallationBatchRenderer host, Action render, Material material)
    {
        var owners = new List<InstallationObject>(224);
        for (int i = 0; i < 224; i++)
        {
            var owner = Make(material, i + 1000);
            int parts = i < 177 ? 7 : 6;
            for (int j = 1; j < parts; j++)
                owner.Add(new MeshRenderer { Materials = new[] { material }, Filter = new MeshFilter { sharedMesh = new Mesh() } });
            foreach (Renderer renderer in owner.Renderers)
                renderer.transform.localToWorldMatrix = Matrix4x4.TRS(new(i, 0, 0), Quaternion.identity, new(1, 1, 1));
            InstallationBatchRenderer.Register(owner); owners.Add(owner); WorldVisualUpdateManager.Visible.Add(owner);
        }
        render();
        Check(host.VisibleCount == 224 && host.MatrixCount == 1521, "snapshot-shaped scene submits all 224 models and 1521 parts");
        int keywords = Shader.KeywordReads;
        Renderer.PropertyBlockReads = 0;
        for (int i = 0; i < 20; i++) render();
        Check(host.MatrixCacheHits == 1521 && host.MatrixCacheMisses == 0 && host.BatchKeyRebuilds == 0,
            "snapshot-shaped static scene reuses all 1521 matrix metadata and batch keys");
        Check(Shader.KeywordReads == keywords && Renderer.PropertyBlockReads == 0,
            "snapshot-shaped plain scene performs no repeated shader keyword searches or empty block copies");
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) render();
        Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "snapshot-shaped warm submission allocates zero bytes");
        foreach (var owner in owners) InstallationBatchRenderer.Unregister(owner);
        WorldVisualUpdateManager.Visible.Clear();
    }
}
