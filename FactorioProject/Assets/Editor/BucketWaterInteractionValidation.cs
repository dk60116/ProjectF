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

        if (!itemManager.TryGetItemDefinitionById(74, out ItemDefinition emptyBucket)
            || !Bucket.IsEmptyBucketDefinition(emptyBucket)
            || !Bucket.TryResolveWaterBucketDefinition(itemManager, out ItemDefinition waterBucket))
        {
            Fail("Bucket or Water Bucket definition is not available at runtime.");
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
        bag.RemoveItems(emptyBucket.id, int.MaxValue);
        bag.RemoveItems(waterBucket.id, int.MaxValue);

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

        Bucket installedWaterBucket = UnityEngine.Object.Instantiate(waterBucket.mapObject as Bucket);
        if (installedWaterBucket == null
            || !installedWaterBucket.IsInstalledWaterSurfaceVisible)
        {
            if (installedWaterBucket != null)
            {
                UnityEngine.Object.Destroy(installedWaterBucket.gameObject);
            }

            Fail("installed Water Bucket did not create its water surface visual.");
            return;
        }

        Bucket installedEmptyBucket = UnityEngine.Object.Instantiate(emptyBucket.mapObject as Bucket);
        int waterItemId = Pump.ResolveWaterItemId(null);
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
            || waterItemId < 0
            || !installedEmptyBucket.CanAcceptFluidItem(waterItemId, quarterFillLiters)
            || installedEmptyBucket.CanProvideFluidItem(waterItemId))
        {
            UnityEngine.Object.Destroy(installedWaterBucket.gameObject);
            if (installedEmptyBucket != null)
            {
                UnityEngine.Object.Destroy(installedEmptyBucket.gameObject);
            }

            Fail("installed empty Bucket did not accept and retain a partial water fill.");
            return;
        }

        installedEmptyBucket.SetStoredFluid(waterItemId, quarterFillLiters);
        PortableBucketWaterVisual partialWaterVisual =
            installedEmptyBucket.GetComponent<PortableBucketWaterVisual>();
        float quarterFillSurfaceY = partialWaterVisual != null
            ? partialWaterVisual.SurfaceLocalY
            : float.NaN;
        if (partialWaterVisual == null
            || !partialWaterVisual.IsSurfaceVisible
            || Mathf.Abs(partialWaterVisual.FillRatio - 0.25f) > 0.001f
            || float.IsNaN(quarterFillSurfaceY))
        {
            UnityEngine.Object.Destroy(installedWaterBucket.gameObject);
            UnityEngine.Object.Destroy(installedEmptyBucket.gameObject);
            Fail("installed empty Bucket did not show the initial partial-water visual.");
            return;
        }

        installedEmptyBucket.SetStoredFluid(waterItemId, fillCapacityLiters * 0.5f);
        if (Mathf.Abs(installedEmptyBucket.InstalledWaterFillRatio - 0.5f) > 0.001f
            || Mathf.Abs(partialWaterVisual.FillRatio - 0.5f) > 0.001f
            || partialWaterVisual.SurfaceLocalY <= quarterFillSurfaceY + 0.0001f)
        {
            UnityEngine.Object.Destroy(installedWaterBucket.gameObject);
            UnityEngine.Object.Destroy(installedEmptyBucket.gameObject);
            Fail("installed empty Bucket did not show the partial rising-water visual.");
            return;
        }

        installedEmptyBucket.TryGetInstalledFullWaterSurfaceTransform(
            out Vector3 prefabFullSurfacePosition,
            out _,
            out _);
        installedEmptyBucket.SetStoredFluid(waterItemId, fillCapacityLiters);
        if (Mathf.Abs(installedEmptyBucket.StoredFluidLiters - fillCapacityLiters) > 0.0001f
            || Mathf.Abs(partialWaterVisual.FillRatio - 1f) > 0.001f
            || Mathf.Abs(partialWaterVisual.SurfaceLocalY - prefabFullSurfacePosition.y) > 0.0001f
            || prefabFullSurfacePosition.y - quarterFillSurfaceY < 0.05f)
        {
            UnityEngine.Object.Destroy(installedWaterBucket.gameObject);
            UnityEngine.Object.Destroy(installedEmptyBucket.gameObject);
            Fail("full empty-Bucket water surface did not finish at the Water Bucket prefab position.");
            return;
        }

        UnityEngine.Object.Destroy(installedWaterBucket.gameObject);
        UnityEngine.Object.Destroy(installedEmptyBucket.gameObject);
        StopRuntimeValidation();
        Debug.Log(
            "Bucket item conversion validation passed: 2x empty Bucket stack -> Water Bucket in bag -> "
            + "last Bucket replaced in hand, with portable/installed water visuals, partial pipe-fill storage, "
            + "and no portable fluid gauge state.");
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
