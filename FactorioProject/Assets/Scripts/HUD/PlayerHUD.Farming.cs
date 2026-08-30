using UnityEngine;

public partial class PlayerHUD
{
    [SerializeField]
    private Sprite seedInteractionIcon;

    private bool seedGroundInteractionActive;
    private ItemDefinition currentSeedGroundDefinition;

    private bool TryActivateSeedGroundInteraction(
        Player currentPlayer,
        PlayerController playerController)
    {
        if (playerController == null
            || !playerController.TryGetSeedGroundInteractionBlock(
                out _,
                out ItemDefinition selectedSeedDefinition)
            || !TryResolveHeldItem(currentPlayer, out ItemDefinition seedDefinition)
            || !ItemDefinition.IsPlantableSeedDefinition(seedDefinition)
            || selectedSeedDefinition != seedDefinition)
        {
            return false;
        }

        Sprite icon = seedInteractionIcon;
        if (icon == null
            && seedDefinition.interactionButtonList != null
            && seedDefinition.interactionButtonList.Count > 0)
        {
            icon = seedDefinition.interactionButtonList[0];
        }

        if (icon == null || InteractionButton == null)
        {
            ClearContextInteractionButtonState();
            return true;
        }

        ClearInteractionTargets();
        seedGroundInteractionActive = true;
        currentSeedGroundDefinition = seedDefinition;
        SetActiveInteractionButton(InteractionButton, icon);
        return true;
    }
}
