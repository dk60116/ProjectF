#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class TerrainGenerator
{
    [MenuItem("Tools/ProjectF/Diagnostics/Validate Conveyor Vacancy Wake")]
    private static void ValidateConveyorVacancyWake()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        GameObject fixture = new GameObject("Conveyor Vacancy Wake Fixture");
        fixture.SetActive(false);
        SceneManager.MoveGameObjectToScene(fixture, scene);
        try
        {
            // Keep Awake/OnEnable and the world simulation out of this test.
            TerrainGenerator terrain = fixture.AddComponent<TerrainGenerator>();
            terrain.ValidateConveyorVacancyRetry(false);
            terrain.ValidateConveyorVacancyRetry(true);
            Debug.Log("[ConveyorVacancyWakeHarness] Passed: blocked/ready retry cancellation, "
                + "pending range preservation, duplicate wake coalescing, next-frame delivery.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private void ValidateConveyorVacancyRetry(bool readyDelay)
    {
        const int lineId = 1;
        ConveyorLineWakeRange pendingRange = new ConveyorLineWakeRange(4, 12, false);
        ConveyorLineWakeRange vacancyRange = new ConveyorLineWakeRange(5, 7, false);
        conveyorLineRetryStatesById[lineId] = new ConveyorLineRetryState(
            pendingRange, Time.time + 100f, 3, readyDelay);

        // The old vacancy path used this ordinary wake and left it delayed.
        RequireConveyorVacancy(!QueueConveyorLineWake(lineId, vacancyRange)
            && conveyorLineWakeQueue.Count == 0, "fixture must reproduce a suppressed ordinary wake");

        QueueConveyorLineVacancyWake(lineId, vacancyRange);
        RequireConveyorVacancy(!conveyorLineRetryStatesById.ContainsKey(lineId)
            && !conveyorLineRetryAttemptsByDueLineId.ContainsKey(lineId), "retry must be cancelled");
        RequireConveyorVacancy(conveyorLineWakeRangesById.TryGetValue(lineId, out ConveyorLineWakeRange queued)
            && queued.minSlotIndex == 4 && queued.maxSlotIndex == 12, "pending range must survive");

        QueueConveyorLineVacancyWake(lineId, new ConveyorLineWakeRange(1, 2, false));
        queued = conveyorLineWakeRangesById[lineId];
        RequireConveyorVacancy(conveyorLineWakeQueue.Count == 1
            && queued.minSlotIndex == 1 && queued.maxSlotIndex == 12, "wakes must merge without duplication");

        // A vacancy after this line's tick must survive until the next frame.
        conveyorLineWakeQueue.Dequeue();
        conveyorLineWakeRangesById.Remove(lineId);
        conveyorLinesTickedThisFrame.Add(lineId);
        RequireConveyorVacancy(TryTickStraightConveyorLine(lineId, queued)
            && deferredConveyorLineWakeQueue.Count == 1, "already-ticked line must defer its wake");
        conveyorLinesTickedThisFrame.Clear();
        RequireConveyorVacancy(PromoteDeferredConveyorLineWakes() == 1
            && conveyorLineWakeQueue.Count == 1, "deferred vacancy must be delivered next frame");

        conveyorLineWakeQueue.Clear();
        conveyorLineWakeRangesById.Clear();
    }

    private static void RequireConveyorVacancy(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Conveyor vacancy wake regression: " + message);
        }
    }
}
#endif
