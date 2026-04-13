using UnityEngine;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [SerializeField]
    private PlayerHUD playerHUD;

    public PlayerHUD PlayerHUD => playerHUD;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (playerHUD == null)
        {
            playerHUD = GetComponentInChildren<PlayerHUD>(true);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void BindPlayerBag(PlayerBag bag)
    {
        if (playerHUD == null)
        {
            playerHUD = GetComponentInChildren<PlayerHUD>(true);
        }

        playerHUD?.Bind(bag);
    }
}
