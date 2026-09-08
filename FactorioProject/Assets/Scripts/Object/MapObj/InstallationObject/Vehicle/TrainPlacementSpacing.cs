using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Trains
{
    // Complete-only layout: contract each connected chain along the route it already
    // occupies. Planning every pose first avoids partially moving an invalid chain.
    internal sealed class TrainPlacementSpacing
    {
        private const float Epsilon = 0.0001f;
        private readonly HashSet<Train> visited = new HashSet<Train>();
        private readonly List<Train> group = new List<Train>();
        private readonly List<Train> order = new List<Train>();
        private readonly List<Pose> originals = new List<Pose>();
        private readonly List<Pose> targets = new List<Pose>();
        private readonly List<float> originalOffsets = new List<float>();
        private readonly List<Segment> route = new List<Segment>();
        private float routeLength;

        private struct Pose
        {
            public Railload Rail;
            public float Distance;
            public Vector2 Point;
            public Vector2 Facing;
            public Railload BridgeTarget;
            public float BridgeTargetDistance;
            public Vector2 BridgeTargetPoint;
            public Vector2 BridgeTargetTangent;
            public float BridgeLength;
            public float BridgeProgress;
        }

        private struct Segment
        {
            public Pose Start;
            public Pose End;
            public float Offset;
            public float Length;
            public float BridgeStartProgress;
            public float BridgeEndProgress;
        }

        public bool AlignPlacedTrains(IReadOnlyList<MapObject> placedObjects)
        {
            bool success = true;
            visited.Clear();
            for (int i = 0; i < placedObjects.Count; i++)
            {
                if (placedObjects[i] is Train train && !visited.Contains(train))
                {
                    success &= TryAlignGroup(train);
                }
            }

            visited.Clear();
            group.Clear();
            order.Clear();
            originals.Clear();
            targets.Clear();
            originalOffsets.Clear();
            route.Clear();
            return success;
        }

        private bool TryAlignGroup(Train seed)
        {
            group.Clear();
            group.Add(seed);
            visited.Add(seed);
            for (int i = 0; i < group.Count; i++)
            {
                foreach (Train next in group[i].ConnectedTrains)
                {
                    if (next != null && next.gameObject.activeInHierarchy && visited.Add(next))
                    {
                        group.Add(next);
                    }
                }
            }

            if (group.Count < 2)
            {
                return true;
            }

            Train anchor = null;
            for (int i = 0; i < group.Count; i++)
            {
                Train candidate = group[i];
                if (candidate.ConnectedTrains.Count == 1
                    && (anchor == null || candidate.RuntimePlacementSequence < anchor.RuntimePlacementSequence))
                {
                    anchor = candidate;
                }
            }

            order.Clear();
            Train previous = null;
            while (anchor != null)
            {
                if (order.Contains(anchor)) return false;
                order.Add(anchor);
                Train next = null;
                foreach (Train candidate in anchor.ConnectedTrains)
                {
                    if (candidate == null || candidate == previous) continue;
                    if (next != null) return false;
                    next = candidate;
                }

                previous = anchor;
                anchor = next;
            }

            if (order.Count != group.Count || !TryPlanPoses())
            {
                return false;
            }

            for (int i = 0; i < order.Count; i++)
            {
                Train train = order[i];
                Pose target = targets[i];
                if (train is RailHandcar handcar)
                {
                    handcar.ResetRailPlacementState();
                    handcar.TryApplyExplicitRailPose(target.Rail, target.Distance, target.Point, target.Facing);
                }
                else
                {
                    train.TryApplyRailPose(target.Rail, target.Distance, target.Point, target.Facing);
                }

                if (target.BridgeTarget != null)
                {
                    train.ConfigureCurrentRailConnectionTransition(
                        target.BridgeTarget, target.BridgeTargetDistance,
                        target.BridgeTargetPoint, target.BridgeTargetTangent,
                        target.BridgeLength, target.BridgeProgress);
                }
            }

            return true;
        }

        private bool TryPlanPoses()
        {
            originals.Clear();
            originalOffsets.Clear();
            targets.Clear();
            route.Clear();
            routeLength = 0f;
            for (int i = 0; i < order.Count; i++)
            {
                if (!TryReadPose(order[i], out Pose pose)) return false;
                originals.Add(pose);
                if (i > 0 && !AppendPair(originals[i - 1], pose)) return false;
                originalOffsets.Add(routeLength);
            }

            float requiredLength = (order.Count - 1) * Train.ConnectionCenterDistance;
            if (routeLength + Epsilon < requiredLength && !TryExtendTail(requiredLength))
            {
                return false;
            }

            for (int i = 0; i < order.Count; i++)
            {
                if (!TrySample(i * Train.ConnectionCenterDistance, out Pose target)
                    || !TrySample(originalOffsets[i], out Pose originalRoutePose)) return false;
                if (Vector2.Dot(originals[i].Facing, originalRoutePose.Facing) < 0f)
                {
                    target.Facing = -target.Facing;
                }

                targets.Add(target);
            }

            return true;
        }

        private bool TryExtendTail(float requiredLength)
        {
            if (!TrySample(routeLength, out Pose tail)) return false;
            if (tail.BridgeTarget != null)
            {
                float endpointProgress = Vector2.Dot(tail.Facing, tail.BridgeTargetPoint - tail.Point) >= 0f
                    ? tail.BridgeLength : 0f;
                if (!AppendBridge(tail, tail.BridgeProgress, endpointProgress)
                    || !TryGetBridgeEndpoint(tail, endpointProgress > 0f, out Pose endpoint)) return false;
                endpoint.Facing = Vector2.Dot(endpoint.Facing, tail.Facing) >= 0f ? endpoint.Facing : -endpoint.Facing;
                tail = endpoint;
            }

            if (routeLength + Epsilon >= requiredLength) return true;
            if (!tail.Rail.TrySampleRenderedPath(tail.Distance, out _, out Vector2 tangent)
                || !tail.Rail.TryGetRenderedPathLength(out float railLength)) return false;
            float sign = Vector2.Dot(tail.Facing, tangent) >= 0f ? 1f : -1f;
            float distance = tail.Distance + sign * (requiredLength - routeLength);
            if (distance < 0f || distance > railLength) return false;
            Pose end = tail;
            end.Distance = distance;
            if (!end.Rail.TrySampleRenderedPath(distance, out end.Point, out end.Facing)) return false;
            AppendSegment(tail, end, requiredLength - routeLength);
            return true;
        }

        private static bool TryReadPose(Train train, out Pose pose)
        {
            pose = default;
            if (!train.TryGetCurrentRailPose(out pose.Rail, out pose.Distance, out pose.Point, out pose.Facing))
            {
                return false;
            }

            train.TryGetCurrentRailConnectionTransition(
                out pose.BridgeTarget, out pose.BridgeTargetDistance,
                out pose.BridgeTargetPoint, out pose.BridgeTargetTangent,
                out pose.BridgeLength, out pose.BridgeProgress);

            return pose.Facing.sqrMagnitude > Epsilon;
        }

        private bool AppendPair(Pose from, Pose to)
        {
            if (from.BridgeTarget != null)
            {
                if (from.Rail == to.Rail && from.BridgeTarget == to.BridgeTarget
                    && Mathf.Abs(from.Distance - to.Distance) < Epsilon)
                {
                    return AppendBridge(from, from.BridgeProgress, to.BridgeProgress);
                }

                bool forward = Vector2.Dot(to.Point - from.Point, from.BridgeTargetPoint - from.Point) >= 0f;
                if (!TryGetBridgeEndpoint(from, forward, out Pose endpoint)) return false;
                return AppendBridge(from, from.BridgeProgress, forward ? from.BridgeLength : 0f)
                       && AppendPair(endpoint, to);
            }

            if (to.BridgeTarget != null)
            {
                bool approachFromTarget = Vector2.Dot(from.Point - to.Point, to.BridgeTargetPoint - to.Point) > 0f;
                if (!TryGetBridgeEndpoint(to, approachFromTarget, out Pose endpoint)) return false;
                return AppendPair(from, endpoint)
                       && AppendBridge(to, approachFromTarget ? to.BridgeLength : 0f, to.BridgeProgress);
            }

            if (from.Rail == to.Rail)
            {
                AppendSegment(from, to, Mathf.Abs(to.Distance - from.Distance));
                return true;
            }

            float bestLength = float.PositiveInfinity;
            Pose leftJoin = default;
            Pose rightJoin = default;
            // Use the cars' current rails to keep the selected branch. Connections
            // can meet either an endpoint or the interior of the adjacent rail.
            for (int i = 0; i < 4; i++)
            {
                bool reverse = i >= 2;
                Pose left = reverse ? to : from;
                Pose right = reverse ? from : to;
                if (!left.Rail.TryGetRenderedEndpointSample(i % 2 == 0,
                        out float leftDistance, out Vector2 leftPoint, out Vector2 leftTangent)
                    || !right.Rail.TryFindNearestRenderedPathSample(leftPoint,
                        out float rightDistance, out Vector2 rightPoint, out Vector2 rightTangent, out float gapSqr)
                    || gapSqr > RailConnectionUtility.ConnectionDistance * RailConnectionUtility.ConnectionDistance)
                {
                    continue;
                }

                float length = Mathf.Abs(left.Distance - leftDistance) + Mathf.Sqrt(gapSqr)
                               + Mathf.Abs(right.Distance - rightDistance);
                if (length >= bestLength) continue;
                bestLength = length;
                Pose leftCandidate = new Pose { Rail = left.Rail, Distance = leftDistance, Point = leftPoint, Facing = leftTangent };
                Pose rightCandidate = new Pose { Rail = right.Rail, Distance = rightDistance, Point = rightPoint, Facing = rightTangent };
                leftJoin = reverse ? rightCandidate : leftCandidate;
                rightJoin = reverse ? leftCandidate : rightCandidate;
            }

            if (float.IsPositiveInfinity(bestLength)) return false;
            AppendSegment(from, leftJoin, Mathf.Abs(leftJoin.Distance - from.Distance));
            AppendSegment(leftJoin, rightJoin, Vector2.Distance(leftJoin.Point, rightJoin.Point));
            AppendSegment(rightJoin, to, Mathf.Abs(to.Distance - rightJoin.Distance));
            return true;
        }

        private void AppendSegment(Pose from, Pose to, float length)
        {
            if (length <= Epsilon) return;
            route.Add(new Segment { Start = from, End = to, Length = length, Offset = routeLength, BridgeEndProgress = length });
            routeLength += length;
        }

        private static bool TryGetBridgeEndpoint(Pose bridge, bool target, out Pose endpoint)
        {
            endpoint = default;
            endpoint.Rail = target ? bridge.BridgeTarget : bridge.Rail;
            endpoint.Distance = target ? bridge.BridgeTargetDistance : bridge.Distance;
            return endpoint.Rail != null
                   && endpoint.Rail.TrySampleRenderedPath(endpoint.Distance, out endpoint.Point, out endpoint.Facing);
        }

        private bool AppendBridge(Pose bridge, float startProgress, float endProgress)
        {
            float length = Mathf.Abs(endProgress - startProgress);
            if (length <= Epsilon) return true;
            if (!TryGetBridgeEndpoint(bridge, false, out Pose start)
                || !TryGetBridgeEndpoint(bridge, true, out Pose end)) return false;
            route.Add(new Segment
            {
                Start = start, End = end, Length = length, Offset = routeLength,
                BridgeStartProgress = startProgress, BridgeEndProgress = endProgress
            });
            routeLength += length;
            return true;
        }

        private bool TrySample(float distance, out Pose pose)
        {
            pose = default;
            if (distance < -Epsilon || distance > routeLength + Epsilon) return false;
            for (int i = 0; i < route.Count; i++)
            {
                Segment segment = route[i];
                if (distance > segment.Offset + segment.Length + Epsilon) continue;
                float along = Mathf.Clamp(distance - segment.Offset, 0f, segment.Length);
                float t = along / segment.Length;
                pose = segment.Start;
                if (segment.Start.Rail == segment.End.Rail)
                {
                    pose.Distance = Mathf.Lerp(segment.Start.Distance, segment.End.Distance, t);
                    if (!pose.Rail.TrySampleRenderedPath(pose.Distance, out pose.Point, out pose.Facing)) return false;
                    pose.Facing *= Mathf.Sign(segment.End.Distance - segment.Start.Distance);
                }
                else
                {
                    float bridgeLength = Vector2.Distance(segment.Start.Point, segment.End.Point);
                    float progress = Mathf.Lerp(segment.BridgeStartProgress, segment.BridgeEndProgress, t);
                    pose.Point = Vector2.Lerp(segment.Start.Point, segment.End.Point, progress / bridgeLength);
                    pose.Facing = (segment.End.Point - segment.Start.Point).normalized
                                  * Mathf.Sign(segment.BridgeEndProgress - segment.BridgeStartProgress);
                    pose.BridgeTarget = segment.End.Rail;
                    pose.BridgeTargetDistance = segment.End.Distance;
                    pose.BridgeTargetPoint = segment.End.Point;
                    pose.BridgeTargetTangent = segment.End.Facing;
                    pose.BridgeLength = bridgeLength;
                    pose.BridgeProgress = progress;
                }

                return true;
            }

            return false;
        }
    }
}
