namespace ProjectF.Simulation
{
    public enum PlantingOperatingState { Ready, LoadingSeed, Planting, NoSeeds, NoPower, InvalidGround, TargetOccupied }

    [System.Serializable]
    public struct LoggingProcess
    {
        public int Direction;
        public float HingeAngle, EmptyDirectionElapsed;
        public long ConsumedEnergyUnits;
        public GridCell TargetCoordinate;
        public string TargetDefinitionKey;
        public bool HasTarget;
        public void ClearTarget()
        { HasTarget = false; ConsumedEnergyUnits = 0; TargetDefinitionKey = null; }
    }

    public struct PlantingProcess
    {
        public long ElapsedUnits, TransferRemainingUnits;
        public bool HasLoadedSeed;
        public int LoadedSeedItemId;
        public GridCell LoadedSeedInputCoordinate;
        public PlantingOperatingState OperatingState;
        public int CurrentSeedItemId, CurrentSeedCount;
        public GridCell CurrentInputCoordinate;
        public bool HasCurrentInputCoordinate, RequestingPower, IsOperating;
    }
}
