using UnityEngine;
using UnityEngine.UI;
using PlantResource = ProjectF.MapObjects.Tree;

[DisallowMultipleComponent]
public sealed class PlantGrowthWorldGauge : MonoBehaviour
{
    private const float CanvasScale = 0.004f;
    private const float HeightOffset = 0.3f;
    private const float RequirementEpsilon = 0.0001f;
    private const float GaugeFillLerpSpeed = 8f;
    private const float GaugeFillSnapEpsilon = 0.001f;
    private const float UnfilledSaturationMultiplier = 0.25f;
    private const float UnfilledValueMultiplier = 0.45f;
    private static readonly Vector2 GaugeCanvasSize = new Vector2(72f, 72f);
    private static readonly Vector2 WaterGaugeSize = new Vector2(64f, 64f);
    private static readonly Vector2 FertilizerBorderSize = new Vector2(49f, 49f);
    private static readonly Vector2 FertilizerGaugeSize = new Vector2(45f, 45f);
    private static readonly Vector2 InnerBorderSize = new Vector2(30f, 30f);
    private const float CenterHoleDiameter = 26f;
    private static readonly Color OuterBackgroundColor =
        new Color(0.025f, 0.025f, 0.025f, 1f);
    private static readonly Color NoRequirementColor = Color.black;
    private static readonly Color WaterColor = new Color(0.08f, 0.48f, 1f, 1f);
    private static readonly Color FertilizerColor = new Color(0.16f, 0.82f, 0.28f, 1f);
    private static readonly Color TimeColor = Color.white;

    private static Material sharedOverlayMaterial;

    private PlantResource tree;
    private PlantGrowthRingGraphic primaryBackground;
    private PlantGrowthRingGraphic fertilizerBorder;
    private PlantGrowthRingGraphic fertilizerBackground;
    private PlantGrowthRingGraphic waterFill;
    private PlantGrowthRingGraphic fertilizerFill;
    private PlantGrowthRingGraphic timeFill;
    private Camera targetCamera;
    private bool renderedVisible;
    private float targetWaterFillAmount;
    private float targetFertilizerFillAmount;
    private float targetTimeFillAmount;

    public static PlantGrowthWorldGauge Create(PlantResource owner)
    {
        if (owner == null)
        {
            return null;
        }

        Transform existing = owner.transform.Find(nameof(PlantGrowthWorldGauge));
        if (existing != null)
        {
            PlantGrowthWorldGauge existingGauge =
                existing.GetComponent<PlantGrowthWorldGauge>();
            if (existingGauge != null)
            {
                existingGauge.Initialize(owner);
                return existingGauge;
            }
        }

        GameObject root = new GameObject(
            nameof(PlantGrowthWorldGauge),
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasGroup));
        root.transform.SetParent(owner.transform, false);

        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.sizeDelta = GaugeCanvasSize;
        rootRect.localScale = Vector3.one * CanvasScale;

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 1000;

        CreateRing(
            root.transform,
            "Outer Background",
            GaugeCanvasSize,
            OuterBackgroundColor,
            1f);

        PlantGrowthRingGraphic primaryBackground = CreateRing(
            root.transform,
            "Primary Background",
            WaterGaugeSize,
            ResolveUnfilledColor(WaterColor),
            1f);
        PlantGrowthRingGraphic water = CreateRing(
            root.transform,
            "Water Fill",
            WaterGaugeSize,
            WaterColor);
        PlantGrowthRingGraphic time = CreateRing(
            root.transform,
            "Growth Time Fill",
            WaterGaugeSize,
            TimeColor);
        PlantGrowthRingGraphic fertilizerBorder = CreateRing(
            root.transform,
            "Fertilizer Border",
            FertilizerBorderSize,
            Color.black,
            1f);
        PlantGrowthRingGraphic fertilizerBackground = CreateRing(
            root.transform,
            "Fertilizer Background",
            FertilizerGaugeSize,
            ResolveUnfilledColor(FertilizerColor),
            1f);
        PlantGrowthRingGraphic fertilizer = CreateRing(
            root.transform,
            "Fertilizer Fill",
            FertilizerGaugeSize,
            FertilizerColor);
        CreateRing(
            root.transform,
            "Inner Border",
            InnerBorderSize,
            Color.black,
            1f);

