using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

[DisallowMultipleComponent, DefaultExecutionOrder(1000)]
public sealed class RobotArmRenderBatcher : MonoBehaviour
{
    private const float BatchCellSize = 8f;

    private static readonly ProfilerMarker RenderMarker =
        new ProfilerMarker("RobotArmRenderBatcher.Render");

    private readonly List<RobotArm> registeredRobotArms = new List<RobotArm>(64);
    private readonly HashSet<RobotArm> registeredRobotArmSet = new HashSet<RobotArm>();
    private readonly VirtualRenderBatchCollection batches = new VirtualRenderBatchCollection();
    private readonly ProjectF.Rendering.CameraRenderCulling cameraCulling =
        new ProjectF.Rendering.CameraRenderCulling();
    private Camera mainCamera;
    private bool registeredRobotArmsDirty;

    public int RegisteredRobotArmCount => registeredRobotArmSet.Count;
    public int LastVisibleRobotArmCount { get; private set; }
    public int LastCulledRobotArmCount { get; private set; }
    public int LastBuiltMatrixCount { get; private set; }
    public int ActiveBatchCount => batches.ActiveBatchCount;
    public int EstimatedDrawCallCount => batches.EstimatedDrawCallCount;

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
            if (mainCamera == null || !mainCamera.isActiveAndEnabled)
            {
                mainCamera = Camera.main;
            }

            cameraCulling.Update(mainCamera);
            RebuildBatches();

            if (registeredRobotArmSet.Count <= 0)
            {
                enabled = false;
                return;
            }

            if (batches.ActiveBatchCount <= 0)
            {
                return;
            }

            using (MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArmRenderBatcher), "Robot Arm Render Submit"))
            {
                batches.RenderBatches(mainCamera);
            }
        }
    }

    private void RebuildBatches()
    {
        using var sample = MapObjectTickProfiler.SampleNamed("Runtime", nameof(RobotArmRenderBatcher), "Robot Arm Render Build");
        if (registeredRobotArmsDirty)
        {
            CompactRegisteredRobotArms();
        }

        batches.ClearActiveMatrices();
        LastVisibleRobotArmCount = 0;
        LastCulledRobotArmCount = 0;
        LastBuiltMatrixCount = 0;
        for (int i = 0; i < registeredRobotArms.Count; i++)
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

            if (!robotArm.IsInstancedRenderVisible(cameraCulling))
            {
                LastCulledRobotArmCount++;
                continue;
            }

            LastVisibleRobotArmCount++;
            LastBuiltMatrixCount += robotArm.AppendInstancedRenderData(batches, BatchCellSize);
        }

        if (registeredRobotArmsDirty)
        {
            CompactRegisteredRobotArms();
        }
    }

    private void OnDisable()
    {
        LastVisibleRobotArmCount = 0;
        LastCulledRobotArmCount = registeredRobotArmSet.Count;
        LastBuiltMatrixCount = 0;
        batches.SuspendRendering();
    }

    private void OnDestroy()
    {
        batches.Dispose();
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
