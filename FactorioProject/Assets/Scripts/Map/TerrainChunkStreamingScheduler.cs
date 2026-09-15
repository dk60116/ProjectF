using System;
using System.Collections;
using System.Collections.Generic;
using ProjectF.Persistence;
using Unity.Profiling;
using UnityEngine;

internal sealed class TerrainChunkStreamingScheduler
{
    // A null child yield is a budget checkpoint and can be consumed in the same
    // frame. This marker is reserved for work that must wait for a later frame,
    // such as polling an asynchronous terrain job without blocking on Complete().
    internal static readonly object WaitForNextFrame = new object();

    private readonly MonoBehaviour owner;
    private readonly Func<Vector2Int, bool> isChunkLoaded;
    private readonly Func<Vector2Int, bool> shouldGenerateChunk;
    private readonly Action<Vector2Int, int> generateChunkImmediate;
    private readonly Func<Vector2Int, int, bool, IEnumerator> createGenerateChunkRoutine;
    private readonly ProfilerMarker generateStepMarker;
    private readonly Action cleanupGenerationTransientState;
    private readonly Func<float> frameBudgetMilliseconds;
    private readonly Action<Exception> onGenerationFailed;
    private int budgetFrame = -1;
    private double frameStartedAt;
    private readonly Queue<ChunkGenerationRequest> pendingChunkGenerations = new Queue<ChunkGenerationRequest>();
    private readonly HashSet<Vector2Int> pendingChunkGenerationCoordinates = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> activeChunkGenerationCoordinates = new HashSet<Vector2Int>();

    private Coroutine chunkGenerationCoroutine;
    private IEnumerator generationRoutine;
    private int totalGenerationCount;
    private int completedGenerationCount;

    public bool IsBusy =>
        pendingChunkGenerations.Count > 0
        || activeChunkGenerationCoordinates.Count > 0
        || chunkGenerationCoroutine != null;

    public int PendingCount => pendingChunkGenerations.Count;
    public bool HasFrameBudget
    {
        get
        {
            if (budgetFrame != Time.frameCount)
            {
                budgetFrame = Time.frameCount;
                frameStartedAt = Time.realtimeSinceStartupAsDouble;
            }
            return (Time.realtimeSinceStartupAsDouble - frameStartedAt) * 1000d
                < Mathf.Max(0.25f, frameBudgetMilliseconds());
        }
    }
    public int TotalGenerationCount => totalGenerationCount;
    public int CompletedGenerationCount => completedGenerationCount;
    public float GenerationProgress => totalGenerationCount > 0
        ? Mathf.Clamp01(completedGenerationCount / (float)totalGenerationCount)
        : 0f;

    public TerrainChunkStreamingScheduler(
        MonoBehaviour owner,
        Func<Vector2Int, bool> isChunkLoaded,
        Func<Vector2Int, bool> shouldGenerateChunk,
        Action<Vector2Int, int> generateChunkImmediate,
        Func<Vector2Int, int, bool, IEnumerator> createGenerateChunkRoutine,
        ProfilerMarker generateStepMarker,
        Action cleanupGenerationTransientState,
        Func<float> frameBudgetMilliseconds,
        Action<Exception> onGenerationFailed)
    {
        this.owner = owner;
        this.isChunkLoaded = isChunkLoaded;
        this.shouldGenerateChunk = shouldGenerateChunk;
        this.generateChunkImmediate = generateChunkImmediate;
        this.createGenerateChunkRoutine = createGenerateChunkRoutine;
        this.generateStepMarker = generateStepMarker;
        this.cleanupGenerationTransientState = cleanupGenerationTransientState;
        this.frameBudgetMilliseconds = frameBudgetMilliseconds;
        this.onGenerationFailed = onGenerationFailed;
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

        if (!IsBusy && completedGenerationCount >= totalGenerationCount)
        {
            totalGenerationCount = 0;
            completedGenerationCount = 0;
        }

        pendingChunkGenerations.Enqueue(new ChunkGenerationRequest(chunkCoordinate, normalizedChunkSize));
        pendingChunkGenerationCoordinates.Add(chunkCoordinate);
        totalGenerationCount++;
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
            generationRoutine = ProcessGenerationQueue();
            chunkGenerationCoroutine = owner.StartCoroutine(generationRoutine);
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
                completedGenerationCount++;
                continue;
            }

