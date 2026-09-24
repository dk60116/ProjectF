public partial class PlayerHUD
{
    private WorkableCraftingPanel workableCraftingPanel;

    private void ToggleWorkableCraftingPanel(WorkableObject target)
    {
        if (target == null)
        {
            CloseWorkableCraftingPanel();
            return;
        }

        CollapseExpandedBagSlot(true);
        HideFilterPanelsImmediate();
        if (workableCraftingPanel == null)
        {
            workableCraftingPanel = WorkableCraftingPanel.Create(this);
        }

        workableCraftingPanel?.Toggle(target);
    }

    private void CloseWorkableCraftingPanel()
    {
        workableCraftingPanel?.Close();
    }
}
