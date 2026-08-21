using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public static class AnimalScreenSpaceOutline
{
    private static Renderer hoveredRenderer;
    private static Renderer focusedRenderer;

    public static Renderer ActiveRenderer
    {
        get
        {
            focusedRenderer = ResolveActive(focusedRenderer);
            hoveredRenderer = ResolveActive(hoveredRenderer);
            return focusedRenderer != null ? focusedRenderer : hoveredRenderer;
        }
    }

    public static void ShowHovered(Renderer renderer)
    {
        hoveredRenderer = renderer;
    }

    public static void HideHovered(Renderer renderer)
    {
        if (hoveredRenderer == renderer)
        {
            hoveredRenderer = null;
        }
    }

    public static void ShowFocused(Renderer renderer)
    {
        focusedRenderer = renderer;
    }

    public static void HideFocused(Renderer renderer)
    {
        if (focusedRenderer == renderer)
        {
            focusedRenderer = null;
        }
    }

    private static Renderer ResolveActive(Renderer renderer)
    {
        return renderer != null
               && renderer.enabled
               && renderer.gameObject.activeInHierarchy
            ? renderer
            : null;
    }
}

public sealed class AnimalScreenSpaceOutlineRendererFeature : ScriptableRendererFeature
{
    private const string MaskShaderName = "Hidden/ProjectF/AnimalScreenSpaceOutlineMask";
    private const string CompositeShaderName = "Hidden/ProjectF/AnimalScreenSpaceOutlineComposite";

    [SerializeField, Range(1f, 8f)] private float widthPixels = 4f;
    [SerializeField] private Color outlineColor = Color.white;
    [SerializeField, HideInInspector] private Shader maskShader;
    [SerializeField, HideInInspector] private Shader compositeShader;

    private Material maskMaterial;
    private Material compositeMaterial;
    private AnimalOutlinePass outlinePass;

    public override void Create()
    {
        CoreUtils.Destroy(maskMaterial);
        CoreUtils.Destroy(compositeMaterial);

        if (maskShader == null)
        {
            maskShader = Shader.Find(MaskShaderName);
        }

        if (compositeShader == null)
        {
            compositeShader = Shader.Find(CompositeShaderName);
        }

        maskMaterial = maskShader != null ? CoreUtils.CreateEngineMaterial(maskShader) : null;
        compositeMaterial = compositeShader != null ? CoreUtils.CreateEngineMaterial(compositeShader) : null;
        outlinePass = new AnimalOutlinePass(maskMaterial, compositeMaterial)
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Renderer targetRenderer = AnimalScreenSpaceOutline.ActiveRenderer;
        if (targetRenderer == null
            || outlinePass == null
            || maskMaterial == null
            || compositeMaterial == null
            || renderingData.cameraData.cameraType != CameraType.Game
            || renderingData.cameraData.renderType != CameraRenderType.Base)
        {
            return;
        }

        outlinePass.SetTarget(targetRenderer, widthPixels, outlineColor);
        renderer.EnqueuePass(outlinePass);
    }

    public void SetShaders(Shader newMaskShader, Shader newCompositeShader)
    {
        maskShader = newMaskShader;
        compositeShader = newCompositeShader;
    }

    protected override void Dispose(bool disposing)
    {
        outlinePass = null;
        CoreUtils.Destroy(maskMaterial);
        CoreUtils.Destroy(compositeMaterial);
        maskMaterial = null;
        compositeMaterial = null;
    }

    private sealed class AnimalOutlinePass : ScriptableRenderPass
    {
        private static readonly int MaskTextureId = Shader.PropertyToID("_AnimalOutlineMask");
        private static readonly int MaskTexelSizeId = Shader.PropertyToID("_AnimalOutlineMask_TexelSize");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidthPixels");
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Animal Screen-Space Outline");

        private readonly Material maskMaterial;
        private readonly Material compositeMaterial;
        private readonly Renderer[] maskRenderers = new Renderer[32];
        private readonly MaterialPropertyBlock compositeProperties = new MaterialPropertyBlock();
        private Renderer targetRenderer;
        private float widthPixels;
        private Color outlineColor;

        private sealed class MaskPassData
        {
            public Material material;
            public Renderer[] renderers;
            public int rendererCount;
        }

        private sealed class CompositePassData
        {
            public Material material;
            public MaterialPropertyBlock properties;
            public bool useScissor;
            public Rect scissorRect;
        }

        public AnimalOutlinePass(Material maskMaterial, Material compositeMaterial)
        {
            this.maskMaterial = maskMaterial;
            this.compositeMaterial = compositeMaterial;
        }

