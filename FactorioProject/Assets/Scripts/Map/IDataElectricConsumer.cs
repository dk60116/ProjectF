using System.Collections.Generic;
using UnityEngine;

// Shared electric-network boundary for installations whose lifetime is data-owned.
public interface IDataElectricConsumer : IMapObjectSimulationIdentity
{
    bool IsRuntimeActive { get; }
    IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates { get; }
    Vector3 PowerLineWorldPosition { get; }
    bool TryGetElectricPowerRequirement(out float watts);
    bool TryGetElectricPowerDemand(out float watts);
    void WakeForElectricPowerChange();
}