        PlantGrowthWorldGauge gauge = root.AddComponent<PlantGrowthWorldGauge>();
        gauge.primaryBackground = primaryBackground;
        gauge.fertilizerBorder = fertilizerBorder;
        gauge.fertilizerBackground = fertilizerBackground;
        gauge.waterFill = water;
        gauge.fertilizerFill = fertilizer;
        gauge.timeFill = time;
        gauge.Initialize(owner);
        return gauge;
    }

    public void Refresh()
    {
        if (tree == null)
        {
            SetVisible(false);
            return;
        }

        ResourceDefinition definition = tree.Definition;
        bool visible = definition != null
                       && definition.HasGrowthSchedule
                       && tree.CanGrowAnotherLevel
                       && tree.ResourceCount > 0;
        SetVisible(visible);
        if (!visible)
        {
            return;
        }

        ResolveReferences();
        float requiredWater = tree.RequiredGrowthWaterLiters;
        float requiredFertilizer = tree.RequiredGrowthFertilizerAmount;
        float waterRatio = requiredWater <= RequirementEpsilon
            ? 1f
            : tree.CurrentGrowthWaterLiters / requiredWater;
        float fertilizerRatio = requiredFertilizer <= RequirementEpsilon
            ? 1f
            : tree.CurrentGrowthFertilizerAmount / requiredFertilizer;
        bool requirementsMet = tree.AreCurrentGrowthRequirementsMet;
        float growthDuration = definition.GrowthDurationPerLevelSeconds;
        float timeRatio = requirementsMet && growthDuration > RequirementEpsilon
            ? tree.GrowthElapsedSeconds / growthDuration
            : 0f;

        Color waterGaugeColor = requiredWater <= RequirementEpsilon
            ? NoRequirementColor
            : WaterColor;
        Color fertilizerGaugeColor = requiredFertilizer <= RequirementEpsilon
            ? NoRequirementColor
            : FertilizerColor;
        waterFill.color = waterGaugeColor;
        fertilizerFill.color = fertilizerGaugeColor;
        primaryBackground.color = ResolveUnfilledColor(
            requirementsMet ? TimeColor : waterGaugeColor);
        fertilizerBackground.color = ResolveUnfilledColor(fertilizerGaugeColor);
        targetWaterFillAmount = Mathf.Clamp01(waterRatio);
        targetFertilizerFillAmount = Mathf.Clamp01(fertilizerRatio);
        targetTimeFillAmount = Mathf.Clamp01(timeRatio);
        waterFill.enabled = !requirementsMet;
        fertilizerBorder.enabled = !requirementsMet;
        fertilizerFill.enabled = !requirementsMet;
        fertilizerBackground.enabled = !requirementsMet;
        timeFill.enabled = requirementsMet;
        RefreshTransform();
    }

    public void Hide()
    {
        SetVisible(false);
    }

    private void Initialize(PlantResource owner)
    {
        tree = owner;
        ResolveReferences();
        Refresh();
    }

    private void ResolveReferences()
    {
        primaryBackground ??= transform.Find("Primary Background")
            ?.GetComponent<PlantGrowthRingGraphic>();
        fertilizerBorder ??= transform.Find("Fertilizer Border")
            ?.GetComponent<PlantGrowthRingGraphic>();
        fertilizerBackground ??= transform.Find("Fertilizer Background")
            ?.GetComponent<PlantGrowthRingGraphic>();
        waterFill ??= transform.Find("Water Fill")?.GetComponent<PlantGrowthRingGraphic>();
        fertilizerFill ??= transform.Find("Fertilizer Fill")?.GetComponent<PlantGrowthRingGraphic>();
        timeFill ??= transform.Find("Growth Time Fill")?.GetComponent<PlantGrowthRingGraphic>();
    }

    private void SetVisible(bool visible)
    {
        renderedVisible = visible;
        CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            return;
        }

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private void LateUpdate()
    {
        if (tree == null)
        {
            Destroy(gameObject);
            return;
        }

        if (renderedVisible)
        {
            UpdateGaugeFillAmounts(Time.deltaTime);
            RefreshTransform();
        }
    }

    private void UpdateGaugeFillAmounts(float deltaTime)
    {
        LerpGaugeFillUp(waterFill, targetWaterFillAmount, deltaTime);
        LerpGaugeFillUp(fertilizerFill, targetFertilizerFillAmount, deltaTime);
        LerpGaugeFillUp(timeFill, targetTimeFillAmount, deltaTime);
    }

    private static void LerpGaugeFillUp(
        PlantGrowthRingGraphic ring,
        float targetFillAmount,
        float deltaTime)
    {
        if (ring == null)
        {
            return;
        }

        float target = Mathf.Clamp01(targetFillAmount);
        if (target <= ring.FillAmount)
        {
            ring.FillAmount = target;
            return;
        }

        float lerpAmount = 1f - Mathf.Exp(
            -GaugeFillLerpSpeed * Mathf.Max(0f, deltaTime));
        float nextFillAmount = Mathf.Lerp(ring.FillAmount, target, lerpAmount);
        ring.FillAmount = target - nextFillAmount <= GaugeFillSnapEpsilon
            ? target
            : nextFillAmount;
    }

    private void RefreshTransform()
    {
        transform.position = tree.FocusPoint + Vector3.up * HeightOffset;
        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera != null)
        {
            transform.rotation = targetCamera.transform.rotation;
        }
    }

    private static Color ResolveUnfilledColor(Color fillColor)
    {
        Color.RGBToHSV(fillColor, out float hue, out float saturation, out float value);
        Color unfilledColor = Color.HSVToRGB(
            hue,
            saturation * UnfilledSaturationMultiplier,
            value * UnfilledValueMultiplier);
        unfilledColor.a = fillColor.a;
        return unfilledColor;
    }

    private static PlantGrowthRingGraphic CreateRing(
        Transform parent,
        string objectName,
        Vector2 size,
        Color color,
        float fillAmount = 0f)
    {
        GameObject ringObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(PlantGrowthRingGraphic));
        RectTransform rectTransform = ringObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = size;
        PlantGrowthRingGraphic ring = ringObject.GetComponent<PlantGrowthRingGraphic>();
        ConfigureRing(ring, color, fillAmount);
        return ring;
    }

    private static void ConfigureRing(
        PlantGrowthRingGraphic ring,
        Color color,
        float fillAmount)
    {
        ring.material = ResolveOverlayMaterial();
        ring.color = color;
        ring.CenterHoleDiameter = CenterHoleDiameter;
        ring.FillAmount = fillAmount;
        ring.raycastTarget = false;
    }

    private static Material ResolveOverlayMaterial()
    {
        if (sharedOverlayMaterial != null)
        {
            return sharedOverlayMaterial;
        }

        Shader shader = Shader.Find("UI/Default");
        if (shader == null)
        {
            return null;
        }

        sharedOverlayMaterial = new Material(shader)
        {
            name = "Generated Plant Growth Gauge Overlay Material",
            hideFlags = HideFlags.DontSave,
            renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay
        };
        sharedOverlayMaterial.SetInt(
            "unity_GUIZTestMode",
            (int)UnityEngine.Rendering.CompareFunction.Always);
        return sharedOverlayMaterial;
    }
}

