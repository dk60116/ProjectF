using DG.Tweening;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class PortableStack
{
    public List<PortableObject> stack;
}

/// <summary>
/// Managed portable-item entity. Authoritative state lives in PortableObjectWorld;
/// an optional PortableObjectView exists only for individual Unity presentation.
/// </summary>
public sealed class PortableObject : IDisposable
{
    public const float MoveToDuration = 0.3f;

    private static readonly HashSet<PortableObject> liveObjects = new HashSet<PortableObject>();
    private static readonly HashSet<int> missingPortableAssetWarnings = new HashSet<int>();
    private readonly PortableObjectWorld world;
    private PortableObjectHandle handle;
    private PortableObjectView view;
    private Transform presentationParent;
    private bool creatingGeneratedView;
    private bool ownsGeneratedView;
    private bool presentationPinned;
    private PortableItemRenderer portableItemRenderer;
    private DroppedItemPickupGate pickupGate;
    private AnimalTemporaryDropping temporaryDropping;
    private Tween moveTween;
    private string objectName;
    private ItemDefinition cachedItemDefinition;
    private Mesh cachedMesh;
    private Material cachedMaterial;
    private ShadowCastingMode cachedShadowCastingMode = ShadowCastingMode.On;
    private bool cachedReceiveShadows = true;
    private MaterialPropertyBlock debugVisualPropertyBlock;
    private bool hoverOutlineVisible;
    private bool focusedOutlineVisible;
    private bool pickupOutlineVisible;
    private bool restoreBatchedRenderingAfterOutline;
    private bool bodyRendererTemporarilyHidden;
    private List<PortableObject> focusStack;
    private PortableObject focusOutlineOwner;

    public event Action<PortableObject> MoveCancelled;

    public PortableObject()
        : this(Vector3.zero, Quaternion.identity, Vector3.one, 0, "PortableObject")
    {
    }

    private PortableObject(Vector3 position, Quaternion rotation, Vector3 scale, int layer, string name)
    {
        world = PortableObjectWorld.Ensure();
        handle = world.Create(position, rotation, scale, layer);
        objectName = string.IsNullOrEmpty(name) ? "PortableObject" : name;
        liveObjects.Add(this);
    }

    public static PortableObject Create(
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        int layer,
        string name = null) => new PortableObject(position, rotation, scale, layer, name);

    public static PortableObject Create(PortableObjectTemplate template, bool useAuthoringObjectAsView = false) =>
        template != null ? template.CreateEntity(useAuthoringObjectAsView) : new PortableObject();

    public PortableObject Clone(Vector3 position, Quaternion rotation)
    {
        PortableObject clone = new PortableObject(position, rotation, WorldScale, Layer, objectName);
        if (ItemId >= 0) clone.SetItem(ItemId);
        return clone;
    }

    public PortableObjectHandle Handle => handle;
    public bool IsAlive => world != null && world.IsAlive(handle);
    public int ItemId => Read().ItemId;
    public bool IsMovingToTarget => Read().Moving;
    public bool IsOnConveyor => Read().OnConveyor;
    public bool IsUsingBatchedRendering => Read().BatchedRendering;
    public bool IsVisualRenderingSuppressed => Read().SuppressRendering;
    public bool IsActive => Read().Active;
    public int Layer
    {
        get => Read().Layer;
        set
        {
            Mutate((ref PortableObjectComponent c) => c.Layer = value);
            if (view != null) view.gameObject.layer = value;
            MarkPortableItemRenderDataDirty();
        }
    }
    public Block PickupSourceBlock { get; private set; }
    public Vector3 WorldPosition { get { SyncStateFromView(view); return Read().WorldPosition; } }
    public Quaternion WorldRotation { get { SyncStateFromView(view); return Read().WorldRotation; } }
    public Vector3 WorldScale { get { SyncStateFromView(view); return Read().WorldScale; } }
    public Transform PresentationParent => presentationParent;
    public bool WasMovedByConveyorThisFrame => Read().LastConveyorMoveFrame == Time.frameCount;
    public bool HasPresentationView => view != null;
    public Transform PresentationTransform => EnsurePinnedView()?.transform;
    public GameObject PresentationGameObject => EnsurePinnedView()?.gameObject;
    public string name
    {
        get => objectName;
        set
        {
            objectName = string.IsNullOrEmpty(value) ? "PortableObject" : value;
            if (view != null) view.gameObject.name = objectName;
        }
    }

