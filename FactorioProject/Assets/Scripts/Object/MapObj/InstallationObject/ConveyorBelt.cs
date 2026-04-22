using UnityEngine;

public class ConveyorBelt : InstallationObject
{
    private static readonly int UvScrollXShaderId = Shader.PropertyToID("_UVScrollX");
    private static readonly int UvScrollYShaderId = Shader.PropertyToID("_UVScrollY");

    [SerializeField]
    private ConveyorBelt straightVariantPrefab;
    [SerializeField]
    private ConveyorBelt cornerVariantPrefab;
    [SerializeField]
    private ConveyorBelt reverseCornerVariantPrefab;
    [SerializeField]
    private bool isCornerVariant;
    [SerializeField]
    private bool isReverseCornerVariant;
    [SerializeField]
    private InstallationFacingDirection localOutputDirection = InstallationFacingDirection.NegativeZ;
    [SerializeField]
    private InstallationFacingDirection localInputDirection = InstallationFacingDirection.PositiveZ;
    [SerializeField, Min(0f)]
    private float conveyorSpeed = 1f;
    [SerializeField]
    private MeshRenderer beltTopRenderer;

    private MaterialPropertyBlock beltTopPropertyBlock;
    private float lastAppliedUvScrollY = float.NaN;

    public float ConveyorSpeed => Mathf.Max(0f, conveyorSpeed);
    public ConveyorBelt StraightVariantPrefab => straightVariantPrefab != null ? straightVariantPrefab : this;
    public ConveyorBelt CornerVariantPrefab => cornerVariantPrefab != null ? cornerVariantPrefab : this;
    public ConveyorBelt ReverseCornerVariantPrefab => reverseCornerVariantPrefab != null ? reverseCornerVariantPrefab : CornerVariantPrefab;
    public bool IsCornerVariant => isCornerVariant || isReverseCornerVariant;
    public bool IsReverseCornerVariant => isReverseCornerVariant;
    public int PlacementRotationQuarterTurnOffset => IsCornerVariant ? 3 : 0;

    public static bool TryGetFlowDirection(Quaternion rotation, out Vector2Int flowDirection)
    {
        return TryResolveCardinalDirection(rotation * Vector3.forward, out flowDirection);
    }

    public static Vector2Int RotateDirectionClockwise(Vector2Int direction)
    {
        return new Vector2Int(direction.y, -direction.x);
    }

    public static Vector2Int RotateDirectionCounterClockwise(Vector2Int direction)
    {
        return new Vector2Int(-direction.y, direction.x);
    }

    public bool TryGetInputDirection(Quaternion rotation, out Vector2Int inputDirection)
    {
        return TryResolveDirection(rotation, localInputDirection, out inputDirection);
    }

    public bool TryGetOutputDirection(Quaternion rotation, out Vector2Int outputDirection)
    {
        return TryResolveDirection(rotation, localOutputDirection, out outputDirection);
    }

    public ConveyorBelt GetPlacementPreviewVariantPrefab(bool useCornerVariant)
    {
        if (useCornerVariant)
        {
            return CornerVariantPrefab != null ? CornerVariantPrefab : this;
        }

        return StraightVariantPrefab != null ? StraightVariantPrefab : this;
    }

    public void HandlePlacementRotation(ref int quarterTurns, ref bool useCornerVariant, bool canUseCornerVariantAfterTurn)
    {
        quarterTurns = ((quarterTurns % 4) + 4) % 4;

        if (useCornerVariant)
        {
            useCornerVariant = false;
            return;
        }

        quarterTurns = (quarterTurns + 1) % 4;
        useCornerVariant = canUseCornerVariantAfterTurn;
    }

    public static bool IsPerpendicular(Vector2Int left, Vector2Int right)
    {
        return left != Vector2Int.zero
               && right != Vector2Int.zero
               && ((left.x * right.x) + (left.y * right.y)) == 0;
    }

