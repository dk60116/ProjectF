using ProjectF.Attributes;
using System.Collections;
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
}