    public int FocusStackCount
    {
        get
        {
            int count = 0;
            if (focusStack != null)
            {
                for (int i = 0; i < focusStack.Count; i++)
                {
                    PortableObject member = focusStack[i];
                    if (member != null && member.ItemId == ItemId && member.IsActive
                        && !member.IsMovingToTarget && !member.IsVisualRenderingSuppressed) count++;
                }
            }
            return count > 0 ? count : (ItemId >= 0 ? 1 : 0);
        }
    }

    public DroppedItemPickupGate PickupGate => pickupGate;
    public static int LiveEntityCount => PortableObjectWorld.Current?.Count ?? 0;

    public static void RefreshAllSleepAwakeVisuals()
    {
        foreach (PortableObject entity in liveObjects) entity?.RefreshSleepAwakeVisual(true);
    }

    public static void RefreshAllBeltItemLineDebugVisuals()
    {
        foreach (PortableObject entity in liveObjects) entity?.RefreshSleepAwakeVisual(true);
    }

    public static bool operator ==(PortableObject left, PortableObject right)
    {
        bool leftInvalid = ReferenceEquals(left, null) || !left.IsAlive;
        bool rightInvalid = ReferenceEquals(right, null) || !right.IsAlive;
        return leftInvalid || rightInvalid ? leftInvalid == rightInvalid : ReferenceEquals(left, right);
    }

    public static bool operator !=(PortableObject left, PortableObject right) => !(left == right);
    public override bool Equals(object value) => ReferenceEquals(this, value);
    public override int GetHashCode() => handle.GetHashCode();

    public T GetComponent<T>() where T : class
    {
        if (typeof(T) == typeof(DroppedItemPickupGate)) return pickupGate as T;
        if (typeof(T) == typeof(AnimalTemporaryDropping)) return temporaryDropping as T;
        return view != null ? view.GetComponent(typeof(T)) as T : null;
    }

    public T GetComponentInParent<T>() where T : class =>
        view != null ? view.GetComponentInParent(typeof(T), true) as T : null;

    public bool TryGetComponent<T>(out T component) where T : class
    {
        component = GetComponent<T>();
        return component != null;
    }

    public DroppedItemPickupGate GetOrAddPickupGate() => pickupGate ??= new DroppedItemPickupGate(this);
    public AnimalTemporaryDropping GetOrAddTemporaryDropping() =>
        temporaryDropping ??= new AnimalTemporaryDropping(this);
    public void SetPickupSourceBlock(Block sourceBlock) => PickupSourceBlock = sourceBlock;

    public void SetConveyorOwnership(bool ownedByConveyor)
    {
        Mutate((ref PortableObjectComponent c) => c.OnConveyor = ownedByConveyor);
        if (ownedByConveyor) ClearFocusOutlines(true);
    }

    public void SetSleepAwakeSleeping(bool sleeping)
    {
        Mutate((ref PortableObjectComponent c) => c.Sleeping = sleeping);
        RefreshSleepAwakeVisual(true);
    }

    public void RefreshSleepAwakeVisual(bool force = false)
    {
        portableItemRenderer?.MarkDirty();
        MeshRenderer renderer = view != null ? view.BodyRenderer : null;
        if (renderer == null) return;
        PortableObjectComponent component = Read();
        bool dark = component.Sleeping && GameManager.Instance != null && GameManager.Instance.ShowSleepAwake;
        bool belt = component.BeltDebugActive && GameManager.Instance != null && GameManager.Instance.ShowBeltItemLine;
        if (!dark && !belt)
        {
            renderer.SetPropertyBlock(null);
            return;
        }
        debugVisualPropertyBlock ??= new MaterialPropertyBlock();
        debugVisualPropertyBlock.Clear();
        if (belt)
        {
            Color color = component.BeltDebugColor;
            BeltItemLineDebugVisual.ApplySolidColor(
                debugVisualPropertyBlock,
                dark ? SleepAwakeDebugVisual.Darken(color) : color);
        }
        else SleepAwakeDebugVisual.ApplySleepingColor(debugVisualPropertyBlock, renderer.sharedMaterial);
        renderer.SetPropertyBlock(debugVisualPropertyBlock);
    }

    public void SetBeltItemLineDebugColor(bool active, Color32 color)
    {
        Mutate((ref PortableObjectComponent c) =>
        {
            c.BeltDebugActive = active;
            c.BeltDebugColor = active ? color : (Color32)Color.white;
        });
        RefreshSleepAwakeVisual(true);
    }

    public void ClearBeltItemLineDebugColor() => SetBeltItemLineDebugColor(false, Color.white);
    public void MarkMovedByConveyorThisFrame() =>
        Mutate((ref PortableObjectComponent c) => c.LastConveyorMoveFrame = Time.frameCount);

