using UnityEngine;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [SerializeField]
    private PlayerHUD playerHUD;

    [SerializeField]
    private Sprite arrowImage;

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

    public PlayerHUD PlayerHUD => playerHUD;
    public Sprite ArrowImage => arrowImage;
}
