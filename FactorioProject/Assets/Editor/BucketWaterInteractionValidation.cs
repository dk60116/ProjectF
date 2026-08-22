using System;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BucketWaterInteractionValidation
{
    private const string MenuPath = "Tools/ProjectF/Validation/Bucket Item Conversion %#F8";
    private const string PendingSessionKey = "ProjectF.BucketItemConversionValidation.Pending";
    private const double SetupTimeoutSeconds = 30d;
    private static double setupDeadline;

    static BucketWaterInteractionValidation()
    {
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem(MenuPath)]
    public static void Run()
    {
        WaterBucketItemGenerator.ValidateGeneratedAssets();
        if (EditorApplication.isPlaying)
        {
            BeginRuntimeValidation();
            return;
        }

        SessionState.SetBool(PendingSessionKey, true);
        EditorApplication.isPlaying = true;
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode
            && SessionState.GetBool(PendingSessionKey, false))
        {
            BeginRuntimeValidation();
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            StopRuntimeValidation();
        }
    }

    private static void BeginRuntimeValidation()
    {
        setupDeadline = EditorApplication.timeSinceStartup + SetupTimeoutSeconds;
        EditorApplication.update -= TryValidateRuntime;
        EditorApplication.update += TryValidateRuntime;
    }

    private static void TryValidateRuntime()
    {
        if (!EditorApplication.isPlaying)
        {
            StopRuntimeValidation();
            return;
        }

        if (EditorApplication.timeSinceStartup >= setupDeadline)
        {
            Fail("runtime setup did not finish within 30 seconds.");
            return;
        }

        GameManager gameManager = GameManager.Instance;
        Player player = gameManager != null ? gameManager.Player : null;
        ItemManager itemManager = gameManager != null ? gameManager.ItemManger : null;
        if (player == null || itemManager == null || gameManager.PlayerInteractionLocked)
        {
            return;
        }

        if (!TryFindDefinition(itemManager, Bucket.IsEmptyBucketDefinition, out ItemDefinition emptyBucket)
            || !Bucket.TryResolveWaterBucketDefinition(itemManager, out ItemDefinition waterBucket)
            || !Bucket.TryResolveOilBucketDefinition(itemManager, out ItemDefinition oilBucket)
            || !TryFindDefinition(
                itemManager,
                definition => string.Equals(definition.itemName, "Oil", StringComparison.OrdinalIgnoreCase),
                out ItemDefinition oilFluid))
        {
            Fail("Bucket, Water Bucket, Oil Bucket, or Oil definition is not available at runtime.");
            return;
        }

        PlayerBag handBag = player.GetHandBag();
        PlayerBag bag = player.GetBag();
        if (handBag == null || bag == null || handBag == bag)
        {
            Fail("player hand/bag inventory is unavailable.");
            return;
        }

        ClearCurrentHand(handBag);
        handBag.RemoveItems(emptyBucket.id, int.MaxValue);
        handBag.RemoveItems(waterBucket.id, int.MaxValue);
        handBag.RemoveItems(oilBucket.id, int.MaxValue);
        bag.RemoveItems(emptyBucket.id, int.MaxValue);
        bag.RemoveItems(waterBucket.id, int.MaxValue);
        bag.RemoveItems(oilBucket.id, int.MaxValue);

        if (!player.TryAddToHand(emptyBucket.id, out _)
            || !player.TryAddToHand(emptyBucket.id, out _)
            || handBag.GetSlotCount(0) != 2)
        {
            Fail("Bucket did not stack to two items in the hand after OneItem removal.");
            return;
        }

        if (!player.TryConvertHeldItem(emptyBucket.id, waterBucket.id)
            || handBag.GetSlotItemId(0) != emptyBucket.id
            || handBag.GetSlotCount(0) != 1
            || bag.GetTotalItemCount(waterBucket.id) != 1)
        {
            Fail("first conversion did not move one Water Bucket to the bag while retaining one empty Bucket.");
            return;
        }

        if (!player.TryConvertHeldItem(emptyBucket.id, waterBucket.id)
            || handBag.GetSlotItemId(0) != waterBucket.id
            || handBag.GetSlotCount(0) != 1
            || bag.GetTotalItemCount(waterBucket.id) != 1)
        {
            Fail("second conversion did not replace the last held Bucket with Water Bucket.");
            return;
        }

        PortableObject heldWaterBucket = handBag.GetTopObject(0);
        PortableBucketWaterVisual waterVisual = heldWaterBucket != null
            ? heldWaterBucket.GetComponent<PortableBucketWaterVisual>()
            : null;
        if (heldWaterBucket == null
            || heldWaterBucket.ItemId != waterBucket.id
            || waterVisual == null
            || waterVisual.OutlineFillRenderer != null
            || emptyBucket.oneItem
            || waterBucket.oneItem
            || emptyBucket.storesFluid
            || waterBucket.storesFluid)
        {
            Fail("Water Bucket visual or stack/fluid flags are inconsistent.");
            return;
        }

        int waterItemId = Pump.ResolveWaterItemId(null);
        if (!TryValidateInstalledFluid(
                emptyBucket,
                waterBucket,
                waterItemId,
                "water",
                out string fluidFailure)
            || !TryValidateInstalledFluid(
                emptyBucket,
                oilBucket,
                oilFluid.id,
                "oil",
                out fluidFailure))
        {
            Fail(fluidFailure);
            return;
        }

        StopRuntimeValidation();
        Debug.Log(
            "Bucket item conversion validation passed: 2x empty Bucket stack -> Water Bucket in bag -> "
            + "last Bucket replaced in hand, with portable/installed water and oil visuals, partial pipe-fill storage, "
            + "and no portable fluid gauge state.");
    }

    private static bool TryValidateInstalledFluid(
        ItemDefinition emptyBucket,
        ItemDefinition filledBucket,
        int fluidItemId,
        string fluidName,
        out string failure)
    {
        failure = null;
        Bucket installedFilledBucket = UnityEngine.Object.Instantiate(filledBucket.mapObject as Bucket);
        Bucket installedEmptyBucket = UnityEngine.Object.Instantiate(emptyBucket.mapObject as Bucket);
        try
        {
            PortableBucketWaterVisual filledVisual = installedFilledBucket != null
                ? installedFilledBucket.GetComponent<PortableBucketWaterVisual>()
                : null;
            if (installedFilledBucket == null
                || !installedFilledBucket.IsInstalledFluidSurfaceVisible
                || filledVisual == null
                || filledVisual.SurfaceMaterial != installedFilledBucket.ResolveFluidSurfaceMaterial(fluidItemId))
            {
                failure = $"installed {filledBucket.itemName} did not create its {fluidName} surface visual.";
                return false;
            }

            float fillCapacityLiters = installedEmptyBucket != null
                ? installedEmptyBucket.FluidStorageCapacityLiters
                : 0f;
            float quarterFillLiters = fillCapacityLiters * 0.25f;
            float maxInputLitersPerSecond = installedEmptyBucket != null
                ? installedEmptyBucket.MaximumFluidInputLitersPerSecond
                : 0f;
            if (installedEmptyBucket == null
                || fillCapacityLiters <= 0f
                || Mathf.Abs(fillCapacityLiters - emptyBucket.BucketFillDurationSeconds) > 0.0001f
                || maxInputLitersPerSecond <= 0f
                || Mathf.Abs(
                    fillCapacityLiters / maxInputLitersPerSecond
                    - emptyBucket.BucketFillDurationSeconds) > 0.0001f
                || fluidItemId < 0
                || !installedEmptyBucket.CanAcceptFluidItem(fluidItemId, quarterFillLiters)
                || installedEmptyBucket.CanProvideFluidItem(fluidItemId))
            {
                failure = $"installed empty Bucket did not accept and retain a partial {fluidName} fill.";
                return false;
            }

            installedEmptyBucket.SetStoredFluid(fluidItemId, quarterFillLiters);
            PortableBucketWaterVisual partialVisual =
                installedEmptyBucket.GetComponent<PortableBucketWaterVisual>();
            float quarterFillSurfaceY = partialVisual != null
                ? partialVisual.SurfaceLocalY
                : float.NaN;
            if (partialVisual == null
                || !partialVisual.IsSurfaceVisible
                || partialVisual.SurfaceMaterial != installedEmptyBucket.ResolveFluidSurfaceMaterial(fluidItemId)
                || Mathf.Abs(partialVisual.FillRatio - 0.25f) > 0.001f
                || float.IsNaN(quarterFillSurfaceY))
            {
                failure = $"installed empty Bucket did not show the initial partial-{fluidName} visual.";
                return false;
            }

            installedEmptyBucket.SetStoredFluid(fluidItemId, fillCapacityLiters * 0.5f);
            if (Mathf.Abs(installedEmptyBucket.InstalledFluidFillRatio - 0.5f) > 0.001f
                || Mathf.Abs(partialVisual.FillRatio - 0.5f) > 0.001f
                || partialVisual.SurfaceLocalY <= quarterFillSurfaceY + 0.0001f)
            {
                failure = $"installed empty Bucket did not show the rising-{fluidName} visual.";
                return false;
            }

            installedEmptyBucket.TryGetInstalledFullSurfaceTransform(
                fluidItemId,
                out Vector3 prefabFullSurfacePosition,
                out _,
                out _);
            installedEmptyBucket.SetStoredFluid(fluidItemId, fillCapacityLiters);
            if (Mathf.Abs(installedEmptyBucket.StoredFluidLiters - fillCapacityLiters) > 0.0001f
                || Mathf.Abs(partialVisual.FillRatio - 1f) > 0.001f
                || Mathf.Abs(partialVisual.SurfaceLocalY - prefabFullSurfacePosition.y) > 0.0001f
                || prefabFullSurfacePosition.y - quarterFillSurfaceY < 0.05f)
            {
                failure = $"full empty-Bucket {fluidName} surface did not finish at the filled prefab position.";
                return false;
            }

            return true;
        }
        finally
        {
            if (installedFilledBucket != null)
            {
                UnityEngine.Object.Destroy(installedFilledBucket.gameObject);
            }

            if (installedEmptyBucket != null)
            {
                UnityEngine.Object.Destroy(installedEmptyBucket.gameObject);
            }
        }
    }

    private static void ClearCurrentHand(PlayerBag handBag)
    {
        handBag.RefreshExternalStackCounts(false);
        int itemId = handBag.GetSlotItemId(0);
        if (itemId >= 0)
        {
            handBag.RemoveItems(itemId, int.MaxValue);
        }
    }

    private static bool TryFindDefinition(
        ItemManager itemManager,
        Predicate<ItemDefinition> predicate,
        out ItemDefinition result)
    {
        result = null;
        if (itemManager == null || itemManager.ItemDefinitions == null || predicate == null)
        {
            return false;
        }

        for (int i = 0; i < itemManager.ItemDefinitions.Count; i++)
        {
            ItemDefinition definition = itemManager.ItemDefinitions[i];
            if (definition == null || !predicate(definition))
            {
                continue;
            }

            result = definition;
            return true;
        }

        return false;
    }

    private static void Fail(string reason)
    {
        StopRuntimeValidation();
        Debug.LogError($"Bucket item conversion validation failed: {reason}");
    }

    private static void StopRuntimeValidation()
    {
        SessionState.SetBool(PendingSessionKey, false);
        EditorApplication.update -= TryValidateRuntime;
    }
}
