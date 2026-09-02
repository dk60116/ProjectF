using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class ObjectInfoPanel : MonoBehaviour
{
    private const float LayoutRefreshInterval = 0.5f;

    [SerializeField, HideInInspector]
    [FormerlySerializedAs("focusedObjectSlot")]
    private ItemSlot focusedObjectSlot;
    [SerializeField]
    private List<GameObject> focusedInfoPanels = new List<GameObject>();
    [SerializeField]
    private List<ItemSlot> focusedObjectSlots = new List<ItemSlot>();
    [SerializeField]
    private ItemInfoDescription infoLine;
    [SerializeField]
    private Sprite farmlandIcon;
    [SerializeField]
    private TextMeshProUGUI stackCountText;

    private Component boundTarget;
    private Component focusedPanelTarget;
    private Resource focusedPanelUnderlyingResource;
    private bool referencesResolved;
    private float nextLayoutRefreshTime;
    private int displayedStackCount = -1;

    private void Awake()
    {
        ResolveReferences();
        Clear();
    }

    private void OnValidate()
    {
        referencesResolved = false;
        ResolveReferences();
    }

    public void Bind(Component target)
    {
        ResolveReferences();
        if (!(target is MapObject)
            && !(target is Animal)
            && !(target is PortableObject)
            && !(target is Block farmlandBlock && IsFarmlandBlock(farmlandBlock)))
        {
            Clear();
            return;
        }

        boundTarget = target;
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        Resource underlyingResource = target is MapObject mapObject
            ? ResolveUnderlyingResource(mapObject)
            : null;
        RefreshFocusedInfoPanels(target, underlyingResource);

        RefreshInfoLine(target, underlyingResource);

        RefreshInfoLineRectTransformImmediate();
        nextLayoutRefreshTime = Time.unscaledTime + LayoutRefreshInterval;
    }

    public void Refresh()
    {
        if (boundTarget == null)
        {
            Clear();
            return;
        }

        ResolveReferences();
        Resource underlyingResource = boundTarget is MapObject mapObject
            ? ResolveUnderlyingResource(mapObject)
            : null;
        RefreshFocusedInfoPanels(boundTarget, underlyingResource);
        if (boundTarget is RailHandcar || boundTarget is ProjectF.MapObjects.Tree)
        {
            // Live values and gauges are updated by ItemInfoDescription itself.
            return;
        }

        RefreshInfoLine(boundTarget, underlyingResource);
        RefreshInfoLineRectTransformThrottled();
    }

    public void Clear()
    {
        boundTarget = null;
        nextLayoutRefreshTime = 0f;
        ResolveReferences();
        ClearFocusedInfoPanels();
        SetStackCountDisplay(0);
        CloseInfoLine();
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    public bool IsBoundTo(Component target)
    {
        return boundTarget == target;
    }

    private void ResolveReferences()
    {
        if (referencesResolved
            && infoLine != null
            && IsLocalComponent(infoLine)
            && stackCountText != null
            && IsLocalComponent(stackCountText))
        {
            return;
        }

        if (infoLine == null || !IsLocalComponent(infoLine))
        {
            infoLine = GetComponentInChildren<ItemInfoDescription>(true);
        }

        ResolveStackCountText();

        ResolveFocusedInfoPanelReferences();
        CloseExtraInfoLines();
        referencesResolved = true;
    }

    private void RefreshInfoLine(Component target, Resource underlyingResource)
    {
        if (target is Animal animal)
        {
            ShowAnimalInfo(animal);
            return;
        }

        if (target is PortableObject)
        {
            CloseInfoLine();
            return;
        }

        if (target is Block farmlandBlock)
        {
            ShowFarmlandInfo(farmlandBlock);
            return;
        }

        MapObject mapObject = target as MapObject;
        if (mapObject is Resource resource)
        {
            ShowResourceInfo(resource);
            return;
        }

        if (mapObject is BoxObject boxObject)
        {
            ShowBoxObjectInfo(boxObject, underlyingResource);
            return;
        }

        if (mapObject is Handcart handcart)
        {
            ShowHandcartInfo(handcart, underlyingResource);
            return;
        }

        if (mapObject is Desk desk)
        {
            ShowDeskInfo(desk, underlyingResource);
            return;
        }

        if (mapObject is ConveyorBelt conveyorBelt)
        {
            ShowConveyorBeltInfo(conveyorBelt, underlyingResource);
            return;
        }

        if (mapObject is Pipe pipe)
        {
            ShowPipeInfo(pipe, underlyingResource);
            return;
        }

        if (mapObject is RobotArm robotArm)
        {
            ShowRobotArmInfo(robotArm, underlyingResource);
            return;
        }

        if (mapObject is LoggingMachine loggingMachine)
        {
            ShowLoggingMachineInfo(loggingMachine, underlyingResource);
            return;
        }

        if (mapObject is UtilityPole utilityPole)
        {
            ShowUtilityPoleInfo(utilityPole, underlyingResource);
            return;
        }

        if (mapObject is LightObject lightObject)
        {
            ShowLightObjectInfo(lightObject, underlyingResource);
            return;
        }

        if (mapObject is RailHandcar railHandcar)
        {
            ShowRailHandcarInfo(railHandcar, underlyingResource);
            return;
        }

        if (mapObject is Trainstation trainstation)
        {
            ShowTrainstationInfo(trainstation, underlyingResource);
            return;
        }

        if (mapObject is InputOutputModule inputOutputModule)
        {
            ShowInputOutputModuleInfo(inputOutputModule, underlyingResource);
            return;
        }

        if (mapObject is InstallationObject installationObject && installationObject.CanStoreFluid)
        {
            ShowInstallationObjectInfo(installationObject, underlyingResource);
            return;
        }

        CloseInfoLine();
    }

    private void RefreshFocusedInfoPanels(Component target, Resource underlyingResource)
    {
        SetStackCountDisplay(target is PortableObject focusedPortableObject
            ? Mathf.Max(1, focusedPortableObject.FocusStackCount)
            : 0);

        bool selectionChanged = focusedPanelTarget != target
                                || focusedPanelUnderlyingResource != underlyingResource;
        if (selectionChanged)
        {
            ClearFocusedInfoPanels();
        }

        if (target is Animal animal)
        {
            SetFocusedInfoPanelAnimal(animal);
            focusedPanelTarget = animal;
            focusedPanelUnderlyingResource = null;
            return;
        }

        if (target is PortableObject portableObject)
        {
            SetFocusedInfoPanelPortableObject(portableObject);
            focusedPanelTarget = portableObject;
            focusedPanelUnderlyingResource = null;
            return;
        }

        if (target is Block farmlandBlock)
        {
            SetFocusedInfoPanelFarmland();
            focusedPanelTarget = farmlandBlock;
            focusedPanelUnderlyingResource = null;
            return;
        }

        MapObject mapObject = target as MapObject;
        if (mapObject == null)
        {
            return;
        }

        SetFocusedInfoPanelItem(1, underlyingResource, underlyingResource != null);
        SetFocusedInfoPanelItem(0, mapObject, true);
        focusedPanelTarget = mapObject;
        focusedPanelUnderlyingResource = underlyingResource;
    }

    private void ClearFocusedInfoPanels()
    {
        focusedPanelTarget = null;
        focusedPanelUnderlyingResource = null;

        int count = Mathf.Max(
            focusedInfoPanels != null ? focusedInfoPanels.Count : 0,
            focusedObjectSlots != null ? focusedObjectSlots.Count : 0);
        for (int i = 0; i < count; i++)
        {
            ItemSlot slot = GetListItem(focusedObjectSlots, i);
            if (slot != null)
            {
                slot.Clear();
            }

            SetFocusedInfoPanelVisible(i, false);
        }
    }

    private void SetFocusedInfoPanelItem(int index, MapObject mapObject, bool forceVisible)
    {
        ItemSlot slot = GetListItem(focusedObjectSlots, index);
        int itemId = mapObject != null ? mapObject.ResolveItemId() : -1;
        Resource resource = mapObject as Resource;
        Sprite resourceIcon = resource != null && resource.Definition != null
            ? resource.Definition.ResourceIcon
            : null;
        bool visible = forceVisible || itemId >= 0 || resourceIcon != null;
        SetFocusedInfoPanelVisible(index, visible);
        if (slot == null)
        {
            return;
        }

        if (resourceIcon != null)
        {
            slot.SetCustomDisplay(
                itemId,
                resourceIcon,
                ResolveResourceObjectName(resource),
                string.Empty);
            return;
        }

        if (itemId >= 0)
        {
            string displayNameOverride = resource != null
                ? ResolveResourceObjectName(resource)
                : null;
            slot.SetItemDisplay(
                itemId,
                1,
                0,
                true,
                true,
                displayNameOverride);
        }
        else
        {
            slot.Clear();
        }
    }

    private static string ResolveResourceObjectName(Resource resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(resource.ObjectName))
        {
            return resource.ObjectName.Trim();
        }

        string instanceName = resource.gameObject != null ? resource.gameObject.name : null;
        return string.IsNullOrWhiteSpace(instanceName)
            ? null
            : instanceName.Replace("(Clone)", string.Empty).Trim();
    }

    private void SetFocusedInfoPanelAnimal(Animal animal)
    {
        SetFocusedInfoPanelVisible(0, true);
        ItemSlot slot = GetListItem(focusedObjectSlots, 0);
        if (slot == null)
        {
            return;
        }

        AnimalDefinition definition = animal != null ? animal.Definition : null;
        Sprite icon = definition != null ? definition.AdultIcon : null;
        if (icon == null && definition != null)
        {
            icon = definition.ChildIcon;
        }

        string displayName = definition != null && !string.IsNullOrWhiteSpace(definition.AnimalName)
            ? definition.AnimalName
            : (animal != null ? animal.gameObject.name.Replace("(Clone)", string.Empty).Trim() : string.Empty);
        slot.SetCustomDisplay(icon, displayName, string.Empty);
    }

    private void SetFocusedInfoPanelFarmland()
    {
        bool hasIcon = farmlandIcon != null;
        SetFocusedInfoPanelVisible(0, hasIcon);
        ItemSlot slot = GetListItem(focusedObjectSlots, 0);
        if (slot == null)
        {
            return;
        }

        if (hasIcon)
        {
            slot.SetCustomDisplay(farmlandIcon, "Farmland", string.Empty);
        }
        else
        {
            slot.Clear();
        }
    }

    private void SetFocusedInfoPanelPortableObject(PortableObject portableObject)
    {
        int itemId = portableObject != null ? portableObject.ItemId : -1;
        SetFocusedInfoPanelVisible(0, itemId >= 0);
        ItemSlot slot = GetListItem(focusedObjectSlots, 0);
        if (slot == null)
        {
            return;
        }

        if (itemId >= 0)
        {
            slot.SetItemDisplay(itemId, 1, 0, true, false);
        }
        else
        {
            slot.Clear();
        }
    }

    private void SetFocusedInfoPanelVisible(int index, bool visible)
    {
        GameObject panel = GetListItem(focusedInfoPanels, index);
        if (panel == null)
        {
            ItemSlot slot = GetListItem(focusedObjectSlots, index);
            panel = slot != null ? slot.gameObject : null;
        }

        if (panel != null && panel.activeSelf != visible)
        {
            panel.SetActive(visible);
        }
    }

    private void ResolveFocusedInfoPanelReferences()
    {
        if (focusedInfoPanels == null)
        {
            focusedInfoPanels = new List<GameObject>();
        }

        if (focusedObjectSlots == null)
        {
            focusedObjectSlots = new List<ItemSlot>();
        }

        bool hasExplicitFocusedInfoReferences = focusedInfoPanels.Count > 0 || focusedObjectSlots.Count > 0;
        AddUnique(focusedObjectSlots, focusedObjectSlot);

        for (int i = 0; i < focusedInfoPanels.Count; i++)
        {
            GameObject panel = focusedInfoPanels[i];
            if (panel == null)
            {
                continue;
            }

            ItemSlot slot = panel.GetComponent<ItemSlot>();
            if (slot == null)
            {
                slot = panel.GetComponentInChildren<ItemSlot>(true);
            }

            AddUnique(focusedObjectSlots, slot);
        }

        if (!hasExplicitFocusedInfoReferences)
        {
            ItemSlot[] slots = GetComponentsInChildren<ItemSlot>(true);
            for (int i = 0; i < slots.Length; i++)
            {
                ItemSlot slot = slots[i];
                if (slot == null || IsInsideInfoLine(slot.transform))
                {
                    continue;
                }

                AddUnique(focusedObjectSlots, slot);
            }
        }

        for (int i = 0; i < focusedObjectSlots.Count; i++)
        {
            ItemSlot slot = focusedObjectSlots[i];
            if (slot != null)
            {
                AddUnique(focusedInfoPanels, slot.gameObject);
            }
        }
    }

    private void ShowAnimalInfo(Animal animal)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowAnimal(animal);
    }

    private void ResolveStackCountText()
    {
        if (stackCountText != null && IsLocalComponent(stackCountText))
        {
            return;
        }

        stackCountText = null;
        TextMeshProUGUI[] textComponents = GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < textComponents.Length; i++)
        {
            TextMeshProUGUI candidate = textComponents[i];
            if (candidate != null && candidate.name == "Stack Count")
            {
                stackCountText = candidate;
                break;
            }
        }

        displayedStackCount = -1;
    }

    private void SetStackCountDisplay(int stackCount)
    {
        int normalizedCount = Mathf.Max(0, stackCount);
        if (stackCountText == null)
        {
            displayedStackCount = normalizedCount;
            return;
        }

        bool visible = normalizedCount > 0;
        if (displayedStackCount != normalizedCount)
        {
            stackCountText.text = visible ? $"x {normalizedCount}" : string.Empty;
            displayedStackCount = normalizedCount;
        }

        if (stackCountText.gameObject.activeSelf != visible)
        {
            stackCountText.gameObject.SetActive(visible);
        }
    }

    private void ShowResourceInfo(Resource resource)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowResourceReserves(resource);
    }

    private void ShowFarmlandInfo(Block farmlandBlock)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowFarmland(farmlandBlock);
    }

    private void ShowBoxObjectInfo(BoxObject boxObject, Resource underlyingResource)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowBoxObject(boxObject, underlyingResource);
    }

    private void ShowHandcartInfo(Handcart handcart, Resource underlyingResource)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowHandcart(handcart, underlyingResource);
    }

    private void ShowConveyorBeltInfo(ConveyorBelt conveyorBelt, Resource underlyingResource)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowConveyorBelt(conveyorBelt, underlyingResource);
    }

    private void ShowPipeInfo(Pipe pipe, Resource underlyingResource)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowPipe(pipe, underlyingResource);
    }

    private void ShowRobotArmInfo(RobotArm robotArm, Resource underlyingResource)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowRobotArm(robotArm, underlyingResource);
    }

    private void ShowLoggingMachineInfo(LoggingMachine loggingMachine, Resource underlyingResource)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowLoggingMachine(loggingMachine, underlyingResource);
    }

    private void ShowUtilityPoleInfo(UtilityPole utilityPole, Resource underlyingResource)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowUtilityPole(utilityPole, underlyingResource);
    }

    private void ShowLightObjectInfo(LightObject lightObject, Resource underlyingResource)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowLightObject(lightObject, underlyingResource);
    }

    private void ShowRailHandcarInfo(RailHandcar railHandcar, Resource underlyingResource)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowRailHandcar(railHandcar, underlyingResource);
    }

    private void ShowTrainstationInfo(Trainstation trainstation, Resource underlyingResource)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowTrainstation(trainstation, underlyingResource);
    }

    private void ShowInputOutputModuleInfo(InputOutputModule inputOutputModule, Resource underlyingResource)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowInputOutputModule(inputOutputModule, underlyingResource);
    }

    private void ShowDeskInfo(Desk desk, Resource underlyingResource)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowDesk(desk, underlyingResource);
    }

    private void ShowInstallationObjectInfo(InstallationObject installationObject, Resource underlyingResource)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowInstallationObject(installationObject, underlyingResource);
    }

    private static Resource ResolveUnderlyingResource(MapObject mapObject)
    {
        if (mapObject == null || mapObject is Resource)
        {
            return null;
        }

        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain != null
            && mapObject is InstallationObject installationObject
            && installationObject.RuntimeOccupiedCoordinates != null)
        {
            IReadOnlyList<Vector2Int> occupiedCoordinates = installationObject.RuntimeOccupiedCoordinates;
            for (int i = 0; i < occupiedCoordinates.Count; i++)
            {
                if (terrain.TryGetLoadedBlock(occupiedCoordinates[i], out Block block)
                    && TryGetUnderlyingResource(block, mapObject, out Resource resource))
                {
                    return resource;
                }
            }
        }

        Block parentBlock = mapObject.GetComponentInParent<Block>();
        return TryGetUnderlyingResource(parentBlock, mapObject, out Resource parentResource)
            ? parentResource
            : null;
    }

    private static bool IsFarmlandBlock(Block block)
    {
        TerrainGenerator terrain = TerrainGenerator.ResolveActive();
        return block != null
               && terrain != null
               && terrain.IsFarmlandAt(block.Coordinate);
    }

    private static bool TryGetUnderlyingResource(Block block, MapObject displayedObject, out Resource resource)
    {
        resource = null;
        if (block == null)
        {
            return false;
        }

        resource = block.Resource;
        if (resource == null
            || resource == displayedObject
            || !resource.CanHarvest)
        {
            resource = null;
            return false;
        }

        return true;
    }

    private void CloseInfoLine()
    {
        if (infoLine == null)
        {
            return;
        }

        infoLine.Clear();
        if (infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(false);
        }
    }

    private void RefreshInfoLineRectTransformThrottled()
    {
        float now = Time.unscaledTime;
        if (now < nextLayoutRefreshTime)
        {
            return;
        }

        nextLayoutRefreshTime = now + LayoutRefreshInterval;
        RefreshInfoLineRectTransformImmediate();
    }

    private void RefreshInfoLineRectTransformImmediate()
    {
        if (infoLine == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        ForceRebuildLayoutImmediate(infoLine.transform as RectTransform);
        ForceRebuildLayoutImmediate(infoLine.transform.parent as RectTransform);
        ForceRebuildLayoutImmediate(transform as RectTransform);
        Canvas.ForceUpdateCanvases();
    }

    private static void ForceRebuildLayoutImmediate(RectTransform target)
    {
        if (target != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(target);
        }
    }

    private void CloseExtraInfoLines()
    {
        ItemInfoDescription[] lines = GetComponentsInChildren<ItemInfoDescription>(true);
        for (int i = 0; i < lines.Length; i++)
        {
            ItemInfoDescription line = lines[i];
            if (line == null || line == infoLine)
            {
                continue;
            }

            line.Clear();
            if (line.gameObject.activeSelf)
            {
                line.gameObject.SetActive(false);
            }
        }
    }

    private bool IsLocalComponent(Component component)
    {
        return component != null && component.transform != null && component.transform.IsChildOf(transform);
    }

    private bool IsInsideInfoLine(Transform target)
    {
        return target != null
               && infoLine != null
               && infoLine.transform != null
               && target.IsChildOf(infoLine.transform);
    }

    private static T GetListItem<T>(List<T> list, int index) where T : class
    {
        return list != null && index >= 0 && index < list.Count ? list[index] : null;
    }

    private static void AddUnique<T>(List<T> list, T item) where T : class
    {
        if (list == null || item == null || list.Contains(item))
        {
            return;
        }

        list.Add(item);
    }
}
