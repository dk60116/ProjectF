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
    private readonly Func<Vector2Int, bool> shouldUnloadChunk;
    private readonly Action<Vector2Int, int> generateChunkImmediate;
    private readonly Func<Vector2Int, int, bool, IEnumerator> createGenerateChunkRoutine;
    private readonly Action<Vector2Int> unloadChunkImmediate;
    private readonly Func<Vector2Int, bool, IEnumerator> createUnloadChunkRoutine;
    private readonly Func<int> getChunkUnloadBudget;
    private readonly ProfilerMarker generateStepMarker;
    private readonly ProfilerMarker unloadStepMarker;
    private readonly Queue<ChunkGenerationRequest> pendingChunkGenerations = new Queue<ChunkGenerationRequest>();
    private readonly HashSet<Vector2Int> pendingChunkGenerationCoordinates = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> activeChunkGenerationCoordinates = new HashSet<Vector2Int>();
    private readonly Queue<Vector2Int> pendingChunkUnloads = new Queue<Vector2Int>();
    private readonly HashSet<Vector2Int> pendingChunkUnloadCoordinates = new HashSet<Vector2Int>();

    private Coroutine chunkGenerationCoroutine;
    private Coroutine chunkUnloadCoroutine;

    public TerrainChunkStreamingScheduler(
        MonoBehaviour owner,
        Func<Vector2Int, bool> isChunkLoaded,
        Func<Vector2Int, bool> shouldGenerateChunk,
        Func<Vector2Int, bool> shouldUnloadChunk,
        Action<Vector2Int, int> generateChunkImmediate,
        Func<Vector2Int, int, bool, IEnumerator> createGenerateChunkRoutine,
        Action<Vector2Int> unloadChunkImmediate,
        Func<Vector2Int, bool, IEnumerator> createUnloadChunkRoutine,
        Func<int> getChunkUnloadBudget,
        ProfilerMarker generateStepMarker,
        ProfilerMarker unloadStepMarker)
    {
        this.owner = owner;
        this.isChunkLoaded = isChunkLoaded;
        this.shouldGenerateChunk = shouldGenerateChunk;
        this.shouldUnloadChunk = shouldUnloadChunk;
        this.generateChunkImmediate = generateChunkImmediate;
        this.createGenerateChunkRoutine = createGenerateChunkRoutine;
        this.unloadChunkImmediate = unloadChunkImmediate;
        this.createUnloadChunkRoutine = createUnloadChunkRoutine;
        this.getChunkUnloadBudget = getChunkUnloadBudget;
        this.generateStepMarker = generateStepMarker;
        this.unloadStepMarker = unloadStepMarker;
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

    public void QueueUnload(Vector2Int chunkCoordinate)
    {
        if (pendingChunkUnloadCoordinates.Contains(chunkCoordinate)
            || activeChunkGenerationCoordinates.Contains(chunkCoordinate)
            || !isChunkLoaded(chunkCoordinate))
        {
            return;
        }

        pendingChunkUnloads.Enqueue(chunkCoordinate);
        pendingChunkUnloadCoordinates.Add(chunkCoordinate);
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

    public void EnsureUnloadProcessing()
    {
        if (pendingChunkUnloads.Count <= 0)
        {
            return;
        }

        if (!Application.isPlaying)
        {
            ProcessQueuedUnloadsImmediate();
            return;
        }

        if (chunkUnloadCoroutine == null)
        {
            chunkUnloadCoroutine = owner.StartCoroutine(ProcessUnloadQueue());
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

    public void ProcessQueuedUnloadsImmediate()
    {
        while (pendingChunkUnloads.Count > 0)
        {
            Vector2Int chunkCoordinate = pendingChunkUnloads.Dequeue();
            pendingChunkUnloadCoordinates.Remove(chunkCoordinate);
            if (!shouldUnloadChunk(chunkCoordinate))
            {
                continue;
            }

            unloadChunkImmediate(chunkCoordinate);
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

        pendingChunkUnloads.Clear();
        pendingChunkUnloadCoordinates.Clear();

        if (chunkUnloadCoroutine != null)
        {
            owner.StopCoroutine(chunkUnloadCoroutine);
            chunkUnloadCoroutine = null;
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

            MarkGenerationComplete(request.coordinate);
        }

        chunkGenerationCoroutine = null;
    }

    private IEnumerator ProcessUnloadQueue()
    {
        yield return null;

        int unloadsThisFrame = 0;
        int unloadBudget = Mathf.Max(1, getChunkUnloadBudget());
        while (pendingChunkUnloads.Count > 0)
        {
            Vector2Int chunkCoordinate = pendingChunkUnloads.Dequeue();
            pendingChunkUnloadCoordinates.Remove(chunkCoordinate);
            if (!shouldUnloadChunk(chunkCoordinate))
            {
                continue;
            }

            IEnumerator unloadRoutine = createUnloadChunkRoutine(chunkCoordinate, true);
            while (true)
            {
                bool hasNext;
                object current = null;
                using (unloadStepMarker.Auto())
                {
                    hasNext = unloadRoutine.MoveNext();
                    if (hasNext)
                    {
                        current = unloadRoutine.Current;
                    }
                }

                if (!hasNext)
                {
                    break;
                }

                yield return current;
            }

            unloadsThisFrame++;
            if (unloadsThisFrame >= unloadBudget)
            {
                unloadsThisFrame = 0;
                yield return null;
            }
        }

        chunkUnloadCoroutine = null;
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
