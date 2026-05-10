using DG.Tweening;
using System;
using System.Collections.Generic;
using ProjectF.Attributes;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
public class PortableStack
{
    public List<PortableObject> stack;
}

public class PortableObject : MonoBehaviour
{
    public const float MoveToDuration = 0.3f;

    private static readonly HashSet<PortableObject> liveObjects = new HashSet<PortableObject>();
    public event Action<PortableObject> MoveCancelled;

    [SerializeField, ReadOnly]
    private int id;

    [SerializeField]
    private MeshFilter body;

    private MeshRenderer bodyRenderer;
    private PortableItemRenderer portableItemRenderer;
    private Tween moveTween;
    private Transform cachedTransform;
    private GameObject cachedGameObject;
    private DroppedItemPickupGate cachedPickupGate;
    private MaterialPropertyBlock debugVisualPropertyBlock;
    private bool useBatchedRendering;
    private bool suppressVisualRendering;
    private bool isMovingToTarget;
    private bool sleepAwakeSleeping;
    private bool debugVisualStateInitialized;
    private bool lastSleepAwakeDarkTint;
    private bool beltItemLineDebugActive;
    private bool lastBeltItemLineDebugActive;
    private Color32 beltItemLineDebugColor = Color.white;
    private Color32 lastBeltItemLineDebugColor;
    private int lastConveyorMoveFrame = -1;

    public int ItemId => id;
    public bool IsMovingToTarget => isMovingToTarget;
    public bool IsUsingBatchedRendering => useBatchedRendering;
    public bool IsVisualRenderingSuppressed => suppressVisualRendering;
    public bool WasMovedByConveyorThisFrame => lastConveyorMoveFrame == Time.frameCount;
    public Transform CachedTransform => cachedTransform != null ? cachedTransform : (cachedTransform = transform);
    public GameObject CachedGameObject => cachedGameObject != null ? cachedGameObject : (cachedGameObject = gameObject);
    public Vector3 WorldPosition => CachedTransform.position;
    public DroppedItemPickupGate PickupGate
    {
        get
        {
            if (cachedPickupGate == null)
            {
                cachedPickupGate = GetComponent<DroppedItemPickupGate>();
            }

            return cachedPickupGate;
        }
    }

    public static void RefreshAllSleepAwakeVisuals()
    {
        foreach (PortableObject portableObject in liveObjects)
        {
            portableObject?.RefreshSleepAwakeVisual(true);
        }
    }

    public static void RefreshAllBeltItemLineDebugVisuals()
    {
        foreach (PortableObject portableObject in liveObjects)
        {
            portableObject?.RefreshSleepAwakeVisual(true);
        }
    }

    public DroppedItemPickupGate GetOrAddPickupGate()
    {
        DroppedItemPickupGate gate = PickupGate;
        if (gate == null)
        {
            gate = CachedGameObject.AddComponent<DroppedItemPickupGate>();
            cachedPickupGate = gate;
        }

        return gate;
    }

    public void SetSleepAwakeSleeping(bool sleeping)
    {
        if (sleepAwakeSleeping == sleeping)
        {
            RefreshSleepAwakeVisual();
            return;
        }

        sleepAwakeSleeping = sleeping;
        RefreshSleepAwakeVisual(true);
        MarkPortableItemRenderDataDirty();
    }

