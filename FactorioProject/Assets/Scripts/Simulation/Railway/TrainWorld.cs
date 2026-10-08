using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Railway
{
    public sealed partial class TrainWorld
    {
        public static TrainWorld Shared { get; } = new TrainWorld();
        private readonly HashSet<TrainInstance> instances = new HashSet<TrainInstance>();
        private readonly Dictionary<Vector2Int, List<TrainInstance>> byCoordinate =
            new Dictionary<Vector2Int, List<TrainInstance>>();
        private readonly Stack<List<TrainInstance>> emptyBuckets = new Stack<List<TrainInstance>>();

        public int Count => instances.Count;
        public ulong ConnectionGraphRevision { get; private set; }
        public float MaxConnectionSnapDistance { get; private set; }
        public float MaxConnectionLateralDistance { get; private set; }

        public void IncludeConnectionRange(float snapDistance, float lateralDistance)
        {
            // Conservative maxima stay valid across pooling without rescanning the fleet.
            MaxConnectionSnapDistance = Mathf.Max(MaxConnectionSnapDistance, snapDistance);
            MaxConnectionLateralDistance = Mathf.Max(MaxConnectionLateralDistance, lateralDistance);
        }

        public TrainInstance Create()
        {
            var instance = new TrainInstance(this);
            instances.Add(instance);
            return instance;
        }

        public bool Contains(TrainInstance instance) => instance != null && instances.Contains(instance);

        public void SetPlacement(TrainInstance instance, bool placed, long sequence)
        {
            if (!Contains(instance) || instance.IsPlaced == placed && instance.PlacementSequence == sequence) return;
            Unindex(instance);
            instance.IsPlaced = placed;
            instance.PlacementSequence = sequence;
            Index(instance);
        }

        public void SetActive(TrainInstance instance, bool active)
        {
            if (!Contains(instance) || instance.IsActive == active) return;
            Unindex(instance);
            instance.IsActive = active;
            Index(instance);
        }

        public void SetWorldPose(TrainInstance instance, Vector3 position, Quaternion rotation)
        {
            if (!Contains(instance)) return;
            var coordinate = new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z));
            if (instance.Coordinate != coordinate)
            {
                Unindex(instance);
                instance.Coordinate = coordinate;
                Index(instance);
            }
            instance.WorldPosition = position;
            instance.WorldRotation = rotation;
        }

        public void CollectNearby(Vector2 point, float radius, List<TrainInstance> results)
        {
            results.Clear();
            radius = Mathf.Max(0, radius);
            int minX = Mathf.RoundToInt(point.x - radius), maxX = Mathf.RoundToInt(point.x + radius);
            int minY = Mathf.RoundToInt(point.y - radius), maxY = Mathf.RoundToInt(point.y + radius);
            for (int x = minX; x <= maxX; x++) for (int y = minY; y <= maxY; y++)
                if (byCoordinate.TryGetValue(new Vector2Int(x, y), out var bucket))
                    for (int i = 0; i < bucket.Count; i++) results.Add(bucket[i]);
        }

        public bool Connect(TrainInstance first, TrainInstance second, bool firstAtFront, bool secondAtFront)
        {
            if (!Contains(first) || !Contains(second) || ReferenceEquals(first, second)) return false;
            bool changed = AddConnection(first, second, firstAtFront);
            changed |= AddConnection(second, first, secondAtFront);
            if (changed) IncrementGraphRevision();
            return changed;
        }

        private static bool AddConnection(TrainInstance first, TrainInstance second, bool atFront)
        {
            if (first.FindConnection(second) >= 0) return false;
            first.Connections.Add(new TrainConnection(second, atFront));
            return true;
        }

        public void SetConnectionEnd(TrainInstance first, TrainInstance second, bool atFront)
        {
            if (!Contains(first) || !Contains(second)) return;
            int index = first.FindConnection(second);
            if (index < 0 || first.Connections[index].AtFront == atFront) return;
            first.Connections[index] = new TrainConnection(second, atFront);
            IncrementGraphRevision();
        }

        public void Disconnect(TrainInstance first, TrainInstance second)
        {
            if (!Contains(first) || !Contains(second)) return;
            bool changed = RemoveConnection(first, second);
            changed |= RemoveConnection(second, first);
            if (changed) IncrementGraphRevision();
        }

        private static bool RemoveConnection(TrainInstance first, TrainInstance second)
        {
            int index = first.FindConnection(second);
            if (index < 0) return false;
            first.Connections.RemoveAt(index);
            return true;
        }

        public void ClearConnections(TrainInstance instance)
        {
            if (!Contains(instance)) return;
            while (instance.ConnectionCount > 0)
                Disconnect(instance, instance.GetConnection(instance.ConnectionCount - 1).Target);
        }

        public void Release(TrainInstance instance)
        {
            if (!Contains(instance)) return;
            ClearConnections(instance);
            Unindex(instance);
            RemoveView(instance);
            instances.Remove(instance);
            instance.ClearRailSample();
            instance.Motion = default;
            instance.Handle = default;
            instance.IsActive = instance.IsPlaced = false;
        }

        public void Clear()
        {
            foreach (var instance in instances)
            {
                instance.Connections.Clear();
                instance.ClearRailSample();
                instance.Motion = default;
                instance.Handle = default;
                instance.IsActive = instance.IsPlaced = false;
            }
            instances.Clear();
            byCoordinate.Clear();
            emptyBuckets.Clear();
            MaxConnectionSnapDistance = MaxConnectionLateralDistance = 0;
            ClearViews();
            IncrementGraphRevision();
        }

        private void Index(TrainInstance instance)
        {
            if (!instance.IsActive || !instance.IsPlaced) return;
            if (!byCoordinate.TryGetValue(instance.Coordinate, out var bucket))
            {
                bucket = emptyBuckets.Count > 0 ? emptyBuckets.Pop() : new List<TrainInstance>(2);
                byCoordinate.Add(instance.Coordinate, bucket);
            }
            bucket.Add(instance);
        }

        private void Unindex(TrainInstance instance)
        {
            if (!instance.IsActive || !instance.IsPlaced
                || !byCoordinate.TryGetValue(instance.Coordinate, out var bucket)) return;
            bucket.Remove(instance);
            if (bucket.Count > 0) return;
            byCoordinate.Remove(instance.Coordinate);
            emptyBuckets.Push(bucket);
        }

        private void IncrementGraphRevision()
        {
            unchecked
            {
                ConnectionGraphRevision++;
                if (ConnectionGraphRevision == 0) ConnectionGraphRevision = 1;
            }
        }

        partial void RemoveView(TrainInstance instance);
        partial void ClearViews();
    }
}
