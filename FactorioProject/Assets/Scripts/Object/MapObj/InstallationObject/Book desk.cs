using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class Desk : InstallationObject, IPlayerMapObjectInteraction, IPersistentInstallationItemStorage
{
    private const string LegacyManualPointName = "Menual Point";
    private const string ManualPointName = "Manual Point";
    private static readonly Dictionary<int, int> ActiveStoredManualCounts = new Dictionary<int, int>();

    [SerializeField, FormerlySerializedAs("menualPoint")]
    private Transform manualPoint;
    [SerializeField, HideInInspector]
    private int storedManualItemId = -1;

    private PortableObject manualVisual;
    private PortableObject manualMoveVisual;
    private bool storedManualRegistered;

    public bool HasManual => storedManualItemId >= 0;
    public int StoredManualItemId => storedManualItemId;
    public int PersistentStoredItemId => storedManualItemId;

    public static bool HasStoredManual(int itemId)
    {
        return itemId >= 0
               && ActiveStoredManualCounts.TryGetValue(itemId, out int count)
               && count > 0;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        RegisterStoredManual();
        RefreshManualVisual();
    }

    protected override void OnDisable()
    {
        UnregisterStoredManual();
        CancelManualMoveAnimation();
        SetManualVisualActive(false);
        base.OnDisable();
    }

    public override void PrepareForPool()
    {
        CancelManualMoveAnimation();
        SetStoredManualItemId(-1, false);
        base.PrepareForPool();
    }

    public bool CanPlayerInteract(Player player)
    {
        if (player == null)
        {
            return false;
        }

        if (HasManual)
        {
            PlayerBag handBag = player.GetHandBag();
            handBag?.RefreshExternalStackCounts(false);
            return (handBag != null && handBag.HasSpaceForItem(storedManualItemId))
                   || (player.GetBag() is PlayerBag bag && bag.HasSpaceForItem(storedManualItemId));
        }

        return TryGetHeldManual(player, out _, out _);
    }

    public bool TryPlayerInteract(Player player)
    {
        if (player == null)
        {
            return false;
        }

        if (HasManual)
        {
            return TryTakeManual(player);
        }

        return TryGetHeldManual(player, out PlayerBag handBag, out _)
               && TryStoreManualFromSlot(player, handBag, 0);
    }

    public int GetInteractionIconItemId(Player player)
    {
        if (HasManual)
        {
            return storedManualItemId;
        }

        return TryGetHeldManual(player, out _, out int itemId)
            ? itemId
            : ResolveItemId();
    }

    public bool TryStoreManualFromSlot(Player player, PlayerBag sourceBag, int slotIndex)
    {
        if (HasManual || player == null || sourceBag == null || slotIndex < 0)
        {
            return false;
        }

        sourceBag.RefreshExternalStackCounts(false);
        int itemId = sourceBag.GetSlotItemId(slotIndex);
        if (sourceBag.GetSlotCount(slotIndex) <= 0 || !IsManualItem(itemId))
        {
            return false;
        }

        PortableObject sourcePortableObject = sourceBag.GetTopObject(slotIndex);
        Vector3 startPosition = sourcePortableObject != null
            ? sourcePortableObject.transform.position
            : player.transform.position;
        Quaternion startRotation = sourcePortableObject != null
            ? sourcePortableObject.transform.rotation
            : Quaternion.identity;
        Vector3 startScale = sourcePortableObject != null
            ? sourcePortableObject.transform.lossyScale
            : Vector3.one;

        if (!sourceBag.TryRemoveOneAtSlot(slotIndex, out int removedItemId, false)
            || removedItemId != itemId)
        {
            RestoreUnexpectedRemovedItem(player, sourceBag, slotIndex, removedItemId);
            return false;
        }

        SetStoredManualItemId(removedItemId, true);
        player.UpdateCarryState();
        PlayManualMoveAnimation(
            removedItemId,
            sourcePortableObject,
            startPosition,
            startRotation,
            startScale,
            ResolveManualPoint());
        return true;
    }

    public bool TryTakeManual(Player player)
    {
        if (player == null || !HasManual)
        {
            return false;
        }

        int itemId = storedManualItemId;
        Transform sourcePoint = ResolveManualPoint();
        PortableObject sourcePortableObject = manualVisual;
        Vector3 startPosition = sourcePortableObject != null
            ? sourcePortableObject.transform.position
            : sourcePoint != null ? sourcePoint.position : transform.position;
        Quaternion startRotation = sourcePortableObject != null
            ? sourcePortableObject.transform.rotation
            : sourcePoint != null ? sourcePoint.rotation : transform.rotation;
        Vector3 startScale = sourcePortableObject != null
            ? sourcePortableObject.transform.lossyScale
            : Vector3.one;

        if (!player.TryAddToHand(itemId, out PortableObject targetPortableObject)
            && !player.TryAddToBag(itemId, out targetPortableObject))
        {
            return false;
        }

        SetStoredManualItemId(-1, true);
        player.UpdateCarryState();
        PlayManualMoveAnimation(
            itemId,
            sourcePortableObject != null ? sourcePortableObject : targetPortableObject,
            startPosition,
            startRotation,
            startScale,
            targetPortableObject != null ? targetPortableObject.transform : null);
        return true;
    }

    public void ApplyPersistentStoredItemId(int itemId)
    {
        SetStoredManualItemId(itemId >= 0 && IsManualItem(itemId) ? itemId : -1, false);
    }

    private static bool TryGetHeldManual(Player player, out PlayerBag handBag, out int itemId)
    {
        handBag = player != null ? player.GetHandBag() : null;
        itemId = -1;
        if (handBag == null)
        {
            return false;
        }

        handBag.RefreshExternalStackCounts(false);
        itemId = handBag.GetSlotItemId(0);
        return handBag.GetSlotCount(0) > 0 && IsManualItem(itemId);
    }

    private static bool IsManualItem(int itemId)
    {
        ItemManager itemManager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        return itemManager != null
               && itemManager.TryGetItemDefinitionById(itemId, out ItemDefinition definition)
               && definition != null
               && definition.isManual;
    }

    private static void RestoreUnexpectedRemovedItem(
        Player player,
        PlayerBag sourceBag,
        int slotIndex,
        int removedItemId)
    {
        if (removedItemId < 0
            || sourceBag.TryAddObjectToSlotOnly(slotIndex, removedItemId, out _)
            || player.TryAddToBag(removedItemId, out _))
        {
            return;
        }

        player.TryAddToHand(removedItemId, out _);
    }

    private void SetStoredManualItemId(int itemId, bool savePersistentState)
    {
        if (storedManualItemId != itemId)
        {
            CancelManualMoveAnimation();
            UnregisterStoredManual();
            storedManualItemId = itemId;
            RegisterStoredManual();
        }

        RefreshManualVisual();
        if (savePersistentState)
        {
            SavePersistentState();
        }
    }

    private void PlayManualMoveAnimation(
        int itemId,
        PortableObject template,
        Vector3 startPosition,
        Quaternion startRotation,
        Vector3 startScale,
        Transform target)
    {
        if (target == null || template == null || !isActiveAndEnabled)
        {
            return;
        }

        PortableObject movingPortableObject = Instantiate(template, startPosition, startRotation);
        if (movingPortableObject == null)
        {
            return;
        }

        movingPortableObject.name = $"{template.name}_ManualMove";
        movingPortableObject.transform.SetParent(null, true);
        movingPortableObject.transform.position = startPosition;
        movingPortableObject.transform.localScale = startScale;
        if (!movingPortableObject.gameObject.activeSelf)
        {
            movingPortableObject.gameObject.SetActive(true);
        }

        if (!movingPortableObject.SetItem(itemId))
        {
            Destroy(movingPortableObject.gameObject);
            return;
        }

        CancelManualMoveAnimation();
        manualMoveVisual = movingPortableObject;
        SetManualVisualActive(false);
        movingPortableObject.MoveCancelled += HandleManualMoveAnimationCancelled;
        movingPortableObject.MoveTo(
            target,
            0f,
            null,
            () => CompleteManualMoveAnimation(movingPortableObject),
            false);
    }

    private void CompleteManualMoveAnimation(PortableObject movingPortableObject)
    {
        if (movingPortableObject != null)
        {
            movingPortableObject.MoveCancelled -= HandleManualMoveAnimationCancelled;
        }

        if (manualMoveVisual != movingPortableObject)
        {
            DestroyManualMoveVisual(movingPortableObject);
            return;
        }

        manualMoveVisual = null;
        DestroyManualMoveVisual(movingPortableObject);
        if (isActiveAndEnabled)
        {
            RefreshManualVisual();
        }
    }

    private void HandleManualMoveAnimationCancelled(PortableObject movingPortableObject)
    {
        if (manualMoveVisual != movingPortableObject)
        {
            return;
        }

        movingPortableObject.MoveCancelled -= HandleManualMoveAnimationCancelled;
        manualMoveVisual = null;
        DestroyManualMoveVisual(movingPortableObject);
        RefreshManualVisual();
    }

    private void CancelManualMoveAnimation()
    {
        PortableObject movingPortableObject = manualMoveVisual;
        manualMoveVisual = null;
        if (movingPortableObject != null)
        {
            movingPortableObject.MoveCancelled -= HandleManualMoveAnimationCancelled;
            movingPortableObject.CancelMove();
            DestroyManualMoveVisual(movingPortableObject);
        }
    }

    private static void DestroyManualMoveVisual(PortableObject movingPortableObject)
    {
        if (movingPortableObject != null)
        {
            Destroy(movingPortableObject.gameObject);
        }
    }

    private void RegisterStoredManual()
    {
        if (storedManualRegistered || storedManualItemId < 0 || !isActiveAndEnabled)
        {
            return;
        }

        ActiveStoredManualCounts.TryGetValue(storedManualItemId, out int count);
        ActiveStoredManualCounts[storedManualItemId] = count + 1;
        storedManualRegistered = true;
    }

    private void UnregisterStoredManual()
    {
        if (!storedManualRegistered)
        {
            return;
        }

        if (ActiveStoredManualCounts.TryGetValue(storedManualItemId, out int count))
        {
            if (count <= 1)
            {
                ActiveStoredManualCounts.Remove(storedManualItemId);
            }
            else
            {
                ActiveStoredManualCounts[storedManualItemId] = count - 1;
            }
        }

        storedManualRegistered = false;
    }

    private void RefreshManualVisual()
    {
        if (!HasManual)
        {
            SetManualVisualActive(false);
            return;
        }

        Transform targetPoint = ResolveManualPoint();
        if (targetPoint == null)
        {
            SetManualVisualActive(false);
            return;
        }

        if (manualVisual == null)
        {
            manualVisual = targetPoint.GetComponentInChildren<PortableObject>(true);
        }

        if (manualVisual == null)
        {
            GameObject visualObject = new GameObject("Manual Visual");
            visualObject.SetActive(false);
            visualObject.layer = gameObject.layer;
            visualObject.transform.SetParent(targetPoint, false);
            visualObject.AddComponent<MeshFilter>();
            visualObject.AddComponent<MeshRenderer>();
            manualVisual = visualObject.AddComponent<PortableObject>();
        }

        Transform visualTransform = manualVisual.transform;
        visualTransform.SetParent(targetPoint, false);
        visualTransform.localPosition = Vector3.zero;
        visualTransform.localRotation = Quaternion.identity;
        visualTransform.localScale = Vector3.one;
        manualVisual.SetBatchedRendering(true);
        manualVisual.SetCachedActive(manualVisual.SetItem(storedManualItemId));
    }

    private Transform ResolveManualPoint()
    {
        if (manualPoint != null && manualPoint.IsChildOf(transform))
        {
            return manualPoint;
        }

        manualPoint = transform.Find(ManualPointName) ?? transform.Find(LegacyManualPointName);
        return manualPoint;
    }

    private void SetManualVisualActive(bool active)
    {
        if (manualVisual != null)
        {
            manualVisual.SetCachedActive(active);
        }
    }

    private void SavePersistentState()
    {
        TerrainGenerator.ResolveActive()?.SaveRuntimeInstallationState(this);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        ResolveManualPoint();
    }
#endif
}