    public void RefreshSleepAwakeVisual(bool force = false)
    {
        ResolveBodyRenderer();
        bool useDarkTint = ShouldUseSleepAwakeDarkTint();
        bool useBeltItemLineDebugColor = ShouldUseBeltItemLineDebugColor();
        if (!force
            && debugVisualStateInitialized
            && lastSleepAwakeDarkTint == useDarkTint
            && lastBeltItemLineDebugActive == useBeltItemLineDebugColor
            && (!useBeltItemLineDebugColor || lastBeltItemLineDebugColor.Equals(beltItemLineDebugColor)))
        {
            return;
        }

        debugVisualStateInitialized = true;
        lastSleepAwakeDarkTint = useDarkTint;
        lastBeltItemLineDebugActive = useBeltItemLineDebugColor;
        lastBeltItemLineDebugColor = beltItemLineDebugColor;
        portableItemRenderer?.MarkDirty();

        if (bodyRenderer == null)
        {
            return;
        }

        if (!useDarkTint && !useBeltItemLineDebugColor)
        {
            bodyRenderer.SetPropertyBlock(null);
            return;
        }

        debugVisualPropertyBlock ??= new MaterialPropertyBlock();
        debugVisualPropertyBlock.Clear();
        if (useBeltItemLineDebugColor)
        {
            Color color = beltItemLineDebugColor;
            if (useDarkTint)
            {
                color = SleepAwakeDebugVisual.Darken(color);
            }

            BeltItemLineDebugVisual.ApplySolidColor(debugVisualPropertyBlock, color);
        }
        else
        {
            SleepAwakeDebugVisual.ApplySleepingColor(debugVisualPropertyBlock, bodyRenderer.sharedMaterial);
        }

        bodyRenderer.SetPropertyBlock(debugVisualPropertyBlock);
    }

    public void SetBeltItemLineDebugColor(bool active, Color32 color)
    {
        if (beltItemLineDebugActive == active
            && (!active || beltItemLineDebugColor.Equals(color)))
        {
            RefreshSleepAwakeVisual();
            return;
        }

        beltItemLineDebugActive = active;
        beltItemLineDebugColor = active ? color : (Color32)Color.white;
        RefreshSleepAwakeVisual(true);
        MarkPortableItemRenderDataDirty();
    }

    public void ClearBeltItemLineDebugColor()
    {
        SetBeltItemLineDebugColor(false, Color.white);
    }

    public void MarkMovedByConveyorThisFrame()
    {
        lastConveyorMoveFrame = Time.frameCount;
    }

    public void SetCachedParent(Transform parent, bool worldPositionStays)
    {
        CachedTransform.SetParent(parent, worldPositionStays);
        MarkPortableItemRenderDataDirty();
    }

    public void SetCachedActive(bool active)
    {
        if (CachedGameObject.activeSelf == active)
        {
            UpdateRendererVisibility();
            return;
        }

        CachedGameObject.SetActive(active);
        MarkPortableItemRenderDataDirty();
        UpdateRendererVisibility();
    }

    public void SetWorldPosition(Vector3 position)
    {
        CachedTransform.position = position;
        MarkPortableItemRenderDataDirty();
    }

    public void MarkBatchedRenderDataDirty()
    {
        MarkPortableItemRenderDataDirty();
    }

    private void MarkPortableItemRenderDataDirty()
    {
        if (useBatchedRendering)
        {
            portableItemRenderer?.MarkDirty();
        }
    }
    
    public bool SetItem(int id)
    {
        this.id = id;

        if (body == null)
        {
            body = GetComponent<MeshFilter>();
            if (body == null)
            {
                body = GetComponentInChildren<MeshFilter>(true);
            }
        }

        ResolveBodyRenderer();
        if (body == null || GameManager.Instance == null || GameManager.Instance.ItemManger == null)
        {
            return false;
        }

        ItemManager itemManager = GameManager.Instance.ItemManger;
        if (itemManager == null || !itemManager.TryGetItemSetById(id, out ItemManager.ItemSet itemSet))
        {
            return false;
        }

        Mesh portableMesh = itemSet.portableMesh;
        Material portableMat = itemSet.portableMat;

        if (bodyRenderer == null)
        {
            return false;
        }

        if (portableMat != null && !portableMat.enableInstancing)
        {
            portableMat.enableInstancing = true;
        }

        body.sharedMesh = portableMesh;
        bodyRenderer.sharedMaterial = portableMat;
        MarkPortableItemRenderDataDirty();
        UpdateRendererVisibility();
        RefreshSleepAwakeVisual(true);
        return true;
    }

