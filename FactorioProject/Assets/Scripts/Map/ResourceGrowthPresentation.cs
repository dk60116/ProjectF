using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Plant traces and growth gauges use instanced quads; no per-plant Canvas/child objects.
internal sealed class ResourceGrowthPresentation : IDisposable
{
    private const int Capacity = 1023;
    private static readonly int GrowthDataId = Shader.PropertyToID("_GrowthData");
    private static readonly int RequirementsId = Shader.PropertyToID("_Requirements");
    private readonly Matrix4x4[] matrices = new Matrix4x4[Capacity];
    private readonly Vector4[] data = new Vector4[Capacity];
    private readonly Vector4[] requirements = new Vector4[Capacity];
    private readonly Plane[] planes = new Plane[6];
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private Camera camera;
    private Mesh quad;
    private Material gaugeMaterial, traceMaterial;

    internal void Render(HashSet<ResourceInstance> resources, int layer)
    {
        if (camera == null || !camera.isActiveAndEnabled) camera = Camera.main;
        if (camera == null || !EnsureMaterials()) return;
        GeometryUtility.CalculateFrustumPlanes(camera, planes);
        for (int pass = 0; pass < 2; pass++)
        {
            int count = 0;
            bool trace = pass == 0;
            foreach (ResourceInstance resource in resources)
            {
                if (!(resource is ProjectF.MapObjects.TreeInstance tree) || !tree.IsRuntimeActive || tree.ResourceCount <= 0) continue;
                if (trace ? tree.Growth > 0.0001f
                    : tree.Definition == null || !tree.Definition.HasGrowthSchedule || !tree.CanGrowAnotherLevel) continue;
                Vector3 position = trace ? tree.WorldPosition + Vector3.up * 0.012f
                    : tree.FocusPoint + Vector3.up * 0.3f;
                if (!GeometryUtility.TestPlanesAABB(planes, new Bounds(position, Vector3.one))) continue;
                matrices[count] = Matrix4x4.TRS(position, trace ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity,
                    Vector3.one * (trace ? 0.5f : 0.288f));
                bool met = tree.AreCurrentGrowthRequirementsMet;
                if (trace) data[count] = new Vector4(0f, 0f, 0f, -1f);
                else
                {
                    Vector3 target = new Vector3(
                        Ratio(tree.CurrentGrowthWaterLiters, tree.RequiredGrowthWaterLiters),
                        Ratio(tree.CurrentGrowthFertilizerAmount, tree.RequiredGrowthFertilizerAmount),
                        met ? Ratio(tree.GrowthElapsedSeconds, tree.Definition.GrowthDurationPerLevelSeconds) : 0f);
                    Vector3 fill = tree.SharedGaugeFill;
                    float lerp = 1f - Mathf.Exp(-8f * Mathf.Max(0f, Time.deltaTime));
                    for (int axis = 0; axis < 3; axis++)
                        fill[axis] = target[axis] <= fill[axis] ? target[axis] : Mathf.Lerp(fill[axis], target[axis], lerp);
                    tree.SharedGaugeFill = fill;
                    data[count] = new Vector4(fill.x, fill.y, fill.z, met ? 1f : 0f);
                }
                requirements[count] = new Vector4(tree.RequiredGrowthWaterLiters > 0.0001f ? 1f : 0f,
                    tree.RequiredGrowthFertilizerAmount > 0.0001f ? 1f : 0f, 0f, 0f);
                if (++count == Capacity)
                {
                    Draw(count, trace, layer);
                    count = 0;
                }
            }
            if (count > 0) Draw(count, trace, layer);
        }
    }

    private static float Ratio(float value, float required) => required <= 0.0001f ? 1f : Mathf.Clamp01(value / required);

    private bool EnsureMaterials()
    {
        if (gaugeMaterial != null) return true;
        // The Resources material also keeps the instancing variant in player builds.
        Material template = Resources.Load<Material>("ResourceGrowthGauge");
        if (template == null) return false;
        gaugeMaterial = new Material(template) { name = "Shared resource growth gauges", enableInstancing = true };
        traceMaterial = new Material(template) { name = "Shared resource seed traces", enableInstancing = true };
        traceMaterial.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
        quad = new Mesh { name = "Shared resource gauge quad" };
        quad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f), new Vector3(0.5f, -0.5f) };
        quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        quad.RecalculateBounds();
        return true;
    }

    private void Draw(int count, bool trace, int layer)
    {
        properties.SetVectorArray(GrowthDataId, data);
        properties.SetVectorArray(RequirementsId, requirements);
        Graphics.DrawMeshInstanced(quad, 0, trace ? traceMaterial : gaugeMaterial, matrices, count, properties,
            ShadowCastingMode.Off, false, layer, camera, LightProbeUsage.Off);
    }

    public void Dispose()
    {
        if (quad != null) UnityEngine.Object.Destroy(quad);
        if (gaugeMaterial != null) UnityEngine.Object.Destroy(gaugeMaterial);
        if (traceMaterial != null) UnityEngine.Object.Destroy(traceMaterial);
    }
}
