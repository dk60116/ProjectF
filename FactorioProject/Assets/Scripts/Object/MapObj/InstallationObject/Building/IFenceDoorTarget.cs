using UnityEngine;

public interface IFenceDoorTarget : IMapObjectTarget
{
    bool IsOpen { get; }
    void ToggleOpenState(Vector3 interactorWorldPosition);
}
