using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class RobotArmRenderBatcher : MonoBehaviour
{
    private const float BatchCellSize = 8f;

    private static readonly ProfilerMarker RenderMarker =
        new ProfilerMarker("RobotArmRenderBatcher.Render");

    private readonly List<RobotArm> registeredRobotArms = new List<RobotArm>(64);
    private readonly HashSet<RobotArm> registeredRobotArmSet = new HashSet<RobotArm>();
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
    private Camera mainCamera;
    private bool registeredRobotArmsDirty;

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
        if (registeredRobotArmsDirty)
        {
            CompactRegisteredRobotArms();
        }

        if (robotArm == null || !registeredRobotArmSet.Add(robotArm))
        {
            return;
        }

        registeredRobotArms.Add(robotArm);
        enabled = true;
    }

    public void Unregister(RobotArm robotArm)
    {
        if (robotArm == null || !registeredRobotArmSet.Remove(robotArm))
        {
            return;
        }

        registeredRobotArmsDirty = true;
        if (registeredRobotArmSet.Count <= 0)
        {
            batches.ClearActiveMatrices();
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (registeredRobotArmSet.Count <= 0)
        {
            batches.ClearActiveMatrices();
            enabled = false;
            return;
        }

        using (RenderMarker.Auto())
        {
            if (registeredRobotArmsDirty)
            {
                CompactRegisteredRobotArms();
            }

            batches.ClearActiveMatrices();

            int count = registeredRobotArms.Count;
            for (int i = 0; i < count; i++)
            {
                RobotArm robotArm = registeredRobotArms[i];
                if (robotArm == null)
                {
                    registeredRobotArmSet.Remove(robotArm);
                    registeredRobotArmsDirty = true;
                    continue;
                }

                if (registeredRobotArmsDirty && !registeredRobotArmSet.Contains(robotArm))
                {
                    continue;
                }

                robotArm.AppendInstancedRenderData(batches, BatchCellSize);
            }

            if (registeredRobotArmsDirty)
            {
                CompactRegisteredRobotArms();
            }

            if (registeredRobotArmSet.Count <= 0)
            {
                enabled = false;
                return;
            }

            if (batches.ActiveBatchCount <= 0)
            {
                return;
            }

            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }

            batches.RenderBatches(mainCamera);
        }
    }

    private void CompactRegisteredRobotArms()
    {
        for (int i = registeredRobotArms.Count - 1; i >= 0; i--)
        {
            RobotArm robotArm = registeredRobotArms[i];
            if (robotArm != null && registeredRobotArmSet.Contains(robotArm))
            {
                continue;
            }

            registeredRobotArms.RemoveAt(i);
        }

        registeredRobotArmsDirty = false;
    }
}
