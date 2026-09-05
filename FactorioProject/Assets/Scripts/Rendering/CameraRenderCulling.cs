using UnityEngine;

namespace ProjectF.Rendering
{
    // View-only state. Never suspends simulation roots or changes item readiness.
    public sealed class CameraRenderCulling
    {
        private readonly Plane[] planes = new Plane[6];
        private Camera camera;
        private Matrix4x4 cullingMatrix;
        private int layerMask;
        private bool initialized;
        private static Camera playerViewCamera;
        private static Matrix4x4 playerViewMatrix;

        internal static void SetPlayerView(Camera owner, Matrix4x4 matrix)
        {
            playerViewCamera = owner;
            playerViewMatrix = matrix;
        }

        internal static void ClearPlayerView(Camera owner)
        {
            if (playerViewCamera == owner)
                playerViewCamera = null;
        }

        internal static bool TryGetPlayerView(Camera renderCamera, out Matrix4x4 matrix)
        {
            matrix = playerViewMatrix;
            GameManager gameManager = GameManager.Instance;
            return renderCamera != null && playerViewCamera == renderCamera
                && gameManager != null && !gameManager.DisableCameraCulling
                && gameManager.FreeCamera && gameManager.FreeCameraPlayerCulling;
        }

        public bool Enabled { get; private set; }
        public int Version { get; private set; }
        public static bool Disabled => GameManager.Instance != null && GameManager.Instance.DisableCameraCulling;

        public bool Update(Camera renderCamera)
        {
            bool enabled = renderCamera != null && !Disabled;
            Matrix4x4 matrix = Matrix4x4.identity;
            if (enabled && !TryGetPlayerView(renderCamera, out matrix))
                matrix = renderCamera.cullingMatrix;
            int mask = enabled ? renderCamera.cullingMask : -1;
            if (initialized && camera == renderCamera && Enabled == enabled
                && cullingMatrix.Equals(matrix) && layerMask == mask)
                return false;

            initialized = true;
            camera = renderCamera;
            Enabled = enabled;
            cullingMatrix = matrix;
            layerMask = mask;
            unchecked { Version++; }
            if (enabled)
                GeometryUtility.CalculateFrustumPlanes(matrix, planes);
            return true;
        }

        public bool IsLayerVisible(int layer) => !Enabled || layer < 0 || layer > 31
            || (layerMask & (1 << layer)) != 0;

        public bool IsAnyLayerVisible(int mask) => !Enabled || (layerMask & mask) != 0;

        public bool Intersects(Bounds bounds) => !Enabled || GeometryUtility.TestPlanesAABB(planes, bounds);
    }
}
