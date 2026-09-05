using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using ProjectF.Rendering;

namespace UnityEngine
{
    public class MonoBehaviour { }
    public static class Application { public static bool isPlaying = true; }
}
namespace UnityEngine.Rendering
{
    public struct ScriptableRenderContext { }
    public static class GraphicsSettings { public static object currentRenderPipeline = new(); }
    public static class RenderPipelineManager
    {
        public static event Action<ScriptableRenderContext, Camera> beginCameraRendering, endCameraRendering;
        public static void Begin(Camera camera) => beginCameraRendering?.Invoke(default, camera);
        public static void End(Camera camera) => endCameraRendering?.Invoke(default, camera);
    }
}

public static class WorldChecks
{
    private static void Lifecycle(WorldCameraCulling owner, string name) =>
        typeof(WorldCameraCulling).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null);

    public static void Check()
    {
        var owner = new GameObject();
        WorldCameraCulling.EnsureFor(owner);
        var service = owner.GetComponent<WorldCameraCulling>();
        WorldCameraCulling.EnsureFor(owner);
        Checks.Require(ReferenceEquals(service, owner.GetComponent<WorldCameraCulling>()), "manager registration is idempotent");
        Lifecycle(service, "OnEnable");
        var camera = new Camera { AutomaticCullingMatrix = Checks.View(25).cullingMatrix, cullingMask = 3 };
        var projection = camera.projectionMatrix;
        GameManager.Instance.DisableCameraCulling = false;
        RenderPipelineManager.Begin(camera);
        Checks.Require(camera.cullingMatrix.Equals(camera.AutomaticCullingMatrix) && camera.useOcclusionCulling,
            "default ON leaves native world culling untouched");
        RenderPipelineManager.End(camera);

        GameManager.Instance.DisableCameraCulling = true;
        RenderPipelineManager.Begin(camera);
        Checks.Require(!camera.cullingMatrix.Equals(camera.AutomaticCullingMatrix) && !camera.useOcclusionCulling,
            "OFF bypasses native camera frustum and occlusion rejection");
        Checks.Require(camera.projectionMatrix.Equals(projection) && camera.cullingMask == 3,
            "world toggle preserves screen projection and layer visibility");
        var planes = new Plane[6];
        GeometryUtility.CalculateFrustumPlanes(camera.cullingMatrix, planes);
        bool coversWorld = true;
        foreach (var point in new[] { new Vector3(int.MinValue, 0, 0), new Vector3(int.MaxValue, 0, 0),
            new Vector3(0, int.MinValue, 0), new Vector3(0, int.MaxValue, 0),
            new Vector3(0, 0, int.MinValue), new Vector3(0, 0, int.MaxValue) })
            coversWorld &= GeometryUtility.TestPlanesAABB(planes, new Bounds(point, Vector3.one));
        Checks.Require(coversWorld, "unculled volume includes the int-coordinate world in all six directions");

        RenderPipelineManager.Begin(camera);
        RenderPipelineManager.End(camera);
        Checks.Require(!camera.useOcclusionCulling, "nested camera begin/end retains the outer override");
        RenderPipelineManager.End(camera);
        camera.AutomaticCullingMatrix = Checks.View(50).cullingMatrix;
        Checks.Require(camera.cullingMatrix.Equals(camera.AutomaticCullingMatrix) && camera.useOcclusionCulling,
            "end callback restores automatic camera tracking after movement");

        camera.cullingMatrix = Checks.View(80).cullingMatrix;
        Matrix4x4 custom = camera.cullingMatrix;
        camera.useOcclusionCulling = false;
        RenderPipelineManager.Begin(camera); RenderPipelineManager.End(camera);
        Checks.Require(camera.cullingMatrix.Equals(custom) && !camera.useOcclusionCulling,
            "preexisting custom culling matrix and occlusion preference survive");

        var second = new Camera();
        RenderPipelineManager.Begin(camera); RenderPipelineManager.Begin(second);
        RenderPipelineManager.End(camera);
        Checks.Require(camera.cullingMatrix.Equals(custom) && !second.useOcclusionCulling,
            "multiple game cameras restore independently");
        Lifecycle(service, "LateUpdate");
        Checks.Require(second.useOcclusionCulling && second.cullingMatrix.Equals(second.AutomaticCullingMatrix),
            "interrupted rendering recovers before the next frame");

        var preview = new Camera { cameraType = CameraType.SceneView };
        RenderPipelineManager.Begin(preview);
        Checks.Require(preview.cullingMatrix.Equals(preview.AutomaticCullingMatrix) && preview.useOcclusionCulling,
            "editor and preview cameras are not modified");
        Application.isPlaying = false;
        RenderPipelineManager.Begin(second);
        Checks.Require(second.useOcclusionCulling, "edit mode does not override game-camera state");
        Application.isPlaying = true;

        Camera.Begin(second);
        Checks.Require(second.useOcclusionCulling, "built-in callback cannot double-apply during SRP rendering");
        GraphicsSettings.currentRenderPipeline = null;
        Camera.Begin(second);
        Checks.Require(!second.useOcclusionCulling, "built-in pipeline also receives the toggle");
        Camera.End(second);
        Checks.Require(second.useOcclusionCulling, "built-in post-render restores camera state");
        GraphicsSettings.currentRenderPipeline = new();

        RenderPipelineManager.Begin(second);
        Lifecycle(service, "OnDisable");
        Checks.Require(second.useOcclusionCulling, "disabling the service restores outstanding overrides");
        RenderPipelineManager.Begin(second);
        Checks.Require(second.useOcclusionCulling, "service disable removes rendering callbacks");
        GameManager.Instance.DisableCameraCulling = false;
    }
}
