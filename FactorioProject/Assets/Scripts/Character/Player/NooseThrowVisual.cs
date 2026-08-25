using System.Collections.Generic;
using UnityEngine;

public sealed class NooseThrowVisual : MonoBehaviour
{
    private const int TetherPointCount = 24;
    private const int LoopPointCount = 40;
    private const int TubeRadialSegmentCount = 24;
    private const float LoopRadius = 0.2f;
    private const float TetherTubeRadius = 0.02f;
    private const float LoopTubeRadius = 0.018f;
    private const float RopeTextureTilesPerMeter = 1.6f;
    private const float CapturedRopeLength = 2.4f;

    private readonly List<AnimalAIController> animalCandidates =
        new List<AnimalAIController>(32);
    private Transform tetherOrigin;
    private RopeTubeMesh tetherTube;
    private RopeTubeMesh loopTube;
    private Material tubeMaterial;
    private Vector3[] tetherPoints;
    private Vector3[] loopPoints;
    private Vector3 direction;
    private Vector3 loopRight;
    private float distance;
    private float windupDuration;
    private float outboundDuration;
    private float holdDuration;
    private float returnDuration;
    private float arcHeight;
    private float elapsed;
    private Vector3 previousLoopCenter;
    private bool hasPreviousLoopCenter;
    private Player ownerPlayer;
    private PlayerBag tetherBag;
    private int tetherItemId = -1;
    private bool retainsHandItem;
    private Animal leashedAnimal;
    private AnimalAIController leashedController;
    private bool attachedAnimalMoved;

    public bool HasAttachedAnimal =>
        leashedAnimal != null
        && leashedAnimal.IsAlive
        && leashedAnimal.gameObject.activeInHierarchy
        && leashedController != null
        && leashedController.IsNooseLeashed;
    public float AttachedMovementSpeedLimit => HasAttachedAnimal
        && attachedAnimalMoved
        ? leashedController.NooseMovementSpeed
        : float.PositiveInfinity;
    private float AttachedAnimalMovementSpeed => HasAttachedAnimal
        ? leashedController.NooseMovementSpeed
        : 0f;

    public bool TryGetAttachedAnimalId(out long deterministicId)
    {
        deterministicId = 0L;
        TerrainAnimalInstance instance = HasAttachedAnimal
            ? leashedController.TerrainInstance
            : null;
        if (instance == null || instance.DeterministicId == 0L)
        {
            return false;
        }

        deterministicId = instance.DeterministicId;
        return true;
    }

    public bool TryGetAttachedAnimal(out Animal animal)
    {
        animal = HasAttachedAnimal ? leashedAnimal : null;
        return animal != null;
    }

    public bool TryAttachExisting(
        Animal animal,
        AnimalAIController controller)
    {
        if (!AttachAnimal(animal, controller))
        {
            return false;
        }

        UpdateAttachedRope();
        return true;
    }

    public void ReleaseAttachment(bool consumeTetherItem = false)
    {
        bool hadRetainedTetherItem = retainsHandItem && tetherBag != null;
        bool shouldConsumeTetherItem = consumeTetherItem
                                       && hadRetainedTetherItem
                                       && tetherBag.GetSlotItemId(0) == tetherItemId
                                       && tetherBag.GetSlotCount(0) > 0;
        if (leashedController != null)
        {
            leashedController.SetNooseLeashed(false);
        }

        leashedAnimal = null;
        leashedController = null;
        attachedAnimalMoved = false;
        if (hadRetainedTetherItem)
        {
            tetherBag.ClearSlotMinimumRetainedCount(0);
        }

        retainsHandItem = false;
        if (shouldConsumeTetherItem)
        {
            tetherBag.TryRemoveOneAtSlot(0, out _, false);
        }

        if (hadRetainedTetherItem)
        {
            ownerPlayer?.UpdateCarryState();
        }
    }

