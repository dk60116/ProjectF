using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class FilterSelectUI
{
    private GameObject splitterControls;
    private readonly Button[] splitterModeButtons = new Button[3];

    private void RefreshSplitterControls()
    {
        Spliterbelt splitter = boundTarget as Spliterbelt;
        if (splitter == null)
        {
            if (splitterControls != null) splitterControls.SetActive(false);
            return;
        }
        if (splitterControls == null)
        {
            splitterControls = new GameObject("Splitter Filter Output", typeof(RectTransform));
            RectTransform root = splitterControls.GetComponent<RectTransform>();
            root.SetParent(transform, false);
            root.anchorMin = root.anchorMax = root.pivot = Vector2.one;
            root.anchoredPosition = new Vector2(-28f, -26f);
            root.sizeDelta = new Vector2(552f, 68f);
            TextMeshProUGUI style = GetComponentInChildren<TextMeshProUGUI>(true);
            string[] labels = { "FILTER OFF", "FILTER LEFT", "FILTER RIGHT" };
            for (int i = 0; i < 3; i++)
            {
                RectTransform buttonRect = CreateSliderImage(labels[i], root, Color.white,
                    new Vector2(i / 3f, 0f), new Vector2((i + 1) / 3f, 1f),
                    ResolveBulkButtonSprite(), Image.Type.Sliced);
                buttonRect.offsetMin = new Vector2(3f, 0f);
                buttonRect.offsetMax = new Vector2(-3f, 0f);
                Button button = buttonRect.gameObject.AddComponent<Button>();
                button.targetGraphic = buttonRect.GetComponent<Image>();
                CreateSliderText("Label", buttonRect, style, 20f, TextAlignmentOptions.Center).text = labels[i];
                int mode = i;
                button.onClick.AddListener(() => SetSplitterFilterMode(mode));
                splitterModeButtons[i] = button;
            }
        }
        splitterControls.SetActive(true);
        for (int i = 0; i < splitterModeButtons.Length; i++)
            splitterModeButtons[i].interactable = i != (int)splitter.SelectedFilterOutput;
    }

    private void SetSplitterFilterMode(int mode)
    {
        if (!(ResolveCurrentTarget() is Spliterbelt splitter))
            return;
        splitter.SetFilterOutput((Spliterbelt.FilterOutput)mode);
        PersistTargetFilterState(splitter);
        Refresh();
    }
}