    public void SetBatchedRendering(bool shouldUseBatchedRendering)
    {
        suppressVisualRendering = false;
        ResolveBodyRenderer();
        if (useBatchedRendering == shouldUseBatchedRendering && (!useBatchedRendering || portableItemRenderer != null))
        {
            MarkPortableItemRenderDataDirty();
            UpdateRendererVisibility();
            return;
        }

        useBatchedRendering = shouldUseBatchedRendering;
        if (!useBatchedRendering)
        {
            UnregisterFromPortableItemRenderer();
            UpdateRendererVisibility();
            return;
        }

        portableItemRenderer = ResolvePortableItemRenderer();
        if (portableItemRenderer == null)
        {
            useBatchedRendering = false;
            UpdateRendererVisibility();
            return;
        }

        portableItemRenderer.Register(this);
        MarkPortableItemRenderDataDirty();
        UpdateRendererVisibility();
    }

    public void SetVisualRenderingSuppressed(bool suppressed)
    {
        ResolveBodyRenderer();
        if (suppressVisualRendering == suppressed)
        {
            UpdateRendererVisibility();
            return;
        }

        suppressVisualRendering = suppressed;
        if (suppressVisualRendering)
        {
            if (useBatchedRendering)
            {
                UnregisterFromPortableItemRenderer();
            }

            useBatchedRendering = false;
        }

        MarkPortableItemRenderDataDirty();
        UpdateRendererVisibility();
    }

    public bool TryGetBatchRenderData(
        out int itemId,
        out Mesh mesh,
        out Material material,
        out Matrix4x4 localToWorldMatrix,
        out Vector3 worldPosition,
        out int layer,
        out ShadowCastingMode shadowCastingMode,
        out bool receiveShadows,
        out bool useSleepAwakeDarkTint,
        out bool useBeltItemLineDebugColor,
        out Color32 beltItemLineDebugColor)
    {
        ResolveBodyRenderer();

        itemId = id;
        mesh = body != null ? body.sharedMesh : null;
        material = bodyRenderer != null ? bodyRenderer.sharedMaterial : null;
        Transform targetTransform = CachedTransform;
        GameObject targetGameObject = CachedGameObject;
        localToWorldMatrix = targetTransform.localToWorldMatrix;
        worldPosition = targetTransform.position;
        layer = targetGameObject.layer;
        shadowCastingMode = bodyRenderer != null ? bodyRenderer.shadowCastingMode : ShadowCastingMode.Off;
        receiveShadows = bodyRenderer != null && bodyRenderer.receiveShadows;
        useSleepAwakeDarkTint = ShouldUseSleepAwakeDarkTint();
        useBeltItemLineDebugColor = ShouldUseBeltItemLineDebugColor();
        beltItemLineDebugColor = useBeltItemLineDebugColor ? this.beltItemLineDebugColor : (Color32)Color.white;

        return useBatchedRendering
               && targetGameObject.activeInHierarchy
               && itemId >= 0
               && bodyRenderer != null
               && mesh != null
               && material != null;
    }

    public void MoveTo(Transform target, Action onComplete = null)
    {
        if (target == null)
        {
            onComplete?.Invoke();
            return;
        }

        MoveTo(() => target != null ? target.position : WorldPosition, 0f, null, onComplete, true);
    }

    public void MoveTo(Transform target, float delay = 0f, Func<Vector3> startPositionProvider = null, Action onComplete = null, bool deactivateOnComplete = true, bool useJumpArc = true, float moveDuration = MoveToDuration, bool trackStartPositionDuringMove = true)
    {
        if (target == null)
        {
            onComplete?.Invoke();
            return;
        }

        MoveTo(() => target != null ? target.position : WorldPosition, delay, startPositionProvider, onComplete, deactivateOnComplete, useJumpArc, moveDuration, trackStartPositionDuringMove);
    }

    public void MoveTo(Vector3 targetPosition, float delay = 0f, Action onComplete = null, bool deactivateOnComplete = true, bool useJumpArc = true, float moveDuration = MoveToDuration)
    {
        MoveTo(() => targetPosition, delay, null, onComplete, deactivateOnComplete, useJumpArc, moveDuration);
    }

