using System;
using System.Collections.Generic;

namespace BuildingLifecycleHarness
{
    // Rendering/physics ports only; suppression, render scheduling and geometry selection are production methods.
    public struct Vector3 { }
    public struct Matrix4x4
    {
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => default;
    }
    public sealed class Camera
    {
        public bool isActiveAndEnabled = true;
        public static Camera main = new Camera();
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static bool Approximately(float a, float b) => Math.Abs(a-b) < 0.00001f;
    }
    public sealed class TerrainGenerator
    {
        public bool IsBenchmarkPlacementInProgress;
        public object transform;
    }
    public static class MapObjectTickManager { public static bool WaitingForWorldLoad; }
    public sealed class Batches
    {
        public void SuspendRendering() { }
        public void RenderBatches(Camera camera, float size) { }
    }
    public sealed class BuildingRuntimeRecord
    {
        public bool PlacementPresentationSuppressed;
        public float PlacementPresentationScale = 1f;
        public BuildingWorld.BuildingTemplate Template;
        public Matrix4x4 GetRootMatrix() => default;
        public bool IncludeCollider(BuildingWorld.ColliderPart part) => true;
        public Matrix4x4 ApplyDoorLeafRotation(Matrix4x4 local) => local;
        public bool UpdateDoorPresentation(float dt) => false;
    }
    public sealed class BuildingWorldView
    {
        private BuildingWorld world;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        public int Rebuilds;
        public int VertexCount => vertices.Count;
        public static BuildingWorldView Create(BuildingWorld owner, object parent, string hostName)
            => new BuildingWorldView { world = owner };
        public void RebuildCollision()
        {
            Rebuilds++;
            vertices.Clear(); triangles.Clear();
            world.AppendCollisionGeometry(default, vertices, triangles);
        }
    }
    public sealed class BuildingWorld
    {
        public struct ColliderPart
        {
            public Matrix4x4 LocalToRoot;
            public Vector3 Center, Size;
            public bool IsDoorLeaf;
        }
        public sealed class BuildingTemplate { public ColliderPart[] ColliderParts; }
        private const string HostName = "BuildingWorld";
        private const float BatchCellSize = 16f;
        private readonly Dictionary<int, BuildingRuntimeRecord> byStorageKey = new Dictionary<int, BuildingRuntimeRecord>();
        private readonly List<BuildingRuntimeRecord> animatingDoors = new List<BuildingRuntimeRecord>();
        private readonly Batches batches = new Batches();
        private BuildingWorldView view;
        private Camera mainCamera;
        private bool collisionDirty = true, disposed = false;
        private readonly TerrainGenerator Owner = new TerrainGenerator();
        private bool Contains(BuildingRuntimeRecord record) => byStorageKey.ContainsValue(record);
        private void RefreshRecordBatches(BuildingRuntimeRecord record) { }
        private static void AppendBox(Matrix4x4 matrix, Vector3 center, Vector3 size,
            List<Vector3> vertices, List<int> triangles)
        { for (int i = 0; i < 8; i++) vertices.Add(default); }

        // PRODUCTION_ATTACH
        // PRODUCTION_VIEW_DESTROYED
        // PRODUCTION_SUPPRESSION
        // PRODUCTION_SCALE
        // PRODUCTION_RENDER
        // PRODUCTION_GEOMETRY

        private static void Require(bool condition, string message, ref int checks)
        { checks++; if (!condition) throw new Exception(message); }

        public static int Run()
        {
            int checks = 0;
            for (int partCount = 1; partCount <= 4; partCount++)
            {
                var world = new BuildingWorld();
                var record = new BuildingRuntimeRecord
                { Template = new BuildingTemplate { ColliderParts = new ColliderPart[partCount] } };
                world.byStorageKey.Add(1, record);
                world.AttachView();
                world.SetPlacementPresentationSuppressed(record, true);
                world.Render(0.016f);
                Require(world.view.VertexCount == 0, "Hidden placement has no collision geometry", ref checks);
                int rebuilds = world.view.Rebuilds;
                world.Render(0.016f);
                Require(world.view.Rebuilds == rebuilds, "Stationary hidden placement does not rebuild each frame", ref checks);
                world.SetPlacementPresentationSuppressed(record, false);
                world.Render(0.016f);
                Require(world.view.VertexCount == partCount * 8,
                    "Revealed placement must restore collision, parts=" + partCount, ref checks);
                rebuilds = world.view.Rebuilds;
                world.SetPlacementPresentationSuppressed(record, false);
                world.Render(0.016f);
                Require(world.view.Rebuilds == rebuilds, "Repeated reveal avoids redundant rebuild", ref checks);
                world.SetPlacementPresentationScale(record, 0.25f);
                world.Render(0.016f);
                Require(world.view.Rebuilds == rebuilds && world.view.VertexCount == partCount * 8,
                    "Visual pop scale does not remove logical collision", ref checks);
                world.SetPlacementPresentationSuppressed(record, true);
                world.Render(0.016f);
                Require(world.view.VertexCount == 0, "Hiding previously built collision removes it", ref checks);
                MapObjectTickManager.WaitingForWorldLoad = true;
                world.SetPlacementPresentationSuppressed(record, false);
                world.Render(0.016f);
                Require(world.view.VertexCount == 0, "Loading barrier defers collision rebuild", ref checks);
                MapObjectTickManager.WaitingForWorldLoad = false;
                world.Render(0.016f);
                Require(world.view.VertexCount == partCount * 8, "Deferred reveal rebuild survives loading barrier", ref checks);
                world.SetPlacementPresentationSuppressed(record, true);
                world.Render(0.016f);
                world.Owner.IsBenchmarkPlacementInProgress = true;
                world.SetPlacementPresentationSuppressed(record, false);
                world.Render(0.016f);
                world.Owner.IsBenchmarkPlacementInProgress = false;
                world.Render(0.016f);
                Require(world.view.VertexCount == partCount * 8, "Reveal survives benchmark placement barrier", ref checks);
                BuildingWorldView previous = world.view;
                world.OnViewDestroyed(previous);
                world.AttachView();
                world.Render(0.016f);
                Require(world.view != previous && world.view.VertexCount == partCount * 8,
                    "Recreated view rebuilds existing wall collision", ref checks);
                rebuilds = world.view.Rebuilds;
                world.AttachView(); world.Render(0.016f);
                Require(world.view.Rebuilds == rebuilds, "Attaching existing view does not dirty collision", ref checks);
                world.byStorageKey.Clear();
                world.collisionDirty = true;
                world.Render(0.016f);
                Require(world.view.VertexCount == 0, "Cleared records no longer emit collision", ref checks);
            }
            return checks;
        }
    }
}
