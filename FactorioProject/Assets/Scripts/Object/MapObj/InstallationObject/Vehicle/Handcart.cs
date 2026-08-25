using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class Handcart : Vehicle, IPlayerItemStorage, IPersistentInstallationItemCollectionStorage
{
    private const int DefaultStackCapacity = 10;
    private const float MinimumMovementDistance = 0.0001f;
    private const float MinimumConnectionDistance = 0.05f;
    private const float ConnectionSideEpsilon = 0.01f;
    private const float HandleApproachMinimumFraction = 0.25f;
    private const int HandleConnectionSide = 1;
    private const int ObstacleBufferSize = 32;
    private const int TerrainBiomeWeightCount = 6;

    private static readonly HashSet<Handcart> ActiveRuntimeHandcarts = new HashSet<Handcart>();

    [Header("Cargo")]
    [SerializeField]
    private List<Transform> itemPoints;

    [SerializeField]
    private GameObject handleObject;

    [SerializeField]
    private PortableObject itemObjectPrefab;
    [SerializeField, Min(0.001f)]
    private float itemStackVerticalSpacing = 0.05f;
    [SerializeField, HideInInspector]
    private List<int> storedItemIds = new List<int>();

    [Header("Driving")]
    [SerializeField, Min(1f)]
    private float steeringDegreesPerSecond = 120f;
    [SerializeField, Range(0f, 0.5f)]
    private float inputDeadZone = 0.1f;
    [SerializeField, Range(-1f, 0f)]
    private float reverseInputDotThreshold = -0.5f;
    [SerializeField, Min(0.01f)]
    private float movementSubstepDistance = 0.08f;
    [SerializeField, Range(1, 64)]
    private int maxMovementSubsteps = 24;

    [Header("Coupling")]
    [SerializeField, Min(0.05f)]
    private float connectionCenterDistance = 1f;
    [SerializeField, Min(0.01f)]
    private float connectionSnapMaxDistance = 0.9f;
    [SerializeField, Min(0.1f)]
    [Tooltip("동물 중심이 운전대에서 이 거리와 동물 반경을 합친 범위 안에 있으면 연결할 수 있습니다.")]
    private float draftAnimalConnectionMaxDistance = 1.25f;
    [FormerlySerializedAs("draftAnimalHandleGap")]
    [SerializeField, Min(0f)]
    [Tooltip("동물 크기 비례 간격에 추가로 더하는 거리입니다.")]
    private float draftAnimalCenterOffset = 0.1f;
    [SerializeField, Range(0f, 1f)]
    [Tooltip("운전대 PlayerPoint와 동물 중심 사이에 적용할 동물 반경의 비율입니다.")]
    private float draftAnimalRadiusClearanceRatio = 0.5f;

    [Header("Driving Collision")]
    [SerializeField]
    private BoxCollider drivingCollider;
    [SerializeField]
    private LayerMask obstacleLayers = 393;
    [SerializeField, Min(0f)]
    private float collisionSkinWidth = 0.03f;
    [SerializeField]
    private bool blockWater = true;

    private readonly Collider[] obstacleBuffer = new Collider[ObstacleBufferSize];
    private readonly float[] terrainBiomeWeightBuffer = new float[TerrainBiomeWeightCount];
    private readonly List<PortableObject> itemVisuals = new List<PortableObject>();
    private readonly List<int> cargoStackItemIds = new List<int>();
    private readonly List<int> cargoStackCounts = new List<int>();
    private readonly HashSet<Handcart> connectedHandcarts = new HashSet<Handcart>();
    private readonly HashSet<Handcart> connectedGroupVisited = new HashSet<Handcart>();
    private readonly List<Handcart> connectedGroupScratch = new List<Handcart>(8);
    private readonly List<Handcart> connectionAlignmentGroupScratch = new List<Handcart>(8);
    private readonly List<Handcart> connectionSourceGroupScratch = new List<Handcart>(8);
    private TerrainGenerator cachedTerrain;
    private bool hideHandleForConnectionPreview;
    private Animal draftAnimal;
    private bool draftAnimalDriveActive;
    private Player draftAnimalRider;

    public int Capacity
    {
        get
        {
            ItemDefinition definition = BoundItemDefinition;
            return definition != null && definition.capacity > 0
                ? definition.capacity
                : DefaultStackCapacity;
        }
    }
    public int StackCount
    {
        get => GetUsableItemPointCount();
    }
    public int TotalCapacity => StackCount * Capacity;
    public int StoredItemCount => storedItemIds != null ? storedItemIds.Count : 0;
    public float ConnectionCenterDistance => Mathf.Max(MinimumConnectionDistance, connectionCenterDistance);
    public float ConnectionSnapMaxDistance => Mathf.Max(MinimumConnectionDistance, connectionSnapMaxDistance);
    public IReadOnlyCollection<Handcart> ConnectedHandcarts => connectedHandcarts;
    public Animal DraftAnimal => draftAnimal != null && draftAnimal.IsAlive ? draftAnimal : null;
    public bool HasDraftAnimal => DraftAnimal != null;
    public override float EffectiveVehicleMaxSpeed => VehicleMaxSpeed * ResolveConnectedLoadSpeedMultiplier();

    public float ResolvePlayerDrivenMaxSpeed(float playerMoveSpeed)
    {
        return Mathf.Max(0f, playerMoveSpeed) * ResolveConnectedLoadSpeedMultiplier();
    }

    public static float ResolveStrengthAdjustedLoadSpeedMultiplier(
        float baseLoadSpeedMultiplier,
        float strength)
    {
        float normalizedBaseMultiplier = Mathf.Clamp01(baseLoadSpeedMultiplier);
        float normalizedStrength = Mathf.Clamp(
            strength,
            AnimalDefinition.MinStrength,
            AnimalDefinition.MaxStrength) * 0.01f;
        float adjustedReduction = (1f - normalizedBaseMultiplier)
                                  * (1f - normalizedStrength);
        return Mathf.Clamp(1f - adjustedReduction, 0.01f, 1f);
    }

    public int GetStackCapacityForItem(int itemId)
    {
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        return ItemDefinition.ResolveStackCapacity(itemManager, itemId, Capacity);
    }

    public int CopyObjectInfoStacks(List<int> itemIds, List<int> itemCounts, int maxStackCount)
    {
        itemIds?.Clear();
        itemCounts?.Clear();
        if (itemIds == null || itemCounts == null || maxStackCount <= 0)
        {
            return 0;
        }

        BuildCargoStackLayout(storedItemIds != null ? storedItemIds.Count : 0, out _, out _);
        int count = Mathf.Min(
            Mathf.Min(cargoStackItemIds.Count, GetUsableItemPointCount()),
            maxStackCount);
        for (int i = 0; i < count; i++)
        {
            itemIds.Add(cargoStackItemIds[i]);
            itemCounts.Add(cargoStackCounts[i]);
        }

        return count;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ActiveRuntimeHandcarts.Add(this);
        hideHandleForConnectionPreview = false;
        RefreshConnectedGroupHandleObjects();
        RebuildCargoVisuals();
    }

    protected override void OnDisable()
    {
        ActiveRuntimeHandcarts.Remove(this);
        RefreshActiveConnectedGroupsAfterDeactivation();
        ClearCargoVisuals();
        base.OnDisable();
    }

    private void OnDestroy()
    {
        DetachDraftAnimal();
        ClearHandcartConnections();
        ActiveRuntimeHandcarts.Remove(this);
    }

    public override void PrepareForPool()
    {
        DetachDraftAnimal();
        hideHandleForConnectionPreview = false;
        ClearHandcartConnections();
        ActiveRuntimeHandcarts.Remove(this);
        ClearCargoVisuals();
        storedItemIds?.Clear();
        base.PrepareForPool();
    }

    protected override void OnPlacementRuntimeCleared()
    {
        DetachDraftAnimal();
        ClearHandcartConnections();
        base.OnPlacementRuntimeCleared();
    }

    public static bool TryFindDraftConnectionCandidate(Animal animal, out Handcart result)
    {
        result = null;
        if (animal == null || !animal.IsAlive || !animal.gameObject.activeInHierarchy)
        {
            return false;
        }

        float bestSqrDistance = float.PositiveInfinity;
        Vector3 animalCenter = animal.GetWorldCenter();
        foreach (Handcart candidate in ActiveRuntimeHandcarts)
        {
            if (candidate == null
                || !candidate.CanAttachDraftAnimal(animal)
                || !candidate.TryGetPlayerPoint(0, out Transform handlePoint))
            {
                continue;
            }

            Vector3 offset = animalCenter - handlePoint.position;
            offset.y = 0f;
            float sqrDistance = offset.sqrMagnitude;
            if (sqrDistance >= bestSqrDistance)
            {
                continue;
            }

            bestSqrDistance = sqrDistance;
            result = candidate;
        }

        return result != null;
    }

    public static bool TryFindByPlacementRuntime(
        Vector2Int anchorCoordinate,
        long placementSequence,
        out Handcart result)
    {
        foreach (Handcart candidate in ActiveRuntimeHandcarts)
        {
            if (candidate != null
                && candidate.gameObject.activeInHierarchy
                && candidate.RuntimePlacementSequence == placementSequence
                && candidate.TryGetPlacementRuntime(out Vector2Int candidateAnchor, out _)
                && candidateAnchor == anchorCoordinate)
            {
                result = candidate;
                return true;
            }
        }

        result = null;
        return false;
    }

    public bool CanAttachDraftAnimal(Animal animal)
    {
        if (!CanOwnDraftAnimal(animal)
            || !TryGetPlayerPoint(0, out Transform handlePoint))
        {
            return false;
        }

        Vector3 offset = animal.GetWorldCenter() - handlePoint.position;
        offset.y = 0f;
        float maxDistance = Mathf.Max(0.1f, draftAnimalConnectionMaxDistance)
                            + animal.GetWorldRadius();
        return offset.sqrMagnitude <= maxDistance * maxDistance;
    }

    public bool TryAttachDraftAnimal(Animal animal)
    {
        return TryAttachDraftAnimal(animal, true);
    }

    private bool TryAttachDraftAnimal(Animal animal, bool requireConnectionDistance)
    {
        if (!CanOwnDraftAnimal(animal)
            || requireConnectionDistance && !CanAttachDraftAnimal(animal)
            || !animal.TrySetAttachedDraftHandcart(this))
        {
            return false;
        }

        draftAnimal = animal;
        ResetVehicleMotion();
        SnapDraftAnimalToHandle();
        Physics.SyncTransforms();
        return true;
    }

    private bool CanOwnDraftAnimal(Animal animal)
    {
        return animal != null
               && animal.IsAlive
               && animal.gameObject.activeInHierarchy
               && DraftAnimal == null
               && animal.AttachedDraftHandcart == null
               && gameObject.activeInHierarchy
               && TryGetPlacementRuntime(out _, out _)
               && IsConnectedGroupHandleOwner()
               && TryGetPlayerPoint(0, out _);
    }

    public bool DetachDraftAnimal(Animal expectedAnimal = null)
    {
        Animal attachedAnimal = draftAnimal;
        if (attachedAnimal == null)
        {
            draftAnimal = null;
            draftAnimalDriveActive = false;
            draftAnimalRider = null;
            return false;
        }

        if (expectedAnimal != null && attachedAnimal != expectedAnimal)
        {
            return false;
        }

        draftAnimal = null;
        draftAnimalDriveActive = false;
        draftAnimalRider = null;
        ResetVehicleMotion();
        attachedAnimal.ClearAttachedDraftHandcart(this);
        return true;
    }

    public bool TryMovePulledByAnimal(
        Animal animal,
        Vector3 worldMoveDirection,
        float animalMoveSpeed,
        float deltaTime,
        Player animalRider,
        out float actualMoveSpeed)
    {
        actualMoveSpeed = 0f;
        if (animal == null
            || animal != DraftAnimal
            || !animal.IsAlive
            || !IsConnectedGroupHandleOwner()
            || deltaTime <= 0f)
        {
            return false;
        }

        Vector3 startPosition = transform.position;
        draftAnimalDriveActive = true;
        draftAnimalRider = animalRider;
        try
        {
            HandleMountedInput(worldMoveDirection, animalMoveSpeed, deltaTime, null);
        }
        finally
        {
            draftAnimalDriveActive = false;
            draftAnimalRider = null;
        }

        SnapDraftAnimalToHandle();
        Vector3 moved = transform.position - startPosition;
        moved.y = 0f;
        actualMoveSpeed = moved.magnitude / Mathf.Max(0.0001f, deltaTime);
        return actualMoveSpeed > MinimumMovementDistance;
    }

    private void SnapDraftAnimalToHandle()
    {
        Animal attachedAnimal = DraftAnimal;
        if (attachedAnimal == null || !TryGetPlayerPoint(0, out Transform handlePoint))
        {
            return;
        }

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= MinimumMovementDistance)
        {
            forward = Vector3.forward;
        }
        else
        {
            forward.Normalize();
        }

        Vector3 animalRootPosition = attachedAnimal.MovementRootPosition;
        Vector3 rootToCenter = attachedAnimal.GetWorldCenter() - animalRootPosition;
        float centerDistance = ResolveDraftAnimalCenterDistance(attachedAnimal);
        Vector3 targetCenter = handlePoint.position + forward * centerDistance;
        Vector3 targetRootPosition = targetCenter - rootToCenter;
        targetRootPosition.y = animalRootPosition.y;
        attachedAnimal.ApplyDraftPose(
            targetRootPosition,
            Quaternion.LookRotation(forward, Vector3.up));
    }

    private float ResolveDraftAnimalCenterDistance(Animal attachedAnimal)
    {
        float animalRadius = attachedAnimal != null
            ? attachedAnimal.GetWorldRadius()
            : 0f;
        return animalRadius * Mathf.Clamp01(draftAnimalRadiusClearanceRatio)
               + Mathf.Max(0f, draftAnimalCenterOffset);
    }

    public static void CollectActiveRuntimeHandcarts(ICollection<Handcart> results)
    {
        if (results == null || ActiveRuntimeHandcarts.Count <= 0)
        {
            return;
        }

        foreach (Handcart handcart in ActiveRuntimeHandcarts)
        {
            if (handcart == null
                || !handcart.gameObject.activeInHierarchy
                || !handcart.TryGetPlacementRuntime(out _, out _))
            {
                continue;
            }

            results.Add(handcart);
        }
    }

    public bool ConnectTo(Handcart other)
    {
        if (!CanConnectTo(other)
            || !TryResolveConnectionSnapPose(
                this,
                other,
                requireAvailableSides: true,
                out Handcart alignmentTarget,
                out Handcart alignmentSource,
                out int sourceSide,
                out Vector3 snappedPosition,
                out Quaternion snappedRotation))
        {
            return false;
        }

        TryGetConnectedGroupDraftAnimal(
            this,
            connectionSourceGroupScratch,
            out Animal draftAnimalToPreserve,
            out _);
        if (draftAnimalToPreserve == null)
        {
            TryGetConnectedGroupDraftAnimal(
                other,
                connectionAlignmentGroupScratch,
                out draftAnimalToPreserve,
                out _);
        }

        if (!TryAlignConnectionTargetGroup(
                alignmentTarget,
                alignmentSource,
                sourceSide,
                snappedPosition,
                snappedRotation))
        {
            ClearConnectionAlignmentScratch();
            return false;
        }

        bool changed = connectedHandcarts.Add(other);
        changed |= other.connectedHandcarts.Add(this);
        if (changed)
        {
            RefreshConnectedGroupHandleObjects();
            ReassignConnectedGroupDraftAnimal(draftAnimalToPreserve);
            RefreshConnectionAlignmentRuntimePlacements();
            Physics.SyncTransforms();
        }

        ClearConnectionAlignmentScratch();
        return changed;
    }

    private bool TryAlignConnectionTargetGroup(
        Handcart alignmentTarget,
        Handcart alignmentSource,
        int sourceSide,
        Vector3 snappedPosition,
        Quaternion snappedRotation)
    {
        CollectConnectedGroup(alignmentTarget, connectionAlignmentGroupScratch);
        CollectConnectedGroup(alignmentSource, connectionSourceGroupScratch);
        Animal alignmentDraftAnimal = null;
        for (int i = 0; i < connectionAlignmentGroupScratch.Count; i++)
        {
            Animal candidate = connectionAlignmentGroupScratch[i]?.DraftAnimal;
            if (candidate != null)
            {
                alignmentDraftAnimal = candidate;
                break;
            }
        }

        for (int i = 0; i < connectionAlignmentGroupScratch.Count; i++)
        {
            if (connectionSourceGroupScratch.Contains(connectionAlignmentGroupScratch[i]))
            {
                return false;
            }
        }

        Vector3 targetStartPosition = alignmentTarget.transform.position;
        Quaternion targetStartRotation = alignmentTarget.transform.rotation;
        Quaternion positionRotationDelta = snappedRotation * Quaternion.Inverse(targetStartRotation);
        if (connectionAlignmentGroupScratch.Count > 1
            && TryGetConnectedGroupExtensionDirection(
                alignmentTarget,
                connectionAlignmentGroupScratch,
                out Vector3 currentExtensionDirection))
        {
            Vector2 sourceForward = ResolvePlanarForward(alignmentSource.transform);
            Vector3 desiredExtensionDirection = new Vector3(
                sourceForward.x * sourceSide,
                0f,
                sourceForward.y * sourceSide);
            positionRotationDelta = ResolvePlanarRotationDelta(
                currentExtensionDirection,
                desiredExtensionDirection);
        }

        bool targetPoseChanges = (targetStartPosition - snappedPosition).sqrMagnitude
                                 > MinimumMovementDistance * MinimumMovementDistance
                                 || Quaternion.Angle(targetStartRotation, snappedRotation) > 0.01f
                                 || Quaternion.Angle(positionRotationDelta, Quaternion.identity) > 0.01f;
        if (!targetPoseChanges)
        {
            return true;
        }

        for (int i = 0; i < connectionAlignmentGroupScratch.Count; i++)
        {
            Handcart handcart = connectionAlignmentGroupScratch[i];
            if (handcart == null)
            {
                continue;
            }

            Vector3 targetPosition = snappedPosition
                                     + positionRotationDelta
                                     * (handcart.transform.position - targetStartPosition);
            Quaternion targetRotation = snappedRotation;
            if (handcart.IsDrivePoseBlocked(
                    targetPosition,
                    targetRotation,
                    alignmentDraftAnimal != null ? alignmentDraftAnimal.MountedRider : null,
                    connectionAlignmentGroupScratch,
                    alignmentDraftAnimal))
            {
                return false;
            }
        }

        for (int i = 0; i < connectionAlignmentGroupScratch.Count; i++)
        {
            Handcart handcart = connectionAlignmentGroupScratch[i];
            if (handcart == null)
            {
                continue;
            }

            Vector3 targetPosition = snappedPosition
                                     + positionRotationDelta
                                     * (handcart.transform.position - targetStartPosition);
            handcart.transform.SetPositionAndRotation(targetPosition, snappedRotation);
        }

        return true;
    }

    private static bool TryGetConnectedGroupExtensionDirection(
        Handcart endpoint,
        IReadOnlyList<Handcart> group,
        out Vector3 extensionDirection)
    {
        extensionDirection = Vector3.zero;
        if (endpoint == null || group == null)
        {
            return false;
        }

        for (int i = 0; i < group.Count; i++)
        {
            Handcart candidate = group[i];
            if (candidate == null
                || candidate == endpoint
                || !endpoint.connectedHandcarts.Contains(candidate))
            {
                continue;
            }

            extensionDirection = candidate.transform.position - endpoint.transform.position;
            extensionDirection.y = 0f;
            if (extensionDirection.sqrMagnitude <= MinimumMovementDistance)
            {
                continue;
            }

            extensionDirection.Normalize();
            return true;
        }

        extensionDirection = Vector3.zero;
        return false;
    }

    private static Quaternion ResolvePlanarRotationDelta(
        Vector3 currentDirection,
        Vector3 desiredDirection)
    {
        currentDirection.y = 0f;
        desiredDirection.y = 0f;
        if (currentDirection.sqrMagnitude <= MinimumMovementDistance
            || desiredDirection.sqrMagnitude <= MinimumMovementDistance)
        {
            return Quaternion.identity;
        }

        float signedAngle = Vector3.SignedAngle(
            currentDirection,
            desiredDirection,
            Vector3.up);
        return Quaternion.AngleAxis(signedAngle, Vector3.up);
    }

    private void RefreshConnectionAlignmentRuntimePlacements()
    {
        for (int i = 0; i < connectionAlignmentGroupScratch.Count; i++)
        {
            Handcart handcart = connectionAlignmentGroupScratch[i];
            if (handcart != null)
            {
                handcart.RefreshRuntimePlacement(
                    handcart.transform.position,
                    handcart.transform.rotation);
            }
        }
    }

    private void ClearConnectionAlignmentScratch()
    {
        connectionAlignmentGroupScratch.Clear();
        connectionSourceGroupScratch.Clear();
    }

    public void ConnectToNearbyActiveHandcarts()
    {
        if (!gameObject.activeInHierarchy || !TryGetPlacementRuntime(out _, out _))
        {
            return;
        }

        foreach (Handcart other in ActiveRuntimeHandcarts)
        {
            if (other != null && other != this)
            {
                ConnectTo(other);
            }
        }
    }

    public void DisconnectFrom(Handcart other)
    {
        if (other == null || !connectedHandcarts.Contains(other))
        {
            return;
        }

        TryGetConnectedGroupDraftAnimal(
            this,
            connectedGroupScratch,
            out Animal draftAnimalToPreserve,
            out Handcart draftAnimalCart);
        bool changed = connectedHandcarts.Remove(other);
        changed |= other.connectedHandcarts.Remove(this);
        if (changed)
        {
            RefreshConnectedGroupHandleObjects();
            other.RefreshConnectedGroupHandleObjects();
            if (draftAnimalToPreserve != null && draftAnimalCart != null)
            {
                draftAnimalCart.ReassignConnectedGroupDraftAnimal(draftAnimalToPreserve);
            }
        }
    }

    public void ClearHandcartConnections()
    {
        while (connectedHandcarts.Count > 0)
        {
            Handcart connected = null;
            foreach (Handcart candidate in connectedHandcarts)
            {
                connected = candidate;
                break;
            }

            if (connected == null)
            {
                connectedHandcarts.Clear();
                break;
            }

            DisconnectFrom(connected);
        }

        RefreshConnectedGroupHandleObjects();
    }

    public bool CanConnectTo(Handcart other)
    {
        if (other == null
            || other == this
            || connectedHandcarts.Contains(other)
            || !gameObject.activeInHierarchy
            || !other.gameObject.activeInHierarchy
            || !TryGetPlacementRuntime(out _, out _)
            || !other.TryGetPlacementRuntime(out _, out _)
            || IsInSameConnectedGroup(other))
        {
            return false;
        }

        bool thisGroupHasDraftAnimal = TryGetConnectedGroupDraftAnimal(
            this,
            connectionSourceGroupScratch,
            out _,
            out _);
        bool otherGroupHasDraftAnimal = TryGetConnectedGroupDraftAnimal(
            other,
            connectionAlignmentGroupScratch,
            out _,
            out _);
        return !(thisGroupHasDraftAnimal && otherGroupHasDraftAnimal)
               && TryResolveConnectionSnapPose(
                   this,
                   other,
                   requireAvailableSides: true,
                   out _,
                   out _,
                   out _,
                   out _,
                   out _);
    }

    private bool TryGetConnectedGroupDraftAnimal(
        Handcart root,
        List<Handcart> scratch,
        out Animal animal,
        out Handcart animalCart)
    {
        CollectConnectedGroup(root, scratch);
        for (int i = 0; i < scratch.Count; i++)
        {
            Handcart candidateCart = scratch[i];
            Animal candidateAnimal = candidateCart != null
                ? candidateCart.DraftAnimal
                : null;
            if (candidateAnimal != null)
            {
                animal = candidateAnimal;
                animalCart = candidateCart;
                scratch.Clear();
                return true;
            }
        }

        scratch.Clear();
        animal = null;
        animalCart = null;
        return false;
    }

    private void ReassignConnectedGroupDraftAnimal(Animal animal)
    {
        if (animal == null)
        {
            return;
        }

        CollectConnectedGroup();
        Handcart handleOwner = ResolveConnectedGroupHandleOwner();
        connectedGroupScratch.Clear();
        Handcart currentOwner = animal.AttachedDraftHandcart;
        if (handleOwner == null)
        {
            currentOwner?.DetachDraftAnimal(animal);
            return;
        }

        if (currentOwner == handleOwner)
        {
            handleOwner.SnapDraftAnimalToHandle();
            return;
        }

        currentOwner?.DetachDraftAnimal(animal);
        handleOwner.TryAttachDraftAnimal(animal, false);
    }

    private bool IsInSameConnectedGroup(Handcart other)
    {
        CollectConnectedGroup(this, connectionSourceGroupScratch);
        bool contains = connectionSourceGroupScratch.Contains(other);
        connectionSourceGroupScratch.Clear();
        return contains;
    }

    public static bool CanConnectByPose(Handcart first, Handcart second)
    {
        return TryResolveConnectionSnapPose(
            first,
            second,
            requireAvailableSides: false,
            out _,
            out _,
            out _,
            out _,
            out _);
    }

    public static bool TryResolveConnectionPreviewPose(
        Handcart preview,
        Handcart connectionSource,
        Vector3 previewGridPosition,
        out int connectionSourceSide,
        out Vector3 snappedPosition,
        out Quaternion snappedRotation)
    {
        return TryResolveTargetConnectionSnapPose(
            connectionSource,
            preview,
            previewGridPosition,
            requireAvailableSides: true,
            out connectionSourceSide,
            out snappedPosition,
            out snappedRotation);
    }

    internal bool ApplyConnectionPreviewSide(int connectionSide)
    {
        if (connectionSide != HandleConnectionSide)
        {
            return false;
        }

        hideHandleForConnectionPreview = true;
        RefreshConnectionHandleObject();
        return true;
    }

    internal void ClearConnectionPreviewHandleOverride()
    {
        hideHandleForConnectionPreview = false;
        RefreshConnectionHandleObject();
    }

    internal void RefreshConnectionHandleObject()
    {
        CollectConnectedGroup();
        Handcart handleOwner = ResolveConnectedGroupHandleOwner();
        ApplyConnectionHandleObjectState(handleOwner);
        connectedGroupScratch.Clear();
    }

    private void RefreshConnectedGroupHandleObjects()
    {
        CollectConnectedGroup();
        Handcart handleOwner = ResolveConnectedGroupHandleOwner();
        for (int i = 0; i < connectedGroupScratch.Count; i++)
        {
            connectedGroupScratch[i]?.ApplyConnectionHandleObjectState(handleOwner);
        }

        connectedGroupScratch.Clear();
    }

    private void RefreshActiveConnectedGroupsAfterDeactivation()
    {
        foreach (Handcart connected in connectedHandcarts)
        {
            if (connected != null && connected.gameObject.activeInHierarchy)
            {
                connected.RefreshConnectedGroupHandleObjects();
            }
        }
    }

    private Handcart ResolveConnectedGroupHandleOwner()
    {
        Vector3 activeGroupCenter = Vector3.zero;
        int activeGroupCount = 0;
        for (int i = 0; i < connectedGroupScratch.Count; i++)
        {
            Handcart candidate = connectedGroupScratch[i];
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            activeGroupCenter += candidate.transform.position;
            activeGroupCount++;
        }

        if (activeGroupCount <= 0)
        {
            return null;
        }

        activeGroupCenter /= activeGroupCount;
        Handcart exteriorEndpoint = null;
        float exteriorEndpointScore = float.NegativeInfinity;
        Handcart invalidTopologyFallback = null;
        float invalidTopologyFallbackScore = float.NegativeInfinity;
        for (int i = 0; i < connectedGroupScratch.Count; i++)
        {
            Handcart candidate = connectedGroupScratch[i];
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            float exteriorScore = ResolveHandleExteriorScore(candidate, activeGroupCenter);
            if (IsBetterHandleOwner(
                    invalidTopologyFallback,
                    invalidTopologyFallbackScore,
                    candidate,
                    exteriorScore))
            {
                invalidTopologyFallback = candidate;
                invalidTopologyFallbackScore = exteriorScore;
            }

            if (candidate.GetActiveConnectionCount() > 1
                || !IsBetterHandleOwner(
                    exteriorEndpoint,
                    exteriorEndpointScore,
                    candidate,
                    exteriorScore))
            {
                continue;
            }

            exteriorEndpoint = candidate;
            exteriorEndpointScore = exteriorScore;
        }

        return exteriorEndpoint ?? invalidTopologyFallback;
    }

    private static float ResolveHandleExteriorScore(Handcart candidate, Vector3 groupCenter)
    {
        Vector2 forward = ResolvePlanarForward(candidate.transform);
        Vector3 centerOffset = candidate.transform.position - groupCenter;
        return centerOffset.x * forward.x + centerOffset.z * forward.y;
    }

    private static bool IsBetterHandleOwner(
        Handcart current,
        float currentScore,
        Handcart candidate,
        float candidateScore)
    {
        if (current == null || candidateScore > currentScore + ConnectionSideEpsilon)
        {
            return true;
        }

        return Mathf.Abs(candidateScore - currentScore) <= ConnectionSideEpsilon
               && SelectPreferredHandleOwner(current, candidate) == candidate;
    }

    private static Handcart SelectPreferredHandleOwner(Handcart current, Handcart candidate)
    {
        if (current == null)
        {
            return candidate;
        }

        long currentSequence = current.RuntimePlacementSequence;
        long candidateSequence = candidate.RuntimePlacementSequence;
        if (currentSequence != candidateSequence)
        {
            if (currentSequence <= 0)
            {
                return candidate;
            }

            if (candidateSequence > 0 && candidateSequence < currentSequence)
            {
                return candidate;
            }

            return current;
        }

        return candidate.GetInstanceID() < current.GetInstanceID()
            ? candidate
            : current;
    }

    private int GetActiveConnectionCount()
    {
        int count = 0;
        foreach (Handcart connected in connectedHandcarts)
        {
            if (connected != null && connected.gameObject.activeInHierarchy)
            {
                count++;
            }
        }

        return count;
    }

    private void ApplyConnectionHandleObjectState(Handcart handleOwner)
    {
        SetHandleObjectActive(
            this == handleOwner
            && !hideHandleForConnectionPreview);
    }

    private static bool TryResolveConnectionSnapPose(
        Handcart first,
        Handcart second,
        bool requireAvailableSides,
        out Handcart alignmentTarget,
        out Handcart alignmentSource,
        out int sourceSide,
        out Vector3 snappedPosition,
        out Quaternion snappedRotation)
    {
        alignmentTarget = null;
        alignmentSource = null;
        sourceSide = 0;
        snappedPosition = default;
        snappedRotation = Quaternion.identity;
        if (first == null || second == null || first == second)
        {
            return false;
        }

        alignmentTarget = first.RuntimePlacementSequence >= second.RuntimePlacementSequence
            ? first
            : second;
        alignmentSource = alignmentTarget == first ? second : first;
        return TryResolveTargetConnectionSnapPose(
            alignmentSource,
            alignmentTarget,
            alignmentTarget.transform.position,
            requireAvailableSides,
            out sourceSide,
            out snappedPosition,
            out snappedRotation);
    }

    private static bool TryResolveTargetConnectionSnapPose(
        Handcart connectionSource,
        Handcart connectionTarget,
        Vector3 targetPosition,
        bool requireAvailableSides,
        out int sourceSide,
        out Vector3 snappedPosition,
        out Quaternion snappedRotation)
    {
        sourceSide = 0;
        snappedPosition = default;
        snappedRotation = Quaternion.identity;
        if (connectionSource == null
            || connectionTarget == null
            || connectionSource == connectionTarget)
        {
            return false;
        }

        Vector2 sourceForward = ResolvePlanarForward(connectionSource.transform);
        float centerDistance = (connectionSource.ConnectionCenterDistance
                                + connectionTarget.ConnectionCenterDistance) * 0.5f;
        float maxSnapDistance = Mathf.Max(
            connectionSource.ConnectionSnapMaxDistance,
            connectionTarget.ConnectionSnapMaxDistance);
        float maxSnapDistanceSqr = maxSnapDistance * maxSnapDistance;
        float bestDistanceSqr = float.MaxValue;
        for (int side = -1; side <= 1; side += 2)
        {
            if (requireAvailableSides
                && (!connectionSource.CanUseConnectionSide(side, connectionTarget)
                    || !connectionTarget.CanAcceptEndpointConnection(connectionSource)))
            {
                continue;
            }

            Vector3 candidatePosition = connectionSource.transform.position;
            candidatePosition.x += sourceForward.x * centerDistance * side;
            candidatePosition.z += sourceForward.y * centerDistance * side;
            float distanceSqr = (targetPosition - candidatePosition).sqrMagnitude;
            if (distanceSqr > maxSnapDistanceSqr || distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            bestDistanceSqr = distanceSqr;
            sourceSide = side;
            snappedPosition = candidatePosition;
            snappedRotation = connectionSource.transform.rotation;
        }

        if (sourceSide == 0)
        {
            return false;
        }

        return true;
    }

    private bool CanAcceptEndpointConnection(Handcart ignoredCandidate)
    {
        int connectionCount = 0;
        foreach (Handcart connected in connectedHandcarts)
        {
            if (connected == null || connected == ignoredCandidate)
            {
                continue;
            }

            connectionCount++;
            if (connectionCount >= 2)
            {
                return false;
            }
        }

        return true;
    }

    private bool CanUseConnectionSide(int side, Handcart ignoredCandidate)
    {
        if (side == 0)
        {
            return false;
        }

        foreach (Handcart connected in connectedHandcarts)
        {
            if (connected != null
                && connected != ignoredCandidate
                && ResolveConnectionSide(connected) == side)
            {
                return false;
            }
        }

        return true;
    }

    private bool HasConnectionOnSide(int side)
    {
        foreach (Handcart connected in connectedHandcarts)
        {
            if (connected != null && ResolveConnectionSide(connected) == side)
            {
                return true;
            }
        }

        return false;
    }

    private int ResolveConnectionSide(Handcart other)
    {
        if (other == null)
        {
            return 0;
        }

        Vector2 forward = ResolvePlanarForward(transform);
        Vector3 worldDelta = other.transform.position - transform.position;
        float along = Vector2.Dot(new Vector2(worldDelta.x, worldDelta.z), forward);
        if (Mathf.Abs(along) <= ConnectionSideEpsilon)
        {
            return 0;
        }

        return along > 0f ? 1 : -1;
    }

    private static Vector2 ResolvePlanarForward(Transform target)
    {
        Vector3 forward3D = target != null ? target.forward : Vector3.forward;
        Vector2 forward = new Vector2(forward3D.x, forward3D.z);
        return forward.sqrMagnitude > MinimumMovementDistance
            ? forward.normalized
            : Vector2.up;
    }

    private void SetHandleObjectActive(bool active)
    {
        if (handleObject != null && handleObject.activeSelf != active)
        {
            handleObject.SetActive(active);
        }
    }

    public bool TryAddItemStack(
        int itemId,
        int itemCount,
        Vector3 startWorldPosition,
        Func<Vector3> startWorldPositionProvider,
        float moveInterval,
        out int addedCount)
    {
        addedCount = 0;
        if (itemId < 0
            || itemCount <= 0
            || InputOutputModule.IsFluidItemId(itemId))
        {
            return false;
        }

        EnsureCargoState();
        if (GetUsableItemPointCount() <= 0)
        {
            return false;
        }

        float interval = Mathf.Max(0f, moveInterval);
        for (int i = 0; i < itemCount; i++)
        {
            if (!CanAddCargoItem(itemId))
            {
                break;
            }

            int cargoIndex = storedItemIds.Count;
            storedItemIds.Add(itemId);

            PortableObject visual = CreateCargoVisual(itemId);
            itemVisuals.Add(visual);
            if (visual != null)
            {
                PlayCargoMove(
                    visual,
                    cargoIndex,
                    startWorldPosition,
                    startWorldPositionProvider,
                    i * interval);
            }

            addedCount++;
        }

        if (addedCount <= 0)
        {
            return false;
        }

        SaveCargoState();
        return true;
    }

    public bool TryPickupOneItemToBag(
        Player player,
        Vector3 playerPosition,
        float pickupRange,
        int preferredSlotIndex,
        int preferredItemId = -1)
    {
        return TryPickupOneItem(
            player,
            playerPosition,
            pickupRange,
            preferredItemId,
            preferredSlotIndex,
            false);
    }

    public bool TryPickupOneItemToHand(
        Player player,
        Vector3 playerPosition,
        float pickupRange,
        int preferredItemId = -1)
    {
        return TryPickupOneItem(
            player,
            playerPosition,
            pickupRange,
            preferredItemId,
            -1,
            true);
    }

    private bool TryPickupOneItem(
        Player player,
        Vector3 playerPosition,
        float pickupRange,
        int preferredItemId,
        int preferredSlotIndex,
        bool handOnly)
    {
        if (player == null || pickupRange <= 0f)
        {
            return false;
        }

        EnsureCargoState();
        if (!TryFindPickupCargoItemIndex(
                preferredItemId,
                playerPosition,
                pickupRange,
                out int cargoIndex))
        {
            return false;
        }

        int itemId = storedItemIds[cargoIndex];
        bool accepted = handOnly
            ? player.TryAddToHand(itemId, out PortableObject targetPortableObject)
            : PlayerItemStorageUtility.TryAddToPlayerStorage(
                player,
                itemId,
                preferredSlotIndex,
                out targetPortableObject);
        if (!accepted)
        {
            return false;
        }

        PortableObject sourceVisual = cargoIndex < itemVisuals.Count
            ? itemVisuals[cargoIndex]
            : null;
        storedItemIds.RemoveAt(cargoIndex);
        if (cargoIndex < itemVisuals.Count)
        {
            itemVisuals.RemoveAt(cargoIndex);
        }

        PlayerItemStorageUtility.MoveVisualToPlayerStorage(sourceVisual, targetPortableObject);
        ReflowCargoVisuals(cargoIndex);
        SaveCargoState();
        return true;
    }

    public bool TryPreviewPickupItems(
        Player player,
        Vector3 playerPosition,
        float pickupRange,
        int preferredItemId,
        out int previewItemId,
        out int previewPickupCount)
    {
        previewItemId = -1;
        previewPickupCount = 0;
        if (player == null || pickupRange <= 0f)
        {
            return false;
        }

        EnsureCargoState();
        if (!TryFindPickupCargoItemIndex(
                preferredItemId,
                playerPosition,
                pickupRange,
                out int cargoIndex))
        {
            return false;
        }

        previewItemId = storedItemIds[cargoIndex];
        for (int i = 0; i < storedItemIds.Count; i++)
        {
            if (storedItemIds[i] == previewItemId)
            {
                previewPickupCount++;
            }
        }

        return previewPickupCount > 0;
    }

    public void CapturePersistentStoredItemIds(List<int> destination)
    {
        if (destination == null)
        {
            return;
        }

        destination.Clear();
        if (storedItemIds == null)
        {
            return;
        }

        for (int i = 0; i < storedItemIds.Count; i++)
        {
            int itemId = storedItemIds[i];
            if (itemId >= 0)
            {
                destination.Add(itemId);
            }
        }
    }

    public void ApplyPersistentStoredItemIds(IReadOnlyList<int> itemIds)
    {
        ClearCargoVisuals();
        storedItemIds ??= new List<int>();
        storedItemIds.Clear();
        if (itemIds != null)
        {
            for (int i = 0; i < itemIds.Count; i++)
            {
                if (itemIds[i] >= 0)
                {
                    storedItemIds.Add(itemIds[i]);
                }
            }
        }

        RebuildCargoVisuals();
    }

    private void EnsureCargoState()
    {
        storedItemIds ??= new List<int>();
        if (itemVisuals.Count == storedItemIds.Count)
        {
            return;
        }

        RebuildCargoVisuals();
    }

    private void RebuildCargoVisuals()
    {
        ClearCargoVisuals();
        storedItemIds ??= new List<int>();
        for (int i = 0; i < storedItemIds.Count; i++)
        {
            PortableObject visual = CreateCargoVisual(storedItemIds[i]);
            itemVisuals.Add(visual);
            SettleCargoVisual(visual, i);
        }
    }

    private PortableObject CreateCargoVisual(int itemId)
    {
        PortableObject visual = itemObjectPrefab != null
            ? Instantiate(itemObjectPrefab)
            : CreateGeneratedCargoVisual(itemId);
        if (visual == null)
        {
            return null;
        }

        visual.gameObject.layer = gameObject.layer;
        if (!visual.SetItem(itemId))
        {
            PlayerItemStorageUtility.DestroyPortableObject(visual);
            return null;
        }

        visual.SetBatchedRendering(false);
        return visual;
    }

    private PortableObject CreateGeneratedCargoVisual(int itemId)
    {
        GameObject itemObject = new GameObject($"HandcartCargoItem_{itemId}");
        itemObject.transform.SetParent(transform, false);
        itemObject.AddComponent<MeshFilter>();
        itemObject.AddComponent<MeshRenderer>();
        return itemObject.AddComponent<PortableObject>();
    }

    private void PlayCargoMove(
        PortableObject visual,
        int cargoIndex,
        Vector3 startWorldPosition,
        Func<Vector3> startWorldPositionProvider,
        float delay)
    {
        if (visual == null
            || !TryGetCargoPose(cargoIndex, out Transform targetPoint, out Vector3 targetLocalPosition))
        {
            return;
        }

        visual.transform.SetParent(targetPoint, true);
        visual.transform.position = startWorldPositionProvider != null
            ? startWorldPositionProvider()
            : startWorldPosition;
        visual.transform.rotation = targetPoint.rotation;
        visual.transform.localScale = Vector3.one;
        visual.gameObject.SetActive(true);

        Vector3 finalWorldPosition = targetPoint.TransformPoint(targetLocalPosition);
        visual.MoveTo(
            () => targetPoint != null ? targetPoint.TransformPoint(targetLocalPosition) : finalWorldPosition,
            Mathf.Max(0f, delay),
            startWorldPositionProvider,
            () => SettleCargoVisual(visual, cargoIndex),
            false,
            true,
            PortableObject.MoveToDuration,
            false);
    }

    private void ReflowCargoVisuals(int startIndex)
    {
        for (int i = Mathf.Max(0, startIndex); i < itemVisuals.Count; i++)
        {
            itemVisuals[i]?.CancelMove();
            SettleCargoVisual(itemVisuals[i], i);
        }
    }

    private void SettleCargoVisual(PortableObject visual, int cargoIndex)
    {
        if (visual == null
            || !TryGetCargoPose(cargoIndex, out Transform targetPoint, out Vector3 targetLocalPosition))
        {
            return;
        }

        visual.transform.SetParent(targetPoint, false);
        visual.transform.localPosition = targetLocalPosition;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;
        visual.gameObject.SetActive(true);
        visual.SetBatchedRendering(false);
        visual.GetOrAddPickupGate()?.MarkSettled();
    }

    private bool TryGetCargoPose(
        int cargoIndex,
        out Transform targetPoint,
        out Vector3 targetLocalPosition)
    {
        targetPoint = null;
        targetLocalPosition = Vector3.zero;
        if (cargoIndex < 0
            || storedItemIds == null
            || cargoIndex >= storedItemIds.Count
            || !TryResolveCargoStack(cargoIndex, out int stackIndex, out int stackItemIndex))
        {
            return false;
        }

        targetPoint = GetUsableItemPoint(stackIndex);
        if (targetPoint == null)
        {
            return false;
        }

        targetLocalPosition.y = stackItemIndex * Mathf.Max(0.001f, itemStackVerticalSpacing);
        return true;
    }

    private bool CanAddCargoItem(int itemId)
    {
        BuildCargoStackLayout(storedItemIds != null ? storedItemIds.Count : 0, out _, out _);
        int stackCapacity = GetStackCapacityForItem(itemId);
        int usableStackCount = GetUsableItemPointCount();
        int visibleStackCount = Mathf.Min(cargoStackItemIds.Count, usableStackCount);
        for (int i = 0; i < visibleStackCount; i++)
        {
            if (cargoStackItemIds[i] == itemId && cargoStackCounts[i] < stackCapacity)
            {
                return true;
            }
        }

        return cargoStackItemIds.Count < usableStackCount;
    }

    private bool TryResolveCargoStack(
        int cargoIndex,
        out int stackIndex,
        out int stackItemIndex)
    {
        if (storedItemIds == null || cargoIndex < 0 || cargoIndex >= storedItemIds.Count)
        {
            stackIndex = -1;
            stackItemIndex = -1;
            return false;
        }

        BuildCargoStackLayout(cargoIndex + 1, out stackIndex, out stackItemIndex);
        return stackIndex >= 0 && stackIndex < GetUsableItemPointCount();
    }

    private void BuildCargoStackLayout(
        int itemLimit,
        out int lastStackIndex,
        out int lastStackItemIndex)
    {
        cargoStackItemIds.Clear();
        cargoStackCounts.Clear();
        lastStackIndex = -1;
        lastStackItemIndex = -1;
        if (storedItemIds == null)
        {
            return;
        }

        int count = Mathf.Min(Mathf.Max(0, itemLimit), storedItemIds.Count);
        for (int itemIndex = 0; itemIndex < count; itemIndex++)
        {
            int itemId = storedItemIds[itemIndex];
            int stackCapacity = GetStackCapacityForItem(itemId);
            int targetStackIndex = -1;
            for (int stackIndex = 0; stackIndex < cargoStackItemIds.Count; stackIndex++)
            {
                if (cargoStackItemIds[stackIndex] == itemId
                    && cargoStackCounts[stackIndex] < stackCapacity)
                {
                    targetStackIndex = stackIndex;
                    break;
                }
            }

            if (targetStackIndex < 0)
            {
                targetStackIndex = cargoStackItemIds.Count;
                cargoStackItemIds.Add(itemId);
                cargoStackCounts.Add(0);
            }

            lastStackIndex = targetStackIndex;
            lastStackItemIndex = cargoStackCounts[targetStackIndex];
            cargoStackCounts[targetStackIndex] = lastStackItemIndex + 1;
        }
    }

    private bool TryFindPickupCargoItemIndex(
        int preferredItemId,
        Vector3 playerPosition,
        float pickupRange,
        out int cargoIndex)
    {
        cargoIndex = -1;
        if (storedItemIds == null || pickupRange <= 0f)
        {
            return false;
        }

        float pickupRangeSqr = pickupRange * pickupRange;
        float nearestDistanceSqr = float.MaxValue;
        for (int i = storedItemIds.Count - 1; i >= 0; i--)
        {
            int itemId = storedItemIds[i];
            if (itemId < 0 || (preferredItemId >= 0 && itemId != preferredItemId))
            {
                continue;
            }

            Vector3 offset = ResolveCargoItemWorldPosition(i) - playerPosition;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr > pickupRangeSqr || distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            nearestDistanceSqr = distanceSqr;
            cargoIndex = i;
        }

        return cargoIndex >= 0;
    }

    private Vector3 ResolveCargoItemWorldPosition(int cargoIndex)
    {
        if (cargoIndex >= 0
            && cargoIndex < itemVisuals.Count
            && itemVisuals[cargoIndex] != null)
        {
            return itemVisuals[cargoIndex].transform.position;
        }

        if (TryGetCargoPose(cargoIndex, out Transform targetPoint, out Vector3 localPosition))
        {
            return targetPoint.TransformPoint(localPosition);
        }

        return transform.position;
    }

    private int GetUsableItemPointCount()
    {
        if (itemPoints == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < itemPoints.Count; i++)
        {
            if (itemPoints[i] != null)
            {
                count++;
            }
        }

        return count;
    }

    private Transform GetUsableItemPoint(int stackIndex)
    {
        if (itemPoints == null || stackIndex < 0)
        {
            return null;
        }

        int currentStackIndex = 0;
        for (int i = 0; i < itemPoints.Count; i++)
        {
            Transform itemPoint = itemPoints[i];
            if (itemPoint == null)
            {
                continue;
            }

            if (currentStackIndex == stackIndex)
            {
                return itemPoint;
            }

            currentStackIndex++;
        }

        return null;
    }

    private void ClearCargoVisuals()
    {
        for (int i = itemVisuals.Count - 1; i >= 0; i--)
        {
            PlayerItemStorageUtility.DestroyPortableObject(itemVisuals[i]);
        }

        itemVisuals.Clear();
    }

    private void SaveCargoState()
    {
        TerrainGenerator.ResolveActive()?.SaveRuntimeInstallationState(this);
    }

    public override bool CanPlayerDock(Player targetPlayer)
    {
        return base.CanPlayerDock(targetPlayer)
               && !HasDraftAnimal
               && IsConnectedGroupHandleOwner()
               && IsPlayerAtHandleEnd(targetPlayer);
    }

    protected override bool PreparePlayerForDock(Player targetPlayer)
    {
        return TryClearPlayerHandsForDriving(targetPlayer);
    }

    private bool IsConnectedGroupHandleOwner()
    {
        if (handleObject == null
            || hideHandleForConnectionPreview
            || !gameObject.activeInHierarchy)
        {
            return false;
        }

        CollectConnectedGroup();
        bool isHandleOwner = ResolveConnectedGroupHandleOwner() == this;
        connectedGroupScratch.Clear();
        return isHandleOwner;
    }

    private bool IsPlayerAtHandleEnd(Player targetPlayer)
    {
        Transform playerTransform = targetPlayer != null && targetPlayer.BodyTransform != null
            ? targetPlayer.BodyTransform
            : targetPlayer != null
                ? targetPlayer.transform
                : null;
        if (playerTransform == null)
        {
            return false;
        }

        Vector2 forward = ResolvePlanarForward(transform);
        Vector3 playerDelta = playerTransform.position - transform.position;
        float playerAlong = Vector2.Dot(new Vector2(playerDelta.x, playerDelta.z), forward);
        float minimumAlong = ConnectionSideEpsilon;
        if (TryGetPlayerPoint(0, out Transform drivingPoint))
        {
            Vector3 pointDelta = drivingPoint.position - transform.position;
            float pointAlong = Vector2.Dot(new Vector2(pointDelta.x, pointDelta.z), forward);
            minimumAlong = Mathf.Max(
                minimumAlong,
                pointAlong * HandleApproachMinimumFraction);
        }

        return playerAlong >= minimumAlong;
    }

    private static bool TryClearPlayerHandsForDriving(Player targetPlayer)
    {
        if (targetPlayer == null)
        {
            return false;
        }

        if (targetPlayer.GetHandItemCount() <= 0)
        {
            return true;
        }

        return targetPlayer.TryStoreHandItemsInBag()
               && targetPlayer.GetHandItemCount() <= 0;
    }

    public override void HandleMountedInput(
        Vector3 worldMoveDirection,
        float moveSpeed,
        float deltaTime,
        Player mountedPlayer)
    {
        if (!IsConnectedGroupHandleOwner()
            || (HasDraftAnimal && !draftAnimalDriveActive)
            || (mountedPlayer != null && !TryClearPlayerHandsForDriving(mountedPlayer)))
        {
            ResetVehicleMotion();
            return;
        }

        float normalizedDeltaTime = Mathf.Max(0f, deltaTime);
        if (normalizedDeltaTime <= 0f)
        {
            return;
        }

        CollectConnectedGroup();
        Vector3 planarInput = worldMoveDirection;
        planarInput.y = 0f;
        float inputMagnitude = Mathf.Clamp01(planarInput.magnitude);
        bool hasInput = inputMagnitude > inputDeadZone;
        Vector3 inputDirection = hasInput
            ? planarInput / Mathf.Max(MinimumMovementDistance, planarInput.magnitude)
            : Vector3.zero;

        Vector3 currentPosition = transform.position;
        Quaternion startRotation = transform.rotation;
        Quaternion currentRotation = startRotation;
        float driveInput = 0f;
        if (hasInput)
        {
            Vector3 currentForward = currentRotation * Vector3.forward;
            currentForward.y = 0f;
            if (currentForward.sqrMagnitude <= MinimumMovementDistance)
            {
                currentForward = Vector3.forward;
            }
            else
            {
                currentForward.Normalize();
            }

            bool driveInReverse = !draftAnimalDriveActive
                                  && Vector3.Dot(currentForward, inputDirection)
                                  <= reverseInputDotThreshold;
            Vector3 targetFacing = driveInReverse ? -inputDirection : inputDirection;
            Quaternion targetRotation = Quaternion.LookRotation(targetFacing, Vector3.up);
            Quaternion steeredRotation = Quaternion.RotateTowards(
                currentRotation,
                targetRotation,
                steeringDegreesPerSecond * inputMagnitude * normalizedDeltaTime);
            if (!IsConnectedGroupPoseBlocked(
                    currentPosition,
                    startRotation,
                    currentPosition,
                    steeredRotation,
                    mountedPlayer != null ? mountedPlayer : draftAnimalRider,
                    draftAnimalDriveActive ? DraftAnimal : null))
            {
                currentRotation = steeredRotation;
            }

            driveInput = (driveInReverse ? -1f : 1f) * inputMagnitude;
        }

        float resolvedMaxSpeed = ResolvePlayerDrivenMaxSpeed(moveSpeed);
        float signedSpeed;
        if (resolvedMaxSpeed <= MinimumMovementDistance)
        {
            ResetVehicleMotion();
            signedSpeed = 0f;
        }
        else
        {
            signedSpeed = UpdateVehicleSignedSpeed(
                driveInput,
                normalizedDeltaTime,
                resolvedMaxSpeed);
        }
        float requestedDistance = signedSpeed * normalizedDeltaTime;
        float maxFrameDistance = movementSubstepDistance * maxMovementSubsteps;
        requestedDistance = Mathf.Clamp(requestedDistance, -maxFrameDistance, maxFrameDistance);

        float actualDistance = MoveWithCollision(
            currentPosition,
            startRotation,
            currentRotation,
            requestedDistance,
            mountedPlayer != null ? mountedPlayer : draftAnimalRider,
            draftAnimalDriveActive ? DraftAnimal : null,
            out Vector3 resolvedPosition,
            out bool movementBlocked);

        bool poseChanged = (resolvedPosition - transform.position).sqrMagnitude
                           > MinimumMovementDistance * MinimumMovementDistance
                           || Mathf.Abs(Quaternion.Dot(currentRotation, transform.rotation)) < 0.999999f;
        if (poseChanged)
        {
            ApplyConnectedGroupPose(
                currentPosition,
                startRotation,
                resolvedPosition,
                currentRotation,
                actualDistance);
            Physics.SyncTransforms();
        }

        if (movementBlocked)
        {
            ResetVehicleMotion();
        }

        if (poseChanged)
        {
            RefreshConnectedGroupRuntimePlacements();
        }
    }

    private float MoveWithCollision(
        Vector3 startPosition,
        Quaternion startRotation,
        Quaternion rotation,
        float requestedDistance,
        Player mountedPlayer,
        Animal ignoredDraftAnimal,
        out Vector3 resolvedPosition,
        out bool movementBlocked)
    {
        resolvedPosition = startPosition;
        movementBlocked = false;
        if (Mathf.Abs(requestedDistance) <= MinimumMovementDistance)
        {
            return 0f;
        }

        Vector3 moveDirection = rotation * Vector3.forward;
        moveDirection.y = 0f;
        if (moveDirection.sqrMagnitude <= MinimumMovementDistance)
        {
            return 0f;
        }

        moveDirection.Normalize();
        int substepCount = Mathf.Clamp(
            Mathf.CeilToInt(Mathf.Abs(requestedDistance) / movementSubstepDistance),
            1,
            maxMovementSubsteps);
        float substepDistance = requestedDistance / substepCount;
        for (int i = 0; i < substepCount; i++)
        {
            Vector3 candidatePosition = resolvedPosition + moveDirection * substepDistance;
            candidatePosition.y = startPosition.y;
            if (IsConnectedGroupPoseBlocked(
                    startPosition,
                    startRotation,
                    candidatePosition,
                    rotation,
                    mountedPlayer,
                    ignoredDraftAnimal))
            {
                movementBlocked = true;
                break;
            }

            resolvedPosition = candidatePosition;
        }

        Vector3 moved = resolvedPosition - startPosition;
        return Vector3.Dot(moved, moveDirection);
    }

    private bool IsDrivePoseBlocked(
        Vector3 worldPosition,
        Quaternion worldRotation,
        Player mountedPlayer,
        IReadOnlyList<Handcart> ignoredHandcarts,
        Animal ignoredDraftAnimal = null)
    {
        ResolveDrivingCollisionBox(
            worldPosition,
            worldRotation,
            out Vector3 boxCenter,
            out Vector3 halfExtents);
        return IsBlockedByObstacle(
                   boxCenter,
                   halfExtents,
                   worldRotation,
                   mountedPlayer,
                   ignoredHandcarts,
                   ignoredDraftAnimal)
               || IsBlockedByWater(boxCenter, halfExtents, worldRotation);
    }

    private bool IsBlockedByObstacle(
        Vector3 boxCenter,
        Vector3 halfExtents,
        Quaternion worldRotation,
        Player mountedPlayer,
        IReadOnlyList<Handcart> ignoredHandcarts,
        Animal ignoredDraftAnimal)
    {
        if (obstacleLayers.value == 0)
        {
            return false;
        }

        int overlapCount = Physics.OverlapBoxNonAlloc(
            boxCenter,
            halfExtents,
            obstacleBuffer,
            worldRotation,
            obstacleLayers,
            QueryTriggerInteraction.Ignore);
        if (overlapCount >= obstacleBuffer.Length)
        {
            return true;
        }

        Transform playerRoot = mountedPlayer != null ? mountedPlayer.transform : null;
        Transform playerBody = mountedPlayer != null ? mountedPlayer.BodyTransform : null;
        Transform draftAnimalRoot = ignoredDraftAnimal != null
            ? ignoredDraftAnimal.MovementRoot
            : null;
        for (int i = 0; i < overlapCount; i++)
        {
            Collider obstacle = obstacleBuffer[i];
            obstacleBuffer[i] = null;
            if (obstacle == null
                || obstacle == drivingCollider
                || obstacle.transform.IsChildOf(transform)
                || IsSameOrChildOf(obstacle.transform, playerRoot)
                || IsSameOrChildOf(obstacle.transform, playerBody)
                || IsSameOrChildOf(obstacle.transform, draftAnimalRoot)
                || IsPartOfHandcartGroup(obstacle.transform, ignoredHandcarts))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool IsConnectedGroupPoseBlocked(
        Vector3 leaderStartPosition,
        Quaternion leaderStartRotation,
        Vector3 leaderTargetPosition,
        Quaternion leaderTargetRotation,
        Player mountedPlayer,
        Animal ignoredDraftAnimal = null)
    {
        for (int i = 0; i < connectedGroupScratch.Count; i++)
        {
            Handcart handcart = connectedGroupScratch[i];
            if (handcart == null)
            {
                continue;
            }

            ResolveConnectedPose(
                handcart,
                leaderStartPosition,
                leaderStartRotation,
                leaderTargetPosition,
                leaderTargetRotation,
                out Vector3 targetPosition,
                out Quaternion targetRotation);
            if (handcart.IsDrivePoseBlocked(
                    targetPosition,
                    targetRotation,
                    mountedPlayer,
                    connectedGroupScratch,
                    ignoredDraftAnimal))
            {
                return true;
            }
        }

        return ignoredDraftAnimal != null
               && IsDraftAnimalPoseBlocked(
                   leaderTargetPosition,
                   leaderTargetRotation,
                   mountedPlayer,
                   ignoredDraftAnimal);
    }

    private bool IsDraftAnimalPoseBlocked(
        Vector3 cartPosition,
        Quaternion cartRotation,
        Player mountedPlayer,
        Animal attachedAnimal)
    {
        if (attachedAnimal == null || !TryGetPlayerPoint(0, out Transform handlePoint))
        {
            return false;
        }

        Vector3 handleLocalPosition = transform.InverseTransformPoint(handlePoint.position);
        Vector3 targetHandlePosition = cartPosition + cartRotation * handleLocalPosition;
        Vector3 forward = cartRotation * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= MinimumMovementDistance)
        {
            return false;
        }

        forward.Normalize();
        float animalRadius = attachedAnimal.GetWorldRadius();
        Vector3 targetCenter = targetHandlePosition
                               + forward * ResolveDraftAnimalCenterDistance(attachedAnimal);
        targetCenter.y = attachedAnimal.GetWorldCenter().y;
        int overlapCount = Physics.OverlapSphereNonAlloc(
            targetCenter,
            animalRadius,
            obstacleBuffer,
            obstacleLayers,
            QueryTriggerInteraction.Ignore);
        if (overlapCount >= obstacleBuffer.Length)
        {
            return true;
        }

        Transform playerRoot = mountedPlayer != null ? mountedPlayer.transform : null;
        Transform playerBody = mountedPlayer != null ? mountedPlayer.BodyTransform : null;
        Transform animalRoot = attachedAnimal.MovementRoot;
        for (int i = 0; i < overlapCount; i++)
        {
            Collider obstacle = obstacleBuffer[i];
            obstacleBuffer[i] = null;
            if (obstacle == null
                || IsSameOrChildOf(obstacle.transform, animalRoot)
                || IsSameOrChildOf(obstacle.transform, playerRoot)
                || IsSameOrChildOf(obstacle.transform, playerBody)
                || IsPartOfHandcartGroup(obstacle.transform, connectedGroupScratch))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void ApplyConnectedGroupPose(
        Vector3 leaderStartPosition,
        Quaternion leaderStartRotation,
        Vector3 leaderTargetPosition,
        Quaternion leaderTargetRotation,
        float leaderSignedDistance)
    {
        Vector3 leaderForward = leaderTargetRotation * Vector3.forward;
        leaderForward.y = 0f;
        for (int i = 0; i < connectedGroupScratch.Count; i++)
        {
            Handcart handcart = connectedGroupScratch[i];
            if (handcart == null)
            {
                continue;
            }

            ResolveConnectedPose(
                handcart,
                leaderStartPosition,
                leaderStartRotation,
                leaderTargetPosition,
                leaderTargetRotation,
                out Vector3 targetPosition,
                out Quaternion targetRotation);
            handcart.transform.SetPositionAndRotation(targetPosition, targetRotation);

            Vector3 handcartForward = targetRotation * Vector3.forward;
            handcartForward.y = 0f;
            float signedDistance = Vector3.Dot(leaderForward, handcartForward) < 0f
                ? -leaderSignedDistance
                : leaderSignedDistance;
            handcart.RotateWheelsByDistance(signedDistance);
        }
    }

    private static void ResolveConnectedPose(
        Handcart handcart,
        Vector3 leaderStartPosition,
        Quaternion leaderStartRotation,
        Vector3 leaderTargetPosition,
        Quaternion leaderTargetRotation,
        out Vector3 targetPosition,
        out Quaternion targetRotation)
    {
        Quaternion rotationDelta = leaderTargetRotation * Quaternion.Inverse(leaderStartRotation);
        Vector3 startOffset = handcart.transform.position - leaderStartPosition;
        targetPosition = leaderTargetPosition + rotationDelta * startOffset;
        targetRotation = rotationDelta * handcart.transform.rotation;
    }

    private void RefreshConnectedGroupRuntimePlacements()
    {
        for (int i = 0; i < connectedGroupScratch.Count; i++)
        {
            Handcart handcart = connectedGroupScratch[i];
            if (handcart != null)
            {
                handcart.RefreshRuntimePlacement(handcart.transform.position, handcart.transform.rotation);
            }
        }
    }

    private void CollectConnectedGroup()
    {
        CollectConnectedGroup(this, connectedGroupScratch);
    }

    private void CollectConnectedGroup(Handcart root, List<Handcart> results)
    {
        results.Clear();
        connectedGroupVisited.Clear();
        if (root == null || !root.gameObject.activeInHierarchy)
        {
            return;
        }

        results.Add(root);
        connectedGroupVisited.Add(root);
        for (int i = 0; i < results.Count; i++)
        {
            Handcart current = results[i];
            if (current == null)
            {
                continue;
            }

            foreach (Handcart connected in current.connectedHandcarts)
            {
                if (connected == null
                    || !connected.gameObject.activeInHierarchy
                    || !connectedGroupVisited.Add(connected))
                {
                    continue;
                }

                results.Add(connected);
            }
        }
    }

    private float ResolveConnectedLoadSpeedMultiplier()
    {
        CollectConnectedGroup();
        float draftAnimalStrength = AnimalDefinition.DefaultStrength;
        for (int i = 0; i < connectedGroupScratch.Count; i++)
        {
            Animal connectedDraftAnimal = connectedGroupScratch[i]?.DraftAnimal;
            if (connectedDraftAnimal?.Definition != null)
            {
                draftAnimalStrength = connectedDraftAnimal.Definition.Strength;
                break;
            }
        }

        float multiplier = 1f;
        for (int i = 0; i < connectedGroupScratch.Count; i++)
        {
            Handcart handcart = connectedGroupScratch[i];
            if (handcart != null)
            {
                multiplier *= ResolveStrengthAdjustedLoadSpeedMultiplier(
                    handcart.VehicleLoadSpeedMultiplier,
                    draftAnimalStrength);
            }
        }

        return Mathf.Clamp01(multiplier);
    }

    private bool IsBlockedByWater(
        Vector3 boxCenter,
        Vector3 halfExtents,
        Quaternion worldRotation)
    {
        if (!blockWater)
        {
            return false;
        }

        TerrainGenerator terrain = ResolveTerrain();
        if (terrain == null)
        {
            return false;
        }

        Vector3 right = worldRotation * Vector3.right * halfExtents.x;
        Vector3 forward = worldRotation * Vector3.forward * halfExtents.z;
        return IsWaterAt(terrain, boxCenter)
               || IsWaterAt(terrain, boxCenter + right + forward)
               || IsWaterAt(terrain, boxCenter + right - forward)
               || IsWaterAt(terrain, boxCenter - right + forward)
               || IsWaterAt(terrain, boxCenter - right - forward);
    }

    private bool IsWaterAt(TerrainGenerator terrain, Vector3 worldPosition)
    {
        return terrain.IsWaterSurfaceAtWorldPosition(
            new Vector2(worldPosition.x, worldPosition.z),
            terrainBiomeWeightBuffer);
    }

    private void ResolveDrivingCollisionBox(
        Vector3 worldPosition,
        Quaternion worldRotation,
        out Vector3 boxCenter,
        out Vector3 halfExtents)
    {
        CacheDrivingCollider();
        if (drivingCollider == null)
        {
            boxCenter = worldPosition + worldRotation * new Vector3(0f, 0.5f, -0.25f);
            halfExtents = new Vector3(0.5f, 0.48f, 0.35f);
            return;
        }

        Vector3 scale = transform.lossyScale;
        scale.Set(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        Vector3 scaledCenter = Vector3.Scale(drivingCollider.center, scale);
        boxCenter = worldPosition + worldRotation * scaledCenter;
        halfExtents = Vector3.Scale(drivingCollider.size * 0.5f, scale);
        halfExtents.x = Mathf.Max(0.01f, halfExtents.x - collisionSkinWidth);
        halfExtents.y = Mathf.Max(0.01f, halfExtents.y - collisionSkinWidth);
        halfExtents.z = Mathf.Max(0.01f, halfExtents.z - collisionSkinWidth);
    }

    private void RefreshRuntimePlacement(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (!RefreshSingleCellRuntimePlacement(
                worldPosition,
                ResolveQuarterTurns(worldRotation)))
        {
            return;
        }

        ResolveTerrain()?.SaveRuntimeInstallationState(this);
    }

    private TerrainGenerator ResolveTerrain()
    {
        if (cachedTerrain == null)
        {
            cachedTerrain = TerrainGenerator.ResolveActive();
        }

        return cachedTerrain;
    }

    private void CacheDrivingCollider()
    {
        if (drivingCollider == null || drivingCollider.transform != transform)
        {
            drivingCollider = GetComponent<BoxCollider>();
        }
    }

    private static int ResolveQuarterTurns(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        int quarterTurns = Mathf.RoundToInt(yaw / 90f);
        return ((quarterTurns % 4) + 4) % 4;
    }

    private static bool IsSameOrChildOf(Transform candidate, Transform root)
    {
        return candidate != null
               && root != null
               && (candidate == root || candidate.IsChildOf(root));
    }

    private static bool IsPartOfHandcartGroup(
        Transform candidate,
        IReadOnlyList<Handcart> handcarts)
    {
        if (candidate == null || handcarts == null)
        {
            return false;
        }

        for (int i = 0; i < handcarts.Count; i++)
        {
            Handcart handcart = handcarts[i];
            if (handcart != null && IsSameOrChildOf(candidate, handcart.transform))
            {
                return true;
            }
        }

        return false;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        steeringDegreesPerSecond = Mathf.Max(1f, steeringDegreesPerSecond);
        inputDeadZone = Mathf.Clamp(inputDeadZone, 0f, 0.5f);
        reverseInputDotThreshold = Mathf.Clamp(reverseInputDotThreshold, -1f, 0f);
        movementSubstepDistance = Mathf.Max(0.01f, movementSubstepDistance);
        maxMovementSubsteps = Mathf.Clamp(maxMovementSubsteps, 1, 64);
        connectionCenterDistance = Mathf.Max(MinimumConnectionDistance, connectionCenterDistance);
        connectionSnapMaxDistance = Mathf.Max(MinimumConnectionDistance, connectionSnapMaxDistance);
        collisionSkinWidth = Mathf.Max(0f, collisionSkinWidth);
        itemStackVerticalSpacing = Mathf.Max(0.001f, itemStackVerticalSpacing);
        CacheDrivingCollider();
    }
#endif
}