    public void MoveTo(Func<Vector3> targetPositionProvider, float delay = 0f, Func<Vector3> startPositionProvider = null, Action onComplete = null, bool deactivateOnComplete = true, bool useJumpArc = true, float moveDuration = MoveToDuration, bool trackStartPositionDuringMove = true)
    {
        if (targetPositionProvider == null)
        {
            onComplete?.Invoke();
            return;
        }

        SetBatchedRendering(false);
        SetSleepAwakeSleeping(false);
        ClearBeltItemLineDebugColor();
        moveTween?.Kill();
        moveTween = null;
        CachedTransform.DOKill();
        ResolveBodyRenderer();
        isMovingToTarget = true;

        Sequence sequence = DOTween.Sequence().SetTarget(CachedTransform);
        moveTween = sequence;
        if (delay > 0f)
        {
            SetBodyRendererVisible(false);
            sequence.Append(
                DOVirtual.Float(0f, 1f, delay, _ =>
                {
                    if (startPositionProvider != null)
                    {
                        CachedTransform.position = startPositionProvider();
                    }
                }).SetEase(Ease.Linear));
            sequence.AppendCallback(() => SetBodyRendererVisible(true));
        }
        else
        {
            SetBodyRendererVisible(true);
            if (startPositionProvider != null)
            {
                CachedTransform.position = startPositionProvider();
            }
        }

        Vector3 launchStartPosition = WorldPosition;
        sequence.AppendCallback(() =>
        {
            launchStartPosition = startPositionProvider != null ? startPositionProvider() : WorldPosition;
            CachedTransform.position = launchStartPosition;
        });

        const float jumpPower = 1f;
        float safeMoveDuration = Mathf.Max(0.001f, moveDuration);
        sequence.Append(
            DOVirtual.Float(0f, 1f, safeMoveDuration, t =>
            {
                Vector3 currentStartPosition = trackStartPositionDuringMove && startPositionProvider != null
                    ? startPositionProvider()
                    : launchStartPosition;
                Vector3 currentTargetPosition = targetPositionProvider();
                Vector3 horizontalPosition = Vector3.Lerp(currentStartPosition, currentTargetPosition, t);
                float verticalOffset = useJumpArc ? 4f * jumpPower * t * (1f - t) : 0f;
                CachedTransform.position = horizontalPosition + (Vector3.up * verticalOffset);
            }).SetEase(Ease.Linear));
        sequence.OnComplete(() =>
        {
            if (moveTween == sequence)
            {
                moveTween = null;
                isMovingToTarget = false;
            }

            CachedTransform.position = targetPositionProvider();
            SetBodyRendererVisible(true);
            if (deactivateOnComplete)
            {
                CachedGameObject.SetActive(false);
            }

            onComplete?.Invoke();
        });
        sequence.OnKill(() =>
        {
            if (moveTween == sequence)
            {
                moveTween = null;
                isMovingToTarget = false;
                SetBodyRendererVisible(true);
            }
        });
    }

    public void CancelMove()
    {
        moveTween?.Kill();
        moveTween = null;
        CachedTransform.DOKill();
        isMovingToTarget = false;
        SetBodyRendererVisible(true);
        MoveCancelled?.Invoke(this);
    }

    private void SetBodyRendererVisible(bool isVisible)
    {
        ResolveBodyRenderer();
        if (bodyRenderer == null)
        {
            return;
        }

        if (!isVisible)
        {
            bodyRenderer.enabled = false;
            return;
        }

        UpdateRendererVisibility();
    }

    private void OnEnable()
    {
        liveObjects.Add(this);
        cachedTransform = transform;
        cachedGameObject = gameObject;
        if (useBatchedRendering)
        {
            portableItemRenderer = ResolvePortableItemRenderer();
            portableItemRenderer?.Register(this);
        }

        UpdateRendererVisibility();
        RefreshSleepAwakeVisual(true);
    }

