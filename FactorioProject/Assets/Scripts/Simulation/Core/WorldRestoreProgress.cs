using System;

namespace ProjectF.Simulation
{
    public enum WorldRestorePhase
    {
        None, Records, Chunks, Connections, Checkpoint, Ready, Failed
    }

    /// <summary>One readiness gate: a checkpoint callback must succeed before ticks may resume.</summary>
    public sealed class WorldRestoreProgress
    {
        public WorldRestorePhase Phase { get; private set; }
        public Exception Failure { get; private set; }
        public bool IsReady => Phase == WorldRestorePhase.Ready;
        public bool IsPending => Phase >= WorldRestorePhase.Records && Phase <= WorldRestorePhase.Checkpoint;

        public void Begin() { Failure = null; Phase = WorldRestorePhase.Records; }
        public void RecordsRestored() => Transition(WorldRestorePhase.Records, WorldRestorePhase.Chunks);
        public void BeginConnections() => Transition(WorldRestorePhase.Chunks, WorldRestorePhase.Connections);
        public void Complete(Action restoreCheckpoint)
        {
            Transition(WorldRestorePhase.Connections, WorldRestorePhase.Checkpoint);
            try
            {
                restoreCheckpoint?.Invoke();
                Transition(WorldRestorePhase.Checkpoint, WorldRestorePhase.Ready);
            }
            catch (Exception exception) { Fail(exception); throw; }
        }
        public void Fail(Exception exception) { Failure = exception; Phase = WorldRestorePhase.Failed; }
        private void Transition(WorldRestorePhase expected, WorldRestorePhase next)
        {
            if (Phase != expected) throw new InvalidOperationException($"World restore phase {Phase}; expected {expected}.");
            Phase = next;
        }
    }
}