    public void SetCachedParent(Transform parent, bool worldPositionStays)
    {
        SyncStateFromView(view);
        presentationParent = parent;
        if (view != null)
        {
            view.transform.SetParent(parent, worldPositionStays);
            SyncStateFromView(view);
        }
        else if (!worldPositionStays && parent != null) SetWorldPose(parent.position, parent.rotation);
        MarkPortableItemRenderDataDirty();
    }

    public void SetCachedActive(bool active)
    {
        if (!active)
        {
            pickupGate?.OnOwnerDisabled();
            temporaryDropping?.OnOwnerDisabled();
        }
        Mutate((ref PortableObjectComponent c) => c.Active = active);
        if (view != null && view.gameObject.activeSelf != active) view.gameObject.SetActive(active);
        UpdateRendererVisibility();
        MarkPortableItemRenderDataDirty();
    }

    public void SetWorldPosition(Vector3 position)
    {
        Mutate((ref PortableObjectComponent c) => c.WorldPosition = position);
        if (view != null) view.transform.position = position;
        MarkPortableItemRenderDataDirty();
    }

    public void SetWorldPose(Vector3 position, Quaternion rotation)
    {
        Mutate((ref PortableObjectComponent c) => { c.WorldPosition = position; c.WorldRotation = rotation; });
        if (view != null) view.transform.SetPositionAndRotation(position, rotation);
        MarkPortableItemRenderDataDirty();
    }

    public void SetWorldScale(Vector3 scale)
    {
        Mutate((ref PortableObjectComponent c) => c.WorldScale = scale);
        if (view != null) view.transform.localScale = ResolveLocalScale(scale, presentationParent);
        MarkPortableItemRenderDataDirty();
    }

