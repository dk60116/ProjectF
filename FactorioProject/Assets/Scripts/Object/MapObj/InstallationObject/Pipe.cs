using UnityEngine;

public enum PipeVariantKind
{
    Straight = 0,
    Corner = 1,
    Tee = 2,
    Cross = 3
}

public class Pipe : InstallationObject
{
    [SerializeField]
    private Pipe straightVariantPrefab;
    [SerializeField]
    private Pipe cornerVariantPrefab;
    [SerializeField]
    private Pipe teeVariantPrefab;
    [SerializeField]
    private Pipe crossVariantPrefab;
    [SerializeField]
    private PipeVariantKind variantKind = PipeVariantKind.Straight;
    [SerializeField]
    private InstallationFacingDirection localStraightDirection = InstallationFacingDirection.PositiveZ;
    [SerializeField]
    private InstallationFacingDirection localCornerFirstDirection = InstallationFacingDirection.NegativeX;
    [SerializeField]
    private InstallationFacingDirection localCornerSecondDirection = InstallationFacingDirection.NegativeZ;
    [SerializeField]
    private InstallationFacingDirection localTeeFirstDirection = InstallationFacingDirection.NegativeX;
    [SerializeField]
    private InstallationFacingDirection localTeeSecondDirection = InstallationFacingDirection.PositiveX;
    [SerializeField]
    private InstallationFacingDirection localTeeThirdDirection = InstallationFacingDirection.NegativeZ;

    public Pipe StraightVariantPrefab => straightVariantPrefab != null ? straightVariantPrefab : this;
    public Pipe CornerVariantPrefab => cornerVariantPrefab;
    public Pipe TeeVariantPrefab => teeVariantPrefab;
    public Pipe CrossVariantPrefab => crossVariantPrefab;
    public PipeVariantKind VariantKind => variantKind;
    public int VariantKindId => (int)variantKind;
    public bool IsCornerVariant => variantKind == PipeVariantKind.Corner;
    public bool IsTeeVariant => variantKind == PipeVariantKind.Tee;
    public bool IsCrossVariant => variantKind == PipeVariantKind.Cross;

    public bool HasConnectionTowards(Quaternion rotation, Vector2Int direction)
    {
        if (direction == Vector2Int.zero)
        {
            return false;
        }

        switch (variantKind)
        {
            case PipeVariantKind.Cross:
                return direction == Vector2Int.up
                       || direction == Vector2Int.right
                       || direction == Vector2Int.down
                       || direction == Vector2Int.left;
            case PipeVariantKind.Tee:
                return HasResolvedConnection(rotation, localTeeFirstDirection, direction)
                       || HasResolvedConnection(rotation, localTeeSecondDirection, direction)
                       || HasResolvedConnection(rotation, localTeeThirdDirection, direction);
            case PipeVariantKind.Corner:
                return HasResolvedConnection(rotation, localCornerFirstDirection, direction)
                       || HasResolvedConnection(rotation, localCornerSecondDirection, direction);
            default:
                return HasResolvedConnection(rotation, localStraightDirection, direction)
                       || HasResolvedConnection(rotation, OppositeFacingDirection(localStraightDirection), direction);
        }
    }

    private static bool HasResolvedConnection(
        Quaternion rotation,
        InstallationFacingDirection localDirection,
        Vector2Int direction)
    {
        return TryResolveDirection(rotation, localDirection, out Vector2Int resolvedDirection)
               && resolvedDirection == direction;
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
        if (variantKind == PipeVariantKind.Straight && straightVariantPrefab == null)
        {
            straightVariantPrefab = this;
        }
    }
#endif
}
