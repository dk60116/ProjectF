using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AnimalHerdDebugRenderer : MonoBehaviour
{
    private const int CircleSegments = 64;
    private const float RefreshInterval = 0.2f;
    private const float HerdLineHeight = 0.08f;
    private const float PathLineHeight = 0.14f;

    private sealed class HerdVisual
    {
        public GameObject gameObject;
        public LineRenderer line;
        public int seenGeneration;
    }

    private readonly Dictionary<long, HerdVisual> visuals = new Dictionary<long, HerdVisual>();
    private readonly List<AnimalAIController> controllers = new List<AnimalAIController>();
    private readonly HashSet<long> displayedHerdAreas = new HashSet<long>();
    private readonly Vector3[] focusedPathPoints =
        new Vector3[AnimalAIController.MaxRemainingNavigationPathPoints];

    private Material lineMaterial;
    private LineRenderer focusedPathLine;
    private float refreshTimer;
    private int generation;
    private bool visible;

    private void OnDestroy()
    {
        ClearAnimalTints();
        if (lineMaterial != null)
        {
            Destroy(lineMaterial);
        }
    }

    public void SetVisible(bool value)
    {
        if (visible == value)
        {
            return;
        }

        visible = value;
        refreshTimer = 0f;
        if (!visible)
        {
            foreach (HerdVisual visual in visuals.Values)
            {
                if (visual?.gameObject != null)
                {
                    visual.gameObject.SetActive(false);
                }
            }

            ClearAnimalTints();
            HideFocusedPath();
        }
    }

    private void Update()
    {
        if (!visible)
        {
            return;
        }

        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer > 0f)
        {
            return;
        }

        refreshTimer = RefreshInterval;
        RefreshVisuals();
    }

    private void RefreshVisuals()
    {
        AnimalAIWorld world = AnimalAIWorld.Instance;
        if (world == null)
        {
            return;
        }

        generation++;
        ClearAnimalTints();
        displayedHerdAreas.Clear();
        world.CopyControllers(controllers, true);
        ApplyAnimalTints();
        AnimalAIController focusedController = FindFocusedController();
        if (focusedController != null)
        {
            ShowFocusedController(focusedController);
        }
        else
        {
            ShowAllHerdAreas();
            HideFocusedPath();
        }

        HideUnseenHerdVisuals();
    }

    private AnimalAIController FindFocusedController()
    {
        if (!TryGetFocusedAnimal(out Animal focusedAnimal))
        {
            return null;
        }

        for (int i = 0; i < controllers.Count; i++)
        {
            AnimalAIController controller = controllers[i];
            if (controller != null
                && controller.IsConfigured
                && controller.Animal == focusedAnimal)
            {
                return controller;
            }
        }

        return null;
    }

    private static bool TryGetFocusedAnimal(out Animal focusedAnimal)
    {
        PlayerHUD playerHUD = UIManager.Instance != null
            ? UIManager.Instance.PlayerHUD
            : null;
        focusedAnimal = null;
        return playerHUD != null
               && playerHUD.TryGetObjectInfoFocusedAnimal(out focusedAnimal);
    }

    private void ShowFocusedController(AnimalAIController controller)
    {
        long herdId = controller.HerdId;
        Color color = GetHerdColor(herdId);
        ShowHerdArea(controller, herdId, color);
        UpdateFocusedPath(controller, color);
    }

    private void ApplyAnimalTints()
    {
        for (int i = 0; i < controllers.Count; i++)
        {
            AnimalAIController controller = controllers[i];
            if (controller == null || !controller.IsConfigured)
            {
                continue;
            }

            controller.Animal?.SetHerdDebugColor(
                GetHerdColor(controller.HerdId),
                true);
        }
    }

    private void ShowAllHerdAreas()
    {
        for (int i = 0; i < controllers.Count; i++)
        {
            AnimalAIController controller = controllers[i];
            if (controller == null || !controller.IsConfigured)
            {
                continue;
            }

            long herdId = controller.HerdId;
            Color color = GetHerdColor(herdId);
            if (!displayedHerdAreas.Add(herdId))
            {
                continue;
            }

            ShowHerdArea(controller, herdId, color);
        }
    }

    private void ShowHerdArea(
        AnimalAIController controller,
        long herdId,
        Color color)
    {
        HerdVisual visual = GetOrCreateVisual(herdId, color);
        visual.seenGeneration = generation;
        visual.gameObject.SetActive(true);
        UpdateCircle(
            visual.line,
            controller.HerdAreaCenter + Vector3.up * HerdLineHeight,
            controller.HerdAreaRadius);
    }

    private void HideUnseenHerdVisuals()
    {
        foreach (HerdVisual visual in visuals.Values)
        {
            if (visual != null
                && visual.seenGeneration != generation
                && visual.gameObject != null)
            {
                visual.gameObject.SetActive(false);
            }
        }
    }

    private void UpdateFocusedPath(
        AnimalAIController controller,
        Color herdColor)
    {
        int pointCount = controller.CopyRemainingNavigationPath(focusedPathPoints);
        if (pointCount < 2)
        {
            HideFocusedPath();
            return;
        }

        LineRenderer line = GetOrCreateFocusedPathLine();
        if (line == null)
        {
            return;
        }

        Color pathColor = Color.Lerp(herdColor, Color.white, 0.7f);
        pathColor.a = 1f;
        line.startColor = pathColor;
        line.endColor = pathColor;
        line.positionCount = pointCount;
        for (int i = 0; i < pointCount; i++)
        {
            Vector3 point = focusedPathPoints[i];
            point.y += PathLineHeight;
            line.SetPosition(i, point);
        }

        line.gameObject.SetActive(true);
    }

    private LineRenderer GetOrCreateFocusedPathLine()
    {
        if (focusedPathLine != null)
        {
            return focusedPathLine;
        }

        GameObject pathObject = new GameObject("Focused Animal Movement Path");
        pathObject.transform.SetParent(transform, false);
        focusedPathLine = pathObject.AddComponent<LineRenderer>();
        focusedPathLine.useWorldSpace = true;
        focusedPathLine.loop = false;
        focusedPathLine.positionCount = 0;
        focusedPathLine.startWidth = 0.12f;
        focusedPathLine.endWidth = 0.12f;
        focusedPathLine.numCornerVertices = 2;
        focusedPathLine.numCapVertices = 2;
        focusedPathLine.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        focusedPathLine.receiveShadows = false;
        focusedPathLine.sharedMaterial = GetLineMaterial();
        return focusedPathLine;
    }

    private void HideFocusedPath()
    {
        if (focusedPathLine != null)
        {
            focusedPathLine.gameObject.SetActive(false);
        }
    }

    private HerdVisual GetOrCreateVisual(long herdId, Color color)
    {
        if (visuals.TryGetValue(herdId, out HerdVisual visual) && visual?.line != null)
        {
            return visual;
        }

        GameObject visualObject = new GameObject($"Animal Herd Area {herdId}");
        visualObject.transform.SetParent(transform, false);
        LineRenderer line = visualObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = CircleSegments;
        line.startWidth = 0.08f;
        line.endWidth = 0.08f;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = GetLineMaterial();
        line.startColor = color;
        line.endColor = color;

        visual = new HerdVisual
        {
            gameObject = visualObject,
            line = line,
            seenGeneration = generation
        };
        visuals[herdId] = visual;
        return visual;
    }

    private Material GetLineMaterial()
    {
        if (lineMaterial != null)
        {
            return lineMaterial;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            return null;
        }

        lineMaterial = new Material(shader)
        {
            name = "Animal Herd Debug Line"
        };
        return lineMaterial;
    }

    private static void UpdateCircle(LineRenderer line, Vector3 center, float radius)
    {
        for (int i = 0; i < CircleSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / CircleSegments;
            line.SetPosition(
                i,
                new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y,
                    center.z + Mathf.Sin(angle) * radius));
        }
    }

    private void ClearAnimalTints()
    {
        AnimalAIWorld world = AnimalAIWorld.Instance;
        if (world == null)
        {
            return;
        }

        world.CopyControllers(controllers, false);
        for (int i = 0; i < controllers.Count; i++)
        {
            controllers[i]?.Animal?.SetHerdDebugColor(Color.white, false);
        }
    }

    private static Color GetHerdColor(long herdId)
    {
        unchecked
        {
            uint hash = (uint)herdId ^ (uint)(herdId >> 32);
            hash ^= hash << 13;
            hash ^= hash >> 17;
            hash ^= hash << 5;
            float hue = (hash & 0xFFFFu) / 65535f;
            Color color = Color.HSVToRGB(hue, 0.8f, 1f);
            color.a = 0.9f;
            return color;
        }
    }
}
