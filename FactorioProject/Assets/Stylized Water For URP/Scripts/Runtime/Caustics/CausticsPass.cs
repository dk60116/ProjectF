//━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━																												
// Copyright 2020, Alexander Ameye, All rights reserved.
// https://alexander-ameye.gitbook.io/stylized-water/
//━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━	

#if UNIVERSAL_RENDERER
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace StylizedWater
{
    public class CausticsPass : ScriptableRenderPass
    {
        private const string profilerTag = "Caustics Pass";

        public Material causticsMaterial;
        private static Mesh mesh;
        private readonly float waterLevel;
        private readonly MaterialPropertyBlock materialProperties = new MaterialPropertyBlock();

        private const float BIAS = 0.1f;
        private static readonly int MainLightDirection = Shader.PropertyToID("_MainLightDirection");

        private sealed class PassData
        {
            public Material material;
            public MaterialPropertyBlock materialProperties;
            public Mesh mesh;
            public Matrix4x4 matrix;
        }

        public CausticsPass(float waterLevel)
        {
            this.waterLevel = waterLevel;
            ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            Camera cam = cameraData.camera;

            if (cam.cameraType == CameraType.Preview || !causticsMaterial) return;

            var sunMatrix = RenderSettings.sun != null
                        ? RenderSettings.sun.transform.localToWorldMatrix
                        : Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(-45f, 45f, 0f), Vector3.one);
            materialProperties.SetMatrix(MainLightDirection, sunMatrix);

            if (!mesh) mesh = GenerateQuad(1000f);
            var position = cam.transform.position;
            position.y = cam.transform.position.y > waterLevel ? waterLevel : cam.transform.position.y - BIAS;
            var matrix = Matrix4x4.TRS(position, Quaternion.identity, Vector3.one);

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(profilerTag, out var passData))
            {
                passData.material = causticsMaterial;
                passData.materialProperties = materialProperties;
                passData.mesh = mesh;
                passData.matrix = matrix;

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);

                if (resourceData.cameraOpaqueTexture.IsValid())
                    builder.UseTexture(resourceData.cameraOpaqueTexture, AccessFlags.Read);
                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawMesh(data.mesh, data.matrix, data.material, 0, 0, data.materialProperties);
                });
            }
        }

        private static Mesh GenerateQuad(float size)
        {
            var m = new Mesh();

            size *= 0.5f;

            var verts = new[]
            {
                new Vector3(-size, 0f, -size),
                new Vector3(size, 0f, -size),
                new Vector3(-size, 0f, size),
                new Vector3(size, 0f, size)
            };

            var tris = new[]
            {
                0, 2, 1,
                2, 3, 1
            };

            m.vertices = verts;
            m.triangles = tris;

            return m;
        }
    }
}
#endif
