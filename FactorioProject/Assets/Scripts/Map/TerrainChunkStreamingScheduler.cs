using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

internal sealed class TerrainChunkStreamingScheduler
{
    private readonly MonoBehaviour owner;
    private readonly Func<Vector2Int, bool> isChunkLoaded;
    private readonly Func<Vector2Int, bool> shouldGenerateChunk;
    private readonly Action<Vector2Int, int> generateChunkImmediate;
    private readonly Func<Vector2Int, int, bool, IEnumerator> createGenerateChunkRoutine;
    private readonly ProfilerMarker generateStepMarker;
    private readonly Queue<ChunkGenerationRequest> pendingChunkGenerations = new Queue<ChunkGenerationRequest>();
    private readonly HashSet<Vector2Int> pendingChunkGenerationCoordinates = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> activeChunkGenerationCoordinates = new HashSet<Vector2Int>();

    private Coroutine chunkGenerationCoroutine;

    public bool IsBusy =>
        pendingChunkGenerations.Count > 0
        || activeChunkGenerationCoordinates.Count > 0
        || chunkGenerationCoroutine != null;

    public TerrainChunkStreamingScheduler(
        MonoBehaviour owner,
        Func<Vector2Int, bool> isChunkLoaded,
        Func<Vector2Int, bool> shouldGenerateChunk,
        Action<Vector2Int, int> generateChunkImmediate,
        Func<Vector2Int, int, bool, IEnumerator> createGenerateChunkRoutine,
        ProfilerMarker generateStepMarker)
    {
        this.owner = owner;
        this.isChunkLoaded = isChunkLoaded;
        this.shouldGenerateChunk = shouldGenerateChunk;
        this.generateChunkImmediate = generateChunkImmediate;
        this.createGenerateChunkRoutine = createGenerateChunkRoutine;
        this.generateStepMarker = generateStepMarker;
    }

    public bool IsGenerationActive(Vector2Int chunkCoordinate)
    {
        return activeChunkGenerationCoordinates.Contains(chunkCoordinate);
    }

    public void QueueGeneration(Vector2Int chunkCoordinate, int normalizedChunkSize)
    {
        if (isChunkLoaded(chunkCoordinate)
            || pendingChunkGenerationCoordinates.Contains(chunkCoordinate)
            || activeChunkGenerationCoordinates.Contains(chunkCoordinate))
        {
            return;
        }

        pendingChunkGenerations.Enqueue(new ChunkGenerationRequest(chunkCoordinate, normalizedChunkSize));
        pendingChunkGenerationCoordinates.Add(chunkCoordinate);
    }

    public void EnsureGenerationProcessing()
    {
        if (pendingChunkGenerations.Count <= 0)
        {
            return;
        }

        if (!Application.isPlaying)
        {
            ProcessQueuedGenerationsImmediate();
            return;
        }

        if (chunkGenerationCoroutine == null)
        {
            chunkGenerationCoroutine = owner.StartCoroutine(ProcessGenerationQueue());
        }
    }

    public void ProcessQueuedGenerationsImmediate()
    {
        while (pendingChunkGenerations.Count > 0)
        {
            ChunkGenerationRequest request = pendingChunkGenerations.Dequeue();
            pendingChunkGenerationCoordinates.Remove(request.coordinate);
            if (!shouldGenerateChunk(request.coordinate))
            {
                continue;
            }

            activeChunkGenerationCoordinates.Add(request.coordinate);
            try
            {
                generateChunkImmediate(request.coordinate, request.chunkSize);
            }
            finally
            {
                MarkGenerationComplete(request.coordinate);
            }
        }
    }

    public void MarkGenerationComplete(Vector2Int chunkCoordinate)
    {
        activeChunkGenerationCoordinates.Remove(chunkCoordinate);
    }

    public void Clear()
    {
        pendingChunkGenerations.Clear();
        pendingChunkGenerationCoordinates.Clear();
        activeChunkGenerationCoordinates.Clear();

        if (chunkGenerationCoroutine != null)
        {
            owner.StopCoroutine(chunkGenerationCoroutine);
            chunkGenerationCoroutine = null;
        }

    }

    private IEnumerator ProcessGenerationQueue()
    {
        yield return null;

        while (pendingChunkGenerations.Count > 0)
        {
            ChunkGenerationRequest request = pendingChunkGenerations.Dequeue();
            pendingChunkGenerationCoordinates.Remove(request.coordinate);
            if (!shouldGenerateChunk(request.coordinate))
            {
                continue;
            }

            activeChunkGenerationCoordinates.Add(request.coordinate);
            IEnumerator chunkRoutine = createGenerateChunkRoutine(request.coordinate, request.chunkSize, true);
            try
            {
                while (true)
                {
                    bool hasNext;
                    object current = null;
                    using (generateStepMarker.Auto())
                    {
                        hasNext = chunkRoutine.MoveNext();
                        if (hasNext)
                        {
                            current = chunkRoutine.Current;
                        }
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    yield return current;
                }
            }
            finally
            {
                (chunkRoutine as IDisposable)?.Dispose();
                MarkGenerationComplete(request.coordinate);
            }

            // Do not start the next chunk in the same frame that just finished
            // restoration and mesh upload for the previous one. Those phases are
            // intentionally isolated so their costs cannot stack in one frame.
            if (pendingChunkGenerations.Count > 0)
            {
                yield return null;
            }
        }

        chunkGenerationCoroutine = null;
    }

    private readonly struct ChunkGenerationRequest
    {
        public readonly Vector2Int coordinate;
        public readonly int chunkSize;

        public ChunkGenerationRequest(Vector2Int coordinate, int chunkSize)
        {
            this.coordinate = coordinate;
            this.chunkSize = chunkSize;
        }
    }
}
