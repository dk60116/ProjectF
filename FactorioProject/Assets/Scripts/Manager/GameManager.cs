using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    private UIManager uiManager;
    private ItemManager itemManager;

    [SerializeField]
    private Player player;

    public bool InstallationPlacementActive { get; private set; }
    public bool MapEditActive { get; private set; }
    public bool PlayerInteractionLocked => InstallationPlacementActive || MapEditActive;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        ConfigureShadowQuality();
        ApplySceneShadowSettings();
        SceneManager.sceneLoaded += OnSceneLoaded;
        
        uiManager = GetComponentInChildren<UIManager>();
        itemManager = GetComponentInChildren<ItemManager>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
            Time.timeScale = 1f;
        else if (Input.GetKeyDown(KeyCode.Alpha2))
            Time.timeScale = 2f;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplySceneShadowSettings();
    }

    private void ConfigureShadowQuality()
    {
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.shadowProjection = ShadowProjection.StableFit;
        QualitySettings.shadowCascades = 4;

        if (QualitySettings.shadowDistance < 80f)
        {
            QualitySettings.shadowDistance = 80f;
        }
    }

    private void ApplySceneShadowSettings()
    {
        Renderer[] renderers = FindObjectsOfType<Renderer>(true);
        Light[] lights = FindObjectsOfType<Light>(true);

        foreach (Renderer rendererComponent in renderers)
        {
            rendererComponent.shadowCastingMode = ShadowCastingMode.On;
            rendererComponent.receiveShadows = true;
        }

        foreach (Light lightComponent in lights)
        {
            lightComponent.shadows = LightShadows.Soft;
            lightComponent.shadowStrength = 1f;
            lightComponent.shadowBias = 0.05f;
            lightComponent.shadowNormalBias = 0.4f;
            lightComponent.shadowNearPlane = 0.2f;
            lightComponent.shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;
        }
    }

    public UIManager UIManager => uiManager;
    public ItemManager ItemManger => itemManager;

    public Player Player => player;

    public void SetInstallationPlacementActive(bool isActive)
    {
        InstallationPlacementActive = isActive;
    }

    public void SetMapEditActive(bool isActive)
    {
        MapEditActive = isActive;
    }
}