            activeChunkGenerationCoordinates.Add(request.coordinate);
            bool completed = false;
            try
            {
                generateChunkImmediate(request.coordinate, request.chunkSize);
                completed = true;
            }
            catch (Exception exception) { onGenerationFailed?.Invoke(exception); throw; }
            finally
            {
                FinishGeneration(request.coordinate, null, completed);
            }
        }
    }

    public void MarkGenerationComplete(Vector2Int chunkCoordinate)
    {
        if (activeChunkGenerationCoordinates.Remove(chunkCoordinate))
        {
            completedGenerationCount++;
        }
    }

    public void Clear()
    {
        pendingChunkGenerations.Clear();
        pendingChunkGenerationCoordinates.Clear();
        activeChunkGenerationCoordinates.Clear();
        totalGenerationCount = 0;
        completedGenerationCount = 0;
        budgetFrame = -1;

        IEnumerator stoppedRoutine = generationRoutine;
        generationRoutine = null;
        try
        {
            if (chunkGenerationCoroutine != null) owner.StopCoroutine(chunkGenerationCoroutine);
        }
        finally
        {
            chunkGenerationCoroutine = null;
            // Own iterator cleanup explicitly; do not depend on the coroutine driver
            // to dispose a suspended installation batch or surface job.
            (stoppedRoutine as IDisposable)?.Dispose();
        }
    }

    private IEnumerator ProcessGenerationQueue()
    {
        yield return null;

        try
        {
            while (pendingChunkGenerations.Count > 0)
            {
                // Includes every chunk and every resumed stage in this frame, not a fresh
                // budget per chunk. A single indivisible operation can still exceed the budget.
                if (!HasFrameBudget) yield return null;
                _ = HasFrameBudget;
                ChunkGenerationRequest request = pendingChunkGenerations.Dequeue();
                pendingChunkGenerationCoordinates.Remove(request.coordinate);
                if (!shouldGenerateChunk(request.coordinate))
                {
                    completedGenerationCount++;
                    continue;
                }

                activeChunkGenerationCoordinates.Add(request.coordinate);
                IEnumerator chunkRoutine = null;
                bool completed = false;
                try
                {
                    using (generateStepMarker.Auto()) { chunkRoutine = CreateChunkRoutine(request); }
                    while (true)
                    {
                        _ = HasFrameBudget;
                        bool hasNext;
                        object current = null;
                        long stepStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                        using (generateStepMarker.Auto())
                        {
                            try
                            {
                                hasNext = AdvanceChunkRoutine(chunkRoutine);
                                if (hasNext)
                                {
                                    current = chunkRoutine.Current;
                                }
                            }
                            finally
                            {
                                SlotLoadTimingLog.RecordStageWork(
                                    "chunks",
                                    (System.Diagnostics.Stopwatch.GetTimestamp() - stepStartedAt)
                                    * (1000d / System.Diagnostics.Stopwatch.Frequency));
                            }
                        }

                        if (!hasNext)
                        {
                            completed = true;
                            break;
                        }

                        if (ReferenceEquals(current, WaitForNextFrame))
                        {
                            yield return null;
                            continue;
                        }

                        if (current != null)
                        {
                            yield return current;
                            continue;
                        }

                        // Child routines use null as a cooperative checkpoint. Keep
                        // consuming checkpoints while the shared frame budget remains;
                        // mapping every checkpoint to a Unity frame made large saves
                        // pay thousands of frames of scheduler latency.
                        if (!HasFrameBudget)
                        {
                            yield return null;
                        }
                    }
                }
                finally
                {
                    FinishGeneration(request.coordinate, chunkRoutine, completed);
                }
            }
        }
        finally { chunkGenerationCoroutine = null; generationRoutine = null; }
    }

    private void FinishGeneration(Vector2Int coordinate, IEnumerator routine, bool completed)
    {
        try
        {
            try { (routine as IDisposable)?.Dispose(); }
            finally { cleanupGenerationTransientState?.Invoke(); }
        }
        catch (Exception exception) { completed = false; onGenerationFailed?.Invoke(exception); throw; }
        finally
        {
            if (completed) MarkGenerationComplete(coordinate);
            else activeChunkGenerationCoordinates.Remove(coordinate);
        }
    }

    private bool AdvanceChunkRoutine(IEnumerator routine)
    {
        // Iterator exception handling cannot surround yield returns with a catch.
        try { return routine.MoveNext(); }
        catch (Exception exception) { onGenerationFailed?.Invoke(exception); throw; }
    }

    private IEnumerator CreateChunkRoutine(ChunkGenerationRequest request)
    {
        try { return createGenerateChunkRoutine(request.coordinate, request.chunkSize, true); }
        catch (Exception exception) { onGenerationFailed?.Invoke(exception); throw; }
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
