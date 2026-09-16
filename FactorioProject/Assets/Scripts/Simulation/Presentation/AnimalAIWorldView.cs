using UnityEngine;
using ProjectF.Rendering;
using ProjectF.Animals;

[DisallowMultipleComponent]
public sealed class AnimalAIWorldView : MonoBehaviour
{
    private AnimalAIWorld world;
    private Camera presentationCamera;
    private readonly CameraRenderCulling presentationCulling = new CameraRenderCulling();
    internal static AnimalAIWorldView Create(AnimalAIWorld owner, Transform parent)
    {
        var root = new GameObject("AnimalAIWorldView");
        root.transform.SetParent(parent, false);
        var view = root.AddComponent<AnimalAIWorldView>(); view.world = owner; return view;
    }
    internal void Release()
    {
        world = null;
        gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }
    private void OnDestroy() => world?.OnViewDestroyed(this);
    private void Update()
    {
        using var callerSample = MapObjectTickProfiler.SampleUpdateCaller<AnimalAIWorldView>();
        if (world == null || MapObjectTickManager.SimulationPaused || MapObjectTickManager.WaitingForWorldLoad) return;
        using var sample = MapObjectTickProfiler.SampleNamed("AI Render", "AnimalAI", "Animal Presentation");
        if (presentationCamera == null || !presentationCamera.isActiveAndEnabled) presentationCamera = Camera.main;
        presentationCulling.Update(presentationCamera);
        var controllers = world.Controllers;
        for (int i = 0; i < controllers.Count; i++)
        {
            AnimalAIController controller = controllers[i];
            if (controller == null || !controller.HasPendingPresentation) continue;
            bool visible = controller.IsPresentationVisible(presentationCulling);
            controller.TickCulledPresentation(Time.deltaTime, visible);
            AnimalAIProfiler.Add(visible ? AnimalAIProfiler.Counter.Presentations : AnimalAIProfiler.Counter.CulledPresentations);
        }
    }
    private void LateUpdate()
    {
        using var callerSample = MapObjectTickProfiler.SampleLateUpdateCaller<AnimalAIWorldView>();
        world?.CompletePresentationFrame();
    }
}
