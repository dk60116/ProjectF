using UnityEngine;

/// <summary>
/// Optional presentation bridge. Stable floor, bag and conveyor items render directly from
/// PortableObjectWorld; this component is only retained for individual outline/light views.
/// </summary>
[DisallowMultipleComponent]
public sealed class PortableObjectView : MonoBehaviour
{
    private PortableObject owner;
    private MeshFilter body;
    private MeshRenderer bodyRenderer;

    public PortableObject Owner => owner;
    public MeshFilter Body => body;
    public MeshRenderer BodyRenderer => bodyRenderer;

    public void Bind(PortableObject entity)
    {
        if (ReferenceEquals(owner, entity))
        {
            return;
        }

        owner?.DetachView(this);
        owner = entity;
        body = GetComponent<MeshFilter>();
        if (body == null)
        {
            body = GetComponentInChildren<MeshFilter>(true);
        }

        bodyRenderer = body != null ? body.GetComponent<MeshRenderer>() : null;
        if (bodyRenderer == null)
        {
            bodyRenderer = GetComponentInChildren<MeshRenderer>(true);
        }

        owner?.AttachView(this);
    }

    public void RefreshFromEntity()
    {
        owner?.ApplyStateToView(this);
    }

    private void LateUpdate()
    {
        owner?.SyncStateFromView(this);
    }

    private void OnDestroy()
    {
        PortableObject previousOwner = owner;
        owner = null;
        previousOwner?.DetachView(this);
    }
}
