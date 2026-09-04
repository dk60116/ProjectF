#if UNITY_EDITOR
using System;
using ProjectF.Conveyors;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectF.Diagnostics
{
    internal static class ConveyorMotionTimingHarness
    {
        [MenuItem("Tools/ProjectF/Diagnostics/Validate Conveyor Motion Timing")]
        private static void Run()
        {
            // A long sleep before departure must not advance the new segment.
            ConveyorMotionTiming timing = ConveyorMotionTiming.FromPath(100f, 0.5f, 1f);
            Require(Mathf.Approximately(timing.Evaluate(100f), 0f), "restart must begin at zero");
            Require(Mathf.Approximately(timing.Evaluate(100.125f), 0.25f), "quarter-step travel");
            Require(Mathf.Approximately(timing.Evaluate(100.125f), 0.25f), "same-time reads must not add motion");
            Require(Mathf.Approximately(timing.Evaluate(101f), 1f), "late processing must stop at the reserved slot");

            ConveyorMotionTiming next = ConveyorMotionTiming.FromPath(101f, 0.5f, 1f);
            Require(Mathf.Approximately(next.Evaluate(101f), 0f), "lateness must not carry into the new plan");
            ConveyorMotionTiming restored = ConveyorMotionTiming.FromPath(200f, 0.5f, 1f, timing.Evaluate(100.25f));
            Require(Mathf.Approximately(restored.Evaluate(200f), 0.5f)
                && Mathf.Approximately(restored.Evaluate(200.125f), 0.75f), "restore must preserve progress and speed");
            Require(Mathf.Approximately(ConveyorMotionTiming.FromPath(0f, 0.5f, 1f).Evaluate(0.25f), 0.5f),
                "time zero is valid");
            Require(Mathf.Approximately(ConveyorMotionTiming.FromPath(0f, 0.5f, 1f, 0.5f).Evaluate(0.125f), 0.75f),
                "negative restored start time is valid");

            Scene scene = EditorSceneManager.NewPreviewScene();
            GameObject fixture = new GameObject("Conveyor Motion Timing Fixture");
            fixture.SetActive(false);
            SceneManager.MoveGameObjectToScene(fixture, scene);
            try
            {
                fixture.AddComponent<Block>().ValidateConveyorMotionTransfer(fixture.AddComponent<PortableObject>());
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }

            Debug.Log("[ConveyorMotionTimingHarness] Passed: restart, repeated sampling, late arrival, "
                + "save progress, time zero, linear/corner pickup motion transfer.");
        }

        internal static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Conveyor motion timing regression: " + message);
        }
    }
}

public partial class Block
{
    internal void ValidateConveyorMotionTransfer(PortableObject portableObject)
    {
        // This method is called only on the isolated, inactive editor fixture.
        ConveyorDataMotionState motion = new ConveyorDataMotionState
        {
            active = true,
            startTime = 10f,
            duration = 2f,
            sourceLaneIndex = 2,
            destinationLaneIndex = 0,
            startWorldPosition = Vector3.back,
            hasViaWorldPosition = true,
            viaWorldPosition = Vector3.left,
            pathLength = 2f,
            durationPathLength = 2.5f
        };
        conveyorItemMotionStates.Add(motion);
        ProjectF.Diagnostics.ConveyorMotionTimingHarness.Require(
            TransferConveyorDataMotionToPortable(0, portableObject)
            && !conveyorItemMotionStates[0].active, "virtual motion must release ownership");
        ConveyorLinearMotionState linear = conveyorLinearMotionStates[portableObject];
        ProjectF.Diagnostics.ConveyorMotionTimingHarness.Require(
            Mathf.Approximately(linear.timing.Evaluate(11f), 0.5f)
            && linear.startWorldPosition == motion.startWorldPosition
            && linear.hasViaWorldPosition && linear.viaWorldPosition == motion.viaWorldPosition,
            "linear preview must preserve time and path");

        motion.useCornerMotion = true;
        conveyorItemMotionStates[0] = motion;
        TransferConveyorDataMotionToPortable(0, portableObject);
        ConveyorCornerMotionState corner = conveyorCornerMotionStates[portableObject];
        ProjectF.Diagnostics.ConveyorMotionTimingHarness.Require(
            !conveyorItemMotionStates[0].active && !conveyorLinearMotionStates.ContainsKey(portableObject)
            && Mathf.Approximately(corner.timing.Evaluate(11f), 0.5f)
            && corner.sourceLaneIndex == 2 && corner.destinationLaneIndex == 0
            && corner.durationPathLength == 2.5f, "corner preview must preserve one motion owner");
        conveyorCornerMotionStates.Clear();
        conveyorItemMotionStates.Clear();
    }
}
#endif
