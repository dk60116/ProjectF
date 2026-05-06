using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class RobotArmRenderBatcher : MonoBehaviour
{
    private const float BatchCellSize = 8f;

    private static readonly ProfilerMarker RenderMarker =
        new ProfilerMarker("RobotArmRenderBatcher.Render");

    private readonly HashSet<RobotArm> registeredRobotArms = new HashSet<RobotArm>();
    private readonly List<RobotArm> cleanupBuffer = new List<RobotArm>(64);
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();

    public static RobotArmRenderBatcher EnsureFor(GameObject host)
    {
        if (host == null)
        {
            return null;
        }

        RobotArmRenderBatcher batcher = host.GetComponent<RobotArmRenderBatcher>();
        if (batcher == null)
        {
            batcher = host.AddComponent<RobotArmRenderBatcher>();
        }

        return batcher;
    }

    public void Register(RobotArm robotArm)
    {
        if (robotArm == null)
        {
            return;
        }

        registeredRobotArms.Add(robotArm);
    }

    public void Unregister(RobotArm robotArm)
    {
        if (robotArm == null)
        {
            return;
        }

        registeredRobotArms.Remove(robotArm);
    }

    private void LateUpdate()
    {
        using (RenderMarker.Auto())
        {
            batches.ClearActiveMatrices();
            cleanupBuffer.Clear();

            foreach (RobotArm robotArm in registeredRobotArms)
            {
                if (robotArm == null)
                {
                    cleanupBuffer.Add(robotArm);
                    continue;
                }

                robotArm.AppendInstancedRenderData(batches, BatchCellSize);
            }

            for (int i = 0; i < cleanupBuffer.Count; i++)
            {
                registeredRobotArms.Remove(cleanupBuffer[i]);
            }

            batches.RenderBatches();
        }
    }
}
