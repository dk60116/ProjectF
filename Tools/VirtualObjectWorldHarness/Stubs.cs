using System;
using System.Collections.Generic;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    public readonly struct Vector2Int : IEquatable<Vector2Int>
    {
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public readonly int x;
        public readonly int y;
        public bool Equals(Vector2Int other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is Vector2Int other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y);
        public static bool operator ==(Vector2Int left, Vector2Int right) => left.Equals(right);
        public static bool operator !=(Vector2Int left, Vector2Int right) => !left.Equals(right);
    }

    public readonly struct Vector3
    {
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public readonly float x;
        public readonly float y;
        public readonly float z;
    }

    public readonly struct Quaternion
    {
        private Quaternion(float y) { this.y = y; }
        public readonly float y;
        public static Quaternion identity => new Quaternion(0f);
        public static Quaternion Euler(float x, float y, float z) => new Quaternion(y);
    }

    public static class Mathf
    {
        public static int Max(int first, int second) => Math.Max(first, second);
    }
}

public static class Resource
{
    public struct ResourceSaveState
    {
        public int resourceCount;
        public int maxGauge;
        public int currentGauge;
        public int initialResourceCount;
    }
}

public readonly struct ResourceHandle
{
    public ResourceHandle(int value) { this.value = value; }
    private readonly int value;
    public bool IsValid => value > 0;
}

public sealed class Transform
{
    public UnityEngine.Vector3 position;
    public UnityEngine.Quaternion rotation = UnityEngine.Quaternion.identity;
}

public class InstallationObject
{
    private readonly int instanceId;
    public InstallationObject(int instanceId) { this.instanceId = instanceId; }
    public Transform transform { get; } = new Transform();
    public int GetInstanceID() => instanceId;
}

public sealed class ResourceInstance
{
    public UnityEngine.Vector3 WorldPosition { get; set; }
    public ResourceHandle Handle { get; set; }
}

public static class BlockStateStore
{
    public sealed class InstallationSaveState
    {
        public UnityEngine.Vector2Int anchorCoordinate;
        public bool hasStorageKey;
        public UnityEngine.Vector2Int storageKey;
        public int itemId;
        public int quarterTurns;
        public long placementSequence;
        public bool hasWorldPose;
        public UnityEngine.Vector3 worldPosition;
        public UnityEngine.Quaternion worldRotation = UnityEngine.Quaternion.identity;
        public List<UnityEngine.Vector2Int> occupiedCoordinates = new List<UnityEngine.Vector2Int>();

        public InstallationSaveState Clone()
        {
            return new InstallationSaveState
            {
                anchorCoordinate = anchorCoordinate,
                hasStorageKey = hasStorageKey,
                storageKey = storageKey,
                itemId = itemId,
                quarterTurns = quarterTurns,
                placementSequence = placementSequence,
                hasWorldPose = hasWorldPose,
                worldPosition = worldPosition,
                worldRotation = worldRotation,
                occupiedCoordinates = new List<UnityEngine.Vector2Int>(occupiedCoordinates)
            };
        }
    }

    public static UnityEngine.Vector2Int GetInstallationStorageKey(InstallationSaveState state)
    {
        return state.hasStorageKey ? state.storageKey : state.anchorCoordinate;
    }
}
