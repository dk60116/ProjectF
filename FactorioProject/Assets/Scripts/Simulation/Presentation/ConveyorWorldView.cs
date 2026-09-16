using UnityEngine;

[DisallowMultipleComponent, DefaultExecutionOrder(999)]
public sealed class ConveyorWorldView : MonoBehaviour
{
    private ConveyorWorld world;
    internal static ConveyorWorldView Create(ConveyorWorld owner, Transform parent, string hostName)
    {
        var root = new GameObject(hostName);
        root.transform.SetParent(parent, false);
        var view = root.AddComponent<ConveyorWorldView>(); view.world = owner;
        return view;
    }
    internal void Release()
    {
        world?.SuspendRendering(); world = null;
        gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }
    private void LateUpdate()
    {
        using var callerSample = MapObjectTickProfiler.SampleLateUpdateCaller<ConveyorWorldView>();
        world?.Render();
    }
    private void OnDisable() => world?.SuspendRendering();
    private void OnDestroy() => world?.OnViewDestroyed(this);
}
