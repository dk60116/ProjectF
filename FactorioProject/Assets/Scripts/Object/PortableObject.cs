using DG.Tweening;
using System;
using System.Collections;
using System.Collections.Generic;
using ProjectF.Attributes;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

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

    public int ItemId => id;
    
    public bool SetItem(int id)
    {
        this.id = id;

        if (body == null)
        {
            body = GetComponent<MeshFilter>();
        }

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

        body.mesh = portableMesh;
        body.GetComponent<MeshRenderer>().material = portableMat;
        return true;
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
        transform.DOKill();

        Sequence sequence = DOTween.Sequence();
        if (delay > 0f)
        {
            sequence.AppendInterval(delay);
        }

        sequence.Append(transform.DOJump(targetPosition, 1f, 1, 0.3f));
        sequence.OnComplete(() =>
        {
            if (deactivateOnComplete)
            {
                gameObject.SetActive(false);
            }

            onComplete?.Invoke();
        });
    }
}
