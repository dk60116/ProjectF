using System.Collections.Generic;
using ProjectF.MapObjects;
using ProjectF.Simulation;
using UnityEngine;

namespace ProjectF.Railway
{
    public readonly struct TrainConnection
    {
        internal TrainConnection(TrainInstance target, bool atFront)
        {
            Target = target;
            AtFront = atFront;
        }

        public TrainInstance Target { get; }
        public bool AtFront { get; }
    }

    // Calculation state has no vehicle, Transform or renderer dependency.
    // Released instances are never reused, so old references cannot address a pooled car.
    public sealed class TrainInstance
    {
        private const float StoredRailPointDeviationSqr = 0.000001f;
        internal readonly List<TrainConnection> Connections = new List<TrainConnection>(2);

        internal TrainInstance(TrainWorld world) { World = world; }

        public TrainWorld World { get; }
        public bool IsAlive => World.Contains(this);
        public MapObjectHandle Handle { get; internal set; }
        public long PlacementSequence { get; internal set; }
        public bool IsActive { get; internal set; }
        public bool IsPlaced { get; internal set; }
        public Vector2Int Coordinate { get; internal set; }
        public Vector3 WorldPosition { get; internal set; }
        public Quaternion WorldRotation { get; internal set; } = Quaternion.identity;
        public VehicleMotionState Motion;
        public IRailTarget Rail { get; private set; }
        public long RailDistanceUnits { get; internal set; }
        public Vector2 RailPoint { get; private set; }
        public Vector2 PhysicalForward { get; private set; }
        public IRailTarget ConnectionTargetRail { get; private set; }
        public long ConnectionTargetDistanceUnits { get; private set; }
        public Vector2 ConnectionTargetPoint { get; private set; }
        public Vector2 ConnectionTargetForward { get; private set; }
        public long ConnectionLengthUnits { get; private set; }
        public long ConnectionProgressUnits { get; private set; }
        public int ConnectionCount => Connections.Count;
        public TrainConnection GetConnection(int index) => Connections[index];

        internal int FindConnection(TrainInstance other)
        {
            for (int i = 0; i < Connections.Count; i++)
                if (ReferenceEquals(Connections[i].Target, other)) return i;
            return -1;
        }

        public bool TryGetConnectionFacingSign(TrainInstance other, bool otherIsAheadOnPath, out float sign)
        {
            sign = 1f;
            if (!IsAlive || other == null || !other.IsAlive) return false;
            int index = FindConnection(other);
            if (index < 0) return false;
            sign = Connections[index].AtFront == otherIsAheadOnPath ? 1f : -1f;
            return true;
        }

        public void SetRailSample(IRailTarget rail, float distance, Vector2 point, Vector2 physicalForward)
        {
            Rail = rail;
            RailDistanceUnits = DeterministicSimulationUnits.FromFloat(distance);
            RailPoint = point;
            PhysicalForward = physicalForward;
            ClearRailConnectionTransition();
        }

        public void ClearRailSample()
        {
            Rail = null;
            RailDistanceUnits = 0;
            RailPoint = PhysicalForward = Vector2.zero;
            ClearRailConnectionTransition();
        }

        public bool TryGetRailPose(out IRailTarget rail, out float distance, out Vector2 point, out Vector2 forward)
        {
            rail = null;
            distance = 0;
            point = forward = Vector2.zero;
            if (!IsAlive || !Rail.IsAlive()
                || !Rail.TrySampleRenderedPath(DeterministicSimulationUnits.ToFloat(RailDistanceUnits),
                    out Vector2 sampledPoint, out forward)) return false;

            point = RailPoint;
            // A junction bridge owns its interpolated point/front. Authored rail
            // tangent order must not replace the physical front on a reverse-built rail.
            if ((RailPoint - sampledPoint).sqrMagnitude > StoredRailPointDeviationSqr
                && PhysicalForward.sqrMagnitude > 0.0001f)
                forward = PhysicalForward;
            else if (PhysicalForward.sqrMagnitude > 0.0001f && forward.sqrMagnitude > 0.0001f
                && Vector2.Dot(forward, PhysicalForward.normalized) < 0)
                forward = -forward;

            rail = Rail;
            distance = DeterministicSimulationUnits.ToFloat(RailDistanceUnits);
            return true;
        }

        public void ConfigureRailConnectionTransition(IRailTarget target, float distance,
            Vector2 point, Vector2 physicalForward, float length, float progress)
        {
            if (target == null || length <= 0) { ClearRailConnectionTransition(); return; }
            ConnectionTargetRail = target;
            ConnectionTargetDistanceUnits = DeterministicSimulationUnits.FromFloat(distance);
            ConnectionTargetPoint = point;
            ConnectionTargetForward = physicalForward;
            ConnectionLengthUnits = DeterministicSimulationUnits.FromFloat(length);
            ConnectionProgressUnits = System.Math.Min(
                DeterministicSimulationUnits.FromFloat(Mathf.Max(0, progress)), ConnectionLengthUnits);
        }

        public bool TryGetRailConnectionTransition(out IRailTarget target, out float distance,
            out Vector2 point, out Vector2 physicalForward, out float length, out float progress)
        {
            target = ConnectionTargetRail;
            distance = DeterministicSimulationUnits.ToFloat(ConnectionTargetDistanceUnits);
            point = ConnectionTargetPoint;
            physicalForward = ConnectionTargetForward;
            length = DeterministicSimulationUnits.ToFloat(ConnectionLengthUnits);
            progress = DeterministicSimulationUnits.ToFloat(ConnectionProgressUnits);
            return IsAlive && target.IsAlive() && ConnectionLengthUnits > 0;
        }

        public void ClearRailConnectionTransition()
        {
            ConnectionTargetRail = null;
            ConnectionTargetDistanceUnits = ConnectionLengthUnits = ConnectionProgressUnits = 0;
            ConnectionTargetPoint = ConnectionTargetForward = Vector2.zero;
        }
    }
}
