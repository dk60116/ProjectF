using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MainMenuScreen : MonoBehaviour
{
    private const string LoadingSceneName = "LoadingScene";
    private const string NewGameButtonName = "New Game Button";
    private const string ContinueButtonName = "Continue Button";
    private const string ExitButtonName = "Exit Button";
    private const string ContinuePanelName = "Continue Panel";
    private const string EmptyMessageName = "Empty Message";
    private const int NewGameSlotIndex = 0;

    private readonly Button[] slotButtons = new Button[SaveManager.SlotCount];
    private readonly Text[] slotLabels = new Text[SaveManager.SlotCount];
    private readonly UnityAction[] slotActions = new UnityAction[SaveManager.SlotCount];

    private Button newGameButton;
    private Button continueButton;
    private Button exitButton;
    private GameObject continuePanel;
    private GameObject emptyMessage;
    private bool transitionStarted;

    private void Awake()
    {
        newGameButton = FindButton(transform, NewGameButtonName);
        continueButton = FindButton(transform, ContinueButtonName);
        exitButton = FindButton(transform, ExitButtonName);
        Transform panelTransform = transform.Find(ContinuePanelName);
        continuePanel = panelTransform != null ? panelTransform.gameObject : null;
        emptyMessage = panelTransform?.Find(EmptyMessageName)?.gameObject;

        if (newGameButton == null
            || continueButton == null
            || exitButton == null
            || continuePanel == null)
        {
            Debug.LogError("[MainMenu] MainScene UI 구성이 누락되었습니다.");
            enabled = false;
            return;
        }

        newGameButton.onClick.AddListener(StartNewGame);
        continueButton.onClick.AddListener(ToggleContinuePanel);
        exitButton.onClick.AddListener(ExitGame);

        for (int i = 0; i < slotButtons.Length; i++)
        {
            Transform slotTransform = panelTransform.Find($"Save Slot {i + 1}");
            Button slotButton = slotTransform != null
                ? slotTransform.GetComponent<Button>()
                : null;
            Text slotLabel = slotTransform?.Find("Label")?.GetComponent<Text>();
            if (slotButton == null || slotLabel == null)
            {
                Debug.LogError($"[MainMenu] Save Slot {i + 1} UI 구성이 누락되었습니다.");
                enabled = false;
                return;
            }

            int capturedSlotIndex = i;
            UnityAction action = () => ContinueFromSlot(capturedSlotIndex);
            slotButtons[i] = slotButton;
            slotLabels[i] = slotLabel;
            slotActions[i] = action;
            slotButton.onClick.AddListener(action);
        }

        RefreshSavedSlots();
        continuePanel.SetActive(false);
    }

    private void OnDestroy()
    {
        newGameButton?.onClick.RemoveListener(StartNewGame);
        continueButton?.onClick.RemoveListener(ToggleContinuePanel);
        exitButton?.onClick.RemoveListener(ExitGame);
        for (int i = 0; i < slotButtons.Length; i++)
        {
            if (slotButtons[i] != null && slotActions[i] != null)
            {
                slotButtons[i].onClick.RemoveListener(slotActions[i]);
            }
        }
    }

    private void StartNewGame()
    {
        if (!TryBeginTransition())
        {
            return;
        }

        SaveManager.PrepareNewMapForNextSceneLoad(NewGameSlotIndex);
        SceneManager.LoadScene(LoadingSceneName, LoadSceneMode.Single);
    }

    private void ToggleContinuePanel()
    {
        if (transitionStarted)
        {
            return;
        }

        bool show = !continuePanel.activeSelf;
        if (show)
        {
            RefreshSavedSlots();
        }

        continuePanel.SetActive(show);
    }

    private void ContinueFromSlot(int slotIndex)
    {
        if (transitionStarted || !Application.CanStreamedLevelBeLoaded(LoadingSceneName))
        {
            if (!transitionStarted)
            {
                Debug.LogError($"[MainMenu] {LoadingSceneName} 씬을 불러올 수 없습니다.");
            }
            return;
        }

        if (!SaveManager.PrepareSavedMapForNextSceneLoad(slotIndex))
        {
            RefreshSavedSlots();
            return;
        }

        transitionStarted = true;
        SetButtonsInteractable(false);
        SceneManager.LoadScene(LoadingSceneName, LoadSceneMode.Single);
    }

    private void ExitGame()
    {
        if (!transitionStarted)
        {
            Application.Quit();
        }
    }

    private bool TryBeginTransition()
    {
        if (transitionStarted)
        {
            return false;
        }

        if (!Application.CanStreamedLevelBeLoaded(LoadingSceneName))
        {
            Debug.LogError($"[MainMenu] {LoadingSceneName} 씬을 불러올 수 없습니다.");
            return false;
        }

        transitionStarted = true;
        SetButtonsInteractable(false);
        return true;
    }

    private void RefreshSavedSlots()
    {
        bool hasSavedSlot = false;
        for (int i = 0; i < slotButtons.Length; i++)
        {
            bool exists = SaveManager.TryGetSavedSlotLabel(i, out string label);
            slotButtons[i].gameObject.SetActive(exists);
            if (!exists)
            {
                continue;
            }

            slotLabels[i].text = label;
            hasSavedSlot = true;
        }

        if (emptyMessage != null)
        {
            emptyMessage.SetActive(!hasSavedSlot);
        }
    }

    private void SetButtonsInteractable(bool interactable)
    {
        newGameButton.interactable = interactable;
        continueButton.interactable = interactable;
        exitButton.interactable = interactable;
        for (int i = 0; i < slotButtons.Length; i++)
        {
            slotButtons[i].interactable = interactable;
        }
    }

    private static Button FindButton(Transform parent, string objectName)
    {
        Transform child = parent.Find(objectName);
        return child != null ? child.GetComponent<Button>() : null;
    }
}
