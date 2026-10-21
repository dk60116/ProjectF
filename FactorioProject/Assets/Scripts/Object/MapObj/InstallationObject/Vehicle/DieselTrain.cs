using UnityEngine;

public class DieselTrain : Locomotive
{
    [SerializeField] private ItemDefinition dieselCan;
    [SerializeField] private GameObject exhaustPrefab;
    [SerializeField] private Vector3 exhaustLocalPosition = new Vector3(0f, 0.7f, 0.15f);
    [SerializeField, Min(0.01f)] private float exhaustScale = 0.15f;
    [SerializeField] private Vector3 headlightLocalPosition = new Vector3(0f, 0.25f, 0.5f);
    [SerializeField, Min(0.1f)] private float headlightRange = 6f;
    private GameObject exhaustInstance;
    private Light headlight;

    public override bool UsesWaterSupply => false;
    public override ItemDefinition FuelDisplayDefinition => IsFuelDisplayDefinition(dieselCan) ? dieselCan : null;
    protected override ItemDefinition.EnergyType PropulsionEnergyType => ItemDefinition.EnergyType.Diesel;
    protected override bool NeedsConfiguredFuelUse => true;
    protected override bool AcceptsPropulsionFuel(ItemDefinition definition) =>
        dieselCan != null && definition == dieselCan && !definition.isFluid && base.AcceptsPropulsionFuel(definition);

    protected override void OnEnable()
    {
        // Create once per pooled view, before the shared visual cache is captured.
        if (Application.isPlaying) EnsureDriveEffects();
        base.OnEnable();
    }

    private void EnsureDriveEffects()
    {
        if (particleEffect == null && exhaustInstance == null && exhaustPrefab != null)
        {
            exhaustInstance = Instantiate(exhaustPrefab, transform, false);
            exhaustInstance.name = "Diesel Exhaust";
            exhaustInstance.transform.localPosition = exhaustLocalPosition;
            exhaustInstance.transform.localScale = Vector3.one * exhaustScale;
            particleEffect = exhaustInstance.GetComponentInChildren<ParticleSystem>(true);
        }
        if (headlight == null)
        {
            var holder = new GameObject("Diesel Headlight");
            holder.transform.SetParent(transform, false);
            holder.transform.localPosition = headlightLocalPosition;
            holder.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            headlight = holder.AddComponent<Light>();
            headlight.type = LightType.Spot;
            headlight.range = headlightRange;
            headlight.spotAngle = 65f;
            headlight.intensity = 2f;
            headlight.color = new Color(1f, 0.88f, 0.65f);
            headlight.shadows = LightShadows.None;
        }
        headlight.enabled = false;
    }

    protected override void SetMovementParticleActive(bool isMoving)
    {
        base.SetMovementParticleActive(isMoving);
        if (headlight != null) headlight.enabled = isMoving && ShouldUpdateVisuals;
    }
}
