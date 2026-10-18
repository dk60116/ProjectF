using ProjectF.Combat;
using UnityEngine;

public partial class PlayerHUD
{
    private RangedAttackRangeView rangedAttackRangeView;
    private Player rangedAttackRangeOwner;

    private void UpdateRangedAttackRange()
    {
        GameManager manager = GameManager.Instance;
        Player player = manager != null ? manager.Player : null;
        if (player == null || manager.PlayerInteractionLocked || MapObjectTickManager.WaitingForWorldLoad
            || SaveManager.GameplayInputBlocked || !player.TryGetRangedAttackRange(currentObjectInfoTarget, out float range))
        {
            rangedAttackRangeView?.Hide();
            return;
        }
        if (rangedAttackRangeOwner != player)
        {
            ReleaseRangedAttackRange();
            rangedAttackRangeOwner = player;
            rangedAttackRangeView = new RangedAttackRangeView(player.transform);
        }
        rangedAttackRangeView.Show(player.transform.position + Vector3.up * .05f, range);
    }

    private void ReleaseRangedAttackRange()
    {
        rangedAttackRangeView?.Dispose();
        rangedAttackRangeView = null;
        rangedAttackRangeOwner = null;
    }
}
