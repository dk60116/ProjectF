using UnityEngine;

public partial class TerrainGenerator : MonoBehaviour
{
    public bool IsChunkStreamingBusy => chunkStreamingScheduler != null && chunkStreamingScheduler.IsBusy;

    private void QueueChunkGeneration(Vector2Int chunkCoordinate, int normalizedChunkSize)
    {
        EnsureChunkStreamingScheduler().QueueGeneration(chunkCoordinate, normalizedChunkSize);
    }

    private void EnsureChunkGenerationProcessing()
    {
        EnsureChunkStreamingScheduler().EnsureGenerationProcessing();
    }

    private void ProcessQueuedChunkGenerationsImmediate()
    {
        EnsureChunkStreamingScheduler().ProcessQueuedGenerationsImmediate();
    }

    private void ClearPendingChunkGenerations()
    {
        chunkStreamingScheduler?.Clear();
        DisposeActiveSurfaceBuildJob();
        CleanupChunkGenerationTransientState();
    }

    private void CleanupChunkGenerationTransientState()
    {
        generatedChunkBlockScratch.Clear();
        CancelChunkAnimalSpawnWork();
        CancelChunkGenerationDiagnostics();
    }

    private bool IsChunkGenerationActive(Vector2Int chunkCoordinate)
    {
        return chunkStreamingScheduler != null && chunkStreamingScheduler.IsGenerationActive(chunkCoordinate);
    }

    private TerrainChunkStreamingScheduler EnsureChunkStreamingScheduler()
    {
        if (chunkStreamingScheduler != null)
        {
            return chunkStreamingScheduler;
        }

        chunkStreamingScheduler = new TerrainChunkStreamingScheduler(
            this,
            coordinate => loadedChunks.ContainsKey(coordinate),
            ShouldGenerateChunk,
            GenerateChunk,
            GenerateChunkRoutine,
            GenerateChunkCoroutineStepMarker,
            CleanupChunkGenerationTransientState);

        return chunkStreamingScheduler;
    }

    private bool ShouldGenerateChunk(Vector2Int chunkCoordinate)
    {
        return DoesChunkIntersectMapBounds(chunkCoordinate, Mathf.Max(4, chunkSize));
    }

    private int GetEffectiveLoadRadius()
    {
        int normalizedLoadRadius = Mathf.Max(0, loadRadius);

#if UNITY_EDITOR
        if (!Application.isPlaying && expandEditorPreviewRange)
        {
            return normalizedLoadRadius * 8;
        }
#endif

        return normalizedLoadRadius;
    }

    private static bool IsChunkWithinRadius(Vector2Int chunkCoordinate, Vector2Int centerChunk, int radius)
    {
        int normalizedRadius = Mathf.Max(0, radius);
        return Mathf.Abs(chunkCoordinate.x - centerChunk.x) <= normalizedRadius
               && Mathf.Abs(chunkCoordinate.y - centerChunk.y) <= normalizedRadius;
    }

    private static int GetChunkDistanceSqr(Vector2Int a, Vector2Int b)
    {
        int deltaX = a.x - b.x;
        int deltaY = a.y - b.y;
        return (deltaX * deltaX) + (deltaY * deltaY);
    }
}
