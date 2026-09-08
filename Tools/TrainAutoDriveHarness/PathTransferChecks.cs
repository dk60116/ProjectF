using System;
using System.Collections.Generic;
using UnityEngine;

// Production tape transfer, reversal, interpolation and pose validation run here.
// Rails provide deterministic samples; Unity physics is absent.
public partial class RailHandcar
{
    readonly List<ConsistPathSample> consistPathTape = new();
    readonly List<ConsistPathSample> leaderPathFrameScratch = new();
    readonly List<ConsistPathSample> initialConsistSegmentScratch = new();
    readonly List<ConsistPathSample> reversedConsistPathScratch = new();
    readonly List<Train> consistPathTrainOrder = new();
    readonly List<float> consistPathFollowOffsets = new();
    readonly List<float> reversedConsistPathFollowOffsetsScratch = new();
    readonly List<ConnectedTrainRailMove> connectedTrainRailMoveScratch = new();
    Train consistPathLeader;
    Vector2 consistPathTravelDirection;
    float consistPathEndDistance;
    string railMoveFailureReason;
    void RecordRailMoveFailure(string reason) => railMoveFailureReason = reason;
    float ResolveConsistPathSampleRetainDistance(Train train) => .01f;
    float ResolveRailConnectionMaxDistance() => .2f;
    static float ResolveRailTransitionMovementDistance(RailSample a, RailSample b)
        => Vector2.Distance(a.Point, b.Point);
    public bool HasRecordedPath => consistPathTape.Count > 0;
    public static float ProbeConsistFollowOffset(
        bool rebuildingPathTape,
        float leaderPathDistance,
        float actualPathDistanceFromTail,
        float desiredFollowOffset)
        => ResolveConsistFollowOffset(
            rebuildingPathTape,
            leaderPathDistance,
            actualPathDistanceFromTail,
            desiredFollowOffset);

    static RailSample Sample(Railload rail, float distance)
    {
        TryCreateRailSampleAtDistance(rail, distance, out RailSample sample);
        return sample;
    }

    public void SeedStraightPath(Train left, Train middle, Train right)
    {
        ResetConsistPathTape();
        foreach (Train train in new[] { left, middle, right })
            AddConsistPathSample(consistPathTape, train.Distance - left.Distance, Sample(train.Rail, train.Distance));
        SetRecordedOrder(right, middle, left, right.Distance - middle.Distance, right.Distance - left.Distance);
    }

    void SetRecordedOrder(Train leader, Train middle, Train tail, float middleOffset, float tailOffset)
    {
        consistPathTrainOrder.AddRange(new[] { leader, middle, tail });
        consistPathFollowOffsets.AddRange(new[] { 0f, middleOffset, tailOffset });
        consistPathLeader = leader;
        consistPathTravelDirection = Vector2.right;
        consistPathEndDistance = tailOffset;
    }

    public void SeedJunctionPath(Train left, Train middle, Train right)
    {
        ResetConsistPathTape();
        RailSample a = Sample(left.Rail, 2f);
        RailSample b = Sample(right.Rail, 2f);
        TryCreateRailConnectionBridgeSample(a, b, .1f, .05f, out RailSample bridge);
        AddConsistPathSample(consistPathTape, 0f, Sample(left.Rail, left.Distance));
        AddConsistPathSample(consistPathTape, .9f, a);
        AddConsistPathSample(consistPathTape, .95f, bridge);
        AddConsistPathSample(consistPathTape, 1f, b);
        AddConsistPathSample(consistPathTape, 2f, Sample(right.Rail, right.Distance));
        SetRecordedOrder(right, middle, left, 1f, 2f);
    }

    public void GivePathTo(RailHandcar next) => TransferConsistPathTo(next);

    public bool ReverseRecordedPath(params Train[] newOrder)
    {
        connectedTrainRailMoveScratch.Clear();
        foreach (Train train in newOrder)
        {
            TryBuildCurrentRailSample(train, out RailSample sample);
            connectedTrainRailMoveScratch.Add(new ConnectedTrainRailMove { Train = train, StartSample = sample });
        }
        return TryReverseConsistPathTape(Vector2.left) && IsConsistPathTapeValid(Vector2.left);
    }

    public bool CheckReversedBridge(Railload from, Railload to)
    {
        foreach (ConsistPathSample entry in consistPathTape)
        {
            RailSample sample = entry.Sample;
            if (HasRailConnectionBridgeState(sample))
                return sample.Rail == from && sample.ConnectionTargetRail == to
                    && Math.Abs(sample.ConnectionProgress - .05f) < .0001f;
        }
        return false;
    }

    public Vector2 SampleReturnStep(int index, float step)
    {
        Train leader = consistPathTrainOrder[0];
        AddConsistPathSample(consistPathTape, consistPathEndDistance + step, Sample(leader.Rail, leader.Distance - step));
        if (!TrySampleConsistPathTape(consistPathEndDistance + step - consistPathFollowOffsets[index], out RailSample sample, false))
            throw new Exception("Return movement could not sample a follower: " + railMoveFailureReason);
        return sample.Point;
    }
}

public partial class SteamTrain
{
    static void RunPathTransferChecks()
    {
        Check(Math.Abs(RailHandcar.ProbeConsistFollowOffset(true, 2.004f, 1.002f, 1f) - 1.002f) < .0001f,
            "Initial path alignment must use the car's actual path position");
        Check(Math.Abs(RailHandcar.ProbeConsistFollowOffset(false, 2.004f, 1.002f, 1f) - 1f) < .0001f,
            "Moving consist correction must still converge on one-cell spacing");
        var a = new Railload();
        var b = new Railload { Origin = new Vector2(4.1f, 0), Direction = Vector2.left };
        var left = Engine(1.1f, Vector2.left); left.Rail = a;
        var middle = new Train { Rail = b, Distance = 2f };
        var right = Engine(1f, Vector2.right); right.Rail = b;
        Train.Link(left, middle); Train.Link(middle, right);
        right.SeedJunctionPath(left, middle, right);
        Check(!left.HasRecordedPath, "Inactive return locomotive must start without a movement tape");
        right.GivePathTo(left);
        Check(left.HasRecordedPath && !right.HasRecordedPath, "The live path must move to the new driver");
        Check(left.ReverseRecordedPath(left, middle, right), "A transferred route across oppositely authored rails must reverse with the new leader");
        Check(left.CheckReversedBridge(b, a), "Reversal must preserve junction progress and swap its endpoints");
        for (int i = 0; i < 3; i++)
            Check(Vector2.Distance(left.SampleReturnStep(i, .05f), new Vector2(1.05f + i, 0)) < .0001f,
                "Every car must advance on the return path with one-cell spacing, including the junction");

        left.GivePathTo(right);
        Check(right.HasRecordedPath && !left.HasRecordedPath, "Repeated handoff must replace an earlier leg's route");
        var stranger = Engine(1f, Vector2.left);
        right.GivePathTo(stranger);
        Check(!stranger.HasRecordedPath && right.HasRecordedPath, "An unrelated train must not inherit the route");
        left.Distance += 5f;
        right.GivePathTo(left);
        Check(!left.HasRecordedPath, "A route whose cars were moved since recording must not be reused");
    }
}
