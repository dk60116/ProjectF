using UnityEngine;

public partial class PlayerHUD
{
    [SerializeField]
    private InteractionButton EatInteractionButton;
    [SerializeField]
    private Sprite eatInteractionIcon;

    private InteractionButton boundEatInteractionButton;

    private void ResolveEatInteractionButton()
    {
        ResolveParallelInteractionButton(
            ref EatInteractionButton,
            "EatInteractionButton",
            "EatButton");

        BindEatInteractionButton();
        UpdateInteractionButtonLayout();
    }

    private void BindEatInteractionButton()
    {
        if (EatInteractionButton == null
            || boundEatInteractionButton == EatInteractionButton)
        {
            return;
        }

        EatInteractionButton.SetClickAction(HandleEatInteractionButtonClicked);
        boundEatInteractionButton = EatInteractionButton;
    }

    private void SetEatInteractionButtonState(Player currentPlayer)
    {
        bool canEat = eatInteractionIcon != null
                      && TryResolveHeldItem(currentPlayer, out ItemDefinition heldDefinition)
                      && ItemDefinition.IsFoodEnergyItemDefinition(heldDefinition);

        SetParallelInteractionButtonState(
            EatInteractionButton,
            eatInteractionIcon,
            canEat);
        UpdateInteractionButtonLayout();
    }

    private void HandleEatInteractionButtonClicked()
    {
        if (IsPlacementOrMapEditModeActive()
            || GameManager.Instance == null
            || GameManager.Instance.PlayerInteractionLocked)
        {
            return;
        }

        Player currentPlayer = GameManager.Instance.Player;
        if (!TryResolveHeldItem(currentPlayer, out ItemDefinition heldDefinition)
            || !ItemDefinition.IsFoodEnergyItemDefinition(heldDefinition))
        {
            UpdateInteractionButtonState();
            return;
        }

        PlayerBag handBag = currentPlayer.GetHandBag();
        if (handBag == null
            || handBag.GetSlotItemId(0) != heldDefinition.id
            || !handBag.TryRemoveOneAtSlot(0, out int consumedItemId, false)
            || consumedItemId != heldDefinition.id)
        {
            UpdateInteractionButtonState();
            return;
        }

        TryGrantEatReward(currentPlayer, heldDefinition);
        UpdateInteractionButtonState();
        UpdateHandItemGauge();
    }

    private static void TryGrantEatReward(Player currentPlayer, ItemDefinition consumedDefinition)
    {
        if (currentPlayer == null
            || consumedDefinition == null
            || !consumedDefinition.TryGetEatReward(
                out ItemDefinition rewardDefinition,
                out float chancePercent)
            || Random.value > chancePercent * 0.01f)
        {
            return;
        }

        int rewardItemId = rewardDefinition.id;
        if (currentPlayer.TryAddToBag(rewardItemId, out _)
            || currentPlayer.TryAddToHand(rewardItemId, out _))
        {
            return;
        }

        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        Transform bodyTransform = currentPlayer.BodyTransform != null
            ? currentPlayer.BodyTransform
            : currentPlayer.transform;
        if (terrain != null && bodyTransform != null)
        {
            terrain.TryAddDroppedItemNear(bodyTransform.position, rewardItemId, out _);
        }
    }
}
