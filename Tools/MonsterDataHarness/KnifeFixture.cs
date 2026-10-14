using ProjectF.Monsters;
using ProjectF.Simulation.Monsters;
using UnityEngine;

// Animation/input and unrelated player actions are ports. Knife control and monster HP are production code.
public sealed partial class Player
{
    public bool IsCarrying, PickAnimationStartedThisFrame;
    public Transform BodyTransform => transform;
    public readonly FixturePlayerState State = new();
    public int QueuedPicks, ClearedPicks;
    public void QueuePickAnimation() => QueuedPicks++;
    public void ClearQueuedPickAnimations() { QueuedPicks = 0; ClearedPicks++; }
}
public sealed class FixturePlayerState { public float HarvestRange = 1f; }

public sealed class Animal
{
    public readonly GameObject gameObject = new();
    public Transform transform => gameObject.transform;
    public CombatHealth Health = new(100f);
    public bool CanBeAttacked => !Health.IsDead;
    public bool IsAlive => CanBeAttacked;
    public float NormalizedHealth => Health.Current / Health.Maximum;
    public T GetComponentInParent<T>() where T : class => gameObject.GetComponentInParent<T>();
    public int AttackNotifications;
    public void NotifyAttackAnimationStarted() => AttackNotifications++;
    public void TakeDamage(float damage, Vector3 origin) => Health.Damage(damage);
}

public partial class PlayerController
{
    private const float PickAnimationDuration = 1.1666666f;
    private Player player;
    private Transform interactionPointSnapTarget;
    private Vector3 pendingFacingDirection;
    private bool hasPendingFacingDirection;
    private readonly Transform knifeFallback = new();
    public Transform transform => player?.transform ?? knifeFallback;
    internal static bool KnifeInputLocked;
    private static bool IsPlayerInputLocked() => KnifeInputLocked || SaveManager.GameplayInputBlocked
        || GameManager.TextInputFocused || MonsterSpawnBrush.BlocksGameplayInput;
    private TerrainGenerator ResolveTerrainGenerator() => TerrainGenerator.Active;
    private void CancelActiveResourceHarvest() { }
    private void CancelActiveCorpseHarvest() { }
    internal void BindKnifePlayer(Player value) => player = value;
    internal bool HasKnifeFacing => hasPendingFacingDirection && pendingFacingDirection.sqrMagnitude > 0f;
    internal bool ApproachKnife(out Vector3 direction) => TryGetKnifeApproachDirection(out direction);
    internal bool RequestAnimalKnifeForTest(Animal animal) => RequestKnifeAttack(animal);
    internal void KnifeTick(float time, bool animationStarted = false)
    {
        Time.time = time;
        player.PickAnimationStartedThisFrame = animationStarted;
        TickKnifeAttack();
    }
    internal void CancelKnifeForTest() => CancelKnifeAttack();
    internal void SetKnifeMounted(bool mounted) => interactionPointSnapTarget = mounted ? new Transform() : null;
}
