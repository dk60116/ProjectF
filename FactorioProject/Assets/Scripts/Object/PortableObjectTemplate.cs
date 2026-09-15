using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class PortableTemplateStack
{
    public List<PortableObjectTemplate> stack;
}

/// <summary>
/// Authoring-only marker for portable item anchors and the shared portable prefab.
/// Runtime item state is created in PortableObjectWorld.
/// </summary>
[DisallowMultipleComponent]
public sealed class PortableObjectTemplate : MonoBehaviour
{
    [SerializeField]
    private MeshFilter body;
    private PortableObject runtimeEntity;

    public MeshFilter Body => body != null ? body : GetComponentInChildren<MeshFilter>(true);

    public PortableObject CreateEntity(bool useAuthoringObjectAsView = false)
    {
        if (useAuthoringObjectAsView && runtimeEntity != null && runtimeEntity.IsAlive)
        {
            return runtimeEntity;
        }

        PortableObject entity = PortableObject.Create(
            transform.position,
            transform.rotation,
            transform.lossyScale,
            gameObject.layer,
            gameObject.name);
        if (useAuthoringObjectAsView)
        {
            runtimeEntity = entity;
            entity.SetCachedParent(transform.parent, true);
            PortableObjectView view = GetComponent<PortableObjectView>();
            if (view == null)
            {
                view = gameObject.AddComponent<PortableObjectView>();
            }

            view.Bind(entity);
            enabled = false;
        }

        return entity;
    }

    private void OnDestroy()
    {
        runtimeEntity?.ReleaseForAuthoringObjectDestroy();
        runtimeEntity = null;
    }
}