    public void Initialize(
        Transform hand,
        int itemId,
        Vector3 throwDirection,
        Material material,
        float throwDistance,
        float windup,
        float outbound,
        float hold,
        float returnTime,
        float throwArcHeight)
    {
        tetherOrigin = hand;
        ownerPlayer = hand != null ? hand.GetComponentInParent<Player>() : null;
        tetherBag = hand != null ? hand.GetComponent<PlayerBag>() : null;
        tetherItemId = itemId;
        if (tetherBag != null
            && tetherBag.SetSlotMinimumRetainedCount(0, tetherItemId, 1))
        {
            retainsHandItem = tetherBag.TryHideRetainedSlotObject(0, tetherItemId);
            if (!retainsHandItem)
            {
                tetherBag.ClearSlotMinimumRetainedCount(0);
            }
        }

        ownerPlayer?.UpdateCarryState();
        direction = throwDirection;
        loopRight = Vector3.Cross(Vector3.up, direction).normalized;
        if (loopRight.sqrMagnitude <= 0.0001f)
        {
            loopRight = Vector3.right;
        }

        distance = Mathf.Max(0.1f, throwDistance);
        windupDuration = Mathf.Max(0f, windup);
        outboundDuration = Mathf.Max(0.01f, outbound);
        holdDuration = Mathf.Max(0f, hold);
        returnDuration = Mathf.Max(0.01f, returnTime);
        arcHeight = Mathf.Max(0f, throwArcHeight);

        tubeMaterial = CreateTubeMaterial(material);
        tetherPoints = new Vector3[TetherPointCount];
        loopPoints = new Vector3[LoopPointCount];
        tetherTube = new RopeTubeMesh(
            transform,
            "TetherTube",
            gameObject.layer,
            tubeMaterial,
            TetherPointCount,
            TubeRadialSegmentCount,
            TetherTubeRadius,
            false);
        loopTube = new RopeTubeMesh(
            transform,
            "NooseLoopTube",
            gameObject.layer,
            tubeMaterial,
            LoopPointCount,
            TubeRadialSegmentCount,
            LoopTubeRadius,
            true);
        SetTubeVisibility(false);
    }

    private void Update()
    {
        if (tetherOrigin == null)
        {
            ReleaseAttachment();
            Destroy(gameObject);
            return;
        }

        if (leashedAnimal != null || leashedController != null)
        {
            UpdateAttachedAnimal();
            return;
        }

        elapsed += Time.deltaTime;
        float trajectoryTime = elapsed - windupDuration;
        if (trajectoryTime < 0f)
        {
            return;
        }

        if (!loopTube.Visible)
        {
            SetTubeVisibility(true);
        }

        float normalizedDistance;
        if (trajectoryTime <= outboundDuration)
        {
            normalizedDistance = Mathf.SmoothStep(0f, 1f, trajectoryTime / outboundDuration);
        }
        else if (trajectoryTime <= outboundDuration + holdDuration)
        {
            normalizedDistance = 1f;
        }
        else
        {
            float returnElapsed = trajectoryTime - outboundDuration - holdDuration;
            if (returnElapsed >= returnDuration)
            {
                Destroy(gameObject);
                return;
            }

            normalizedDistance = 1f - Mathf.SmoothStep(0f, 1f, returnElapsed / returnDuration);
        }

        Vector3 origin = ResolveOrigin();
        Vector3 loopCenter = origin + direction * (distance * normalizedDistance);
        loopCenter.y += Mathf.Sin(normalizedDistance * Mathf.PI) * arcHeight;
        if (!hasPreviousLoopCenter)
        {
            previousLoopCenter = origin;
            hasPreviousLoopCenter = true;
        }

        if (TryAttachAnimal(previousLoopCenter, loopCenter))
        {
            UpdateAttachedAnimal();
            return;
        }

        previousLoopCenter = loopCenter;
        UpdateLoop(loopCenter);

        UpdateTether(origin, loopCenter - direction * LoopRadius, normalizedDistance);
    }

    private void UpdateAttachedAnimal()
    {
        if (!HasAttachedAnimal)
        {
            ReleaseAttachment();
            Destroy(gameObject);
            return;
        }

        Vector3 origin = ResolveOrigin();
        float pullSpeed = AttachedAnimalMovementSpeed;
        if (ownerPlayer != null)
        {
            pullSpeed = Mathf.Min(
                pullSpeed,
                Mathf.Max(0f, ownerPlayer.Stat.currentMoveSpeed));
        }

        attachedAnimalMoved = leashedController.TryPullNooseToward(
            origin,
            CapturedRopeLength,
            pullSpeed,
            Time.deltaTime);

        UpdateAttachedRope();
    }

