using System.Collections.Generic;
using UnityEngine;
using ProjectF.MapObjects;

namespace ProjectF.Rendering
{
    // The specialized belt/pipe/arm/building worlds keep their own presentation.
    // This host owns live installation model submission, including animated,
    // inactive and runtime-created parts, without requiring a baked Archetype.
    [DisallowMultipleComponent, DefaultExecutionOrder(1000)]
    public sealed class InstallationBatchRenderer : MonoBehaviour
    {
        private sealed class Part
        {
            internal Renderer Renderer;
            internal MeshFilter Filter;
            internal SpriteRenderer Sprite;
            internal bool OriginalForceOff;
            internal int AnimationNode = -1;
        }
        private sealed class Record
        {
            internal MapObjectHandle Handle;
            internal int HierarchyCount;
            internal InstallationRigidAnimationTemplate Animation;
            internal double AnimationPhase, AnimationSampleTime;
            internal float AnimationSpeed = 1f;
            internal bool AnimationWorking;
            internal InstallationObject Owner;
            internal readonly List<Part> Parts = new List<Part>(8);
        }
        private static InstallationBatchRenderer current;
        private readonly Dictionary<InstallationObject, Record> records = new Dictionary<InstallationObject, Record>();
        private readonly List<InstallationObject> visible = new List<InstallationObject>(256);
        private readonly List<Renderer> rendererScratch = new List<Renderer>(16);
        private readonly List<Material> materialScratch = new List<Material>(4);
        private readonly SpriteMeshCache spriteMeshes = new SpriteMeshCache();
        private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
        private readonly InstallationMaterialVariants materials = new InstallationMaterialVariants();
        private readonly Dictionary<MapObjectArchetype, InstallationRigidAnimationTemplate> animations =
            new Dictionary<MapObjectArchetype, InstallationRigidAnimationTemplate>();
        public int RegisteredCount => records.Count;
        public int VisibleCount { get; private set; }
        public int MatrixCount { get; private set; }
        public int MaterialVariantCount => materials.Count;
        public int SharedAnimationCount { get; private set; }
        public int NativeFallbackPartCount { get; private set; }