    public void SetLocalPose(Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
    {
        presentationParent = parent;
        Vector3 worldPosition = parent != null ? parent.TransformPoint(localPosition) : localPosition;
        Quaternion worldRotation = parent != null ? parent.rotation * localRotation : localRotation;
        Vector3 worldScale = parent != null ? Vector3.Scale(parent.lossyScale, localScale) : localScale;
        Mutate((ref PortableObjectComponent c) =>
        {
            c.WorldPosition = worldPosition;
            c.WorldRotation = worldRotation;
            c.WorldScale = worldScale;
        });
        if (view != null)
        {
            Transform target = view.transform;
            target.SetParent(parent, false);
            target.localPosition = localPosition;
            target.localRotation = localRotation;
            target.localScale = localScale;
        }
        MarkPortableItemRenderDataDirty();
    }

    public void MarkBatchedRenderDataDirty() => MarkPortableItemRenderDataDirty();
    public void RequestBatchedRenderDataRefresh()
    {
        if (IsUsingBatchedRendering) portableItemRenderer?.RequestPortableObjectRenderDataRefresh();
    }

    public bool SetItem(int itemId)
    {
        if (!IsAlive || InputOutputModule.IsFluidItemId(itemId))
        {
            ClearItemState();
            return false;
        }
        ItemManager manager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        if (manager == null || !manager.TryGetItemSetById(itemId, out ItemManager.ItemSet itemSet)) return false;
        Mesh mesh = itemSet.portableMesh;
        Material material = itemSet.portableMat;
        if (mesh == null || material == null)
        {
            ClearItemState();
            if (missingPortableAssetWarnings.Add(itemId))
                Debug.LogWarning($"PortableObject entity: item '{itemSet.name}' (id {itemId}) has no portable {(mesh == null ? "mesh" : "material")}.");
            return false;
        }
        if (!material.enableInstancing) material.enableInstancing = true;
        manager.TryGetItemDefinitionById(itemId, out cachedItemDefinition);
        cachedMesh = mesh;
        cachedMaterial = material;
        Mutate((ref PortableObjectComponent c) => { c.ItemId = itemId; c.SuppressRendering = false; });
        if (view != null) ConfigureViewAssets();
        else if (RequiresIndividualPresentation(cachedItemDefinition))
        {
            presentationPinned = true;
            EnsureView();
        }
        MarkPortableItemRenderDataDirty();
        return true;
    }

    public void SetItemLightToggled(bool active)
    {
        if (view != null) view.GetComponent<ItemLightController>()?.SetToggled(active);
    }

    public void SetBatchedRendering(bool batched)
    {
        if (!batched) DetachFromFocusOutlineForExternalRenderingChange();
        if (batched && HasActiveOutlineRequest)
        {
            restoreBatchedRenderingAfterOutline = true;
            batched = false;
        }
        Mutate((ref PortableObjectComponent c) => { c.BatchedRendering = batched; if (batched) c.SuppressRendering = false; });
        if (batched)
        {
            portableItemRenderer = ResolvePortableItemRenderer();
            portableItemRenderer?.Register(this);
        }
        else
        {
            UnregisterFromPortableItemRenderer();
        }
        MarkPortableItemRenderDataDirty();
        UpdateRendererVisibility();
        if (batched) ReleaseGeneratedPresentationIfPossible();
    }

    public void SetVisualRenderingSuppressed(bool suppressed)
    {
        Mutate((ref PortableObjectComponent c) => c.SuppressRendering = suppressed);
        if (suppressed) ClearFocusOutlines(false);
        MarkPortableItemRenderDataDirty();
        UpdateRendererVisibility();
    }

    public bool TryGetBatchRenderData(
        out int itemId, out Mesh mesh, out Material material, out Matrix4x4 localToWorldMatrix,
        out Vector3 worldPosition, out int layer, out ShadowCastingMode shadowCastingMode,
        out bool receiveShadows, out bool useSleepAwakeDarkTint,
        out bool useBeltItemLineDebugColor, out Color32 beltItemLineDebugColor)
    {
        PortableObjectComponent c = Read();
        itemId = c.ItemId;
        mesh = cachedMesh;
        material = cachedMaterial;
        worldPosition = c.WorldPosition;
        layer = c.Layer;
        shadowCastingMode = cachedShadowCastingMode;
        receiveShadows = cachedReceiveShadows;
        useSleepAwakeDarkTint = c.Sleeping && GameManager.Instance != null && GameManager.Instance.ShowSleepAwake;
        useBeltItemLineDebugColor = c.BeltDebugActive && GameManager.Instance != null && GameManager.Instance.ShowBeltItemLine;
        beltItemLineDebugColor = useBeltItemLineDebugColor ? c.BeltDebugColor : (Color32)Color.white;
        localToWorldMatrix = Matrix4x4.TRS(c.WorldPosition, c.WorldRotation, c.WorldScale);
        return IsAlive && c.Active && c.BatchedRendering && !c.SuppressRendering
            && !bodyRendererTemporarilyHidden && itemId >= 0 && mesh != null && material != null;
    }

    public void MoveTo(Transform target, Action onComplete = null)
    {
        if (target == null) { onComplete?.Invoke(); return; }
        MoveTo(() => target != null ? target.position : WorldPosition, 0f, null, onComplete, true);
    }

    public void MoveTo(Transform target, float delay = 0f, Func<Vector3> startPositionProvider = null,
        Action onComplete = null, bool deactivateOnComplete = true, bool useJumpArc = true,
        float moveDuration = MoveToDuration, bool trackStartPositionDuringMove = true)
    {
        if (target == null) { onComplete?.Invoke(); return; }
        MoveTo(() => target != null ? target.position : WorldPosition, delay, startPositionProvider,
            onComplete, deactivateOnComplete, useJumpArc, moveDuration, trackStartPositionDuringMove);
    }

    public void MoveTo(Vector3 targetPosition, float delay = 0f, Action onComplete = null,
        bool deactivateOnComplete = true, bool useJumpArc = true, float moveDuration = MoveToDuration) =>
        MoveTo(() => targetPosition, delay, null, onComplete, deactivateOnComplete, useJumpArc, moveDuration);

    public void MoveTo(Func<Vector3> targetPositionProvider, float delay = 0f,
        Func<Vector3> startPositionProvider = null, Action onComplete = null,
        bool deactivateOnComplete = true, bool useJumpArc = true,
        float moveDuration = MoveToDuration, bool trackStartPositionDuringMove = true)
    {
        if (!IsAlive || targetPositionProvider == null) { onComplete?.Invoke(); return; }
        moveTween?.Kill();
        moveTween = null;
        SetSleepAwakeSleeping(false);
        ClearBeltItemLineDebugColor();
        SetBatchedRendering(true);
        Mutate((ref PortableObjectComponent c) => c.Moving = true);
        Sequence sequence = DOTween.Sequence().SetTarget(this);
        moveTween = sequence;
        if (delay > 0f)
        {
            bodyRendererTemporarilyHidden = true;
            sequence.Append(DOVirtual.DelayedCall(delay, () =>
            {
                if (startPositionProvider != null) SetWorldPosition(startPositionProvider());
                bodyRendererTemporarilyHidden = false;
            }));
        }
        Vector3 launchStart = startPositionProvider != null ? startPositionProvider() : WorldPosition;
        SetWorldPosition(launchStart);
        float safeDuration = Mathf.Max(0.001f, moveDuration);
        sequence.Append(DOVirtual.Float(0f, 1f, safeDuration, t =>
        {
            Vector3 start = trackStartPositionDuringMove && startPositionProvider != null
                ? startPositionProvider() : launchStart;
            Vector3 position = Vector3.Lerp(start, targetPositionProvider(), t);
            if (useJumpArc) position += Vector3.up * (4f * t * (1f - t));
            SetWorldPosition(position);
        }).SetEase(Ease.Linear));
        sequence.OnComplete(() =>
        {
            if (!IsAlive) return;
            moveTween = null;
            Mutate((ref PortableObjectComponent c) => c.Moving = false);
            SetWorldPosition(targetPositionProvider());
            bodyRendererTemporarilyHidden = false;
            if (deactivateOnComplete) SetCachedActive(false);
            onComplete?.Invoke();
        });
        sequence.OnKill(() =>
        {
            if (!IsAlive) return;
            moveTween = null;
            Mutate((ref PortableObjectComponent c) => c.Moving = false);
            bodyRendererTemporarilyHidden = false;
            MarkPortableItemRenderDataDirty();
        });
    }

    public void CancelMove()
    {
        moveTween?.Kill();
        moveTween = null;
        if (IsAlive) Mutate((ref PortableObjectComponent c) => c.Moving = false);
        bodyRendererTemporarilyHidden = false;
        MoveCancelled?.Invoke(this);
    }

    public bool TryGetWorldFocusBounds(out Bounds bounds)
    {
        bounds = default;
        if (!IsAlive || ItemId < 0 || IsMovingToTarget || IsOnConveyor
            || IsVisualRenderingSuppressed || !IsActive || IsFocusExcludedByContainer()) return false;
        if (cachedMesh == null) return false;
        PortableObjectComponent component = Read();
        bounds = TransformBounds(
            cachedMesh.bounds,
            Matrix4x4.TRS(component.WorldPosition, component.WorldRotation, component.WorldScale));
        bounds.Expand(new Vector3(0.24f, 0.16f, 0.24f));
        return true;
    }

    public void SetFocusStack(List<PortableObject> stack)
    {
        if (IsOnConveyor) { ClearFocusOutlines(true); return; }
        if (!ReferenceEquals(focusStack, stack))
        {
            ReleaseFocusStackOutlineMembers(true);
            focusStack = stack;
        }
        if (HasOwnOutlineRequest) AcquireFocusStackOutlineMembers();
    }

    public void SetHoverOutline(bool visible) => SetOutline(OutlineKind.Hover, visible);
    public void SetFocusedOutline(bool visible) => SetOutline(OutlineKind.Focused, visible);
    public void SetPickupOutline(bool visible) => SetOutline(OutlineKind.Pickup, visible);

    public int CopyOutlineMaskRenderers(Renderer[] destination)
    {
        if (IsOnConveyor || destination == null || destination.Length == 0) return 0;
        int count = 0;
        if (HasOwnOutlineRequest && focusStack != null)
        {
            for (int i = 0; i < focusStack.Count && count < destination.Length; i++)
            {
                PortableObject member = focusStack[i];
                if (member != null && member.focusOutlineOwner == this)
                    count = member.CopyOwnOutlineMaskRenderers(destination, count);
            }
        }
        return count > 0 ? count : CopyOwnOutlineMaskRenderers(destination, count);
    }

    public bool HasActiveOutline => HasActiveOutlineRequest;

    public void AttachView(PortableObjectView attachedView)
    {
        if (attachedView == null || !IsAlive) return;
        view = attachedView;
        ownsGeneratedView = creatingGeneratedView;
        if (!creatingGeneratedView) presentationPinned = true;
        ApplyStateToView(attachedView);
        ConfigureViewAssets();
    }

    public void DetachView(PortableObjectView detachedView)
    {
        if (view != detachedView) return;
        SyncStateFromView(detachedView);
        view = null;
    }

    public void ApplyStateToView(PortableObjectView target)
    {
        if (target == null || target != view || !IsAlive) return;
        PortableObjectComponent c = Read();
        Transform targetTransform = target.transform;
        targetTransform.SetParent(presentationParent, true);
        targetTransform.SetPositionAndRotation(c.WorldPosition, c.WorldRotation);
        targetTransform.localScale = ResolveLocalScale(c.WorldScale, presentationParent);
        target.gameObject.layer = c.Layer;
        target.gameObject.name = objectName;
        if (target.gameObject.activeSelf != c.Active) target.gameObject.SetActive(c.Active);
        UpdateRendererVisibility();
    }

    public void SyncStateFromView(PortableObjectView source)
    {
        if (source == null || source != view || !IsAlive) return;
        Transform sourceTransform = source.transform;
        PortableObjectComponent c = Read();
        Vector3 position = sourceTransform.position;
        Quaternion rotation = sourceTransform.rotation;
        Vector3 scale = sourceTransform.lossyScale;
        if (c.WorldPosition == position && c.WorldRotation == rotation && c.WorldScale == scale
            && c.Layer == source.gameObject.layer && c.Active == source.gameObject.activeSelf) return;
        c.WorldPosition = position;
        c.WorldRotation = rotation;
        c.WorldScale = scale;
        c.Layer = source.gameObject.layer;
        c.Active = source.gameObject.activeSelf;
        world.Set(handle, c);
        MarkPortableItemRenderDataDirty();
    }

    public void Dispose() => Dispose(true);

    internal void ReleaseForAuthoringObjectDestroy() => Dispose(false);

    private void Dispose(bool destroyPresentation)
    {
        if (!IsAlive) return;
        CancelMove();
        ClearFocusOutlines(false);
        UnregisterFromPortableItemRenderer();
        liveObjects.Remove(this);
        PickupSourceBlock = null;
        temporaryDropping?.ClearExpiration();
        temporaryDropping = null;
        pickupGate = null;
        PortableObjectView releasedView = view;
        view = null;
        world.Release(handle);
        handle = default;
        if (!destroyPresentation || releasedView == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(releasedView.gameObject);
        else UnityEngine.Object.DestroyImmediate(releasedView.gameObject);
    }

    private delegate void ComponentMutation(ref PortableObjectComponent component);
    private PortableObjectComponent Read() =>
        world != null && world.TryGet(handle, out PortableObjectComponent c) ? c : default;

    private void Mutate(ComponentMutation mutation)
    {
        if (mutation == null || world == null || !world.TryGet(handle, out PortableObjectComponent c)) return;
        mutation(ref c);
        world.Set(handle, c);
    }

    private void ClearItemState()
    {
        cachedItemDefinition = null;
        cachedMesh = null;
        cachedMaterial = null;
        Mutate((ref PortableObjectComponent c) => { c.ItemId = -1; c.SuppressRendering = true; });
        ConfigureViewAssets();
    }

    private PortableObjectView EnsureView()
    {
        if (view != null) return view;
        if (!IsAlive) return null;
        PortableObjectComponent c = Read();
        GameObject go = new GameObject(objectName);
        go.layer = c.Layer;
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>();
        PortableObjectView created = go.AddComponent<PortableObjectView>();
        go.transform.SetParent(presentationParent, true);
        go.transform.SetPositionAndRotation(c.WorldPosition, c.WorldRotation);
        go.transform.localScale = ResolveLocalScale(c.WorldScale, presentationParent);
        creatingGeneratedView = true;
        created.Bind(this);
        creatingGeneratedView = false;
        ownsGeneratedView = true;
        if (!c.Active) go.SetActive(false);
        return created;
    }

    private PortableObjectView EnsurePinnedView()
    {
        presentationPinned = true;
        return EnsureView();
    }

    private void ReleaseGeneratedPresentationIfPossible()
    {
        if (!ownsGeneratedView || presentationPinned || HasActiveOutlineRequest
            || RequiresIndividualPresentation(cachedItemDefinition) || view == null)
        {
            return;
        }

        PortableObjectView released = view;
        SyncStateFromView(released);
        view = null;
        ownsGeneratedView = false;
        released.Bind(null);
        if (Application.isPlaying) UnityEngine.Object.Destroy(released.gameObject);
        else UnityEngine.Object.DestroyImmediate(released.gameObject);
    }

    private void ConfigureViewAssets()
    {
        if (view == null) return;
        if (view.Body != null) view.Body.sharedMesh = cachedMesh;
        if (view.BodyRenderer != null)
        {
            view.BodyRenderer.sharedMaterial = cachedMaterial;
            cachedShadowCastingMode = view.BodyRenderer.shadowCastingMode;
            cachedReceiveShadows = view.BodyRenderer.receiveShadows;
        }
        ItemDefinition definition = ResolveItemDefinition();
        Bucket bucket = definition != null ? definition.mapObject as Bucket : null;
        PortableBucketWaterVisual bucketVisual = view.GetComponent<PortableBucketWaterVisual>();
        if (bucket != null && bucketVisual == null) bucketVisual = view.gameObject.AddComponent<PortableBucketWaterVisual>();
        PortableObjectComponent c = Read();
        bucketVisual?.Refresh(bucket, Bucket.ResolveContainedFluidItemId(definition), view.Body, c.Active && !c.SuppressRendering);
        if (definition != null)
        {
            ItemLightController.Configure(view.gameObject, definition);
        }
        else
        {
            ItemLightController.Configure(view.gameObject, (ItemDefinition)null);
        }
        UpdateRendererVisibility();
    }

    private static Vector3 ResolveLocalScale(Vector3 worldScale, Transform parent)
    {
        if (parent == null) return worldScale;
        Vector3 parentScale = parent.lossyScale;
        return new Vector3(
            Mathf.Abs(parentScale.x) > 0.000001f ? worldScale.x / parentScale.x : worldScale.x,
            Mathf.Abs(parentScale.y) > 0.000001f ? worldScale.y / parentScale.y : worldScale.y,
            Mathf.Abs(parentScale.z) > 0.000001f ? worldScale.z / parentScale.z : worldScale.z);
    }

    private static Bounds TransformBounds(Bounds localBounds, Matrix4x4 matrix)
    {
        Vector3 localExtents = localBounds.extents;
        Vector3 axisX = matrix.MultiplyVector(new Vector3(localExtents.x, 0f, 0f));
        Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, localExtents.y, 0f));
        Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, localExtents.z));
        Vector3 worldExtents = new Vector3(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
        return new Bounds(matrix.MultiplyPoint3x4(localBounds.center), worldExtents * 2f);
    }

    private ItemDefinition ResolveItemDefinition()
    {
        if (cachedItemDefinition != null && cachedItemDefinition.id == ItemId) return cachedItemDefinition;
        ItemManager manager = GameManager.Instance != null ? GameManager.Instance.ItemManger : null;
        manager?.TryGetItemDefinitionById(ItemId, out cachedItemDefinition);
        return cachedItemDefinition;
    }

    private static bool RequiresIndividualPresentation(ItemDefinition definition) =>
        definition != null
        && (definition.mapObject is Bucket || definition.lightMode != ItemDefinition.ItemLightMode.None);

    private PortableItemRenderer ResolvePortableItemRenderer()
    {
        if (portableItemRenderer != null) return portableItemRenderer;
        TerrainGenerator generator = presentationParent != null
            ? presentationParent.GetComponentInParent<TerrainGenerator>() : null;
        generator ??= TerrainGenerator.Active;
        portableItemRenderer = generator != null ? PortableItemRenderer.EnsureFor(generator.gameObject) : null;
        return portableItemRenderer;
    }

    private void MarkPortableItemRenderDataDirty()
    {
        if (IsUsingBatchedRendering) portableItemRenderer?.MarkDirty();
    }

    private void UnregisterFromPortableItemRenderer()
    {
        portableItemRenderer?.Unregister(this);
        portableItemRenderer = null;
    }

    private void UpdateRendererVisibility()
    {
        MeshRenderer renderer = view != null ? view.BodyRenderer : null;
        if (renderer == null || !IsAlive) return;
        PortableObjectComponent c = Read();
        renderer.enabled = c.Active && !c.BatchedRendering && !c.SuppressRendering && !bodyRendererTemporarilyHidden;
    }

    private bool IsFocusExcludedByContainer() => presentationParent != null
        && (presentationParent.GetComponentInParent<Train>() != null
            || presentationParent.GetComponentInParent<BoxObject>() != null);

    private enum OutlineKind : byte { Hover, Focused, Pickup }
    private bool HasOwnOutlineRequest => hoverOutlineVisible || focusedOutlineVisible || pickupOutlineVisible;
    private bool HasActiveOutlineRequest => HasOwnOutlineRequest || focusOutlineOwner != null;

    private void SetOutline(OutlineKind kind, bool visible)
    {
        if (visible && IsOnConveyor) return;
        MeshRenderer renderer = (visible ? EnsureView() : view)?.BodyRenderer;
        if (renderer == null) return;
        switch (kind)
        {
            case OutlineKind.Hover:
                if (hoverOutlineVisible == visible) return;
                hoverOutlineVisible = visible;
                if (visible) AnimalScreenSpaceOutline.ShowHovered(renderer); else AnimalScreenSpaceOutline.HideHovered(renderer);
                break;
            case OutlineKind.Focused:
                if (focusedOutlineVisible == visible) return;
                focusedOutlineVisible = visible;
                if (visible) AnimalScreenSpaceOutline.ShowFocused(renderer); else AnimalScreenSpaceOutline.HideFocused(renderer);
                break;
            default:
                if (pickupOutlineVisible == visible) return;
                pickupOutlineVisible = visible;
                if (visible) AnimalScreenSpaceOutline.ShowPickup(renderer); else AnimalScreenSpaceOutline.HidePickup(renderer);
                break;
        }
        if (visible)
        {
            AcquireFocusStackOutlineMembers();
            if (IsUsingBatchedRendering)
            {
                restoreBatchedRenderingAfterOutline = true;
                Mutate((ref PortableObjectComponent c) => c.BatchedRendering = false);
                UnregisterFromPortableItemRenderer();
                UpdateRendererVisibility();
            }
        }
        else if (!HasOwnOutlineRequest) ReleaseFocusStackOutlineMembers(true);
    }

    private void AcquireFocusStackOutlineMembers()
    {
        if (focusStack == null || focusStack.Count == 0) { AcquireFocusOutlineOwner(this); return; }
        for (int i = 0; i < focusStack.Count; i++)
        {
            PortableObject member = focusStack[i];
            if (member != null && member.ItemId == ItemId) member.AcquireFocusOutlineOwner(this);
        }
    }

    private void AcquireFocusOutlineOwner(PortableObject owner)
    {
        if (owner == null || focusOutlineOwner != null) return;
        focusOutlineOwner = owner;
        EnsureView();
    }

    private void ReleaseFocusStackOutlineMembers(bool restore)
    {
        if (focusStack != null)
            for (int i = 0; i < focusStack.Count; i++) focusStack[i]?.ReleaseFocusOutlineOwner(this, restore);
        ReleaseFocusOutlineOwner(this, restore);
    }

    private void ReleaseFocusOutlineOwner(PortableObject owner, bool restore)
    {
        if (focusOutlineOwner != owner) return;
        focusOutlineOwner = null;
        if (restore && !HasActiveOutlineRequest && restoreBatchedRenderingAfterOutline)
        {
            restoreBatchedRenderingAfterOutline = false;
            SetBatchedRendering(true);
        }
    }

    private int CopyOwnOutlineMaskRenderers(Renderer[] destination, int count)
    {
        MeshRenderer renderer = view != null ? view.BodyRenderer : null;
        if (renderer != null && renderer.enabled && count < destination.Length) destination[count++] = renderer;
        return count;
    }

    private void DetachFromFocusOutlineForExternalRenderingChange()
    {
        if (HasOwnOutlineRequest) ClearFocusOutlines(false);
        focusOutlineOwner = null;
        restoreBatchedRenderingAfterOutline = false;
    }

    private void ClearFocusOutlines(bool restore)
    {
        MeshRenderer renderer = view != null ? view.BodyRenderer : null;
        if (renderer != null)
        {
            AnimalScreenSpaceOutline.HideHovered(renderer);
            AnimalScreenSpaceOutline.HideFocused(renderer);
            AnimalScreenSpaceOutline.HidePickup(renderer);
        }
        hoverOutlineVisible = false;
        focusedOutlineVisible = false;
        pickupOutlineVisible = false;
        ReleaseFocusStackOutlineMembers(restore);
        focusStack = null;
    }
}

