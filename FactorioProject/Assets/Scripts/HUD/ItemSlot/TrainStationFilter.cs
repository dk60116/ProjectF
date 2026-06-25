using ProjectF.Attributes;
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

    private Trainstation boundStation;

    private void Awake()
    {
        ResolveSerializedReferences();
        BindInputField();
    }

    private void OnEnable()
    {
        ResolveSerializedReferences();
        BindInputField();
        Refresh();
    }

    private void OnDisable()
    {
        CommitNameInput();
        UnbindInputField();
    }

    public void Bind(Trainstation station)
    {
        ResolveSerializedReferences();
        if (boundStation != null && boundStation != station)
        {
            CommitNameInput();
        }

        boundStation = station;
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
        stationColor = Color.white;

        if (nameInputField != null)
        {
            nameInputField.SetTextWithoutNotify(stationName);
        }

        if (colorSelect != null)
        {
            colorSelect.color = stationColor;
        }
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
