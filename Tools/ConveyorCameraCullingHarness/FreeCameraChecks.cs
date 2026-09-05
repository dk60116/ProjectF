using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using ProjectF.Rendering;

public partial class PlayerCameraCullingProbe
{
    private bool hasSavedFreeCameraProjection = true;
    private Camera cachedCamera;
    private Matrix4x4 savedPlayerCullingMatrix;
    private Vector3 savedPlayerCullingFocus;
    private Transform target = new Transform(), focusTarget;
    private Vector3 currentFocus;
    private Vector3 ResolveFollowFocusPosition() => currentFocus;

    public PlayerCameraCullingProbe(Camera camera, Matrix4x4 matrix)
    {
        cachedCamera = camera;
        savedPlayerCullingMatrix = matrix;
    }
    public void MovePlayer(Vector3 position) { currentFocus = position; RefreshPlayerCullingView(); }
    public void LoseTarget() { target = null; RefreshPlayerCullingView(); }
}

public static class FreeCameraChecks
{
    public static void Check()
    {
        GameManager.Instance.DisableCameraCulling = false;
        GameManager.Instance.FreeCamera = true;
        GameManager.Instance.FreeCameraPlayerCulling = true;
        var camera = Checks.View(100);
        var playerMatrix = Checks.View(0).cullingMatrix;
        var probe = new PlayerCameraCullingProbe(camera, playerMatrix);
        probe.MovePlayer(Vector3.zero);
        var helper = new CameraRenderCulling();
        var playerObject = new Bounds(Vector3.zero, Vector3.one);
        var freeViewObject = new Bounds(new Vector3(100, 0, 0), Vector3.one);
        helper.Update(camera);
        Checks.Require(helper.Intersects(playerObject) && !helper.Intersects(freeViewObject),
            "free camera observes player frustum in custom batch and animation helper");
        camera.cullingMatrix = Checks.View(200).cullingMatrix;
        helper.Update(camera);
        Checks.Require(helper.Intersects(playerObject), "free camera movement does not move player culling");
        probe.MovePlayer(new Vector3(40, 0, 0));
        helper.Update(camera);
        Checks.Require(helper.Intersects(new Bounds(new Vector3(40,0,0),Vector3.one))
            && !helper.Intersects(playerObject), "saved player frustum follows live player movement");
        probe.LoseTarget();
        helper.Update(camera);
        Checks.Require(helper.Intersects(playerObject), "missing player target never follows free camera position");

        GameManager.Instance.FreeCameraPlayerCulling = false;
        helper.Update(camera);
        Checks.Require(helper.Intersects(new Bounds(new Vector3(200,0,0),Vector3.one))
            && !helper.Intersects(playerObject), "unchecked toggle restores free camera culling");
        GameManager.Instance.FreeCameraPlayerCulling = true;
        GameManager.Instance.FreeCamera = false;
        helper.Update(camera);
        Checks.Require(!helper.Intersects(playerObject), "player-view toggle is inert outside free camera mode");
        GameManager.Instance.FreeCamera = true;
        var anotherCamera = Checks.View(100);
        helper.Update(anotherCamera);
        Checks.Require(helper.Intersects(freeViewObject), "player-view override is scoped to its owning camera");
        helper.Update(null);
        Checks.Require(helper.Intersects(freeViewObject), "missing render camera retains no-culling fallback");

        var host = new GameObject();
        WorldCameraCulling.EnsureFor(host);
        var service = host.GetComponent<WorldCameraCulling>();
        Lifecycle(service, "OnEnable");
        Matrix4x4 original = camera.cullingMatrix;
        Matrix4x4 projection = camera.projectionMatrix;
        RenderPipelineManager.Begin(camera);
        Checks.Require(camera.cullingMatrix.Equals(playerMatrix) && !camera.useOcclusionCulling,
            "native renderers use the same player frustum without free-eye occlusion");
        Checks.Require(camera.projectionMatrix.Equals(projection),
            "inspection preserves free camera screen projection");
        RenderPipelineManager.End(camera);
        Checks.Require(camera.cullingMatrix.Equals(original) && camera.useOcclusionCulling,
            "render end restores native camera matrix and occlusion preference");
        GameManager.Instance.DisableCameraCulling = true;
        helper.Update(camera);
        Checks.Require(helper.Intersects(playerObject) && helper.Intersects(freeViewObject),
            "disable-culling toggle takes precedence over player-view inspection");
        RenderPipelineManager.Begin(camera);
        Checks.Require(camera.cullingMatrix.Equals(WorldCameraCulling.CreateUnculledWorldMatrix()),
            "native path also honors disable-culling precedence");
        RenderPipelineManager.End(camera);
        GameManager.Instance.DisableCameraCulling = false;
        CameraRenderCulling.ClearPlayerView(anotherCamera);
        Checks.Require(CameraRenderCulling.TryGetPlayerView(camera, out _),
            "unrelated camera cannot clear player view");
        CameraRenderCulling.ClearPlayerView(camera);
        Checks.Require(!CameraRenderCulling.TryGetPlayerView(camera, out _),
            "leaving free camera or disabling owner releases player view");
        Lifecycle(service, "OnDisable");
        GameManager.Instance.FreeCamera = false;
        GameManager.Instance.FreeCameraPlayerCulling = false;
    }
    private static void Lifecycle(WorldCameraCulling service, string method) =>
        typeof(WorldCameraCulling).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(service, null);
}

