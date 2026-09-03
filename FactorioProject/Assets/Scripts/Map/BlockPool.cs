using System;
using UnityEngine;

/// <summary>
/// Serialization shim for scenes that still contain the former pool component.
/// Runtime blocks are now hosted directly by TerrainGenerator, so this component
/// intentionally creates and owns no GameObjects.
/// </summary>
[Obsolete("Per-block GameObject pooling was replaced by TerrainGenerator-hosted Block components.")]
[DisallowMultipleComponent]
public sealed class BlockPool : MonoBehaviour
{
}