    private void OnDisable()
    {
        liveObjects.Remove(this);
        isMovingToTarget = false;
        UnregisterFromPortableItemRenderer();
    }

    private void OnDestroy()
    {
        liveObjects.Remove(this);
        UnregisterFromPortableItemRenderer();
    }

    private void ResolveBodyRenderer()
    {
        if (body == null)
        {
            body = GetComponent<MeshFilter>();
            if (body == null)
            {
                body = GetComponentInChildren<MeshFilter>(true);
            }
        }

        if (bodyRenderer != null)
        {
            return;
        }

        if (body != null)
        {
            bodyRenderer = body.GetComponent<MeshRenderer>();
        }

        if (bodyRenderer == null)
        {
            bodyRenderer = GetComponentInChildren<MeshRenderer>(true);
        }
    }

    private PortableItemRenderer ResolvePortableItemRenderer()
    {
        if (portableItemRenderer != null)
        {
            return portableItemRenderer;
        }

        TerrainGenerator generator = CachedTransform.GetComponentInParent<TerrainGenerator>();
        if (generator == null)
        {
            generator = TerrainGenerator.Active;
        }

        GameObject host = generator != null ? generator.gameObject : null;
        if (host == null)
        {
            return null;
        }

        portableItemRenderer = PortableItemRenderer.EnsureFor(host);
        return portableItemRenderer;
    }

    private void UnregisterFromPortableItemRenderer()
    {
        if (portableItemRenderer == null)
        {
            return;
        }

        portableItemRenderer.Unregister(this);
        if (!useBatchedRendering)
        {
            portableItemRenderer = null;
        }
    }

    private void UpdateRendererVisibility()
    {
        ResolveBodyRenderer();
        if (bodyRenderer == null)
        {
            return;
        }

        bodyRenderer.enabled = CachedGameObject.activeInHierarchy && !useBatchedRendering && !suppressVisualRendering;
        RefreshSleepAwakeVisual();
    }

    private bool ShouldUseSleepAwakeDarkTint()
    {
        return sleepAwakeSleeping
            && GameManager.Instance != null
            && GameManager.Instance.ShowSleepAwake;
    }

    private bool ShouldUseBeltItemLineDebugColor()
    {
        return beltItemLineDebugActive
            && GameManager.Instance != null
            && GameManager.Instance.ShowBeltItemLine;
    }
}

internal static class SleepAwakeDebugVisual
{
    public const float SleepingBrightness = 0.35f;

    public static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    public static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

    public static Color GetMaterialBaseColor(Material material)
    {
        if (material == null)
        {
            return Color.white;
        }

        if (material.HasProperty(BaseColorPropertyId))
        {
            return material.GetColor(BaseColorPropertyId);
        }

        if (material.HasProperty(ColorPropertyId))
        {
            return material.GetColor(ColorPropertyId);
        }

        return Color.white;
    }

    public static Color GetSleepingColor(Material material)
    {
        return Darken(GetMaterialBaseColor(material));
    }

    public static Color Darken(Color color)
    {
        return new Color(
            color.r * SleepingBrightness,
            color.g * SleepingBrightness,
            color.b * SleepingBrightness,
            color.a);
    }

    public static void ApplySleepingColor(MaterialPropertyBlock propertyBlock, Material material)
    {
        if (propertyBlock == null)
        {
            return;
        }

        Color color = GetSleepingColor(material);
        propertyBlock.SetColor(BaseColorPropertyId, color);
        propertyBlock.SetColor(ColorPropertyId, color);
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

    public static void ApplySolidColor(MaterialPropertyBlock propertyBlock, Color color)
    {
        if (propertyBlock == null)
        {
            return;
        }

        propertyBlock.SetColor(SleepAwakeDebugVisual.BaseColorPropertyId, color);
        propertyBlock.SetColor(SleepAwakeDebugVisual.ColorPropertyId, color);
        propertyBlock.SetTexture(BaseMapPropertyId, Texture2D.whiteTexture);
        propertyBlock.SetTexture(MainTexPropertyId, Texture2D.whiteTexture);
    }
}
