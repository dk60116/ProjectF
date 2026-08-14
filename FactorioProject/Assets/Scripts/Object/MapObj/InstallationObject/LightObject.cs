using UnityEngine;

public class LightObject : InstallationObject, IItemLightPowerStateProvider
{
    private static readonly int BaseMapPropertyId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexturePropertyId = Shader.PropertyToID("_MainTex");

    [SerializeField]
    protected Texture2D onTexture;
    [SerializeField]
    private Renderer textureRenderer;

    private ItemLightController observedLightController;
    private MaterialPropertyBlock texturePropertyBlock;

    protected override void OnEnable()
    {
        base.OnEnable();
        BindLightController();
        ApplyLightTexture(observedLightController != null && observedLightController.IsLightActive);
    }

    protected override void OnDisable()
    {
        ApplyLightTexture(false);
        UnbindLightController();
        base.OnDisable();
    }

    public bool TryGetElectricPowerRequirement(out float wattsPerSecond)
    {
        wattsPerSecond = ItemDefinition.ResolveElectricUseWatts(ResolveLightDefinition());
        return wattsPerSecond > 0.0001f;
    }

    public bool TryGetElectricPowerDemand(out float wattsPerSecond)
    {
        wattsPerSecond = 0f;
        return IsItemLightToggled
               && TryGetElectricPowerRequirement(out wattsPerSecond);
    }

    public void GetObjectInfoStatus(out string statusText, out bool isLit)
    {
        isLit = IsItemLightToggled && HasPowerForItemLight;
        statusText = !IsItemLightToggled
            ? "Off"
            : isLit
                ? "Working"
                : "No power";
    }

    public bool HasPowerForItemLight
    {
        get
        {
            ItemDefinition definition = ResolveLightDefinition();
            return definition == null
                   || definition.useEnergyType != ItemDefinition.EnergyType.Electricity
                   || UtilityPole.HasElectricityAvailable(this);
        }
    }

    private ItemDefinition ResolveLightDefinition()
    {
        return BoundItemDefinition != null
            ? BoundItemDefinition
            : InputOutputModule.ResolveItemDefinition(ResolveItemId());
    }

    protected override void OnItemLightToggleStateChanged(bool active)
    {
        BindLightController();
        ApplyLightTexture(observedLightController != null && observedLightController.IsLightActive);
    }

    private void BindLightController()
    {
        ItemLightController nextController = GetComponent<ItemLightController>();
        if (observedLightController == nextController)
        {
            return;
        }

        UnbindLightController();
        observedLightController = nextController;
        if (observedLightController != null)
        {
            observedLightController.LightActiveStateChanged += ApplyLightTexture;
        }
    }

    private void UnbindLightController()
    {
        if (observedLightController != null)
        {
            observedLightController.LightActiveStateChanged -= ApplyLightTexture;
            observedLightController = null;
        }
    }

    private void ApplyLightTexture(bool active)
    {
        Renderer targetRenderer = ResolveTextureRenderer();
        Material material = targetRenderer != null ? targetRenderer.sharedMaterial : null;
        if (material == null)
        {
            return;
        }

        int texturePropertyId = ResolveTexturePropertyId(material);
        if (texturePropertyId < 0)
        {
            return;
        }

        texturePropertyBlock ??= new MaterialPropertyBlock();
        targetRenderer.GetPropertyBlock(texturePropertyBlock);
        texturePropertyBlock.SetTexture(
            texturePropertyId,
            active && onTexture != null ? onTexture : material.GetTexture(texturePropertyId));
        targetRenderer.SetPropertyBlock(texturePropertyBlock);
    }

    private Renderer ResolveTextureRenderer()
    {
        if (textureRenderer != null)
        {
            return textureRenderer;
        }

        textureRenderer = GetComponentInChildren<MeshRenderer>(true);
        if (textureRenderer == null)
        {
            textureRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        return textureRenderer;
    }

    private static int ResolveTexturePropertyId(Material material)
    {
        if (material.HasProperty(BaseMapPropertyId))
        {
            return BaseMapPropertyId;
        }

        return material.HasProperty(MainTexturePropertyId)
            ? MainTexturePropertyId
            : -1;
    }
}