[DisallowMultipleComponent]
internal sealed class PlantGrowthRingGraphic : MaskableGraphic
{
    private const int FullCircleSegmentCount = 64;

    private float centerHoleDiameter;
    private float fillAmount;

    public float CenterHoleDiameter
    {
        get => centerHoleDiameter;
        set
        {
            float nextValue = Mathf.Max(0f, value);
            if (Mathf.Approximately(centerHoleDiameter, nextValue))
            {
                return;
            }

            centerHoleDiameter = nextValue;
            SetVerticesDirty();
        }
    }

    public float FillAmount
    {
        get => fillAmount;
        set
        {
            float nextValue = Mathf.Clamp01(value);
            if (Mathf.Approximately(fillAmount, nextValue))
            {
                return;
            }

            fillAmount = nextValue;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        if (fillAmount <= 0f)
        {
            return;
        }

        Rect rect = rectTransform.rect;
        float outerRadius = Mathf.Max(0f, Mathf.Min(rect.width, rect.height) * 0.5f);
        float innerRadius = Mathf.Clamp(centerHoleDiameter * 0.5f, 0f, outerRadius);
        if (outerRadius <= innerRadius)
        {
            return;
        }

        int segmentCount = Mathf.Max(
            1,
            Mathf.CeilToInt(FullCircleSegmentCount * fillAmount));
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        Vector2 center = rect.center;

        for (int segmentIndex = 0; segmentIndex <= segmentCount; segmentIndex++)
        {
            float normalizedAngle = Mathf.Min(
                fillAmount,
                segmentIndex / (float)FullCircleSegmentCount);
            float angle = Mathf.PI * 0.5f - normalizedAngle * Mathf.PI * 2f;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            vertex.position = center + direction * innerRadius;
            vertexHelper.AddVert(vertex);
            vertex.position = center + direction * outerRadius;
            vertexHelper.AddVert(vertex);
        }

        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            int innerCurrent = segmentIndex * 2;
            int outerCurrent = innerCurrent + 1;
            int innerNext = innerCurrent + 2;
            int outerNext = innerCurrent + 3;
            vertexHelper.AddTriangle(innerCurrent, outerCurrent, outerNext);
            vertexHelper.AddTriangle(innerCurrent, outerNext, innerNext);
        }
    }
}
