using ProjectF.Presentation;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AnimalWorldHealthBar : MonoBehaviour
{
    private Animal animal;
    private WorldHealthBarView view;
    private Camera targetCamera;
    private float heightOffset = 1f;

    public static AnimalWorldHealthBar Create(Animal animal, Renderer modelRenderer)
    {
        if (animal == null) return null;
        TerrainAnimalInstance terrainInstance = animal.GetComponentInParent<TerrainAnimalInstance>();
        Transform parent = terrainInstance != null ? terrainInstance.transform
            : animal.transform.parent != null ? animal.transform.parent : animal.transform;
        var view = new WorldHealthBarView(parent, "Animal Health Bar");
        var healthBar = view.Root.gameObject.AddComponent<AnimalWorldHealthBar>();
        healthBar.animal = animal;
        healthBar.view = view;
        if (modelRenderer != null)
            healthBar.heightOffset = Mathf.Max(.5f, modelRenderer.bounds.max.y - animal.transform.position.y + .25f);
        healthBar.Refresh();
        return healthBar;
    }

    public void NotifyAttackAnimationStarted()
    {
        if (animal == null || !animal.IsAlive) return;
        view.NotifyAttackAnimationStarted(true, animal.NormalizedHealth);
        Refresh();
    }

    public void HideImmediately() => view?.HideImmediately();

    public void Refresh()
    {
        if (animal == null) { HideImmediately(); return; }
        if (targetCamera == null || !targetCamera.isActiveAndEnabled) targetCamera = Camera.main;
        view?.Refresh(animal.IsAlive, animal.NormalizedHealth, animal.transform.position, heightOffset, targetCamera);
    }

    private void LateUpdate()
    {
        using var callerSample = MapObjectTickProfiler.SampleLateUpdateCaller<AnimalWorldHealthBar>();
        Refresh();
    }
}