    private bool TryResolveDirection(Quaternion rotation, InstallationFacingDirection localDirection, out Vector2Int resolvedDirection)
    {
        return TryResolveCardinalDirection(rotation * FacingDirectionToVector(localDirection), out resolvedDirection);
    }

    private static bool TryResolveCardinalDirection(Vector3 directionVector, out Vector2Int resolvedDirection)
    {
        resolvedDirection = Vector2Int.zero;

        directionVector.y = 0f;
        if (directionVector.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        directionVector.Normalize();
        if (Mathf.Abs(directionVector.x) >= Mathf.Abs(directionVector.z))
        {
            resolvedDirection = new Vector2Int(directionVector.x >= 0f ? 1 : -1, 0);
        }
        else
        {
            resolvedDirection = new Vector2Int(0, directionVector.z >= 0f ? 1 : -1);
        }

        return true;
    }

    private static Vector3 FacingDirectionToVector(InstallationFacingDirection direction)
    {
        switch (direction)
        {
            case InstallationFacingDirection.PositiveX:
                return Vector3.right;
            case InstallationFacingDirection.NegativeX:
                return Vector3.left;
            case InstallationFacingDirection.NegativeZ:
                return Vector3.back;
            default:
                return Vector3.forward;
        }
    }

    protected override InstallationFacingDirection ResolveInstalledDirection(Quaternion rotation)
    {
        if (TryGetInputDirection(rotation, out Vector2Int inputDirection))
        {
            return ToFacingDirection(inputDirection);
        }

        if (TryGetOutputDirection(rotation, out Vector2Int outputDirection))
        {
            return ToFacingDirection(outputDirection);
        }

        return base.ResolveInstalledDirection(rotation);
    }

    private static InstallationFacingDirection ToFacingDirection(Vector2Int direction)
    {
        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
        {
            return direction.x >= 0
                ? InstallationFacingDirection.PositiveX
                : InstallationFacingDirection.NegativeX;
        }

        return direction.y >= 0
            ? InstallationFacingDirection.PositiveZ
            : InstallationFacingDirection.NegativeZ;
    }

    protected new void Awake()
    {
        base.Awake();
        ResolveBeltTopRenderer();
        ApplyBeltTopScroll();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ResolveBeltTopRenderer();
        ApplyBeltTopScroll();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        ApplyBeltTopScroll();
    }

    private void ResolveBeltTopRenderer()
    {
        if (beltTopRenderer != null)
        {
            return;
        }

        Transform beltTopTransform = transform.Find("BeltTop");
        if (beltTopTransform != null)
        {
            beltTopRenderer = beltTopTransform.GetComponent<MeshRenderer>();
        }

        if (beltTopRenderer == null)
        {
            MeshRenderer[] childRenderers = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < childRenderers.Length; i++)
            {
                MeshRenderer candidate = childRenderers[i];
                if (candidate != null && candidate.name == "BeltTop")
                {
                    beltTopRenderer = candidate;
                    break;
                }
            }
        }
    }

    private void ApplyBeltTopScroll()
    {
        ResolveBeltTopRenderer();
        if (beltTopRenderer == null)
        {
            return;
        }

        float targetUvScrollY = -ConveyorSpeed * 0.75f;
        if (!float.IsNaN(lastAppliedUvScrollY) && Mathf.Approximately(lastAppliedUvScrollY, targetUvScrollY))
        {
            return;
        }

        if (beltTopPropertyBlock == null)
        {
            beltTopPropertyBlock = new MaterialPropertyBlock();
        }

        beltTopRenderer.GetPropertyBlock(beltTopPropertyBlock);
        beltTopPropertyBlock.SetFloat(UvScrollXShaderId, 0f);
        beltTopPropertyBlock.SetFloat(UvScrollYShaderId, targetUvScrollY);
        beltTopRenderer.SetPropertyBlock(beltTopPropertyBlock);
        lastAppliedUvScrollY = targetUvScrollY;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();

        if (conveyorSpeed < 0f)
        {
            conveyorSpeed = 0f;
        }

        ResolveBeltTopRenderer();
        ApplyBeltTopScroll();
    }
#endif
}
