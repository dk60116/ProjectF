using System;

public interface IMapObjectUpdateTick
{
    void ManagedUpdateTick(float deltaTime);
}

public interface IMapObjectUpdateTickInterval
{
    float ManagedUpdateTickIntervalSeconds { get; }
}

public interface IMapObjectSimulationIdentity
{
    long SimulationId { get; }
}

public interface IPersistenceDirtyTrackable
{
    void MarkPersistenceStateDirty();
}

public interface IMapObjectStagedUpdateTick
{
    void PlanManagedUpdateTick(float deltaTime);

    void ApplyManagedUpdateTick();
}


public static class DeterministicSimulationUnits
{
    // Divisible by 60 so common per-second values retain exact sub-tick units.
    public const long UnitsPerWhole = 60_000_000L;

    public static long FromInt(int value)
    {
        return value <= 0
            ? 0L
            : Math.Min(long.MaxValue, (long)value * UnitsPerWhole);
    }

    public static long FromFloat(float value)
    {
        if (float.IsNaN(value) || value <= 0f)
        {
            return 0L;
        }

        if (float.IsPositiveInfinity(value))
        {
            return long.MaxValue;
        }

        decimal scaled = decimal.Round(
            (decimal)value * UnitsPerWhole,
            0,
            MidpointRounding.AwayFromZero);
        return scaled >= long.MaxValue ? long.MaxValue : (long)scaled;
    }

    public static float ToFloat(long units)
    {
        return units <= 0L ? 0f : (float)((double)units / UnitsPerWhole);
    }

    public static long SecondsToTicks(float seconds)
    {
        if (float.IsNaN(seconds) || seconds <= 0f)
        {
            return 0L;
        }

        return Math.Max(
            1L,
            (long)decimal.Round(
                (decimal)seconds * ProjectF.Simulation.SimulationTickWorld.DefaultSimulationTicksPerSecond,
                0,
                MidpointRounding.AwayFromZero));
    }

    public static float TicksToSeconds(long ticks)
    {
        return ticks <= 0L
            ? 0f
            : ticks * ProjectF.Simulation.SimulationTickWorld.FixedSimulationDeltaSeconds;
    }

    public static long DeltaTimeToTicks(float deltaTime)
    {
        return deltaTime <= 0f ? 0L : Math.Max(1L, SecondsToTicks(deltaTime));
    }

    public static long RateForTicks(float ratePerSecond, long elapsedTicks)
    {
        if (ratePerSecond <= 0f || elapsedTicks <= 0L)
        {
            return 0L;
        }

        decimal units = (decimal)ratePerSecond
                        * UnitsPerWhole
                        * elapsedTicks
                        / ProjectF.Simulation.SimulationTickWorld.DefaultSimulationTicksPerSecond;
        decimal rounded = decimal.Round(units, 0, MidpointRounding.AwayFromZero);
        return rounded >= long.MaxValue ? long.MaxValue : (long)rounded;
    }

    public static long MultiplyRatio(long value, long numerator, long denominator)
    {
        if (value <= 0L || numerator <= 0L || denominator <= 0L)
        {
            return 0L;
        }

        if (numerator >= denominator)
        {
            return value;
        }

        return (long)decimal.Truncate((decimal)value * numerator / denominator);
    }
}
