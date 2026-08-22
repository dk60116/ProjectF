using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class PortableBucketWaterVisual : MonoBehaviour
{
    public const string SurfaceObjectName = "Water Surface";

    private const int CircleSegments = 16;
    private static readonly Vector3 PortableSurfaceLocalPosition = new Vector3(0f, 0.075f, 0f);
    private static readonly Vector3 PortableSurfaceLocalScale = new Vector3(0.08f, 1f, 0.08f);
    // Start near the installed bucket's inner bottom so the surface has enough
    // vertical travel to visibly rise toward the authored Water Bucket surface.
    private const float InstalledFillBottomY = 0.075f;
    private const float InstalledFillBottomRadius = 0.065f;
    private static Mesh sharedCircleMesh;

    private Transform surfaceTransform;
    private MeshRenderer surfaceRenderer;
    private float fillRatio;

    public bool IsSurfaceVisible => surfaceRenderer != null && surfaceRenderer.enabled;
    public Renderer OutlineFillRenderer => null;
    public int SurfaceVertexCount => sharedCircleMesh != null ? sharedCircleMesh.vertexCount : 0;
    public float FillRatio => fillRatio;
    public Material SurfaceMaterial => surfaceRenderer != null
        ? surfaceRenderer.sharedMaterial
        : null;
    public float SurfaceLocalY => surfaceTransform != null
        ? surfaceTransform.localPosition.y
        : float.NaN;

    public void Refresh(
        Bucket bucket,
        int fluidItemId,
        MeshFilter body,
        bool ownerVisualVisible)
    {
        bool containsFluid = fluidItemId >= 0;
        Refresh(bucket, fluidItemId, body, ownerVisualVisible, containsFluid ? 1f : 0f, false);
    }

    public void Refresh(
        Bucket bucket,
        int fluidItemId,
        MeshFilter body,
        bool ownerVisualVisible,
        float requestedFillRatio,
        bool animateInstalledFill)
    {
        fillRatio = Mathf.Clamp01(requestedFillRatio);
        Material surfaceMaterial = bucket != null
            ? bucket.ResolveFluidSurfaceMaterial(fluidItemId)
            : null;
        bool shouldShow = bucket != null
                          && fluidItemId >= 0
                          && fillRatio > 0.0001f
                          && body != null
                          && surfaceMaterial != null;
        if (!shouldShow)
        {
            SetVisible(false);
            return;
        }

        EnsureSurface(
            bucket,
            body.transform,
            TryGetComponent(out PortableObject _),
            animateInstalledFill,
            fillRatio,
            fluidItemId);
        surfaceTransform.gameObject.layer = body.gameObject.layer;
        surfaceRenderer.sharedMaterial = surfaceMaterial;
        SetVisible(ownerVisualVisible);
    }

    private void EnsureSurface(
        Bucket bucket,
        Transform bodyTransform,
        bool usePortableTransform,
        bool animateInstalledFill,
        float requestedFillRatio,
        int fluidItemId)
    {
        if (surfaceTransform == null)
        {
            surfaceTransform = bodyTransform.Find(SurfaceObjectName);
            GameObject surfaceObject = surfaceTransform != null
                ? surfaceTransform.gameObject
                : new GameObject(SurfaceObjectName);
            surfaceTransform = surfaceObject.transform;
            MeshFilter surfaceFilter = surfaceObject.GetComponent<MeshFilter>();
            if (surfaceFilter == null)
            {
                surfaceFilter = surfaceObject.AddComponent<MeshFilter>();
            }

            surfaceRenderer = surfaceObject.GetComponent<MeshRenderer>();
            if (surfaceRenderer == null)
            {
                surfaceRenderer = surfaceObject.AddComponent<MeshRenderer>();
            }

            if (surfaceFilter.sharedMesh == null)
            {
                surfaceFilter.sharedMesh = GetSharedCircleMesh();
            }

            surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
            surfaceRenderer.receiveShadows = false;
            surfaceRenderer.lightProbeUsage = LightProbeUsage.Off;
            surfaceRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            surfaceRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        if (surfaceTransform.parent != bodyTransform)
        {
            surfaceTransform.SetParent(bodyTransform, false);
        }

        if (usePortableTransform)
        {
            surfaceTransform.localPosition = PortableSurfaceLocalPosition;
            surfaceTransform.localRotation = Quaternion.identity;
            surfaceTransform.localScale = PortableSurfaceLocalScale;
        }
        else if (animateInstalledFill)
        {
            float normalizedFill = Mathf.Clamp01(requestedFillRatio);
            bucket.TryGetInstalledFullSurfaceTransform(
                fluidItemId,
                out Vector3 fullLocalPosition,
                out Quaternion fullLocalRotation,
                out Vector3 fullLocalScale);
            surfaceTransform.localPosition = Vector3.Lerp(
                new Vector3(0f, InstalledFillBottomY, 0f),
                fullLocalPosition,
                normalizedFill);
            surfaceTransform.localRotation = Quaternion.Slerp(
                Quaternion.identity,
                fullLocalRotation,
                normalizedFill);
            surfaceTransform.localScale = new Vector3(
                Mathf.Lerp(InstalledFillBottomRadius, fullLocalScale.x, normalizedFill),
                fullLocalScale.y,
                Mathf.Lerp(InstalledFillBottomRadius, fullLocalScale.z, normalizedFill));
        }
    }

    private void SetVisible(bool visible)
    {
        if (surfaceRenderer != null)
        {
            surfaceRenderer.enabled = visible;
        }
    }

    private static Mesh GetSharedCircleMesh()
    {
        if (sharedCircleMesh != null)
        {
            return sharedCircleMesh;
        }

        sharedCircleMesh = CreateCircleMesh("Portable Bucket Water Circle");
        sharedCircleMesh.hideFlags = HideFlags.HideAndDontSave;
        sharedCircleMesh.UploadMeshData(true);
        return sharedCircleMesh;
    }

    public static Mesh CreateCircleMesh(string meshName)
    {
        Vector3[] vertices = new Vector3[CircleSegments + 1];
        Vector3[] normals = new Vector3[CircleSegments + 1];
        Vector2[] uv = new Vector2[CircleSegments + 1];
        int[] triangles = new int[CircleSegments * 3];
        vertices[0] = Vector3.zero;
        normals[0] = Vector3.up;
        uv[0] = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < CircleSegments; i++)
        {
            float angle = (Mathf.PI * 2f * i) / CircleSegments;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);
            int vertexIndex = i + 1;
            vertices[vertexIndex] = new Vector3(x, 0f, z);
            normals[vertexIndex] = Vector3.up;
            uv[vertexIndex] = new Vector2((x + 1f) * 0.5f, (z + 1f) * 0.5f);

            int triangleIndex = i * 3;
            triangles[triangleIndex] = 0;
            triangles[triangleIndex + 1] = ((i + 1) % CircleSegments) + 1;
            triangles[triangleIndex + 2] = vertexIndex;
        }

        return new Mesh
        {
            name = string.IsNullOrWhiteSpace(meshName) ? "Bucket Water Circle" : meshName,
            vertices = vertices,
            normals = normals,
            uv = uv,
            triangles = triangles,
            bounds = new Bounds(Vector3.zero, new Vector3(2f, 0.01f, 2f))
        };
    }
}
