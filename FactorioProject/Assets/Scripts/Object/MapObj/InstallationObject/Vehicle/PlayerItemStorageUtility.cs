using UnityEngine;

internal sealed class PlayerItemStorageReservation
{
    private readonly Player player;
    private readonly PlayerBag bag;
    private bool pending = true;

    public PortableObject Target { get; }

    public PlayerItemStorageReservation(Player player, PortableObject target)
    {
        this.player = player;
        Target = target;
    }

    public PlayerItemStorageReservation(PlayerBag bag, PortableObject target)
    {
        this.bag = bag;
        Target = target;
    }

    public void Commit()
    {
        if (!pending)
        {
            return;
        }

        pending = false;
        if (bag != null)
        {
            bag.CommitReservedObject(Target);
        }
        else if (player != null)
        {
            player.CommitReservedHandObject(Target);
        }
    }

    public void Release()
    {
        if (!pending)
        {
            return;
        }

        pending = false;
        if (bag != null)
        {
            bag.ReleaseReservedObject(Target);
        }
        else if (player != null)
        {
            player.ReleaseReservedHandObject(Target);
        }
    }
}

internal static class PlayerItemStorageUtility
{
    public static bool TryReservePlayerStorage(
        Player player,
        int itemId,
        int preferredSlotIndex,
        bool handOnly,
        out PlayerItemStorageReservation reservation)
    {
        reservation = null;
        if (player == null || itemId < 0)
        {
            return false;
        }

        if (handOnly)
        {
            return TryReserveHand(player, itemId, out reservation);
        }

        if (preferredSlotIndex >= 0)
        {
            if (TryReserveBag(player, itemId, preferredSlotIndex, false, out reservation))
            {
                return true;
            }

            if (player.HasMatchingHandStackSpace(itemId)
                && TryReserveHand(player, itemId, out reservation))
            {
                return true;
            }

            return TryReserveBag(player, itemId, -1, true, out reservation);
        }

        return (player.HasMatchingHandStackSpace(itemId)
                && TryReserveHand(player, itemId, out reservation))
               || TryReserveBag(player, itemId, -1, true, out reservation);
    }

    public static bool TryReserveBag(
        Player player,
        int itemId,
        int preferredSlotIndex,
        bool allowAnySlotFallback,
        out PlayerItemStorageReservation reservation)
    {
        return TryReserveBag(
            player != null ? player.GetBag() : null,
            itemId,
            preferredSlotIndex,
            allowAnySlotFallback,
            out reservation);
    }

    public static bool TryReserveBag(
        PlayerBag bag,
        int itemId,
        int preferredSlotIndex,
        bool allowAnySlotFallback,
        out PlayerItemStorageReservation reservation)
    {
        reservation = null;
        if (bag == null || itemId < 0)
        {
            return false;
        }

        PortableObject target = null;
        bool reserved = preferredSlotIndex >= 0
            && bag.TryReserveObjectToSlotOnly(preferredSlotIndex, itemId, out target);
        if (!reserved && (preferredSlotIndex < 0 || allowAnySlotFallback))
        {
            reserved = bag.TryReserveObject(itemId, out target);
        }

        if (!reserved || target == null)
        {
            return false;
        }

        reservation = new PlayerItemStorageReservation(bag, target);
        return true;
    }

    public static bool TryReserveHand(
        Player player,
        int itemId,
        out PlayerItemStorageReservation reservation)
    {
        reservation = null;
        if (player == null
            || itemId < 0
            || !player.TryReserveHandObject(itemId, out PortableObject target)
            || target == null)
        {
            return false;
        }

        reservation = new PlayerItemStorageReservation(player, target);
        return true;
    }

