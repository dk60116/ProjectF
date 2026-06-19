using System.Collections.Generic;
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

    private MapObject boundObject;
    private MapObject focusedPanelMapObject;
    private Resource focusedPanelUnderlyingResource;
    private bool referencesResolved;
    private float nextLayoutRefreshTime;

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

    public void Bind(MapObject mapObject)
    {
        ResolveReferences();
        if (mapObject == null)
        {
            Clear();
            return;
        }

        boundObject = mapObject;
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        Resource underlyingResource = ResolveUnderlyingResource(mapObject);
        RefreshFocusedInfoPanels(mapObject, underlyingResource);

        RefreshInfoLine(mapObject, underlyingResource);

        RefreshInfoLineRectTransformImmediate();
        nextLayoutRefreshTime = Time.unscaledTime + LayoutRefreshInterval;
    }

    public void Refresh()
    {
        if (boundObject == null)
        {
            Clear();
            return;
        }

        ResolveReferences();
        Resource underlyingResource = ResolveUnderlyingResource(boundObject);
        RefreshFocusedInfoPanels(boundObject, underlyingResource);
        RefreshInfoLine(boundObject, underlyingResource);
        RefreshInfoLineRectTransformThrottled();
    }

    public void Clear()
    {
        boundObject = null;
        nextLayoutRefreshTime = 0f;
        ResolveReferences();
        ClearFocusedInfoPanels();
        CloseInfoLine();
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    public bool IsBoundTo(MapObject mapObject)
    {
        return boundObject == mapObject;
    }

    private void ResolveReferences()
    {
        if (referencesResolved && infoLine != null && IsLocalComponent(infoLine))
        {
            return;
        }

        if (infoLine == null || !IsLocalComponent(infoLine))
        {
            infoLine = GetComponentInChildren<ItemInfoDescription>(true);
        }

        ResolveFocusedInfoPanelReferences();
        CloseExtraInfoLines();
        referencesResolved = true;
    }

    private void RefreshInfoLine(MapObject mapObject, Resource underlyingResource)
    {
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

        if (mapObject is UtilityPole utilityPole)
        {
            ShowUtilityPoleInfo(utilityPole, underlyingResource);
            return;
        }

        if (mapObject is RailHandcar railHandcar)
        {
            ShowRailHandcarInfo(railHandcar, underlyingResource);
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

    private void RefreshFocusedInfoPanels(MapObject mapObject, Resource underlyingResource)
    {
        if (focusedPanelMapObject == mapObject
            && focusedPanelUnderlyingResource == underlyingResource)
        {
            return;
        }

        ClearFocusedInfoPanels();
        SetFocusedInfoPanelItem(1, underlyingResource, underlyingResource != null);
        SetFocusedInfoPanelItem(0, mapObject, true);
        focusedPanelMapObject = mapObject;
        focusedPanelUnderlyingResource = underlyingResource;
    }

    private void ClearFocusedInfoPanels()
    {
        focusedPanelMapObject = null;
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
        bool visible = forceVisible || itemId >= 0;
        SetFocusedInfoPanelVisible(index, visible);
        if (slot == null)
        {
            return;
        }

        if (itemId >= 0)
        {
            slot.SetItemDisplay(itemId, 1, 0, true);
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

        infoLine.ShowResourceReserves(resource != null ? resource.RemainingHarvestOutputCount : 0);
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