        public void SetTarget(Renderer renderer, float width, Color color)
        {
            targetRenderer = renderer;
            widthPixels = Mathf.Clamp(width, 1f, 8f);
            outlineColor = color;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (targetRenderer == null
                || maskMaterial == null
                || compositeMaterial == null)
            {
                return;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.cameraType != CameraType.Game
                || cameraData.renderType != CameraRenderType.Base)
            {
                return;
            }

            int maskRendererCount = CollectMaskRenderers(targetRenderer, maskRenderers);
            if (maskRendererCount == 0)
            {
                return;
            }

            Bounds maskBounds = maskRenderers[0].bounds;
            for (int rendererIndex = 1; rendererIndex < maskRendererCount; rendererIndex++)
            {
                maskBounds.Encapsulate(maskRenderers[rendererIndex].bounds);
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            TextureDesc maskDescriptor = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
            maskDescriptor.name = "_AnimalScreenSpaceOutlineMask";
            maskDescriptor.colorFormat = GraphicsFormat.R8_UNorm;
            maskDescriptor.depthBufferBits = DepthBits.None;
            maskDescriptor.msaaSamples = MSAASamples.None;
            maskDescriptor.bindTextureMS = false;
            maskDescriptor.filterMode = FilterMode.Bilinear;
            maskDescriptor.wrapMode = TextureWrapMode.Clamp;
            maskDescriptor.clearBuffer = true;
            maskDescriptor.clearColor = Color.black;
            TextureHandle maskTexture = renderGraph.CreateTexture(maskDescriptor);

            using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>(
                       "Animal Outline Mask",
                       out var passData,
                       ProfilingSampler))
            {
                passData.material = maskMaterial;
                passData.renderers = maskRenderers;
                passData.rendererCount = maskRendererCount;

                // Only the animal pixels are written. Declaring WriteAll lets RenderGraph
                // discard the clear and can leave the previous frame's mask behind.
                builder.SetRenderAttachment(maskTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                builder.SetGlobalTextureAfterPass(maskTexture, MaskTextureId);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) =>
                {
                    for (int rendererIndex = 0; rendererIndex < data.rendererCount; rendererIndex++)
                    {
                        Renderer maskRenderer = data.renderers[rendererIndex];
                        int subMeshCount = ResolveSubMeshCount(maskRenderer);
                        for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
                        {
                            context.cmd.DrawRenderer(maskRenderer, data.material, subMeshIndex, 0);
                        }
                    }
                });
            }

            RenderTextureDescriptor cameraDescriptor = cameraData.cameraTargetDescriptor;
            compositeProperties.SetVector(
                MaskTexelSizeId,
                new Vector4(
                    1f / cameraDescriptor.width,
                    1f / cameraDescriptor.height,
                    cameraDescriptor.width,
                    cameraDescriptor.height));
            compositeProperties.SetColor(OutlineColorId, outlineColor);
            compositeProperties.SetFloat(OutlineWidthId, widthPixels);

            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>(
                       "Animal Outline Composite",
                       out var passData,
                       ProfilingSampler))
            {
                passData.material = compositeMaterial;
                passData.properties = compositeProperties;
                passData.useScissor = TryGetScissorRect(
                    maskBounds,
                    cameraData.camera,
                    cameraDescriptor.width,
                    cameraDescriptor.height,
                    widthPixels,
                    out passData.scissorRect);

                builder.UseTexture(maskTexture, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
                {
                    if (data.useScissor)
                    {
                        context.cmd.EnableScissorRect(data.scissorRect);
                    }

                    context.cmd.DrawProcedural(
                        Matrix4x4.identity,
                        data.material,
                        0,
                        MeshTopology.Triangles,
                        3,
                        1,
                        data.properties);

                    if (data.useScissor)
                    {
                        context.cmd.DisableScissorRect();
                    }
                });
            }
        }

        private static int ResolveSubMeshCount(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer
                && skinnedMeshRenderer.sharedMesh != null)
            {
                return Mathf.Max(1, skinnedMeshRenderer.sharedMesh.subMeshCount);
            }

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            return meshFilter != null && meshFilter.sharedMesh != null
                ? Mathf.Max(1, meshFilter.sharedMesh.subMeshCount)
                : 1;
        }

        private static int CollectMaskRenderers(Renderer renderer, Renderer[] destination)
        {
            if (renderer == null || destination == null || destination.Length == 0)
            {
                return 0;
            }

            Animal animal = renderer.GetComponentInParent<Animal>();
            if (animal != null)
            {
                int animalRendererCount = animal.CopyOutlineMaskRenderers(destination);
                if (animalRendererCount > 0)
                {
                    return animalRendererCount;
                }
            }

            PortableObject portableObject = renderer.GetComponentInParent<PortableObject>();
            if (portableObject != null)
            {
                int portableRendererCount = portableObject.CopyOutlineMaskRenderers(destination);
                if (portableRendererCount > 0)
                {
                    return portableRendererCount;
                }
            }

            destination[0] = renderer;
            return 1;
        }

        private static bool TryGetScissorRect(
            Bounds bounds,
            Camera camera,
            int targetWidth,
            int targetHeight,
            float outlineWidth,
            out Rect rect)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            float minX = 1f;
            float minY = 1f;
            float maxX = 0f;
            float maxY = 0f;
            bool hasVisibleCorner = false;

            for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                Vector3 corner = center + new Vector3(
                    (cornerIndex & 1) == 0 ? -extents.x : extents.x,
                    (cornerIndex & 2) == 0 ? -extents.y : extents.y,
                    (cornerIndex & 4) == 0 ? -extents.z : extents.z);
                Vector3 viewportPoint = camera.WorldToViewportPoint(corner);
                if (viewportPoint.z <= 0f)
                {
                    continue;
                }

                hasVisibleCorner = true;
                minX = Mathf.Min(minX, viewportPoint.x);
                minY = Mathf.Min(minY, viewportPoint.y);
                maxX = Mathf.Max(maxX, viewportPoint.x);
                maxY = Mathf.Max(maxY, viewportPoint.y);
            }

            if (!hasVisibleCorner)
            {
                rect = default;
                return false;
            }

            float padding = Mathf.Ceil(outlineWidth) + 2f;
            float xMin = Mathf.Clamp(Mathf.Floor(minX * targetWidth - padding), 0f, targetWidth);
            float yMin = Mathf.Clamp(Mathf.Floor(minY * targetHeight - padding), 0f, targetHeight);
            float xMax = Mathf.Clamp(Mathf.Ceil(maxX * targetWidth + padding), 0f, targetWidth);
            float yMax = Mathf.Clamp(Mathf.Ceil(maxY * targetHeight + padding), 0f, targetHeight);
            rect = new Rect(xMin, yMin, Mathf.Max(1f, xMax - xMin), Mathf.Max(1f, yMax - yMin));
            return true;
        }
    }
}
