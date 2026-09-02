using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.MapObjects
{
    [DisallowMultipleComponent]
    [AddComponentMenu("ProjectF/Map Object/Tree")]
    public class Tree : Resource, IMapObjectUpdateTick, IMapObjectUpdateTickInterval
    {
        private const string AppleItemName = "Apple";
        private const string AppleVisualNamePrefix = "Apple (";
        private const string GrowthZeroTraceVisualName = "GrowthZeroTrace";
        private const float GrowthZeroThreshold = 0.0001f;
        private const float GrowthZeroTraceSize = 0.5f;
        private const float GrowthZeroTraceAboveFarmlandOffset = 0.004f;
        private const float GrowthZeroTraceFallbackSurfaceOffset = 0.012f;
        private const int GrowthZeroTraceSegmentCount = 32;
        private const float GrowthZeroTraceInnerRadius = 0.38f;
        private const float GrowthZeroTraceOuterRadius = 0.5f;
        private const float GrowthRequirementEpsilon = 0.0001f;
        private const float GrowthTickIntervalSeconds = 0.25f;

        private static Mesh sharedGrowthZeroTraceMesh;
        private static Material sharedGrowthZeroTraceMaterial;

        [SerializeField, Range(ResourceDefinition.MinGrowth, ResourceDefinition.MaxGrowth)]
        private float growth = ResourceDefinition.DefaultGrowth;
        [SerializeField, Min(0f)]
        private float growthWaterLiters;
        [SerializeField, Min(0f)]
        private float growthFertilizerAmount;
        [SerializeField, Min(0f)]
        private float growthElapsedSeconds;
        private readonly List<GameObject> appleVisualObjects = new List<GameObject>(4);
        private readonly List<Collider> growthControlledColliders = new List<Collider>(4);
        private readonly List<bool> growthControlledColliderDefaults = new List<bool>(4);
        private bool appleVisualObjectsCached;
        private bool growthControlledCollidersCached;
        private GameObject growthZeroTraceVisual;
        private PlantGrowthWorldGauge growthWorldGauge;

        public float Growth => Mathf.Clamp(
            growth,
            ResourceDefinition.MinGrowth,
            ResourceDefinition.MaxGrowth);
        public int TargetGrowthLevel => Mathf.Clamp(
            Mathf.FloorToInt(Growth) + 1,
            ResourceDefinition.MinGrowth + 1,
            ResourceDefinition.MaxGrowth);
        public float RequiredGrowthWaterLiters => CanGrowAnotherLevel && Definition != null
            ? Definition.GetGrowthWaterRequirement(TargetGrowthLevel)
            : 0f;
        public float RequiredGrowthFertilizerAmount => CanGrowAnotherLevel && Definition != null
            ? Definition.GetGrowthFertilizerRequirement(TargetGrowthLevel)
            : 0f;
        public float StoredGrowthWaterLiters => Mathf.Clamp(
            growthWaterLiters,
            0f,
            RequiredGrowthWaterLiters);
        public float StoredGrowthFertilizerAmount => Mathf.Clamp(
            growthFertilizerAmount,
            0f,
            RequiredGrowthFertilizerAmount);
        public float CurrentGrowthWaterLiters => StoredGrowthWaterLiters;
        public float CurrentGrowthFertilizerAmount => StoredGrowthFertilizerAmount;
        public float GrowthElapsedSeconds => Mathf.Max(0f, growthElapsedSeconds);
        public bool CanGrowAnotherLevel => Growth < ResourceDefinition.MaxGrowth;
        public bool CanAcceptGrowthWater => CanGrowAnotherLevel
                                            && Definition != null
                                            && Definition.HasGrowthSchedule
                                            && RequiredGrowthWaterLiters
                                            - CurrentGrowthWaterLiters
                                            > GrowthRequirementEpsilon;
        public bool CanAcceptGrowthFertilizer => CanGrowAnotherLevel
                                                 && Definition != null
                                                 && Definition.HasGrowthSchedule
                                                 && RequiredGrowthFertilizerAmount
                                                 - CurrentGrowthFertilizerAmount
                                                 > GrowthRequirementEpsilon;
        public bool AreCurrentGrowthRequirementsMet => CanGrowAnotherLevel
                                                        && CurrentGrowthWaterLiters
                                                        + GrowthRequirementEpsilon
                                                        >= RequiredGrowthWaterLiters
                                                        && CurrentGrowthFertilizerAmount
                                                        + GrowthRequirementEpsilon
                                                        >= RequiredGrowthFertilizerAmount;
        public float ManagedUpdateTickIntervalSeconds => GrowthTickIntervalSeconds;

        public bool TryGetMachineAppleDrop(out int itemId, out int itemCount)
        {
            itemId = -1;
            itemCount = 0;
            IReadOnlyList<ResourceDropEntry> dropItems = Definition != null
                ? Definition.DropItems
                : null;
            for (int i = 0; dropItems != null && i < dropItems.Count; i++)
            {
                ItemDefinition itemDefinition = dropItems[i]?.ItemDefinition;
                if (itemDefinition == null
                    || !string.Equals(
                        itemDefinition.itemName,
                        AppleItemName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                itemId = itemDefinition.id;
                itemCount = RollNextConfiguredHarvestDropCount(itemId);
                return itemId >= 0 && itemCount > 0;
            }

            return false;
        }

        public void SetGrowth(float value)
        {
            float clampedGrowth = Mathf.Clamp(
                value,
                ResourceDefinition.MinGrowth,
                ResourceDefinition.MaxGrowth);
            bool changedStage = Mathf.FloorToInt(clampedGrowth)
                                != Mathf.FloorToInt(Growth);
            growth = clampedGrowth;
            if (changedStage)
            {
                ResetCurrentGrowthStageProgress();
            }

            RefreshGrowthPresentation();
            RefreshGrowthTickRegistration();
        }

        public bool TryAddGrowthWater(float amountLiters, out float acceptedLiters)
        {
            acceptedLiters = 0f;
            if (amountLiters <= 0f || !CanAcceptGrowthWater)
            {
                return false;
            }

            acceptedLiters = Mathf.Min(
                amountLiters,
                RequiredGrowthWaterLiters - CurrentGrowthWaterLiters);
            if (acceptedLiters <= GrowthRequirementEpsilon)
            {
                acceptedLiters = 0f;
                return false;
            }

            growthWaterLiters = CurrentGrowthWaterLiters + acceptedLiters;
            RefreshGrowthTickRegistration();
            RefreshGrowthWorldGauge();
            return true;
        }

        public void RefreshFarmlandFertilizerConsumption()
        {
            RefreshGrowthTickRegistration();
            RefreshGrowthWorldGauge();
        }

        public void ManagedUpdateTick(float deltaTime)
        {
            if (!HasGrowthTimerRequirements())
            {
                RefreshGrowthTickRegistration();
                return;
            }

            WorldTimeService worldTime = WorldTimeService.Active;
            if (worldTime == null || worldTime.Paused || !worldTime.IsDay)
            {
                return;
            }

            growthElapsedSeconds += Mathf.Max(0f, deltaTime);
            float duration = Definition.GrowthDurationPerLevelSeconds;
            if (growthElapsedSeconds + GrowthRequirementEpsilon < duration)
            {
                RefreshGrowthWorldGauge();
                return;
            }

            int completedGrowthLevel = TargetGrowthLevel;
            growth = completedGrowthLevel;
            ResetCurrentGrowthStageProgress();
            RefreshGrowthPresentation();
            RefreshGrowthTickRegistration();
        }

        protected new void OnEnable()
        {
            base.OnEnable();
            ClampCurrentGrowthStageProgress();
            RefreshGrowthPresentation();
            RefreshGrowthTickRegistration();
        }

        protected new void OnDisable()
        {
            MapObjectTickManager.UnregisterUpdateTick(this);
            growthWorldGauge?.Hide();
            base.OnDisable();
        }

        protected override void OnOwningBlockChanged(Block block)
        {
            base.OnOwningBlockChanged(block);
            RefreshGrowthZeroTraceVisual();
            RefreshGrowthControlledColliders();
            RefreshGrowthWorldGauge();
        }

        private void EnsureGrowthZeroTraceVisual()
        {
            if (growthZeroTraceVisual == null)
            {
                Transform existingVisual = transform.Find(GrowthZeroTraceVisualName);
                growthZeroTraceVisual = existingVisual != null
                    ? existingVisual.gameObject
                    : new GameObject(GrowthZeroTraceVisualName);
            }

            Transform traceTransform = growthZeroTraceVisual.transform;
            traceTransform.SetParent(transform, false);
            growthZeroTraceVisual.layer = gameObject.layer;

            MeshFilter meshFilter = growthZeroTraceVisual.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = growthZeroTraceVisual.AddComponent<MeshFilter>();
            }

            MeshRenderer meshRenderer = growthZeroTraceVisual.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = growthZeroTraceVisual.AddComponent<MeshRenderer>();
            }

            meshFilter.sharedMesh = ResolveGrowthZeroTraceMesh();
            meshRenderer.sharedMaterial = ResolveGrowthZeroTraceMaterial();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            traceTransform.localScale = new Vector3(
                GrowthZeroTraceSize,
                1f,
                GrowthZeroTraceSize);
            traceTransform.rotation = Quaternion.identity;
        }

        private void UpdateGrowthZeroTracePosition(Transform traceTransform)
        {
            if (traceTransform == null)
            {
                return;
            }

            float traceWorldY = transform.position.y + GrowthZeroTraceFallbackSurfaceOffset;
            Block owningBlock = OwningBlock;
            if (owningBlock != null)
            {
                Transform farmlandVisual = owningBlock.transform.Find(
                    TerrainGenerator.FarmlandVisualName);
                traceWorldY = farmlandVisual != null
                    ? farmlandVisual.position.y + GrowthZeroTraceAboveFarmlandOffset
                    : owningBlock.transform.position.y + GrowthZeroTraceFallbackSurfaceOffset;
            }

            traceTransform.position = new Vector3(
                transform.position.x,
                traceWorldY,
                transform.position.z);
        }

        private static Mesh ResolveGrowthZeroTraceMesh()
        {
            if (sharedGrowthZeroTraceMesh != null)
            {
                return sharedGrowthZeroTraceMesh;
            }

            sharedGrowthZeroTraceMesh = new Mesh
            {
                name = "GeneratedGrowthZeroTraceDisc",
                hideFlags = HideFlags.DontSave
            };

            int ringVertexCount = GrowthZeroTraceSegmentCount + 1;
            int innerRingStart = 1;
            int outerRingStart = innerRingStart + ringVertexCount;
            Vector3[] vertices = new Vector3[1 + ringVertexCount * 2];
            Vector2[] uvs = new Vector2[vertices.Length];
            Color32[] colors = new Color32[vertices.Length];
            int[] triangles = new int[GrowthZeroTraceSegmentCount * 9];
            Color32 centerColor = new Color32(0, 0, 0, 180);
            Color32 innerRingColor = new Color32(0, 0, 0, 145);
            Color32 outerColor = new Color32(0, 0, 0, 0);

            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            colors[0] = centerColor;
            for (int segmentIndex = 0; segmentIndex <= GrowthZeroTraceSegmentCount; segmentIndex++)
            {
                float angle = segmentIndex
                              * Mathf.PI
                              * 2f
                              / GrowthZeroTraceSegmentCount;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                int innerIndex = innerRingStart + segmentIndex;
                int outerIndex = outerRingStart + segmentIndex;
                vertices[innerIndex] = new Vector3(
                    direction.x * GrowthZeroTraceInnerRadius,
                    0f,
                    direction.y * GrowthZeroTraceInnerRadius);
                vertices[outerIndex] = new Vector3(
                    direction.x * GrowthZeroTraceOuterRadius,
                    0f,
                    direction.y * GrowthZeroTraceOuterRadius);
                uvs[innerIndex] = new Vector2(
                    0.5f + direction.x * GrowthZeroTraceInnerRadius,
                    0.5f + direction.y * GrowthZeroTraceInnerRadius);
                uvs[outerIndex] = new Vector2(
                    0.5f + direction.x * GrowthZeroTraceOuterRadius,
                    0.5f + direction.y * GrowthZeroTraceOuterRadius);
                colors[innerIndex] = innerRingColor;
                colors[outerIndex] = outerColor;
            }

            int triangleIndex = 0;
            for (int segmentIndex = 0; segmentIndex < GrowthZeroTraceSegmentCount; segmentIndex++)
            {
                int innerCurrent = innerRingStart + segmentIndex;
                int innerNext = innerCurrent + 1;
                int outerCurrent = outerRingStart + segmentIndex;
                int outerNext = outerCurrent + 1;

                triangles[triangleIndex++] = 0;
                triangles[triangleIndex++] = innerNext;
                triangles[triangleIndex++] = innerCurrent;

                triangles[triangleIndex++] = innerCurrent;
                triangles[triangleIndex++] = innerNext;
                triangles[triangleIndex++] = outerNext;
                triangles[triangleIndex++] = innerCurrent;
                triangles[triangleIndex++] = outerNext;
                triangles[triangleIndex++] = outerCurrent;
            }

            sharedGrowthZeroTraceMesh.SetVertices(vertices);
            sharedGrowthZeroTraceMesh.SetUVs(0, uvs);
            sharedGrowthZeroTraceMesh.SetColors(colors);
            sharedGrowthZeroTraceMesh.SetTriangles(triangles, 0);
            sharedGrowthZeroTraceMesh.RecalculateNormals();
            sharedGrowthZeroTraceMesh.RecalculateBounds();
            return sharedGrowthZeroTraceMesh;
        }

        private static Material ResolveGrowthZeroTraceMaterial()
        {
            if (sharedGrowthZeroTraceMaterial != null)
            {
                return sharedGrowthZeroTraceMaterial;
            }

            Shader shader = Shader.Find("Sprites/Default")
                            ?? Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Standard");
            if (shader == null)
            {
                return null;
            }

            sharedGrowthZeroTraceMaterial = new Material(shader)
            {
                name = "GeneratedGrowthZeroTraceMaterial",
                hideFlags = HideFlags.DontSave,
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 10
            };

            Texture2D texture = Texture2D.whiteTexture;
            sharedGrowthZeroTraceMaterial.mainTexture = texture;
            if (sharedGrowthZeroTraceMaterial.HasProperty("_BaseMap"))
            {
                sharedGrowthZeroTraceMaterial.SetTexture("_BaseMap", texture);
            }

            if (sharedGrowthZeroTraceMaterial.HasProperty("_BaseColor"))
            {
                sharedGrowthZeroTraceMaterial.SetColor("_BaseColor", Color.white);
            }
            else if (sharedGrowthZeroTraceMaterial.HasProperty("_Color"))
            {
                sharedGrowthZeroTraceMaterial.color = Color.white;
            }

            ConfigureGrowthZeroTraceTransparency(sharedGrowthZeroTraceMaterial);
            return sharedGrowthZeroTraceMaterial;
        }

        private static void ConfigureGrowthZeroTraceTransparency(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetInt(
                    "_SrcBlend",
                    (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetInt(
                    "_DstBlend",
                    (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetInt("_ZWrite", 0);
            }

            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
        }

        protected override float GetAdditionalBodyScaleRatio()
        {
            if (Growth <= GrowthZeroThreshold)
            {
                return 0f;
            }

            if (Growth < 1f)
            {
                return MinimumBodyScaleRatio * Growth;
            }

            float normalizedGrowth = Mathf.InverseLerp(
                1f,
                ResourceDefinition.MaxGrowth,
                Growth);
            return Mathf.Lerp(
                MinimumBodyScaleRatio,
                MaximumBodyScaleRatio,
                normalizedGrowth);
        }

        protected override void CaptureAdditionalSaveState(ref ResourceSaveState state)
        {
            state.hasGrowth = true;
            state.growth = Growth;
            state.hasPlantGrowthState = true;
            state.growthWaterLiters = StoredGrowthWaterLiters;
            state.growthFertilizerAmount = StoredGrowthFertilizerAmount;
            state.growthElapsedSeconds = GrowthElapsedSeconds;
        }

        protected override void ApplyAdditionalSavedState(ResourceSaveState state)
        {
            if (state.hasGrowth)
            {
                growth = Mathf.Clamp(
                    state.growth,
                    ResourceDefinition.MinGrowth,
                    ResourceDefinition.MaxGrowth);
            }

            if (state.hasPlantGrowthState)
            {
                growthWaterLiters = Mathf.Max(0f, state.growthWaterLiters);
                growthFertilizerAmount = Mathf.Max(0f, state.growthFertilizerAmount);
                growthElapsedSeconds = Mathf.Max(0f, state.growthElapsedSeconds);
            }
            else
            {
                ResetCurrentGrowthStageProgress();
            }

            ClampCurrentGrowthStageProgress();

            RefreshGrowthPresentation();
            RefreshGrowthTickRegistration();
        }

        private bool HasGrowthTimerRequirements()
        {
            if (!CanGrowAnotherLevel
                || Definition == null
                || !Definition.HasGrowthSchedule)
            {
                return false;
            }

            return AreCurrentGrowthRequirementsMet;
        }

        private void RefreshGrowthPresentation()
        {
            RefreshBodyScale();
            RefreshGrowthZeroTraceVisual();
            RefreshAppleVisuals();
            RefreshGrowthControlledColliders();
            RefreshGrowthWorldGauge();
        }

        private void RefreshGrowthWorldGauge()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            bool shouldHaveGauge = Definition != null
                                   && Definition.HasGrowthSchedule
                                   && CanGrowAnotherLevel
                                   && ResourceCount > 0;
            if (!shouldHaveGauge)
            {
                growthWorldGauge?.Hide();
                return;
            }

            if (growthWorldGauge == null)
            {
                growthWorldGauge = GetComponentInChildren<PlantGrowthWorldGauge>(true);
                if (growthWorldGauge == null)
                {
                    growthWorldGauge = PlantGrowthWorldGauge.Create(this);
                }
            }

            growthWorldGauge?.Refresh();
        }

        private void RefreshGrowthTickRegistration()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                MapObjectTickManager.UnregisterUpdateTick(this);
                return;
            }

            TryConsumeAvailableFarmlandFertilizer();
            if (HasGrowthTimerRequirements())
            {
                MapObjectTickManager.RegisterUpdateTick(this);
            }
            else
            {
                MapObjectTickManager.UnregisterUpdateTick(this);
            }
        }

        private bool TryConsumeAvailableFarmlandFertilizer()
        {
            if (!CanAcceptGrowthFertilizer)
            {
                return false;
            }

            Block owningBlock = OwningBlock;
            TerrainGenerator terrainGenerator = TerrainGenerator.ResolveActive();
            if (owningBlock == null
                || terrainGenerator == null
                || !terrainGenerator.IsFarmlandAt(owningBlock.Coordinate))
            {
                return false;
            }

            float requestedAmount = RequiredGrowthFertilizerAmount
                                    - CurrentGrowthFertilizerAmount;
            if (!terrainGenerator.TryConsumeFarmlandFertilizer(
                    owningBlock.Coordinate,
                    requestedAmount,
                    out float consumedAmount)
                || consumedAmount <= GrowthRequirementEpsilon)
            {
                return false;
            }

            growthFertilizerAmount = Mathf.Min(
                RequiredGrowthFertilizerAmount,
                CurrentGrowthFertilizerAmount + consumedAmount);
            return true;
        }

        private void ResetCurrentGrowthStageProgress()
        {
            growthWaterLiters = 0f;
            growthFertilizerAmount = 0f;
            growthElapsedSeconds = 0f;
        }

        private void ClampCurrentGrowthStageProgress()
        {
            if (!CanGrowAnotherLevel)
            {
                ResetCurrentGrowthStageProgress();
                return;
            }

            growthWaterLiters = Mathf.Clamp(
                growthWaterLiters,
                0f,
                RequiredGrowthWaterLiters);
            growthFertilizerAmount = Mathf.Clamp(
                growthFertilizerAmount,
                0f,
                RequiredGrowthFertilizerAmount);
            float duration = Definition != null
                ? Definition.GrowthDurationPerLevelSeconds
                : 0f;
            growthElapsedSeconds = duration > 0f
                ? Mathf.Clamp(growthElapsedSeconds, 0f, duration)
                : 0f;
        }

        private void RefreshGrowthControlledColliders()
        {
            CacheGrowthControlledColliders();
            bool shouldEnable = Growth > GrowthZeroThreshold && ResourceCount > 0;
            for (int i = 0; i < growthControlledColliders.Count; i++)
            {
                Collider targetCollider = growthControlledColliders[i];
                if (targetCollider == null)
                {
                    continue;
                }

                bool enabled = shouldEnable && growthControlledColliderDefaults[i];
                if (targetCollider.enabled != enabled)
                {
                    targetCollider.enabled = enabled;
                }
            }
        }

        private void CacheGrowthControlledColliders()
        {
            if (growthControlledCollidersCached)
            {
                return;
            }

            growthControlledCollidersCached = true;
            growthControlledColliders.Clear();
            growthControlledColliderDefaults.Clear();
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider targetCollider = colliders[i];
                if (targetCollider == null)
                {
                    continue;
                }

                growthControlledColliders.Add(targetCollider);
                growthControlledColliderDefaults.Add(targetCollider.enabled);
            }
        }

        private void RefreshGrowthZeroTraceVisual()
        {
            bool shouldShow = Growth <= GrowthZeroThreshold;
            if (shouldShow)
            {
                EnsureGrowthZeroTraceVisual();
            }
            else if (growthZeroTraceVisual == null)
            {
                Transform existingVisual = transform.Find(GrowthZeroTraceVisualName);
                growthZeroTraceVisual = existingVisual != null ? existingVisual.gameObject : null;
            }

            if (growthZeroTraceVisual != null)
            {
                if (shouldShow)
                {
                    UpdateGrowthZeroTracePosition(growthZeroTraceVisual.transform);
                }

                growthZeroTraceVisual.SetActive(shouldShow);
            }
        }

        private void RefreshAppleVisuals()
        {
            CacheAppleVisualObjects();
            if (appleVisualObjects.Count == 0)
            {
                return;
            }

            bool shouldShow = TryGetAppleMinimumGrowth(out float minimumGrowth)
                              && Growth >= minimumGrowth;
            bool changed = false;
            for (int i = 0; i < appleVisualObjects.Count; i++)
            {
                GameObject appleObject = appleVisualObjects[i];
                if (appleObject == null || appleObject.activeSelf == shouldShow)
                {
                    continue;
                }

                appleObject.SetActive(shouldShow);
                changed = true;
            }

            if (changed)
            {
                MarkBatchRenderDataDirty();
            }
        }

        private void CacheAppleVisualObjects()
        {
            if (appleVisualObjectsCached)
            {
                return;
            }

            appleVisualObjectsCached = true;
            appleVisualObjects.Clear();
            CollectAppleVisualObjects(transform);
        }

        private void CollectAppleVisualObjects(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (int childIndex = 0; childIndex < parent.childCount; childIndex++)
            {
                Transform child = parent.GetChild(childIndex);
                string childName = child.name;
                if (string.Equals(childName, AppleItemName, StringComparison.OrdinalIgnoreCase)
                    || childName.StartsWith(
                        AppleVisualNamePrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    appleVisualObjects.Add(child.gameObject);
                }

                CollectAppleVisualObjects(child);
            }
        }

        private bool TryGetAppleMinimumGrowth(out float minimumGrowth)
        {
            minimumGrowth = ResourceDefinition.MaxGrowth;
            IReadOnlyList<ResourceDropEntry> dropItems = Definition != null
                ? Definition.DropItems
                : null;
            bool found = false;
            for (int i = 0; dropItems != null && i < dropItems.Count; i++)
            {
                ResourceDropEntry entry = dropItems[i];
                ItemDefinition itemDefinition = entry?.ItemDefinition;
                if (itemDefinition == null
                    || entry.Amount <= 0
                    || entry.DropChance <= 0f
                    || !string.Equals(
                        itemDefinition.itemName,
                        AppleItemName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                minimumGrowth = Mathf.Min(minimumGrowth, entry.MinimumGrowth);
                found = true;
            }

            return found;
        }
    }
}
