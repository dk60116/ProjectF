using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent, DefaultExecutionOrder(999)]
public sealed class BuildingWorldView : MonoBehaviour
{
    private readonly List<Vector3> collisionVertices = new List<Vector3>(1024);
    private readonly List<int> collisionTriangles = new List<int>(2048);
    private BuildingWorld world;
    private MeshCollider collision;
    private Mesh collisionMesh;

    internal static BuildingWorldView Create(BuildingWorld owner, Transform parent, string hostName)
    {
        var root = new GameObject(hostName);
        root.transform.SetParent(parent, false);
        var view = root.AddComponent<BuildingWorldView>();
        view.world = owner;
        view.collision = root.AddComponent<MeshCollider>();
        view.collisionMesh = new Mesh
        {
            name = "BuildingWorld Collision",
            hideFlags = HideFlags.HideAndDontSave,
            indexFormat = IndexFormat.UInt32
        };
        return view;
    }

    internal void RebuildCollision()
    {
        if (world == null || collision == null || collisionMesh == null) return;
        collisionVertices.Clear();
        collisionTriangles.Clear();
        world.AppendCollisionGeometry(transform.worldToLocalMatrix, collisionVertices, collisionTriangles);
        collision.sharedMesh = null;
        collisionMesh.Clear();
        collisionMesh.SetVertices(collisionVertices);
        collisionMesh.SetTriangles(collisionTriangles, 0, false);
        collisionMesh.RecalculateBounds();
        collision.sharedMesh = collisionVertices.Count > 0 ? collisionMesh : null;
    }

    internal void Release()
    {
        world?.SuspendRendering();
        world = null;
        if (collision != null) collision.sharedMesh = null;
        if (collisionMesh != null)
        {
            if (Application.isPlaying) Destroy(collisionMesh); else DestroyImmediate(collisionMesh);
            collisionMesh = null;
        }
        gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }

    private void LateUpdate()
    {
        using var callerSample = MapObjectTickProfiler.SampleLateUpdateCaller<BuildingWorldView>();
        world?.Render(Time.deltaTime);
    }

    private void OnDisable() => world?.SuspendRendering();
    private void OnDestroy()
    {
        if (collisionMesh != null)
        {
            if (Application.isPlaying) Destroy(collisionMesh); else DestroyImmediate(collisionMesh);
            collisionMesh = null;
        }
        world?.OnViewDestroyed(this);
    }
}
