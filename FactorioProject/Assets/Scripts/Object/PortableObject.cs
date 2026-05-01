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

    [SerializeField, ReadOnly]
    private int id;

    [SerializeField]
    private MeshFilter body;

    private MeshRenderer bodyRenderer;
    private PortableObjectBatchRenderer batchRenderer;
    private Transform cachedTransform;
    private GameObject cachedGameObject;
    private DroppedItemPickupGate cachedPickupGate;
    private MaterialPropertyBlock sleepAwakePropertyBlock;
    private bool useBatchedRendering;
    private bool suppressVisualRendering;
    private bool isMovingToTarget;
    private bool sleepAwakeSleeping;
    private bool sleepAwakeVisualStateInitialized;
    private bool lastSleepAwakeDarkTint;
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
        NotifyBatchRenderDataChanged();
    }

    public void RefreshSleepAwakeVisual(bool force = false)
    {
        ResolveBodyRenderer();
        bool useDarkTint = ShouldUseSleepAwakeDarkTint();
        if (!force && sleepAwakeVisualStateInitialized && lastSleepAwakeDarkTint == useDarkTint)
        {
            return;
        }

        sleepAwakeVisualStateInitialized = true;
        lastSleepAwakeDarkTint = useDarkTint;
        batchRenderer?.MarkDirty();

        if (bodyRenderer == null)
        {
            return;
        }

        if (!useDarkTint)
        {
            bodyRenderer.SetPropertyBlock(null);
            return;
        }

        sleepAwakePropertyBlock ??= new MaterialPropertyBlock();
        sleepAwakePropertyBlock.Clear();
        SleepAwakeDebugVisual.ApplySleepingColor(sleepAwakePropertyBlock, bodyRenderer.sharedMaterial);
        bodyRenderer.SetPropertyBlock(sleepAwakePropertyBlock);
    }

    public void MarkMovedByConveyorThisFrame()
    {
        lastConveyorMoveFrame = Time.frameCount;
    }

    public void SetCachedParent(Transform parent, bool worldPositionStays)
    {
        CachedTransform.SetParent(parent, worldPositionStays);
        NotifyBatchRenderDataChanged();
    }

    public void SetCachedActive(bool active)
    {
        if (CachedGameObject.activeSelf == active)
        {
            UpdateRendererVisibility();
            return;
        }

        CachedGameObject.SetActive(active);
        NotifyBatchRenderDataChanged();
        UpdateRendererVisibility();
    }

    public void SetWorldPosition(Vector3 position)
    {
        CachedTransform.position = position;
        NotifyBatchRenderDataChanged();
    }

    public void NotifyBatchRenderDataChanged()
    {
        if (useBatchedRendering)
        {
            batchRenderer?.MarkDirty();
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
        NotifyBatchRenderDataChanged();
        UpdateRendererVisibility();
        RefreshSleepAwakeVisual(true);
        return true;
    }

    public void SetBatchedRendering(bool shouldUseBatchedRendering)
    {
        suppressVisualRendering = false;
        ResolveBodyRenderer();
        if (useBatchedRendering == shouldUseBatchedRendering && (!useBatchedRendering || batchRenderer != null))
        {
            NotifyBatchRenderDataChanged();
            UpdateRendererVisibility();
            return;
        }

        useBatchedRendering = shouldUseBatchedRendering;
        if (!useBatchedRendering)
        {
            UnregisterFromBatchRenderer();
            UpdateRendererVisibility();
            return;
        }

        batchRenderer = ResolveBatchRenderer();
        if (batchRenderer == null)
        {
            useBatchedRendering = false;
            UpdateRendererVisibility();
            return;
        }

        batchRenderer.Register(this);
        NotifyBatchRenderDataChanged();
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
                UnregisterFromBatchRenderer();
            }

            useBatchedRendering = false;
        }

        NotifyBatchRenderDataChanged();
        UpdateRendererVisibility();
    }

    public bool TryGetBatchRenderData(
        out Mesh mesh,
        out Material material,
        out Matrix4x4 localToWorldMatrix,
        out Vector3 worldPosition,
        out int layer,
        out ShadowCastingMode shadowCastingMode,
        out bool receiveShadows,
        out bool useSleepAwakeDarkTint)
    {
        ResolveBodyRenderer();

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

        return useBatchedRendering
               && targetGameObject.activeInHierarchy
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

    public void MoveTo(Transform target, float delay = 0f, Func<Vector3> startPositionProvider = null, Action onComplete = null, bool deactivateOnComplete = true)
    {
        if (target == null)
        {
            onComplete?.Invoke();
            return;
        }

        MoveTo(() => target != null ? target.position : WorldPosition, delay, startPositionProvider, onComplete, deactivateOnComplete);
    }

    public void MoveTo(Vector3 targetPosition, float delay = 0f, Action onComplete = null, bool deactivateOnComplete = true)
    {
        MoveTo(() => targetPosition, delay, null, onComplete, deactivateOnComplete);
    }

    public void MoveTo(Func<Vector3> targetPositionProvider, float delay = 0f, Func<Vector3> startPositionProvider = null, Action onComplete = null, bool deactivateOnComplete = true)
    {
        if (targetPositionProvider == null)
        {
            onComplete?.Invoke();
            return;
        }

        SetBatchedRendering(false);
        SetSleepAwakeSleeping(false);
        CachedTransform.DOKill();
        ResolveBodyRenderer();
        isMovingToTarget = true;

        Sequence sequence = DOTween.Sequence();
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
        sequence.Append(
            DOVirtual.Float(0f, 1f, MoveToDuration, t =>
            {
                Vector3 currentStartPosition = startPositionProvider != null ? startPositionProvider() : launchStartPosition;
                Vector3 currentTargetPosition = targetPositionProvider();
                Vector3 horizontalPosition = Vector3.Lerp(currentStartPosition, currentTargetPosition, t);
                float verticalOffset = 4f * jumpPower * t * (1f - t);
                CachedTransform.position = horizontalPosition + (Vector3.up * verticalOffset);
            }).SetEase(Ease.Linear));
        sequence.OnComplete(() =>
        {
            isMovingToTarget = false;
            CachedTransform.position = targetPositionProvider();
            SetBodyRendererVisible(true);
            if (deactivateOnComplete)
            {
                CachedGameObject.SetActive(false);
            }

            onComplete?.Invoke();
        });
    }

    private void SetBodyRendererVisible(bool isVisible)
    {
        ResolveBodyRenderer();
        if (bodyRenderer == null)
        {
            return;
        }

        bodyRenderer.enabled = isVisible;
    }

    private void OnEnable()
    {
        liveObjects.Add(this);
        cachedTransform = transform;
        cachedGameObject = gameObject;
        if (useBatchedRendering)
        {
            batchRenderer = ResolveBatchRenderer();
            batchRenderer?.Register(this);
        }

        UpdateRendererVisibility();
        RefreshSleepAwakeVisual(true);
    }

    private void OnDisable()
    {
        liveObjects.Remove(this);
        isMovingToTarget = false;
        UnregisterFromBatchRenderer();
    }

    private void OnDestroy()
    {
        liveObjects.Remove(this);
        UnregisterFromBatchRenderer();
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

    private PortableObjectBatchRenderer ResolveBatchRenderer()
    {
        if (batchRenderer != null)
        {
            return batchRenderer;
        }

        TerrainGenerator generator = CachedTransform.GetComponentInParent<TerrainGenerator>();
        GameObject host = generator != null ? generator.gameObject : null;
        if (host == null)
        {
            return null;
        }

        batchRenderer = host.GetComponent<PortableObjectBatchRenderer>();
        if (batchRenderer == null)
        {
            batchRenderer = host.AddComponent<PortableObjectBatchRenderer>();
        }

        return batchRenderer;
    }

    private void UnregisterFromBatchRenderer()
    {
        if (batchRenderer == null)
        {
            return;
        }

        batchRenderer.Unregister(this);
        if (!useBatchedRendering)
        {
            batchRenderer = null;
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