internal static class SleepAwakeDebugVisual
{
    public const float SleepingBrightness = 0.35f;
    public static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    public static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

    public static Color GetMaterialBaseColor(Material material)
    {
        if (material == null) return Color.white;
        if (material.HasProperty(BaseColorPropertyId)) return material.GetColor(BaseColorPropertyId);
        if (material.HasProperty(ColorPropertyId)) return material.GetColor(ColorPropertyId);
        return Color.white;
    }

    public static Color GetSleepingColor(Material material) => Darken(GetMaterialBaseColor(material));
    public static Color Darken(Color color) => new Color(color.r * SleepingBrightness,
        color.g * SleepingBrightness, color.b * SleepingBrightness, color.a);

    public static void ApplySleepingColor(MaterialPropertyBlock block, Material material)
    {
        if (block == null) return;
        Color color = GetSleepingColor(material);
        block.SetColor(BaseColorPropertyId, color);
        block.SetColor(ColorPropertyId, color);
    }
}

internal static class BeltItemLineDebugVisual
{
    private static readonly int BaseMapPropertyId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexPropertyId = Shader.PropertyToID("_MainTex");

    public static Color32 GetColor(int lineId)
    {
        float hue = Mathf.Repeat(lineId * 0.61803398875f, 1f);
        Color color = Color.HSVToRGB(hue, 0.74f, 1f);
        color.a = 1f;
        return color;
    }

    public static void ApplySolidColor(MaterialPropertyBlock block, Color color)
    {
        if (block == null) return;
        block.SetColor(SleepAwakeDebugVisual.BaseColorPropertyId, color);
        block.SetColor(SleepAwakeDebugVisual.ColorPropertyId, color);
        block.SetTexture(BaseMapPropertyId, Texture2D.whiteTexture);
        block.SetTexture(MainTexPropertyId, Texture2D.whiteTexture);
    }
}
