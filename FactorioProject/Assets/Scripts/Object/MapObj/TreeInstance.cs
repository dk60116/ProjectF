using System;
using System.Collections.Generic;
using UnityEngine;
using ResourceSaveState = Resource.ResourceSaveState;
namespace ProjectF.MapObjects
{
    public sealed class TreeInstance : ResourceInstance, IMapObjectUpdateTick, IMapObjectUpdateTickInterval
    {
        private const string AppleItemName = "Apple";
        private const float GrowthZeroThreshold = 0.0001f;
        private const float GrowthRequirementEpsilon = 0.0001f;
        private const float GrowthTickIntervalSeconds = 0.25f;
        private ref ResourceRuntimeState GrowthState => ref Handle.World.GetState(Handle);
        private ref float growth => ref GrowthState.Growth;
        private ref float growthWaterLiters => ref GrowthState.GrowthWater;
        private ref float growthFertilizerAmount => ref GrowthState.GrowthFertilizer;
        private ref float growthElapsedSeconds => ref GrowthState.GrowthElapsed;
        internal TreeInstance(ResourceTypeWorld world, ResourceHandle handle) : base(world, handle) { }
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

        public void CollectMachineSeedDrops(List<KeyValuePair<int, int>> results)
        {
            results.Clear();
            IReadOnlyList<ResourceDropEntry> drops = Definition != null ? Definition.DropItems : null;
            for (int i = 0; drops != null && i < drops.Count; i++)
            {
                ItemDefinition seed = drops[i]?.ItemDefinition;
                if (seed == null || !seed.isSeed || seed.id < 0)
                    continue;

                bool alreadyCollected = false;
                for (int j = 0; j < i; j++)
                {
                    if (drops[j]?.ItemDefinition != null && drops[j].ItemDefinition.id == seed.id)
                    {
                        alreadyCollected = true;
                        break;
                    }
                }
                if (alreadyCollected)
                    continue;

                // Use the same growth/probability/depletion roll as other configured tree rewards.
                int count = RollNextConfiguredHarvestDropCount(seed.id);
                if (count > 0)
                    results.Add(new KeyValuePair<int, int>(seed.id, count));
            }
        }

        public void SetGrowth(float value)
        {
            if (!Handle.IsValid) return;
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
            if (!IsRuntimeActive || amountLiters <= 0f || !CanAcceptGrowthWater)
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
            return true;
        }

        public void RefreshFarmlandFertilizerConsumption()
        {
            RefreshGrowthTickRegistration();
        }

        public void ManagedUpdateTick(float deltaTime)
        {
            if (!IsRuntimeActive || !HasGrowthTimerRequirements())
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
                return;
            }

            int completedGrowthLevel = TargetGrowthLevel;
            growth = completedGrowthLevel;
            ResetCurrentGrowthStageProgress();
            RefreshGrowthPresentation();
            RefreshGrowthTickRegistration();
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

        private void RefreshGrowthPresentation() { RefreshBodyScale(); }

        private void RefreshGrowthTickRegistration()
        {
            if (!Application.isPlaying || !IsRuntimeActive)
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
            TerrainGenerator terrainGenerator = Handle.World.Terrain;
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

        protected override void OnOwningBlockChanged(Block block) { RefreshGrowthTickRegistration(); }

        internal bool ShouldShowSharedApples => TryGetAppleMinimumGrowth(out float minimumGrowth) && Growth >= minimumGrowth;

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
