using System;
using UnityEngine;

public static class AnimalGridPathfinder
{
    private const int MaxSearchRadius = 64;
    private const int MaxEscapeSearchRadius = 12;
    private const int StraightCost = 10;
    private const int DiagonalCost = 14;
    private const float LineSampleSpacing = 0.25f;

    private static readonly int[] NeighborX =
    {
        1, -1, 0, 0,
        1, 1, -1, -1
    };

    private static readonly int[] NeighborZ =
    {
        0, 0, 1, -1,
        1, -1, 1, -1
    };

    private static int[] visitStamps = Array.Empty<int>();
    private static int[] closedStamps = Array.Empty<int>();
    private static int[] gCosts = Array.Empty<int>();
    private static int[] parents = Array.Empty<int>();
    private static int[] heap = Array.Empty<int>();
    private static int[] heapPositions = Array.Empty<int>();
    private static int[] reversePath = Array.Empty<int>();

    private static int searchStamp;
    private static int heapCount;
    private static int gridMinX;
    private static int gridMinZ;
    private static int gridWidth;
    private static int gridHeight;
    private static int goalX;
    private static int goalZ;

    public static bool HasWalkableLine(
        TerrainGenerator terrain,
        Vector3 start,
        Vector3 end,
        Vector3 areaCenter,
        float areaRadius,
        bool requireLoadedGround)
    {
        if (terrain == null)
        {
            return true;
        }

        Vector3 delta = end - start;
        delta.y = 0f;
        int sampleCount = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / LineSampleSpacing));
        float radius = Mathf.Max(1f, areaRadius);
        float radiusSqr = radius * radius;
        for (int i = 1; i <= sampleCount; i++)
        {
            Vector3 sample = Vector3.Lerp(start, end, i / (float)sampleCount);
            Vector3 areaOffset = sample - areaCenter;
            areaOffset.y = 0f;
            if (areaOffset.sqrMagnitude > radiusSqr
                || !terrain.CanAnimalMoveTo(sample, requireLoadedGround))
            {
                return false;
            }
        }

        return true;
    }

    public static int FindPath(
        TerrainGenerator terrain,
        Vector3 start,
        Vector3 destination,
        Vector3 areaCenter,
        float areaRadius,
        bool requireLoadedGround,
        Vector3[] output)
    {
        if (terrain == null || output == null || output.Length == 0)
        {
            return 0;
        }

        float searchRadius = BeginGridSearch(areaCenter, areaRadius);

        int startX = Mathf.RoundToInt(start.x);
        int startZ = Mathf.RoundToInt(start.z);
        goalX = Mathf.RoundToInt(destination.x);
        goalZ = Mathf.RoundToInt(destination.z);
        if (!TryGetIndex(startX, startZ, out int startIndex)
            || !TryGetIndex(goalX, goalZ, out int goalIndex)
            || !IsInsideArea(goalX, goalZ, areaCenter, searchRadius)
            || !IsWalkable(
                terrain,
                goalX,
                goalZ,
                start.y,
                requireLoadedGround))
        {
            return 0;
        }

        visitStamps[startIndex] = searchStamp;
        gCosts[startIndex] = 0;
        parents[startIndex] = -1;
        Push(startIndex);

        while (heapCount > 0)
        {
            int currentIndex = Pop();
            if (currentIndex == goalIndex)
            {
                return BuildSmoothedPath(
                    terrain,
                    start,
                    destination,
                    areaCenter,
                    searchRadius,
                    requireLoadedGround,
                    currentIndex,
                    output);
            }

            closedStamps[currentIndex] = searchStamp;
            GetCoordinate(currentIndex, out int currentX, out int currentZ);
            for (int i = 0; i < NeighborX.Length; i++)
            {
                int offsetX = NeighborX[i];
                int offsetZ = NeighborZ[i];
                int nextX = currentX + offsetX;
                int nextZ = currentZ + offsetZ;
                if (!TryGetIndex(nextX, nextZ, out int nextIndex)
                    || closedStamps[nextIndex] == searchStamp
                    || !IsInsideArea(nextX, nextZ, areaCenter, searchRadius)
                    || !IsTraversableStep(
                        terrain,
                        currentX,
                        currentZ,
                        offsetX,
                        offsetZ,
                        start.y,
                        requireLoadedGround))
                {
                    continue;
                }

                bool diagonal = offsetX != 0 && offsetZ != 0;
                int nextCost = gCosts[currentIndex] + (diagonal ? DiagonalCost : StraightCost);
                if (visitStamps[nextIndex] == searchStamp && nextCost >= gCosts[nextIndex])
                {
                    continue;
                }

                bool firstVisit = visitStamps[nextIndex] != searchStamp;
                visitStamps[nextIndex] = searchStamp;
                gCosts[nextIndex] = nextCost;
                parents[nextIndex] = currentIndex;
                if (firstVisit)
                {
                    Push(nextIndex);
                }
                else
                {
                    SiftUp(heapPositions[nextIndex]);
                }
            }
        }

        return 0;
    }

    public static int FindReachableTargetPath(
        TerrainGenerator terrain,
        Vector3 start,
        Vector3 areaCenter,
        float areaRadius,
        bool requireLoadedGround,
        bool requireWaterEdge,
        float minimumDistance,
        uint selectionSeed,
        Vector3[] output,
        out Vector3 destination)
    {
        // 목표를 먼저 정하는 A*와 달리 시작점의 연결 영역을 한 번 순회한 뒤
        // 그 안에서 결정적인 무작위 점수를 사용해 목표와 경로를 함께 선택한다.
        destination = start;
        if (terrain == null || output == null || output.Length == 0)
        {
            return 0;
        }

        float searchRadius = BeginGridSearch(areaCenter, areaRadius);
        int startX = Mathf.RoundToInt(start.x);
        int startZ = Mathf.RoundToInt(start.z);
        if (!TryGetIndex(startX, startZ, out int startIndex)
            || !IsInsideArea(startX, startZ, areaCenter, searchRadius))
        {
            return 0;
        }

        int queueHead = 0;
        int queueTail = 0;
        reversePath[queueTail++] = startIndex;
        visitStamps[startIndex] = searchStamp;
        parents[startIndex] = -1;

        int selectedIndex = -1;
        uint selectedScore = 0u;
        float minimumDistanceSqr = Mathf.Max(0f, minimumDistance);
        minimumDistanceSqr *= minimumDistanceSqr;
        while (queueHead < queueTail)
        {
            int currentIndex = reversePath[queueHead++];
            GetCoordinate(currentIndex, out int currentX, out int currentZ);
            if (currentIndex != startIndex)
            {
                float offsetX = currentX - start.x;
                float offsetZ = currentZ - start.z;
                Vector3 candidate = new Vector3(currentX, start.y, currentZ);
                if (offsetX * offsetX + offsetZ * offsetZ >= minimumDistanceSqr
                    && (!requireWaterEdge || terrain.IsAnimalDrinkLocation(candidate)))
                {
                    uint score = HashCoordinate(currentX, currentZ, selectionSeed);
                    if (selectedIndex < 0 || score > selectedScore)
                    {
                        selectedIndex = currentIndex;
                        selectedScore = score;
                        destination = candidate;
                    }
                }
            }

            for (int i = 0; i < NeighborX.Length; i++)
            {
                int offsetX = NeighborX[i];
                int offsetZ = NeighborZ[i];
                int nextX = currentX + offsetX;
                int nextZ = currentZ + offsetZ;
                if (!TryGetIndex(nextX, nextZ, out int nextIndex)
                    || visitStamps[nextIndex] == searchStamp
                    || !IsInsideArea(nextX, nextZ, areaCenter, searchRadius)
                    || !IsTraversableStep(
                        terrain,
                        currentX,
                        currentZ,
                        offsetX,
                        offsetZ,
                        start.y,
                        requireLoadedGround))
                {
                    continue;
                }

                visitStamps[nextIndex] = searchStamp;
                parents[nextIndex] = currentIndex;
                reversePath[queueTail++] = nextIndex;
            }
        }

        if (selectedIndex < 0)
        {
            destination = start;
            return 0;
        }

        goalX = Mathf.RoundToInt(destination.x);
        goalZ = Mathf.RoundToInt(destination.z);
        return BuildSmoothedPath(
            terrain,
            start,
            destination,
            areaCenter,
            searchRadius,
            requireLoadedGround,
            selectedIndex,
            output);
    }

    public static bool TryFindNearestWalkable(
        TerrainGenerator terrain,
        Vector3 origin,
        bool requireLoadedGround,
        out Vector3 result)
    {
        if (terrain == null)
        {
            result = origin;
            return false;
        }

        int centerX = Mathf.RoundToInt(origin.x);
        int centerZ = Mathf.RoundToInt(origin.z);
        for (int radius = 1; radius <= MaxEscapeSearchRadius; radius++)
        {
            float bestDistanceSqr = float.MaxValue;
            bool found = false;
            Vector3 best = origin;
            for (int z = -radius; z <= radius; z++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (Mathf.Abs(x) != radius && Mathf.Abs(z) != radius)
                    {
                        continue;
                    }

                    Vector3 candidate = new Vector3(centerX + x, origin.y, centerZ + z);
                    if (!terrain.CanAnimalMoveTo(candidate, requireLoadedGround))
                    {
                        continue;
                    }

                    float distanceSqr = (candidate - origin).sqrMagnitude;
                    if (distanceSqr >= bestDistanceSqr)
                    {
                        continue;
                    }

                    bestDistanceSqr = distanceSqr;
                    best = candidate;
                    found = true;
                }
            }

            if (found)
            {
                result = best;
                return true;
            }
        }

        result = origin;
        return false;
    }

    private static int BuildSmoothedPath(
        TerrainGenerator terrain,
        Vector3 start,
        Vector3 destination,
        Vector3 areaCenter,
        float areaRadius,
        bool requireLoadedGround,
        int goalIndex,
        Vector3[] output)
    {
        int reverseCount = 0;
        int currentIndex = goalIndex;
        while (currentIndex >= 0 && reverseCount < reversePath.Length)
        {
            reversePath[reverseCount++] = currentIndex;
            currentIndex = parents[currentIndex];
        }

        if (reverseCount <= 1 || currentIndex >= 0)
        {
            return 0;
        }

        int outputCount = 0;
        int anchor = reverseCount - 1;
        Vector3 anchorPosition = start;
        while (anchor > 0)
        {
            int next = anchor - 1;
            for (int candidate = 0; candidate < anchor; candidate++)
            {
                GetCoordinate(reversePath[candidate], out int candidateX, out int candidateZ);
                Vector3 candidatePosition = new Vector3(candidateX, start.y, candidateZ);
                if (!HasWalkableLine(
                        terrain,
                        anchorPosition,
                        candidatePosition,
                        areaCenter,
                        areaRadius,
                        requireLoadedGround))
                {
                    continue;
                }

                next = candidate;
                break;
            }

            if (outputCount >= output.Length)
            {
                return 0;
            }

            GetCoordinate(reversePath[next], out int nextX, out int nextZ);
            anchorPosition = new Vector3(nextX, start.y, nextZ);
            output[outputCount++] = anchorPosition;
            anchor = next;
        }

        if (outputCount == 0)
        {
            return 0;
        }

        Vector3 finalWaypoint = output[outputCount - 1];
        Vector3 finalDelta = destination - finalWaypoint;
        finalDelta.y = 0f;
        if (finalDelta.sqrMagnitude <= 0.0001f)
        {
            output[outputCount - 1] = destination;
            return outputCount;
        }

        if (outputCount >= output.Length)
        {
            return 0;
        }

        output[outputCount++] = destination;
        return outputCount;
    }

    private static bool IsWalkable(
        TerrainGenerator terrain,
        int x,
        int z,
        float y,
        bool requireLoadedGround)
    {
        return terrain.CanAnimalMoveTo(
            new Vector3(x, y, z),
            requireLoadedGround);
    }

    private static bool IsTraversableStep(
        TerrainGenerator terrain,
        int currentX,
        int currentZ,
        int offsetX,
        int offsetZ,
        float y,
        bool requireLoadedGround)
    {
        if (!IsWalkable(
                terrain,
                currentX + offsetX,
                currentZ + offsetZ,
                y,
                requireLoadedGround))
        {
            return false;
        }

        return offsetX == 0
               || offsetZ == 0
               || IsWalkable(
                   terrain,
                   currentX + offsetX,
                   currentZ,
                   y,
                   requireLoadedGround)
               && IsWalkable(
                   terrain,
                   currentX,
                   currentZ + offsetZ,
                   y,
                   requireLoadedGround);
    }

    private static float BeginGridSearch(Vector3 areaCenter, float areaRadius)
    {
        float searchRadius = Mathf.Clamp(areaRadius, 1f, MaxSearchRadius);
        gridMinX = Mathf.FloorToInt(areaCenter.x - searchRadius);
        gridMinZ = Mathf.FloorToInt(areaCenter.z - searchRadius);
        int gridMaxX = Mathf.CeilToInt(areaCenter.x + searchRadius);
        int gridMaxZ = Mathf.CeilToInt(areaCenter.z + searchRadius);
        gridWidth = gridMaxX - gridMinX + 1;
        gridHeight = gridMaxZ - gridMinZ + 1;
        EnsureCapacity(gridWidth * gridHeight);
        BeginSearch();
        return searchRadius;
    }

    private static uint HashCoordinate(int x, int z, uint seed)
    {
        unchecked
        {
            uint value = seed ^ (uint)x * 0x9E3779B9u ^ (uint)z * 0x85EBCA6Bu;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }
    }

    private static bool IsInsideArea(
        int x,
        int z,
        Vector3 areaCenter,
        float areaRadius)
    {
        float offsetX = x - areaCenter.x;
        float offsetZ = z - areaCenter.z;
        return offsetX * offsetX + offsetZ * offsetZ <= areaRadius * areaRadius;
    }

    private static int Heuristic(int x, int z)
    {
        int deltaX = Mathf.Abs(goalX - x);
        int deltaZ = Mathf.Abs(goalZ - z);
        int diagonal = Mathf.Min(deltaX, deltaZ);
        return diagonal * DiagonalCost
               + (Mathf.Max(deltaX, deltaZ) - diagonal) * StraightCost;
    }

    private static int CompareNodes(int leftIndex, int rightIndex)
    {
        GetCoordinate(leftIndex, out int leftX, out int leftZ);
        GetCoordinate(rightIndex, out int rightX, out int rightZ);
        int leftHeuristic = Heuristic(leftX, leftZ);
        int rightHeuristic = Heuristic(rightX, rightZ);
        int leftScore = gCosts[leftIndex] + leftHeuristic;
        int rightScore = gCosts[rightIndex] + rightHeuristic;
        if (leftScore != rightScore)
        {
            return leftScore.CompareTo(rightScore);
        }

        return leftHeuristic.CompareTo(rightHeuristic);
    }

    private static void Push(int nodeIndex)
    {
        int position = heapCount++;
        heap[position] = nodeIndex;
        heapPositions[nodeIndex] = position;
        SiftUp(position);
    }

    private static int Pop()
    {
        int result = heap[0];
        heapCount--;
        heapPositions[result] = -1;
        if (heapCount > 0)
        {
            int replacement = heap[heapCount];
            heap[0] = replacement;
            heapPositions[replacement] = 0;
            SiftDown(0);
        }

        return result;
    }

    private static void SiftUp(int position)
    {
        while (position > 0)
        {
            int parentPosition = (position - 1) >> 1;
            if (CompareNodes(heap[position], heap[parentPosition]) >= 0)
            {
                break;
            }

            SwapHeap(position, parentPosition);
            position = parentPosition;
        }
    }

    private static void SiftDown(int position)
    {
        while (true)
        {
            int left = position * 2 + 1;
            if (left >= heapCount)
            {
                return;
            }

            int right = left + 1;
            int best = right < heapCount
                       && CompareNodes(heap[right], heap[left]) < 0
                ? right
                : left;
            if (CompareNodes(heap[best], heap[position]) >= 0)
            {
                return;
            }

            SwapHeap(position, best);
            position = best;
        }
    }

    private static void SwapHeap(int left, int right)
    {
        int leftNode = heap[left];
        int rightNode = heap[right];
        heap[left] = rightNode;
        heap[right] = leftNode;
        heapPositions[leftNode] = right;
        heapPositions[rightNode] = left;
    }

    private static void GetCoordinate(int index, out int x, out int z)
    {
        x = gridMinX + index % gridWidth;
        z = gridMinZ + index / gridWidth;
    }

    private static bool TryGetIndex(int x, int z, out int index)
    {
        int localX = x - gridMinX;
        int localZ = z - gridMinZ;
        if (localX < 0 || localZ < 0 || localX >= gridWidth || localZ >= gridHeight)
        {
            index = -1;
            return false;
        }

        index = localX + localZ * gridWidth;
        return true;
    }

    private static void BeginSearch()
    {
        searchStamp++;
        if (searchStamp <= 0)
        {
            Array.Clear(visitStamps, 0, visitStamps.Length);
            Array.Clear(closedStamps, 0, closedStamps.Length);
            searchStamp = 1;
        }

        heapCount = 0;
    }

    private static void EnsureCapacity(int capacity)
    {
        if (visitStamps.Length >= capacity)
        {
            return;
        }

        visitStamps = new int[capacity];
        closedStamps = new int[capacity];
        gCosts = new int[capacity];
        parents = new int[capacity];
        heap = new int[capacity];
        heapPositions = new int[capacity];
        reversePath = new int[capacity];
        searchStamp = 0;
    }
}
