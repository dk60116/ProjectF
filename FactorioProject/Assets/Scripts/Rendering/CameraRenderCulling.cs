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
        private static int cachedBoundsFrame = -1;
        private static Matrix4x4 cachedBoundsMatrix;
        private static Vector2 cachedBoundsMinimum;
        private static Vector2 cachedBoundsMaximum;
        private static bool cachedBoundsValid;

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

        // Returns a conservative XZ cell range for the current frustum. Render systems use
        // this shared result to query their spatial dictionaries instead of scanning every
        // off-screen batch. Exact plane/AABB testing still runs on the returned candidates.
        public bool TryGetVisibleCellRange(
            float cellSize,
            int paddingCells,
            out Vector2Int minimum,
            out Vector2Int maximum)
        {
            minimum = default;
            maximum = default;
            if (!Enabled || cellSize <= 0f || !TryGetVisibleWorldBounds(out Vector2 worldMinimum, out Vector2 worldMaximum))
            {
                return false;
            }

            float normalizedCellSize = Mathf.Max(0.01f, cellSize);
            int padding = Mathf.Max(0, paddingCells);
            minimum = new Vector2Int(
                Mathf.FloorToInt(worldMinimum.x / normalizedCellSize) - padding,
                Mathf.FloorToInt(worldMinimum.y / normalizedCellSize) - padding);
            maximum = new Vector2Int(
                Mathf.FloorToInt(worldMaximum.x / normalizedCellSize) + padding,
                Mathf.FloorToInt(worldMaximum.y / normalizedCellSize) + padding);
            return minimum.x <= maximum.x && minimum.y <= maximum.y;
        }

        public static long GetCellCount(Vector2Int minimum, Vector2Int maximum)
        {
            long width = (long)maximum.x - minimum.x + 1L;
            long height = (long)maximum.y - minimum.y + 1L;
            return width > 0L && height > 0L ? width * height : long.MaxValue;
        }

        private bool TryGetVisibleWorldBounds(out Vector2 minimum, out Vector2 maximum)
        {
            int frame = Time.frameCount;
            if (cachedBoundsFrame != frame || !cachedBoundsMatrix.Equals(cullingMatrix))
            {
                cachedBoundsFrame = frame;
                cachedBoundsMatrix = cullingMatrix;
                cachedBoundsValid = CalculateVisibleWorldBounds(
                    cullingMatrix,
                    out cachedBoundsMinimum,
                    out cachedBoundsMaximum);
            }

            minimum = cachedBoundsMinimum;
            maximum = cachedBoundsMaximum;
            return cachedBoundsValid;
        }

        private static bool CalculateVisibleWorldBounds(
            Matrix4x4 matrix,
            out Vector2 minimum,
            out Vector2 maximum)
        {
            minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            Matrix4x4 inverse = matrix.inverse;
            for (int z = 0; z < 2; z++)
            {
                float clipZ = z == 0 ? -1f : 1f;
                for (int y = 0; y < 2; y++)
                {
                    float clipY = y == 0 ? -1f : 1f;
                    for (int x = 0; x < 2; x++)
                    {
                        float clipX = x == 0 ? -1f : 1f;
                        Vector4 world = inverse * new Vector4(clipX, clipY, clipZ, 1f);
                        if (Mathf.Abs(world.w) <= 0.000001f)
                        {
                            return false;
                        }

                        float worldX = world.x / world.w;
                        float worldZ = world.z / world.w;
                        if (float.IsNaN(worldX) || float.IsInfinity(worldX)
                            || float.IsNaN(worldZ) || float.IsInfinity(worldZ))
                        {
                            return false;
                        }

                        minimum = Vector2.Min(minimum, new Vector2(worldX, worldZ));
                        maximum = Vector2.Max(maximum, new Vector2(worldX, worldZ));
                    }
                }
            }

            return minimum.x <= maximum.x && minimum.y <= maximum.y;
        }

        // Fully visible batches need no per-instance scan. Only boundary batches are compacted.
        public bool Contains(Bounds bounds)
        {
            if (!Enabled) return true;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int i = 0; i < planes.Length; i++)
            {
                Vector3 normal = planes[i].normal;
                float radius = Mathf.Abs(normal.x) * extents.x + Mathf.Abs(normal.y) * extents.y
                    + Mathf.Abs(normal.z) * extents.z;
                if (Vector3.Dot(normal, center) + planes[i].distance < radius) return false;
            }
            return true;
        }
    }
}