    private void UpdateAttachedRope()
    {
        if (!HasAttachedAnimal || tetherOrigin == null)
        {
            return;
        }

        if (!tetherTube.Visible)
        {
            tetherTube.SetVisible(true);
        }

        if (loopTube.Visible)
        {
            loopTube.SetVisible(false);
        }

        Vector3 origin = ResolveOrigin();
        Vector3 loopCenter = leashedAnimal.GetWorldCenter();
        Vector3 attachedDirection = loopCenter - origin;
        attachedDirection.y = 0f;
        if (attachedDirection.sqrMagnitude > 0.0001f)
        {
            direction = attachedDirection.normalized;
            loopRight = Vector3.Cross(Vector3.up, direction).normalized;
        }

        UpdateTether(origin, loopCenter, 1f);
    }

    private bool TryAttachAnimal(Vector3 sweepStart, Vector3 sweepEnd)
    {
        AnimalAIWorld world = AnimalAIWorld.Instance;
        if (world == null)
        {
            return false;
        }

        world.CopyControllers(animalCandidates, false);
        Vector3 sweepMovement = sweepEnd - sweepStart;
        sweepMovement.y = 0f;
        float sweepLengthSqr = sweepMovement.sqrMagnitude;

        Animal closestAnimal = null;
        AnimalAIController closestController = null;
        float closestDistanceSqr = float.PositiveInfinity;
        for (int i = 0; i < animalCandidates.Count; i++)
        {
            AnimalAIController candidateController = animalCandidates[i];
            Animal candidateAnimal = candidateController != null
                ? candidateController.Animal
                : null;

            if (candidateAnimal == null
                || candidateController == null
                || !candidateController.IsConfigured
                || candidateController.IsNooseLeashed
                || !candidateAnimal.IsAlive
                || !candidateAnimal.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 animalPosition = candidateController.SimulationPosition;
            Vector3 fromSweepStart = animalPosition - sweepStart;
            fromSweepStart.y = 0f;
            float sweepT = sweepLengthSqr > 0.000001f
                ? Mathf.Clamp01(Vector3.Dot(fromSweepStart, sweepMovement) / sweepLengthSqr)
                : 0f;
            Vector3 closestPathPosition = sweepStart + sweepMovement * sweepT;
            Vector3 pathOffset = animalPosition - closestPathPosition;
            pathOffset.y = 0f;
            float catchRadius = LoopRadius + candidateController.AvoidanceColliderRadius;
            if (pathOffset.sqrMagnitude > catchRadius * catchRadius)
            {
                continue;
            }

            Vector3 endOffset = animalPosition - sweepEnd;
            endOffset.y = 0f;
            float candidateDistanceSqr = endOffset.sqrMagnitude;
            if (candidateDistanceSqr >= closestDistanceSqr)
            {
                continue;
            }

            closestDistanceSqr = candidateDistanceSqr;
            closestAnimal = candidateAnimal;
            closestController = candidateController;
        }

        animalCandidates.Clear();

        return AttachAnimal(closestAnimal, closestController);
    }

    private bool AttachAnimal(
        Animal animal,
        AnimalAIController controller)
    {
        if (animal == null
            || controller == null
            || !animal.IsAlive
            || !animal.gameObject.activeInHierarchy
            || tetherBag == null
            || !retainsHandItem)
        {
            return false;
        }

        if (tetherItemId < 0
            || tetherBag.GetSlotItemId(0) != tetherItemId
            || tetherBag.GetSlotCount(0) <= 0
            || !controller.SetNooseLeashed(true))
        {
            return false;
        }

        leashedAnimal = animal;
        leashedController = controller;
        attachedAnimalMoved = false;
        return true;
    }

    private void UpdateTether(Vector3 start, Vector3 end, float normalizedDistance)
    {
        float span = Vector3.Distance(start, end);
        float bend = Mathf.Lerp(0.14f, 0.055f, normalizedDistance);
        float sag = Mathf.Min(0.1f, span * 0.08f);
        float phase = elapsed * 7f;

        for (int i = 0; i < TetherPointCount; i++)
        {
            float t = i / (TetherPointCount - 1f);
            float envelope = Mathf.Sin(t * Mathf.PI);
            float sway = Mathf.Sin(phase + t * Mathf.PI * 2f) * 0.025f;
            tetherPoints[i] = Vector3.Lerp(start, end, t)
                              + loopRight * (envelope * (bend + sway))
                              + Vector3.down * (envelope * sag);
        }

        tetherTube.UpdatePath(tetherPoints);
    }

    private void UpdateLoop(Vector3 center)
    {
        float angleStep = Mathf.PI * 2f / LoopPointCount;
        for (int i = 0; i < LoopPointCount; i++)
        {
            float angle = angleStep * i;
            loopPoints[i] = center
                            + loopRight * (Mathf.Cos(angle) * LoopRadius)
                            + direction * (Mathf.Sin(angle) * LoopRadius);
        }

        loopTube.UpdatePath(loopPoints);
    }

    private void SetTubeVisibility(bool visible)
    {
        tetherTube.SetVisible(visible);
        loopTube.SetVisible(visible);
    }

    private Vector3 ResolveOrigin()
    {
        return tetherOrigin.position;
    }

    private void OnDestroy()
    {
        ReleaseAttachment();
        ownerPlayer = null;
        tetherBag = null;
        tetherTube?.Dispose();
        loopTube?.Dispose();
        if (tubeMaterial != null)
        {
            Destroy(tubeMaterial);
        }
    }

    private static Material CreateTubeMaterial(Material sourceMaterial)
    {
        Material result = new Material(sourceMaterial)
        {
            name = sourceMaterial.name + "_NooseTubeRuntime"
        };

        Color ropeColor = sourceMaterial.HasProperty("_BaseColor")
            ? sourceMaterial.GetColor("_BaseColor")
            : new Color(0.65f, 0.38f, 0.15f, 1f);
        if (result.HasProperty("_BaseColor"))
        {
            result.SetColor(
                "_BaseColor",
                new Color(ropeColor.r * 1.12f, ropeColor.g * 1.12f, ropeColor.b * 1.12f, ropeColor.a));
        }

        if (result.HasProperty("_ShadowColor"))
        {
            result.SetColor(
                "_ShadowColor",
                new Color(ropeColor.r * 0.7f, ropeColor.g * 0.7f, ropeColor.b * 0.7f, 1f));
        }

        SetMaterialFloat(result, "_ShadeThreshold", 0.52f);
        SetMaterialFloat(result, "_ShadeSmoothness", 0.075f);
        SetMaterialFloat(result, "_UseSpecular", 1f);
        SetMaterialFloat(result, "_SpecularIntensity", 0.18f);
        SetMaterialFloat(result, "_SpecularPower", 24f);
        SetMaterialFloat(result, "_SpecularThreshold", 0.55f);
        SetMaterialFloat(result, "_SpecularSmoothness", 0.04f);
        if (result.HasProperty("_SpecularColor"))
        {
            result.SetColor("_SpecularColor", Color.Lerp(ropeColor, Color.white, 0.45f));
        }

        return result;
    }

    private static void SetMaterialFloat(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private sealed class RopeTubeMesh
    {
        private readonly Mesh mesh;
        private readonly MeshRenderer meshRenderer;
        private readonly Vector3[] vertices;
        private readonly Vector3[] normals;
        private readonly Vector2[] uvs;
        private readonly int pointCount;
        private readonly int radialSegmentCount;
        private readonly float radius;
        private readonly bool closedPath;

        public RopeTubeMesh(
            Transform parent,
            string objectName,
            int layer,
            Material material,
            int pathPointCount,
            int tubeSegmentCount,
            float tubeRadius,
            bool closePath)
        {
            pointCount = pathPointCount;
            radialSegmentCount = tubeSegmentCount;
            radius = tubeRadius;
            closedPath = closePath;

            GameObject tubeObject = new GameObject(objectName);
            tubeObject.layer = layer;
            tubeObject.transform.SetParent(parent, false);

            mesh = new Mesh
            {
                name = objectName + "_RuntimeMesh"
            };
            mesh.MarkDynamic();

            vertices = new Vector3[pointCount * radialSegmentCount];
            normals = new Vector3[vertices.Length];
            uvs = new Vector2[vertices.Length];
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                float pathV = pointCount > 1 ? pointIndex / (pointCount - 1f) : 0f;
                for (int radialIndex = 0; radialIndex < radialSegmentCount; radialIndex++)
                {
                    int vertexIndex = pointIndex * radialSegmentCount + radialIndex;
                    uvs[vertexIndex] = new Vector2(radialIndex / (float)radialSegmentCount, pathV);
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = BuildTriangles();

            MeshFilter meshFilter = tubeObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;
            meshRenderer = tubeObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.receiveShadows = true;
        }

        public bool Visible => meshRenderer != null && meshRenderer.enabled;

        public void SetVisible(bool visible)
        {
            if (meshRenderer != null)
            {
                meshRenderer.enabled = visible;
            }
        }

        public void UpdatePath(Vector3[] pathPoints)
        {
            if (pathPoints == null || pathPoints.Length < pointCount)
            {
                return;
            }

            float angleStep = Mathf.PI * 2f / radialSegmentCount;
            float pathDistance = 0f;
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                if (pointIndex > 0)
                {
                    pathDistance += Vector3.Distance(pathPoints[pointIndex - 1], pathPoints[pointIndex]);
                }

                Vector3 tangent = ResolveTangent(pathPoints, pointIndex);
                Vector3 frameRight = Vector3.Cross(tangent, Vector3.up);
                if (frameRight.sqrMagnitude <= 0.0001f)
                {
                    frameRight = Vector3.Cross(tangent, Vector3.forward);
                }

                frameRight.Normalize();
                Vector3 frameUp = Vector3.Cross(frameRight, tangent).normalized;
                int vertexOffset = pointIndex * radialSegmentCount;
                for (int radialIndex = 0; radialIndex < radialSegmentCount; radialIndex++)
                {
                    float angle = angleStep * radialIndex;
                    Vector3 radialDirection = frameRight * Mathf.Cos(angle) + frameUp * Mathf.Sin(angle);
                    int vertexIndex = vertexOffset + radialIndex;
                    vertices[vertexIndex] = pathPoints[pointIndex] + radialDirection * radius;
                    normals[vertexIndex] = radialDirection;
                    uvs[vertexIndex] = new Vector2(
                        radialIndex / (float)radialSegmentCount,
                        pathDistance * RopeTextureTilesPerMeter);
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.RecalculateBounds();
        }

        public void Dispose()
        {
            if (mesh != null)
            {
                Object.Destroy(mesh);
            }
        }

        private Vector3 ResolveTangent(Vector3[] pathPoints, int pointIndex)
        {
            int previousIndex = pointIndex - 1;
            int nextIndex = pointIndex + 1;
            if (closedPath)
            {
                previousIndex = (previousIndex + pointCount) % pointCount;
                nextIndex %= pointCount;
            }
            else
            {
                previousIndex = Mathf.Max(0, previousIndex);
                nextIndex = Mathf.Min(pointCount - 1, nextIndex);
            }

            Vector3 tangent = pathPoints[nextIndex] - pathPoints[previousIndex];
            return tangent.sqrMagnitude > 0.000001f ? tangent.normalized : Vector3.forward;
        }

        private int[] BuildTriangles()
        {
            int pathSegmentCount = closedPath ? pointCount : pointCount - 1;
            int[] triangles = new int[pathSegmentCount * radialSegmentCount * 6];
            int triangleIndex = 0;
            for (int pointIndex = 0; pointIndex < pathSegmentCount; pointIndex++)
            {
                int nextPointIndex = (pointIndex + 1) % pointCount;
                for (int radialIndex = 0; radialIndex < radialSegmentCount; radialIndex++)
                {
                    int nextRadialIndex = (radialIndex + 1) % radialSegmentCount;
                    int current = pointIndex * radialSegmentCount + radialIndex;
                    int currentNext = pointIndex * radialSegmentCount + nextRadialIndex;
                    int next = nextPointIndex * radialSegmentCount + radialIndex;
                    int nextNext = nextPointIndex * radialSegmentCount + nextRadialIndex;

                    triangles[triangleIndex++] = current;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = currentNext;
                    triangles[triangleIndex++] = currentNext;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = nextNext;
                }
            }

            return triangles;
        }
    }
}
