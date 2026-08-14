using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UpgradeButton : MonoBehaviour
{
    [SerializeField]
    private Button button;
    [SerializeField]
    private RectTransform upgradeSetsRoot;
    [SerializeField]
    private CraftingSlot upgradeSetTemplate;
    [SerializeField, Min(80f)]
    private float upgradeSetVerticalSpacing = 100f;
    [SerializeField, Min(0.05f)]
    private float ingredientRefreshInterval = 0.2f;

    private readonly List<ItemDefinition> upgradeDefinitions = new List<ItemDefinition>();
    private readonly List<CraftingSlot> upgradeSets = new List<CraftingSlot>();
    private InstallationObject focusedObject;
    private ItemDefinition focusedDefinition;
    private InstallationPlacementController placementController;
    private bool isExpanded;
    private float nextIngredientRefreshTime;

    private void Awake()
    {
        CacheReferences();
        BindButton();
        Clear();
    }

    private void OnDisable()
    {
        HideUpgradeSets();
    }

    private void Update()
    {
        if (!isExpanded || Time.unscaledTime < nextIngredientRefreshTime)
        {
            return;
        }

        nextIngredientRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, ingredientRefreshInterval);
        for (int i = 0; i < upgradeDefinitions.Count && i < upgradeSets.Count; i++)
        {
            upgradeSets[i]?.RefreshIngredientsIfVisible();
        }
    }

    public void Bind(
        MapObject target,
        bool openedByDirectClick,
        InstallationPlacementController controller)
    {
        CacheReferences();
        placementController = controller != null
            ? controller
            : placementController;

        if (!openedByDirectClick
            || !(target is InstallationObject installationObject)
            || !installationObject.gameObject.activeInHierarchy
            || !TryResolveFocusedDefinition(installationObject, out ItemDefinition sourceDefinition))
        {
            Clear();
            return;
        }

        bool targetChanged = !ReferenceEquals(focusedObject, installationObject);
        focusedObject = installationObject;
        focusedDefinition = sourceDefinition;
        CollectUpgradeDefinitions(sourceDefinition);
        if (targetChanged || upgradeDefinitions.Count <= 0)
        {
            HideUpgradeSets();
        }

        SetButtonVisible(upgradeDefinitions.Count > 0);
    }

    public void Clear()
    {
        CacheReferences();
        focusedObject = null;
        focusedDefinition = null;
        upgradeDefinitions.Clear();
        HideUpgradeSets();
        SetButtonVisible(false);
    }

    private void HandleButtonClicked()
    {
        if (isExpanded)
        {
            HideUpgradeSets();
            return;
        }

        if (focusedObject == null || upgradeDefinitions.Count <= 0)
        {
            Clear();
            return;
        }

        ShowUpgradeSets();
    }

    private void ShowUpgradeSets()
    {
        CacheReferences();
        if (upgradeSetsRoot == null || upgradeSetTemplate == null)
        {
            return;
        }

        if (!upgradeSetsRoot.gameObject.activeSelf)
        {
            upgradeSetsRoot.gameObject.SetActive(true);
        }

        EnsureUpgradeSetCount(upgradeDefinitions.Count);
        float centerOffset = (upgradeDefinitions.Count - 1) * 0.5f;
        for (int i = 0; i < upgradeSets.Count; i++)
        {
            CraftingSlot slot = upgradeSets[i];
            if (slot == null)
            {
                continue;
            }

            if (i >= upgradeDefinitions.Count)
            {
                slot.ClearExternalCreateAction();
                slot.HideImmediate();
                continue;
            }

            ItemDefinition targetDefinition = upgradeDefinitions[i];
            slot.SetItem(targetDefinition.id, 1, 0);
            slot.ConfigureExternalCreateAction(
                HandleUpgradeRequested,
                focusedDefinition.id,
                1);
            slot.ShowImmediate(new Vector2(
                0f,
                (centerOffset - i) * Mathf.Max(80f, upgradeSetVerticalSpacing)));
            slot.ShowIngredientsForExternalUse();
        }

        isExpanded = true;
        nextIngredientRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, ingredientRefreshInterval);
    }

    private void HideUpgradeSets()
    {
        isExpanded = false;
        nextIngredientRefreshTime = 0f;
        for (int i = 0; i < upgradeSets.Count; i++)
        {
            CraftingSlot slot = upgradeSets[i];
            if (slot == null)
            {
                continue;
            }

            slot.ClearExternalCreateAction();
            slot.HideImmediate();
        }

        if (upgradeSetsRoot != null && upgradeSetsRoot.gameObject.activeSelf)
        {
            upgradeSetsRoot.gameObject.SetActive(false);
        }
    }

    private bool HandleUpgradeRequested(int targetItemId)
    {
        if (focusedObject == null || focusedDefinition == null)
        {
            Clear();
            return false;
        }

        ItemDefinition targetDefinition = null;
        for (int i = 0; i < upgradeDefinitions.Count; i++)
        {
            ItemDefinition candidate = upgradeDefinitions[i];
            if (candidate != null && candidate.id == targetItemId)
            {
                targetDefinition = candidate;
                break;
            }
        }

        if (targetDefinition == null
            || !IsUpgradeChildOf(targetDefinition, focusedDefinition))
        {
            return false;
        }

        if (placementController == null)
        {
            placementController = GetComponentInParent<InstallationPlacementController>();
            if (placementController == null)
            {
                placementController = FindObjectOfType<InstallationPlacementController>();
            }
        }

        InstallationObject previousObject = focusedObject;
        if (placementController == null
            || !placementController.TryUpgradeInstalledObject(
                previousObject,
                targetDefinition,
                out InstallationObject upgradedObject))
        {
            return false;
        }

        PlayerHUD playerHud = GetComponentInParent<PlayerHUD>();
        if (playerHud != null)
        {
            playerHud.ReplaceFocusedObjectAfterUpgrade(previousObject, upgradedObject);
        }
        else
        {
            Clear();
        }

        return true;
    }

    private void CollectUpgradeDefinitions(ItemDefinition sourceDefinition)
    {
        upgradeDefinitions.Clear();
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        List<ItemDefinition> definitions = itemManager != null
            ? itemManager.ItemDefinitions
            : null;
        if (definitions == null)
        {
            return;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDefinition candidate = definitions[i];
            if (candidate == null
                || candidate == sourceDefinition
                || !candidate.upgradeable
                || !(candidate.mapObject is InstallationObject)
                || !IsUpgradeChildOf(candidate, sourceDefinition))
            {
                continue;
            }

            upgradeDefinitions.Add(candidate);
        }

        upgradeDefinitions.Sort(CompareUpgradeDefinitions);
    }

    private static bool IsUpgradeChildOf(
        ItemDefinition candidate,
        ItemDefinition sourceDefinition)
    {
        if (candidate == null
            || sourceDefinition == null
            || !(candidate.mapObject is InputOutputModule inputOutputModule))
        {
            return false;
        }

        ItemDefinition parentDefinition = inputOutputModule.ParentInputOutputModuleItem;
        return parentDefinition != null
               && (parentDefinition == sourceDefinition
                   || (parentDefinition.id >= 0
                       && parentDefinition.id == sourceDefinition.id));
    }

    private static int CompareUpgradeDefinitions(ItemDefinition left, ItemDefinition right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        int idCompare = left.id.CompareTo(right.id);
        return idCompare != 0
            ? idCompare
            : string.Compare(
                ItemDefinitionLookup.GetDisplayName(left),
                ItemDefinitionLookup.GetDisplayName(right),
                StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveFocusedDefinition(
        InstallationObject installationObject,
        out ItemDefinition definition)
    {
        definition = null;
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : null;
        if (installationObject == null || itemManager == null)
        {
            return false;
        }

        definition = ItemDefinitionLookup.ResolveInstallationById(
            itemManager.ItemDefinitions,
            installationObject.ResolveItemId());
        return definition != null;
    }

    private void EnsureUpgradeSetCount(int requiredCount)
    {
        if (upgradeSetTemplate == null || upgradeSetsRoot == null)
        {
            return;
        }

        if (upgradeSets.Count <= 0)
        {
            upgradeSets.Add(upgradeSetTemplate);
        }

        while (upgradeSets.Count < requiredCount)
        {
            CraftingSlot clone = Instantiate(upgradeSetTemplate, upgradeSetsRoot);
            clone.name = $"UpgradeSet_{upgradeSets.Count + 1}";
            upgradeSets.Add(clone);
        }
    }

    private void CacheReferences()
    {
        if (button == null)
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button candidate = buttons[i];
                if (candidate != null
                    && candidate.GetComponent<CraftingSlot>() == null
                    && string.Equals(candidate.name, "Upgrade Button", StringComparison.OrdinalIgnoreCase))
                {
                    button = candidate;
                    break;
                }
            }
        }

        if (upgradeSetTemplate == null)
        {
            upgradeSetTemplate = GetComponentInChildren<CraftingSlot>(true);
        }

        if (upgradeSetsRoot == null && upgradeSetTemplate != null)
        {
            upgradeSetsRoot = upgradeSetTemplate.transform.parent as RectTransform;
        }

        if (upgradeSetTemplate != null && upgradeSets.Count <= 0)
        {
            upgradeSets.Add(upgradeSetTemplate);
        }
    }

    private void BindButton()
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(HandleButtonClicked);
        button.onClick.AddListener(HandleButtonClicked);
    }

    private void SetButtonVisible(bool visible)
    {
        if (button == null)
        {
            return;
        }

        if (button.gameObject.activeSelf != visible)
        {
            button.gameObject.SetActive(visible);
        }

        button.interactable = visible;
    }
}
