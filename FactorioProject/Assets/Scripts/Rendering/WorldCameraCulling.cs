using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectF.Rendering
{
    // Native Renderer/Terrain/particles and submitted meshes share camera culling.
    // Player-view inspection overrides only culling; the free camera still controls screen projection.
    [DisallowMultipleComponent, DefaultExecutionOrder(-32000)]
    public sealed class WorldCameraCulling : MonoBehaviour
    {
        private readonly Dictionary<Camera, CameraState> overrides = new Dictionary<Camera, CameraState>(4);

        public static void EnsureFor(GameObject owner)
        {
            if (owner.GetComponent<WorldCameraCulling>() == null)
                owner.AddComponent<WorldCameraCulling>();
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
            Camera.onPreCull += BeginBuiltInCamera;
            Camera.onPostRender += EndBuiltInCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            Camera.onPreCull -= BeginBuiltInCamera;
            Camera.onPostRender -= EndBuiltInCamera;
            RestoreAll();
        }

        // Also recover if rendering was interrupted before an end-camera callback.
        private void LateUpdate() => RestoreAll();

        private void BeginCameraRendering(ScriptableRenderContext context, Camera camera) => BeginCamera(camera);
        private void EndCameraRendering(ScriptableRenderContext context, Camera camera) => EndCamera(camera);

        private void BeginBuiltInCamera(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null)
                BeginCamera(camera);
        }

        private void EndBuiltInCamera(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null)
                EndCamera(camera);
        }

        private void BeginCamera(Camera camera)
        {
            if (!Application.isPlaying || camera == null || camera.cameraType != CameraType.Game
                || GameManager.Instance == null)
                return;

            bool disableCulling = CameraRenderCulling.Disabled;
            if (!CameraRenderCulling.TryGetPlayerView(camera, out Matrix4x4 playerMatrix) && !disableCulling)
                return;

            if (overrides.TryGetValue(camera, out CameraState nestedState))
            {
                nestedState.Depth++;
                overrides[camera] = nestedState;
                return;
            }

            Matrix4x4 originalMatrix = camera.cullingMatrix;
            camera.ResetCullingMatrix();
            CameraState state = new CameraState
            {
                Matrix = originalMatrix,
                AutomaticMatrix = originalMatrix.Equals(camera.cullingMatrix),
                OcclusionCulling = camera.useOcclusionCulling,
                Depth = 1
            };
            overrides.Add(camera, state);
            camera.cullingMatrix = disableCulling ? CreateUnculledWorldMatrix() : playerMatrix;
            camera.useOcclusionCulling = false;
        }

        private void EndCamera(Camera camera)
        {
            if (camera == null || !overrides.TryGetValue(camera, out CameraState state))
                return;
            if (--state.Depth > 0)
            {
                overrides[camera] = state;
                return;
            }
            Restore(camera, state);
            overrides.Remove(camera);
        }

        private void RestoreAll()
        {
            foreach (KeyValuePair<Camera, CameraState> pair in overrides)
                Restore(pair.Key, pair.Value);
            overrides.Clear();
        }

        private static void Restore(Camera camera, CameraState state)
        {
            if (camera == null)
                return;
            if (state.AutomaticMatrix)
                camera.ResetCullingMatrix();
            else
                camera.cullingMatrix = state.Matrix;
            camera.useOcclusionCulling = state.OcclusionCulling;
        }

        internal static Matrix4x4 CreateUnculledWorldMatrix()
        {
            // Homogeneously scaled orthographic clip volume, +/- 10 billion world units.
            // Covers the entire int-coordinate map, including objects behind the camera.
            // Unit plane normals avoid precision loss from an extremely small projection scale.
            // Only CPU visibility uses this matrix: screen projection, clipping, and layers stay intact.
            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.m22 = -1f;
            matrix.m33 = 10000000000f;
            return matrix;
        }

        private struct CameraState
        {
            public Matrix4x4 Matrix;
            public bool AutomaticMatrix;
            public bool OcclusionCulling;
            public int Depth;
        }
    }
}