        internal static bool Supports(InstallationObject owner) => owner != null
            && !(owner is ConveyorBelt || owner is Pipe || owner is RobotArm || owner is Building);
        private void Awake()
        {
            current = this;
            InstallationObject.CopyActiveInstances(visible);
            for (int i = 0; i < visible.Count; i++) Register(visible[i]);
            visible.Clear();
        }
        internal static void Register(InstallationObject owner)
        {
            if (current == null || !Application.isPlaying || !Supports(owner)
                || !owner.isActiveAndEnabled || !owner.RuntimeMapObjectHandle.IsValid) return;
            if (current.records.TryGetValue(owner, out Record existing))
            {
                existing.Handle = owner.RuntimeMapObjectHandle;
                return;
            }
            var record = new Record { Handle = owner.RuntimeMapObjectHandle, Owner = owner,
                AnimationSampleTime = MapObjectTickManager.CurrentSimulationTimeSeconds };
            if (owner is MiningMachine && owner.BoundItemDefinition != null)
            {
                MapObjectArchetype archetype = owner.BoundItemDefinition.MapObjectArchetype;
                if (archetype != null)
                {
                    if (!current.animations.TryGetValue(archetype, out record.Animation))
                    {
                        record.Animation = InstallationRigidAnimationTemplate.Create(archetype);
                        current.animations.Add(archetype, record.Animation);
                    }
                }
            }
            current.Capture(owner, record);
            if (record.Animation != null)
            {
                // A shared pose can replace the Animator only when every model
                // part uses that pose. Unsupported shaders keep native animation.
                for (int i = 0; i < record.Parts.Count; i++)
                    if (!current.CanBatch(record.Parts[i].Renderer)) { record.Animation = null; break; }
            }
            current.records.Add(owner, record);
            if (record.Animation != null)
            {
                current.SharedAnimationCount++;
                RefreshWorkAnimator(record);
            }
        }
        internal static void Unregister(InstallationObject owner)
        {
            if (current == null || ReferenceEquals(owner, null)
                || !current.records.TryGetValue(owner, out Record record)) return;
            Restore(record); current.records.Remove(owner);
            if (record.Animation != null)
            {
                current.SharedAnimationCount--;
                RefreshWorkAnimator(record);
            }
        }
        private static void RefreshWorkAnimator(Record record)
        {
            if (record.Owner is InputOutputModule module && module.isActiveAndEnabled)
                module.RefreshWorkAnimatorRendering();
        }
        private void Capture(InstallationObject owner, Record record)
        {
            for (int i = record.Parts.Count - 1; i >= 0; i--)
                if (record.Parts[i].Renderer == null) record.Parts.RemoveAt(i);
            rendererScratch.Clear(); owner.GetComponentsInChildren(true, rendererScratch);
            for (int i = 0; i < rendererScratch.Count; i++)
            {
                Renderer renderer = rendererScratch[i];
                if (renderer == null || renderer.GetComponentInParent<MapObject>(true) != owner) continue;
                bool known = false;
                for (int j = 0; j < record.Parts.Count; j++)
                    if (record.Parts[j].Renderer == renderer) { known = true; break; }
                if (known) continue;
                MeshFilter filter = renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>() : null;
                SpriteRenderer sprite = renderer as SpriteRenderer;
                if (filter == null && (sprite == null || sprite.drawMode != SpriteDrawMode.Simple
                    || sprite.maskInteraction != SpriteMaskInteraction.None)) continue;
                record.Parts.Add(new Part { Renderer = renderer, Filter = filter, Sprite = sprite,
                    AnimationNode = record.Animation != null ? record.Animation.ResolveNode(renderer.transform, owner.transform) : -1,
                    OriginalForceOff = renderer.forceRenderingOff });
                if (isActiveAndEnabled && CanBatch(renderer)) renderer.forceRenderingOff = true;
            }
            record.HierarchyCount = owner.transform.hierarchyCount;
        }
        private bool CanBatch(Renderer renderer)
        {
            if (renderer == null) return false;
            renderer.GetSharedMaterials(materialScratch);
            for (int i = 0; i < materialScratch.Count; i++)
            {
                Material material = materialScratch[i];
                if (material != null && (material.shader == null
                    || !material.shader.keywordSpace.FindKeyword("INSTANCING_ON").isValid)) return false;
            }
            return materialScratch.Count > 0;
        }
        internal static bool TrySetWorkAnimation(InstallationObject owner, bool working, float speed)
        {
            if (current == null || !current.isActiveAndEnabled || !current.records.TryGetValue(owner, out Record record) || record.Animation == null) return false;
            AdvanceAnimation(record);
            if (working && !record.AnimationWorking) record.AnimationPhase = 0d;
            record.AnimationWorking = working;
            record.AnimationSpeed = Mathf.Max(0f, speed);
            return true;
        }
        private static void AdvanceAnimation(Record record)
        {
            double now = MapObjectTickManager.CurrentSimulationTimeSeconds;
            if (record.AnimationWorking) record.AnimationPhase += System.Math.Max(0d, now - record.AnimationSampleTime) * record.AnimationSpeed;
            record.AnimationSampleTime = now;
        }
        private static void Restore(Record record)
        {
            for (int i = 0; i < record.Parts.Count; i++)
            {
                Part part = record.Parts[i];
                if (part.Renderer != null) part.Renderer.forceRenderingOff = part.OriginalForceOff;
            }
        }
        private void LateUpdate()
        {
            using var caller = MapObjectTickProfiler.SampleLateUpdateCaller<InstallationBatchRenderer>();
            using var sample = MapObjectTickProfiler.SampleNamed("Render", "InstallationECS", "Installation Model Submit");
            if (MapObjectTickManager.WaitingForWorldLoad || TerrainGenerator.Active != null
                && TerrainGenerator.Active.IsBenchmarkPlacementInProgress)
            { batches.SuspendRendering(); VisibleCount = MatrixCount = NativeFallbackPartCount = 0; return; }
            WorldVisualUpdateManager.CopyVisibleInstallations(visible);
            batches.ClearActiveMatrices(); VisibleCount = MatrixCount = NativeFallbackPartCount = 0;
            for (int i = 0; i < visible.Count; i++)
            {
                InstallationObject owner = visible[i];
                if (owner == null || !owner.isActiveAndEnabled || !records.TryGetValue(owner, out Record record)
                    || record.Handle != owner.RuntimeMapObjectHandle) continue;
                VisibleCount++;
                // Rails, bucket fluid surfaces and range models can be added after registration.
                if (record.HierarchyCount != owner.transform.hierarchyCount) Capture(owner, record);
                Append(record);
            }
            batches.RenderBatches(Camera.main);
        }
        private void Append(Record record)
        {
            if (record.Animation != null)
            {
                AdvanceAnimation(record);
                record.Animation.Evaluate(record.AnimationPhase, record.AnimationWorking);
            }
            for (int i = 0; i < record.Parts.Count; i++)
            {
                Part part = record.Parts[i]; Renderer renderer = part.Renderer;
                if (renderer == null || part.OriginalForceOff || !renderer.enabled
                    || !renderer.gameObject.activeInHierarchy) continue;
                if (!CanBatch(renderer))
                {
                    if (record.Animation != null)
                    {
                        // Runtime material changes must also return animation to
                        // the native hierarchy used by the fallback renderer.
                        record.Animation = null; SharedAnimationCount--;
                        RefreshWorkAnimator(record);
                    }
                    renderer.forceRenderingOff = part.OriginalForceOff;
                    NativeFallbackPartCount++;
                    continue;
                }
                // The source stays available to gameplay adapters; only its native draw is suppressed.
                renderer.forceRenderingOff = true;
                Mesh mesh = part.Sprite != null ? spriteMeshes.Get(part.Sprite.sprite) : part.Filter.sharedMesh;
                if (mesh == null) continue;
                Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
                if (record.Animation != null && part.AnimationNode >= 0)
                    matrix = record.Owner.transform.localToWorldMatrix * record.Animation.GetMatrix(part.AnimationNode);
                if (part.Sprite != null) matrix *= Matrix4x4.Scale(new Vector3(
                    part.Sprite.flipX ? -1f : 1f, part.Sprite.flipY ? -1f : 1f, 1f));
                for (int sub = 0; sub < Mathf.Min(mesh.subMeshCount, materialScratch.Count); sub++)
                {
                    Material material = materials.Resolve(renderer, materialScratch[sub], sub, part.Sprite);
                    if (material == null) continue;
                    var key = new VirtualRenderBatchKey(mesh, material, renderer.gameObject.layer, sub,
                        renderer.shadowCastingMode, renderer.receiveShadows, false,
                        batchCellX: Mathf.FloorToInt(matrix.m03 / 16f), batchCellZ: Mathf.FloorToInt(matrix.m23 / 16f),
                        invertCulling: matrix.determinant < 0f, renderingLayerMask: renderer.renderingLayerMask);
                    batches.AddMatrix(key, matrix); MatrixCount++;
                }
            }
        }
        private void OnEnable()
        {
            current = this;
            foreach (Record record in records.Values)
            {
                for (int i = 0; i < record.Parts.Count; i++)
                    if (record.Parts[i].Renderer != null && CanBatch(record.Parts[i].Renderer))
                        record.Parts[i].Renderer.forceRenderingOff = true;
                if (record.Animation != null) RefreshWorkAnimator(record);
            }
        }
        private void OnDisable()
        {
            batches.SuspendRendering();
            foreach (Record record in records.Values)
            {
                Restore(record);
                if (record.Animation != null) RefreshWorkAnimator(record);
            }
        }
        private void OnDestroy()
        {
            if (current == this) current = null;
            foreach (Record record in records.Values)
            {
                Restore(record);
                if (record.Animation != null) RefreshWorkAnimator(record);
            }
            records.Clear(); animations.Clear(); SharedAnimationCount = 0; batches.Dispose(); materials.Dispose();
            spriteMeshes.Dispose();
        }
        public static void AppendProfilerCounters()
        {
            MapObjectTickProfiler.AddRuntimeCounter("InstallationECS", "RegisteredModels", current != null ? current.RegisteredCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("InstallationECS", "VisibleModels", current != null ? current.VisibleCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("InstallationECS", "Matrices", current != null ? current.MatrixCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("InstallationECS", "MaterialVariants", current != null ? current.MaterialVariantCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("InstallationECS", "SharedAnimationModels", current != null ? current.SharedAnimationCount : 0);
            MapObjectTickProfiler.AddRuntimeCounter("InstallationECS", "NativeFallbackParts", current != null ? current.NativeFallbackPartCount : 0);
        }
    }
}
