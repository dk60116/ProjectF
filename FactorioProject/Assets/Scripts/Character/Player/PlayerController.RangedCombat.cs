using UnityEngine;

public partial class PlayerController
{
    public void FaceRangedTarget(Vector3 point)
    {
        CancelKnifeAttack();
        CancelActiveResourceHarvest();
        CancelActiveCorpseHarvest();
        Vector3 direction = point - (player.BodyTransform != null ? player.BodyTransform.position : transform.position);
        direction.y = 0f;
        if (direction.sqrMagnitude <= .0001f) return;
        pendingFacingDirection = direction.normalized;
        hasPendingFacingDirection = true;
        RotateBodyTowards(pendingFacingDirection);
    }
}
