using ProjectF.Simulation.Monsters;
using UnityEngine;

public partial class Player
{
    [SerializeField, Min(1f)] private float maximumHealth = 100f;
    private CombatHealth combatHealth;
    private bool healthInitialized;

    public float MaxHealth { get { EnsureHealth(); return combatHealth.Maximum; } }
    public float CurrentHealth { get { EnsureHealth(); return combatHealth.Current; } }
    public bool IsDead { get { EnsureHealth(); return combatHealth.IsDead; } }

    private void EnsureHealth()
    {
        if (healthInitialized) return;
        combatHealth = new CombatHealth(maximumHealth);
        healthInitialized = true;
    }

    public float TakeDamage(float damage)
    {
        EnsureHealth();
        if (!isActiveAndEnabled || MapObjectTickManager.SimulationPaused
            || SaveManager.GameplayInputBlocked) return 0f;
        float applied = combatHealth.Damage(damage);
        if (applied > 0f && combatHealth.IsDead)
        {
            if (playerController == null) playerController = GetComponent<PlayerController>();
            playerController?.StopForDeath();
            StopImmediateActions();
        }
        return applied;
    }

    public bool Revive()
    {
        EnsureHealth();
        return !MapObjectTickManager.SimulationPaused && !SaveManager.GameplayInputBlocked
            && combatHealth.Revive();
    }

    private void RestoreCombatHealth(PlayerSaveData state)
    {
        // Pre-v72 saves have no health field and start alive; zero in v72 stays dead.
        combatHealth = state.hasHealth
            ? new CombatHealth(maximumHealth, state.currentHealth) : new CombatHealth(maximumHealth);
        healthInitialized = true;
        if (combatHealth.IsDead)
        {
            if (playerController == null) playerController = GetComponent<PlayerController>();
            playerController?.StopForDeath();
            StopImmediateActions();
        }
    }
}
