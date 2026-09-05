using UnityEngine;

namespace ProjectF.Rendering
{
    // Camera LateUpdate (default order) must finish before visibility and draw preparation.
    // Keep TerrainGenerator's simulation Update at its existing execution order.
    [DisallowMultipleComponent, DefaultExecutionOrder(900)]
    public sealed class TerrainWorldRenderer : MonoBehaviour
    {
        private TerrainGenerator terrain;

        internal static void EnsureFor(TerrainGenerator owner)
        {
            TerrainWorldRenderer renderer = owner.GetComponent<TerrainWorldRenderer>();
            if (renderer == null)
                renderer = owner.gameObject.AddComponent<TerrainWorldRenderer>();
            renderer.terrain = owner;
        }

        private void LateUpdate()
        {
            if (terrain != null && terrain.isActiveAndEnabled)
                terrain.RenderWorldVisuals();
        }
    }
}
