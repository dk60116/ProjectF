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
    [SerializeField, ReadOnly]
    private int id;

    [SerializeField]
    private MeshFilter body;

    private MeshRenderer bodyRenderer;
    private PortableObjectBatchRenderer batchRenderer;
    private bool useBatchedRendering;
    private bool isMovingToTarget;

    public int ItemId => id;
    public bool IsMovingToTarget => isMovingToTarget;
    
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
        UpdateRendererVisibility();
        return true;
    }

    public void SetBatchedRendering(bool shouldUseBatchedRendering)
    {
        ResolveBodyRenderer();
        if (useBatchedRendering == shouldUseBatchedRendering && (!useBatchedRendering || batchRenderer != null))
        {
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
        UpdateRendererVisibility();
    }

    public bool TryGetBatchRenderData(
        out Mesh mesh,
        out Material material,
        out Matrix4x4 localToWorldMatrix,
        out Vector3 worldPosition,
        out int layer,
        out ShadowCastingMode shadowCastingMode,
        out bool receiveShadows)
    {
        ResolveBodyRenderer();

        mesh = body != null ? body.sharedMesh : null;
        material = bodyRenderer != null ? bodyRenderer.sharedMaterial : null;
        localToWorldMatrix = transform.localToWorldMatrix;
        worldPosition = transform.position;
        layer = gameObject.layer;
        shadowCastingMode = bodyRenderer != null ? bodyRenderer.shadowCastingMode : ShadowCastingMode.Off;
        receiveShadows = bodyRenderer != null && bodyRenderer.receiveShadows;

        return useBatchedRendering
               && gameObject.activeInHierarchy
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

        MoveTo(target.position, 0f, onComplete, true);
    }

    public void MoveTo(Vector3 targetPosition, float delay = 0f, Action onComplete = null, bool deactivateOnComplete = true)
    {
        SetBatchedRendering(false);
        transform.DOKill();
        ResolveBodyRenderer();
        isMovingToTarget = true;

        Sequence sequence = DOTween.Sequence();
        if (delay > 0f)
        {
            SetBodyRendererVisible(false);
            sequence.AppendInterval(delay);
            sequence.AppendCallback(() => SetBodyRendererVisible(true));
        }
        else
        {
            SetBodyRendererVisible(true);
        }

        sequence.Append(transform.DOJump(targetPosition, 1f, 1, 0.3f));
        sequence.OnComplete(() =>
        {
            isMovingToTarget = false;
            SetBodyRendererVisible(true);
            if (deactivateOnComplete)
            {
                gameObject.SetActive(false);
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
        if (useBatchedRendering)
        {
            batchRenderer = ResolveBatchRenderer();
            batchRenderer?.Register(this);
        }

        UpdateRendererVisibility();
    }

    private void OnDisable()
    {
        isMovingToTarget = false;
        UnregisterFromBatchRenderer();
    }

    private void OnDestroy()
    {
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

        TerrainGenerator generator = GetComponentInParent<TerrainGenerator>();
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

        bodyRenderer.enabled = gameObject.activeInHierarchy && !useBatchedRendering;
    }
}