    public static void MoveVisualToPlayerStorage(
        PortableObject portableObject,
        PlayerItemStorageReservation reservation,
        System.Action<PortableObject> releaseSource = null,
        System.Action onComplete = null,
        System.Action onCancelled = null,
        float delay = 0f,
        System.Func<Vector3> startPositionProvider = null,
        bool trackStartPositionDuringMove = true)
    {
        if (reservation == null || reservation.Target == null)
        {
            releaseSource?.Invoke(portableObject);
            onCancelled?.Invoke();
            return;
        }

        if (portableObject == null)
        {
            reservation.Commit();
            onComplete?.Invoke();
            return;
        }

        portableObject.GetComponent<DroppedItemPickupGate>()?.ClearGate();
        portableObject.CancelMove();
        portableObject.SetBatchedRendering(false);
        portableObject.transform.SetParent(null, true);
        if (!Application.isPlaying)
        {
            reservation.Commit();
            ReleaseSource(portableObject, releaseSource);
            onComplete?.Invoke();
            return;
        }

        PortableObject storageTarget = reservation.Target;
        if (portableObject == storageTarget)
        {
            reservation.Commit();
            onComplete?.Invoke();
            return;
        }

        void HandleMoveCancelled(PortableObject cancelledPortableObject)
        {
            if (cancelledPortableObject != null)
            {
                cancelledPortableObject.MoveCancelled -= HandleMoveCancelled;
            }

            reservation.Release();
            ReleaseSource(cancelledPortableObject, releaseSource);
            onCancelled?.Invoke();
        }

        portableObject.MoveCancelled += HandleMoveCancelled;

        portableObject.MoveTo(
            storageTarget.transform,
            Mathf.Max(0f, delay),
            startPositionProvider,
            () =>
            {
                if (portableObject != null)
                {
                    portableObject.MoveCancelled -= HandleMoveCancelled;
                }

                reservation.Commit();
                ReleaseSource(portableObject, releaseSource);
                onComplete?.Invoke();
            },
            false,
            true,
            PortableObject.MoveToDuration,
            trackStartPositionDuringMove);
    }

    public static bool CreateVisualAndMoveToPlayerStorage(
        int itemId,
        PortableObject template,
        Vector3 startPosition,
        Quaternion startRotation,
        Vector3 startScale,
        PlayerItemStorageReservation reservation,
        string nameSuffix,
        float delay = 0f,
        System.Func<Vector3> startPositionProvider = null,
        System.Action onComplete = null,
        System.Action onCancelled = null,
        bool trackStartPositionDuringMove = true)
    {
        if (reservation == null || reservation.Target == null || itemId < 0)
        {
            reservation?.Release();
            onCancelled?.Invoke();
            return false;
        }

        PortableObject visualTemplate = template != null ? template : reservation.Target;
        PortableObject movingPortableObject = Object.Instantiate(
            visualTemplate,
            startPosition,
            startRotation);
        if (movingPortableObject == null)
        {
            reservation.Commit();
            onComplete?.Invoke();
            return true;
        }

        movingPortableObject.name = string.IsNullOrEmpty(nameSuffix)
            ? visualTemplate.name
            : $"{visualTemplate.name}_{nameSuffix}";
        movingPortableObject.transform.SetParent(null, true);
        movingPortableObject.transform.position = startPosition;
        movingPortableObject.transform.localScale = startScale;
        if (!movingPortableObject.gameObject.activeSelf)
        {
            movingPortableObject.gameObject.SetActive(true);
        }

        if (!movingPortableObject.SetItem(itemId))
        {
            DestroyPortableObject(movingPortableObject);
            reservation.Commit();
            onComplete?.Invoke();
            return true;
        }

        MoveVisualToPlayerStorage(
            movingPortableObject,
            reservation,
            null,
            onComplete,
            onCancelled,
            delay,
            startPositionProvider,
            trackStartPositionDuringMove);
        return true;
    }

    private static void ReleaseSource(
        PortableObject portableObject,
        System.Action<PortableObject> releaseSource)
    {
        if (releaseSource != null)
        {
            releaseSource(portableObject);
            return;
        }

        DestroyPortableObject(portableObject);
    }

    public static void DestroyPortableObject(PortableObject portableObject)
    {
        if (portableObject == null)
        {
            return;
        }

        portableObject.CancelMove();
        if (Application.isPlaying)
        {
            Object.Destroy(portableObject.gameObject);
            return;
        }

        Object.DestroyImmediate(portableObject.gameObject);
    }
}
