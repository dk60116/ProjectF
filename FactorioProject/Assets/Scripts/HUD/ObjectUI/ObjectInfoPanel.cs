using UnityEngine;
using UnityEngine.UI;

public class ObjectInfoPanel : MonoBehaviour
{
    [SerializeField]
    private ItemSlot focusedObjectSlot;
    [SerializeField]
    private ItemInfoDescription infoLine;

    private MapObject boundObject;

    private void Awake()
    {
        ResolveReferences();
        Clear();
    }

    private void OnValidate()
    {
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
        int itemId = mapObject.ResolveItemId();
        if (focusedObjectSlot != null)
        {
            if (itemId >= 0)
            {
                focusedObjectSlot.SetItemDisplay(itemId, 1, 0, true);
            }
            else
            {
                focusedObjectSlot.Clear();
            }
        }

        RefreshInfoLine(mapObject);

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        RefreshInfoLineRectTransformImmediate();
    }

    public void Refresh()
    {
        if (boundObject == null)
        {
            Clear();
            return;
        }

        ResolveReferences();
        RefreshInfoLine(boundObject);
        RefreshInfoLineRectTransformImmediate();
    }

    public void Clear()
    {
        boundObject = null;
        ResolveReferences();
        focusedObjectSlot?.Clear();
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
        if (focusedObjectSlot == null)
        {
            focusedObjectSlot = GetComponentInChildren<ItemSlot>(true);
        }

        if (infoLine == null || !IsLocalComponent(infoLine))
        {
            infoLine = GetComponentInChildren<ItemInfoDescription>(true);
        }

        CloseExtraInfoLines();
    }

    private void RefreshInfoLine(MapObject mapObject)
    {
        CloseInfoLine();

        if (mapObject is Resource resource)
        {
            ShowResourceInfo(resource);
            return;
        }

        if (mapObject is BoxObject boxObject)
        {
            ShowBoxObjectInfo(boxObject);
            return;
        }

        if (mapObject is ConveyorBelt conveyorBelt)
        {
            ShowConveyorBeltInfo(conveyorBelt);
            return;
        }

        if (mapObject is RobotArm robotArm)
        {
            ShowRobotArmInfo(robotArm);
            return;
        }

        if (mapObject is InputOutputModule inputOutputModule)
        {
            ShowInputOutputModuleInfo(inputOutputModule);
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

        infoLine.ShowResourceReserves(resource != null ? resource.ResourceCount : 0);
    }

    private void ShowBoxObjectInfo(BoxObject boxObject)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowBoxObject(boxObject);
    }

    private void ShowConveyorBeltInfo(ConveyorBelt conveyorBelt)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowConveyorBelt(conveyorBelt);
    }

    private void ShowRobotArmInfo(RobotArm robotArm)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowRobotArm(robotArm);
    }

    private void ShowInputOutputModuleInfo(InputOutputModule inputOutputModule)
    {
        if (infoLine == null)
        {
            return;
        }

        if (!infoLine.gameObject.activeSelf)
        {
            infoLine.gameObject.SetActive(true);
        }

        infoLine.ShowInputOutputModule(inputOutputModule);
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
}
