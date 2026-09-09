using ProjectF.Attributes;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TrainStationFilter : MonoBehaviour
{
    [SerializeField, ReadOnly]
    private string stationName;
    [SerializeField, ReadOnly]
    private Color stationColor;

    [SerializeField]
    private TMP_InputField nameInputField;
    [SerializeField]
    private Image colorSelect;
    [SerializeField]
    private RectTransform colorPanel;

    private Trainstation boundStation;
    private Button colorSelectButton;
    private readonly List<Button> colorOptionButtons = new List<Button>();

    private void Awake()
    {
        ResolveSerializedReferences();
        BindInputField();
        BindColorButton();
        BuildColorPanel();
        SetColorPanelVisible(false);
    }

    private void OnEnable()
    {
        ResolveSerializedReferences();
        BindInputField();
        BindColorButton();
        BuildColorPanel();
        Refresh();
    }

    private void OnDisable()
    {
        CommitNameInput();
        UnbindInputField();
        UnbindColorButton();
        SetColorPanelVisible(false);
    }

    public void Bind(Trainstation station)
    {
        ResolveSerializedReferences();
        if (boundStation != null && boundStation != station)
        {
            CommitNameInput();
        }

        boundStation = station;
        SetColorPanelVisible(false);
        Refresh();
    }

    public bool TryGetBoundTarget(out Trainstation station)
    {
        station = boundStation;
        return station != null && station.gameObject.activeInHierarchy;
    }

    public void Refresh()
    {
        ResolveSerializedReferences();
        stationName = ResolveDisplayedStationName();
        stationColor = ResolveDisplayedStationColor();

        if (nameInputField != null)
        {
            nameInputField.SetTextWithoutNotify(stationName);
        }

        if (colorSelect != null)
        {
            colorSelect.color = stationColor;
        }
    }

    private Color ResolveDisplayedStationColor()
    {
        if (boundStation == null)
        {
            return Color.white;
        }

        if (!boundStation.HasAssignedStationColor)
        {
            TerrainGenerator.ResolveActive()?.SaveRuntimeInstallationState(boundStation);
        }

        return boundStation.StationColor;
    }

    private string ResolveDisplayedStationName()
    {
        if (boundStation == null)
        {
            return string.Empty;
        }

        if (!boundStation.HasAssignedStationName)
        {
            boundStation.SetStationName(boundStation.StoredStationName);
        }

        return boundStation.StationName;
    }

    private void ResolveSerializedReferences()
    {
        if (nameInputField == null)
        {
            nameInputField = GetComponentInChildren<TMP_InputField>(true);
        }

        if (colorSelect == null)
        {
            Transform colorTransform = transform.Find("Station Color/Color");
            colorSelect = colorTransform != null ? colorTransform.GetComponent<Image>() : null;
        }

        if (colorPanel == null)
        {
            colorPanel = transform.Find("Color Panel") as RectTransform;
        }

        colorSelectButton = colorSelect != null ? colorSelect.GetComponent<Button>() : null;
    }

    private void BindInputField()
    {
        ResolveSerializedReferences();
        if (nameInputField == null)
        {
            return;
        }

        nameInputField.onEndEdit.RemoveListener(HandleNameInputEndEdit);
        nameInputField.onEndEdit.AddListener(HandleNameInputEndEdit);
    }

    private void UnbindInputField()
    {
        if (nameInputField != null)
        {
            nameInputField.onEndEdit.RemoveListener(HandleNameInputEndEdit);
        }
    }

    private void BindColorButton()
    {
        ResolveSerializedReferences();
        if (colorSelectButton == null)
        {
            return;
        }

        colorSelectButton.onClick.RemoveListener(HandleColorSelectClicked);
        colorSelectButton.onClick.AddListener(HandleColorSelectClicked);
    }

    private void UnbindColorButton()
    {
        if (colorSelectButton != null)
        {
            colorSelectButton.onClick.RemoveListener(HandleColorSelectClicked);
        }
    }

    private void BuildColorPanel()
    {
        if (colorPanel == null || colorOptionButtons.Count > 0)
        {
            return;
        }

        for (int i = 0; i < Trainstation.StationColorOptionCount; i++)
        {
            Color32 optionColor = Trainstation.GetStationColorOption(i);
            GameObject optionObject = new GameObject(
                $"Color Option {i + 1}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            optionObject.layer = colorPanel.gameObject.layer;
            optionObject.transform.SetParent(colorPanel, false);

            Image optionImage = optionObject.GetComponent<Image>();
            optionImage.color = optionColor;

            Button optionButton = optionObject.GetComponent<Button>();
            optionButton.targetGraphic = optionImage;
            optionButton.onClick.AddListener(() => SelectStationColor(optionColor));
            colorOptionButtons.Add(optionButton);
        }
    }

    private void HandleColorSelectClicked()
    {
        SetColorPanelVisible(colorPanel != null && !colorPanel.gameObject.activeSelf);
    }

    private void SelectStationColor(Color32 color)
    {
        if (boundStation == null)
        {
            return;
        }

        boundStation.SetStationColor(color);
        SetColorPanelVisible(false);
        Refresh();
    }

    private void SetColorPanelVisible(bool visible)
    {
        if (colorPanel != null && colorPanel.gameObject.activeSelf != visible)
        {
            colorPanel.gameObject.SetActive(visible);
        }
    }

    private void HandleNameInputEndEdit(string value)
    {
        CommitStationName(value);
    }

    private void CommitNameInput()
    {
        ResolveSerializedReferences();
        if (nameInputField == null)
        {
            return;
        }

        CommitStationName(nameInputField.text);
    }

    private void CommitStationName(string value)
    {
        if (boundStation == null)
        {
            return;
        }

        boundStation.SetStationName(value);
        Refresh();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveSerializedReferences();
    }
#endif
}
