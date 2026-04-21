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
        flowDirection = Vector2Int.zero;

        Vector3 forward = rotation * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        forward = -forward.normalized;
        if (Mathf.Abs(forward.x) >= Mathf.Abs(forward.z))
        {
            flowDirection = new Vector2Int(forward.x >= 0f ? 1 : -1, 0);
        }
        else
        {
            flowDirection = new Vector2Int(0, forward.z >= 0f ? 1 : -1);
        }

        return true;
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
        inputDirection = Vector2Int.zero;
        if (!TryGetFlowDirection(rotation, out Vector2Int outputDirection) || outputDirection == Vector2Int.zero)
        {
            return false;
        }

        if (!IsCornerVariant)
        {
            inputDirection = -outputDirection;
            return true;
        }

        inputDirection = IsReverseCornerVariant
            ? RotateDirectionClockwise(outputDirection)
            : RotateDirectionCounterClockwise(outputDirection);
        return true;
    }

    public void HandlePlacementRotation()
    {
    }

    public static bool IsPerpendicular(Vector2Int left, Vector2Int right)
    {
        return left != Vector2Int.zero
               && right != Vector2Int.zero
               && ((left.x * right.x) + (left.y * right.y)) == 0;
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
