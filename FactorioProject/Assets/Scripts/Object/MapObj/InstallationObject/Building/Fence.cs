using UnityEngine;

public enum FenceVariantKind
{
    Straight = 0,
    Corner = 1,
    TriCorner = 2,
    Cross = 3
}

public class Fence : Building
{
    [SerializeField]
    private Fence straightVariantPrefab;
    [SerializeField]
    private Fence cornerVariantPrefab;
    [SerializeField]
    private Fence triCornerVariantPrefab;
    [SerializeField]
    private Fence crossVariantPrefab;
    [SerializeField]
    private bool isCornerVariant;
    [SerializeField]
    private FenceVariantKind variantKind = FenceVariantKind.Straight;
    [SerializeField]
    private InstallationFacingDirection localStraightDirection = InstallationFacingDirection.PositiveX;
    [SerializeField]
    private InstallationFacingDirection localCornerFirstDirection = InstallationFacingDirection.PositiveX;
    [SerializeField]
    private InstallationFacingDirection localCornerSecondDirection = InstallationFacingDirection.PositiveZ;
    [SerializeField]
    private InstallationFacingDirection localTriCornerFirstDirection = InstallationFacingDirection.PositiveX;
    [SerializeField]
    private InstallationFacingDirection localTriCornerSecondDirection = InstallationFacingDirection.PositiveZ;
    [SerializeField]
    private InstallationFacingDirection localTriCornerThirdDirection = InstallationFacingDirection.NegativeX;

    public Fence StraightVariantPrefab => straightVariantPrefab != null ? straightVariantPrefab : this;
    public Fence CornerVariantPrefab => cornerVariantPrefab;
    public Fence TriCornerVariantPrefab => triCornerVariantPrefab;
    public Fence CrossVariantPrefab => crossVariantPrefab;
    public FenceVariantKind VariantKind => ResolveVariantKind();
    public int VariantKindId => (int)VariantKind;
    public bool IsCornerVariant => VariantKind == FenceVariantKind.Corner;
    public bool IsTriCornerVariant => VariantKind == FenceVariantKind.TriCorner;
    public bool IsCrossVariant => VariantKind == FenceVariantKind.Cross;

    public bool TryGetConnectionDirections(Quaternion rotation, out Vector2Int firstDirection, out Vector2Int secondDirection)
    {
        firstDirection = Vector2Int.zero;
        secondDirection = Vector2Int.zero;

        switch (VariantKind)
        {
            case FenceVariantKind.Cross:
                firstDirection = Vector2Int.right;
                secondDirection = Vector2Int.up;
                return true;
            case FenceVariantKind.TriCorner:
                return TryResolveDirection(rotation, localTriCornerFirstDirection, out firstDirection)
                       && TryResolveDirection(rotation, localTriCornerSecondDirection, out secondDirection)
                       && DirectionsAreDistinct(firstDirection, secondDirection);
            case FenceVariantKind.Corner:
                return TryResolveDirection(rotation, localCornerFirstDirection, out firstDirection)
                       && TryResolveDirection(rotation, localCornerSecondDirection, out secondDirection)
                       && DirectionsAreDistinct(firstDirection, secondDirection)
                       && firstDirection != -secondDirection;
        }

        if (!TryResolveDirection(rotation, localStraightDirection, out firstDirection)
            || firstDirection == Vector2Int.zero)
        {
            return false;
        }

        secondDirection = -firstDirection;
        return true;
    }

    public bool HasConnectionTowards(Quaternion rotation, Vector2Int direction)
    {
        if (direction == Vector2Int.zero)
        {
            return false;
        }

        switch (VariantKind)
        {
            case FenceVariantKind.Cross:
                return direction == Vector2Int.up
                       || direction == Vector2Int.right
                       || direction == Vector2Int.down
                       || direction == Vector2Int.left;
            case FenceVariantKind.TriCorner:
                return HasResolvedConnection(rotation, localTriCornerFirstDirection, direction)
                       || HasResolvedConnection(rotation, localTriCornerSecondDirection, direction)
                       || HasResolvedConnection(rotation, localTriCornerThirdDirection, direction);
            case FenceVariantKind.Corner:
                return HasResolvedConnection(rotation, localCornerFirstDirection, direction)
                       || HasResolvedConnection(rotation, localCornerSecondDirection, direction);
            default:
                return HasResolvedConnection(rotation, localStraightDirection, direction)
                       || HasResolvedConnection(rotation, OppositeFacingDirection(localStraightDirection), direction);
        }
    }

    private FenceVariantKind ResolveVariantKind()
    {
        if (variantKind != FenceVariantKind.Straight)
        {
            return variantKind;
        }

        return isCornerVariant ? FenceVariantKind.Corner : FenceVariantKind.Straight;
    }

    private static bool HasResolvedConnection(
        Quaternion rotation,
        InstallationFacingDirection localDirection,
        Vector2Int direction)
    {
        return TryResolveDirection(rotation, localDirection, out Vector2Int resolvedDirection)
               && resolvedDirection == direction;
    }

    private static bool DirectionsAreDistinct(Vector2Int firstDirection, Vector2Int secondDirection)
    {
        return firstDirection != Vector2Int.zero
               && secondDirection != Vector2Int.zero
               && firstDirection != secondDirection;
    }

    private static InstallationFacingDirection OppositeFacingDirection(InstallationFacingDirection direction)
    {
        switch (direction)
        {
            case InstallationFacingDirection.PositiveX:
                return InstallationFacingDirection.NegativeX;
            case InstallationFacingDirection.NegativeX:
                return InstallationFacingDirection.PositiveX;
            case InstallationFacingDirection.NegativeZ:
                return InstallationFacingDirection.PositiveZ;
            default:
                return InstallationFacingDirection.NegativeZ;
        }
    }

    private static bool TryResolveDirection(
        Quaternion rotation,
        InstallationFacingDirection localDirection,
        out Vector2Int resolvedDirection)
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

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (isCornerVariant && variantKind == FenceVariantKind.Straight)
        {
            variantKind = FenceVariantKind.Corner;
        }

        if (VariantKind == FenceVariantKind.Straight && straightVariantPrefab == null)
        {
            straightVariantPrefab = this;
        }
    }
#endif
}
