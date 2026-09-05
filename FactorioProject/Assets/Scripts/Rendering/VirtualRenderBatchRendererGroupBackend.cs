using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Owns the BatchRendererGroup resources used by VirtualRenderBatchCollection.
/// The collection remains responsible for gameplay-facing batch mutation while
/// this backend only uploads render data and emits visible draw commands.
/// </summary>
internal sealed class VirtualRenderBatchRendererGroupBackend : IDisposable
{
    private const int PackedMatrixSize = sizeof(float) * 12;
    private const int ZeroPrefixSize = PackedMatrixSize * 2;
    private const int UvPropertySize = sizeof(float) * 4;
    private const int ColorPropertySize = sizeof(float) * 4;
    private const uint PerInstanceMetadataBit = 0x80000000u;
    private const int OpaqueRenderQueueUpperBound = 3000;
    private const string AlphaTestKeyword = "_ALPHATEST_ON";
    private const string BrgCompatibleTag = "BatchRendererGroupCompatible";
    private const string RenderTypeTag = "RenderType";
    private const string TransparentCutoutRenderType = "TransparentCutout";

    private static readonly int ObjectToWorldShaderId = Shader.PropertyToID("unity_ObjectToWorld");
    private static readonly int WorldToObjectShaderId = Shader.PropertyToID("unity_WorldToObject");
    private static readonly int ConveyorUvDataShaderId = Shader.PropertyToID("_ConveyorUvData");
    private static readonly int BaseColorShaderId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorShaderId = Shader.PropertyToID("_Color");
    private static readonly int AlphaClipShaderId = Shader.PropertyToID("_AlphaClip");
    private static readonly Matrix4x4[] ZeroPrefix = { Matrix4x4.zero };
    private static readonly BatchCullingViewType[] EnabledViewTypes =
    {
        BatchCullingViewType.Camera,
        BatchCullingViewType.Light
    };

    private readonly Dictionary<VirtualRenderBatchKey, BrgBatchState> statesByKey =
        new Dictionary<VirtualRenderBatchKey, BrgBatchState>();
    private readonly List<BrgBatchState> states = new List<BrgBatchState>(64);
    private readonly List<VirtualRenderBatchKey> staleKeys = new List<VirtualRenderBatchKey>(32);
    private readonly Dictionary<Mesh, BatchMeshID> meshIds = new Dictionary<Mesh, BatchMeshID>();
    private readonly Dictionary<Material, BatchMaterialID> materialIds =
        new Dictionary<Material, BatchMaterialID>();
    private readonly Dictionary<Material, bool> materialCompatibility =
        new Dictionary<Material, bool>();

    private BatchRendererGroup rendererGroup;
    private int syncGeneration;
    private bool initializationFailed;

    public static bool IsSupported =>
        SystemInfo.supportsInstancing
        && GraphicsSettings.currentRenderPipeline != null
        && BatchRendererGroup.BufferTarget == BatchBufferTarget.RawBuffer;

    public bool IsAvailable => !initializationFailed;
    public bool DisableCameraCulling { get; set; }

    public int ActiveBatchCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].InstanceCount > 0)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public bool IsRendering(VirtualRenderBatchKey key)
    {
        return !initializationFailed
            && statesByKey.TryGetValue(key, out BrgBatchState state)
            && state.HasBatch
            && state.InstanceCount > 0
            && state.LastSyncGeneration == syncGeneration;
    }

    public bool IsCompatible(VirtualRenderBatchKey key)
    {
        if (!IsSupported
            || key.Mesh == null
            || key.Material == null
            || key.Material.shader == null
            || key.HasConveyorMotion
            || key.UseBeltItemLineDebugColor
            || key.Material.renderQueue >= OpaqueRenderQueueUpperBound)
        {
            return false;
        }

        if (materialCompatibility.TryGetValue(key.Material, out bool isCompatible))
        {
            return isCompatible;
        }

        Shader shader = key.Material.shader;
        bool usesAlphaClip = key.Material.IsKeywordEnabled(AlphaTestKeyword)
            || (key.Material.HasProperty(AlphaClipShaderId)
                && key.Material.GetFloat(AlphaClipShaderId) > 0.5f)
            || string.Equals(
                key.Material.GetTag(RenderTypeTag, false, string.Empty),
                TransparentCutoutRenderType,
                StringComparison.OrdinalIgnoreCase);
        bool hasDotsKeyword = shader.keywordSpace
            .FindKeyword("DOTS_INSTANCING_ON")
            .isValid;
        bool isExplicitlySupported = string.Equals(
            key.Material.GetTag(BrgCompatibleTag, false, string.Empty),
            "True",
            StringComparison.OrdinalIgnoreCase);

        // A DOTS keyword in the shader metadata does not guarantee that the Player build
        // retained a compatible variant or that its BRG property layout matches this backend.
        // Stock URP materials therefore stay on Graphics.RenderMeshInstanced unless their
        // shader explicitly opts into this backend.
        isCompatible = !usesAlphaClip && hasDotsKeyword && isExplicitlySupported;
        materialCompatibility.Add(key.Material, isCompatible);
        return isCompatible;
    }

    public void BeginSync()
    {
        unchecked
        {
            syncGeneration++;
        }
    }

    public bool TrySyncBatch(
        VirtualRenderBatchKey key,
        List<Matrix4x4> matrices,
        List<Vector4> instanceUvData,
        Bounds worldBounds,
        int dataVersion)
    {
        if (!IsCompatible(key)
            || matrices == null
            || matrices.Count == 0
            || !EnsureRendererGroup())
        {
            Deactivate(key);
            return false;
        }

        try
        {
            BrgBatchState state = GetOrCreateState(key);
            EnsureCapacity(state, matrices.Count);
            if (state.UploadedDataVersion != dataVersion
                || state.InstanceCount != matrices.Count)
            {
                UploadInstanceData(state, key, matrices, instanceUvData);
                state.UploadedDataVersion = dataVersion;
            }

            state.InstanceCount = matrices.Count;
            state.WorldBounds = worldBounds;
            state.LastSyncGeneration = syncGeneration;
            return true;
        }
        catch (Exception exception)
        {
            DisableAfterFailure(exception);
            return false;
        }
    }

    public void EndSync()
    {
        if (rendererGroup == null)
        {
            return;
        }

        staleKeys.Clear();
        Bounds globalBounds = default;
        bool hasGlobalBounds = false;

        for (int i = 0; i < states.Count; i++)
        {
            BrgBatchState state = states[i];
            if (state.LastSyncGeneration != syncGeneration)
            {
                staleKeys.Add(state.Key);
                continue;
            }

            if (state.InstanceCount <= 0)
            {
                continue;
            }

            if (!hasGlobalBounds)
            {
                globalBounds = state.WorldBounds;
                hasGlobalBounds = true;
            }
            else
            {
                globalBounds.Encapsulate(state.WorldBounds);
            }
        }

        for (int i = 0; i < staleKeys.Count; i++)
        {
            RemoveState(staleKeys[i]);
        }

        if (hasGlobalBounds)
        {
            rendererGroup.SetGlobalBounds(globalBounds);
        }
    }

    public void Deactivate(VirtualRenderBatchKey key, bool keepAllocated = false)
    {
        if (statesByKey.TryGetValue(key, out BrgBatchState state))
        {
            state.InstanceCount = 0;
            // Camera visibility changes must not destroy/recreate GPU buffers.
            // Unregistered batches still expire when absent from the next sync.
            if (keepAllocated)
                state.LastSyncGeneration = syncGeneration;
        }
    }

    public void DeactivateAll()
    {
        for (int i = 0; i < states.Count; i++)
        {
            states[i].InstanceCount = 0;
        }
    }

    public void Dispose()
    {
        for (int i = states.Count - 1; i >= 0; i--)
        {
            DisposeState(states[i]);
        }

        states.Clear();
        statesByKey.Clear();
        staleKeys.Clear();

        if (rendererGroup != null)
        {
            foreach (KeyValuePair<Material, BatchMaterialID> pair in materialIds)
            {
                rendererGroup.UnregisterMaterial(pair.Value);
            }

            foreach (KeyValuePair<Mesh, BatchMeshID> pair in meshIds)
            {
                rendererGroup.UnregisterMesh(pair.Value);
            }

            rendererGroup.Dispose();
            rendererGroup = null;
        }

        materialIds.Clear();
        meshIds.Clear();
        materialCompatibility.Clear();
    }

    private bool EnsureRendererGroup()
    {
        if (rendererGroup != null)
        {
            return true;
        }

        if (initializationFailed)
        {
            return false;
        }

        try
        {
            rendererGroup = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
            rendererGroup.SetEnabledViewTypes(EnabledViewTypes);
            return true;
        }
        catch (Exception exception)
        {
            DisableAfterFailure(exception);
            return false;
        }
    }

    private BrgBatchState GetOrCreateState(VirtualRenderBatchKey key)
    {
        if (statesByKey.TryGetValue(key, out BrgBatchState state))
        {
            return state;
        }

        state = new BrgBatchState
        {
            Key = key,
            MeshId = ResolveMeshId(key.Mesh),
            MaterialId = ResolveMaterialId(key.Material),
            UploadedDataVersion = int.MinValue
        };
        statesByKey.Add(key, state);
        states.Add(state);
        return state;
    }

    private BatchMeshID ResolveMeshId(Mesh mesh)
    {
        if (!meshIds.TryGetValue(mesh, out BatchMeshID id))
        {
            id = rendererGroup.RegisterMesh(mesh);
            meshIds.Add(mesh, id);
        }

        return id;
    }

    private BatchMaterialID ResolveMaterialId(Material material)
    {
        if (!materialIds.TryGetValue(material, out BatchMaterialID id))
        {
            id = rendererGroup.RegisterMaterial(material);
            materialIds.Add(material, id);
        }

        return id;
    }

    private void EnsureCapacity(BrgBatchState state, int instanceCount)
    {
        if (state.InstanceDataBuffer != null && state.Capacity >= instanceCount)
        {
            return;
        }

        int capacity = Mathf.NextPowerOfTwo(Mathf.Max(1, instanceCount));
        RemoveBatchAndBuffer(state);

        state.Capacity = capacity;
        state.ObjectToWorldByteOffset = ZeroPrefixSize;
        state.WorldToObjectByteOffset =
            state.ObjectToWorldByteOffset + (PackedMatrixSize * capacity);
        state.UvPropertiesByteOffset =
            state.WorldToObjectByteOffset + (PackedMatrixSize * capacity);
        state.ColorPropertyByteOffset =
            state.UvPropertiesByteOffset
            + (state.Key.HasUvScroll ? UvPropertySize * capacity : UvPropertySize);

        int totalBytes = state.ColorPropertyByteOffset + ColorPropertySize;
        int bufferCount = (totalBytes + sizeof(int) - 1) / sizeof(int);
        state.InstanceDataBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Raw,
            bufferCount,
            sizeof(int));
        state.InstanceDataBuffer.SetData(ZeroPrefix, 0, 0, 1);

        int metadataCount = 2;
        if (state.Key.HasUvScroll)
        {
            metadataCount++;
        }

        if (state.Key.UseSleepAwakeDarkTint)
        {
            metadataCount += 2;
        }

        NativeArray<MetadataValue> metadata = new NativeArray<MetadataValue>(
            metadataCount,
            Allocator.Temp,
            NativeArrayOptions.UninitializedMemory);
        try
        {
            int metadataIndex = 0;
            metadata[metadataIndex++] = CreatePerInstanceMetadata(
                ObjectToWorldShaderId,
                state.ObjectToWorldByteOffset);
            metadata[metadataIndex++] = CreatePerInstanceMetadata(
                WorldToObjectShaderId,
                state.WorldToObjectByteOffset);

            if (state.Key.HasUvScroll)
            {
                metadata[metadataIndex++] = CreatePerInstanceMetadata(
                    ConveyorUvDataShaderId,
                    state.UvPropertiesByteOffset);
            }

            if (state.Key.UseSleepAwakeDarkTint)
            {
                metadata[metadataIndex++] = CreateConstantMetadata(
                    BaseColorShaderId,
                    state.ColorPropertyByteOffset);
                metadata[metadataIndex] = CreateConstantMetadata(
                    ColorShaderId,
                    state.ColorPropertyByteOffset);
            }

            state.BatchId = rendererGroup.AddBatch(
                metadata,
                state.InstanceDataBuffer.bufferHandle);
            state.HasBatch = true;
            state.UploadedDataVersion = int.MinValue;
        }
        finally
        {
            metadata.Dispose();
        }
    }

    private static void UploadInstanceData(
        BrgBatchState state,
        VirtualRenderBatchKey key,
        List<Matrix4x4> matrices,
        List<Vector4> instanceUvData)
    {
        int instanceCount = matrices.Count;
        state.EnsureUploadArrayCapacity(instanceCount);
        for (int i = 0; i < instanceCount; i++)
        {
            Matrix4x4 matrix = matrices[i];
            state.ObjectToWorldMatrices[i] = new PackedMatrix(matrix);
            state.WorldToObjectMatrices[i] = new PackedMatrix(matrix.inverse);
        }

        state.InstanceDataBuffer.SetData(
            state.ObjectToWorldMatrices,
            0,
            state.ObjectToWorldByteOffset / PackedMatrixSize,
            instanceCount);
        state.InstanceDataBuffer.SetData(
            state.WorldToObjectMatrices,
            0,
            state.WorldToObjectByteOffset / PackedMatrixSize,
            instanceCount);

        if (key.HasUvScroll)
        {
            Vector4 defaultUvData = new Vector4(0f, -0.5f, 1f, 0f);
            for (int i = 0; i < instanceCount; i++)
            {
                state.UvProperties[i] = instanceUvData != null && i < instanceUvData.Count
                    ? instanceUvData[i]
                    : defaultUvData;
            }

            state.InstanceDataBuffer.SetData(
                state.UvProperties,
                0,
                state.UvPropertiesByteOffset / UvPropertySize,
                instanceCount);
        }

        if (key.UseSleepAwakeDarkTint)
        {
            state.ColorProperty[0] = SleepAwakeDebugVisual.GetSleepingColor(key.Material);
            state.InstanceDataBuffer.SetData(
                state.ColorProperty,
                0,
                state.ColorPropertyByteOffset / ColorPropertySize,
                1);
        }
    }

    private static MetadataValue CreatePerInstanceMetadata(int nameId, int byteOffset)
    {
        return new MetadataValue
        {
            NameID = nameId,
            Value = PerInstanceMetadataBit | (uint)byteOffset
        };
    }

    private static MetadataValue CreateConstantMetadata(int nameId, int byteOffset)
    {
        return new MetadataValue
        {
            NameID = nameId,
            Value = (uint)byteOffset
        };
    }

    private void RemoveState(VirtualRenderBatchKey key)
    {
        if (!statesByKey.TryGetValue(key, out BrgBatchState state))
        {
            return;
        }

        statesByKey.Remove(key);
        states.Remove(state);
        DisposeState(state);
    }

    private void DisposeState(BrgBatchState state)
    {
        RemoveBatchAndBuffer(state);
        state.InstanceCount = 0;
    }

    private void RemoveBatchAndBuffer(BrgBatchState state)
    {
        if (state.HasBatch && rendererGroup != null)
        {
            rendererGroup.RemoveBatch(state.BatchId);
            state.HasBatch = false;
        }

        state.InstanceDataBuffer?.Dispose();
        state.InstanceDataBuffer = null;
    }

    private void DisableAfterFailure(Exception exception)
    {
        if (!initializationFailed)
        {
            Debug.LogWarning(
                $"BatchRendererGroup 초기화/업로드에 실패해 기존 인스턴싱으로 전환합니다: {exception.Message}");
        }

        initializationFailed = true;
        Dispose();
    }

    private unsafe JobHandle OnPerformCulling(
        BatchRendererGroup group,
        BatchCullingContext cullingContext,
        BatchCullingOutput cullingOutput,
        IntPtr userContext)
    {
        int visibleBatchCount = 0;
        int visibleInstanceCount = 0;
        for (int i = 0; i < states.Count; i++)
        {
            BrgBatchState state = states[i];
            if (ResolveSplitVisibilityMask(state, cullingContext) == 0)
            {
                continue;
            }

            visibleBatchCount++;
            visibleInstanceCount += state.InstanceCount;
        }

        BatchCullingOutputDrawCommands* output =
            (BatchCullingOutputDrawCommands*)cullingOutput.drawCommands.GetUnsafePtr();
        InitializeEmptyOutput(output);
        if (visibleBatchCount == 0)
        {
            return default;
        }

        int alignment = UnsafeUtility.AlignOf<long>();
        output->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(
            UnsafeUtility.SizeOf<BatchDrawCommand>() * visibleBatchCount,
            alignment,
            Allocator.TempJob);
        output->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(
            UnsafeUtility.SizeOf<BatchDrawRange>() * visibleBatchCount,
            alignment,
            Allocator.TempJob);
        output->visibleInstances = (int*)UnsafeUtility.Malloc(
            sizeof(int) * visibleInstanceCount,
            alignment,
            Allocator.TempJob);
        output->drawCommandCount = visibleBatchCount;
        output->drawRangeCount = visibleBatchCount;
        output->visibleInstanceCount = visibleInstanceCount;

        int drawIndex = 0;
        int visibleOffset = 0;
        for (int stateIndex = 0; stateIndex < states.Count; stateIndex++)
        {
            BrgBatchState state = states[stateIndex];
            ushort splitVisibilityMask =
                ResolveSplitVisibilityMask(state, cullingContext);
            if (splitVisibilityMask == 0)
            {
                continue;
            }

            BatchDrawCommandFlags flags = state.Key.InvertCulling
                ? BatchDrawCommandFlags.FlipWinding
                : BatchDrawCommandFlags.None;
            output->drawCommands[drawIndex] = new BatchDrawCommand
            {
                visibleOffset = (uint)visibleOffset,
                visibleCount = (uint)state.InstanceCount,
                batchID = state.BatchId,
                materialID = state.MaterialId,
                meshID = state.MeshId,
                submeshIndex = (ushort)Mathf.Max(0, state.Key.SubmeshIndex),
                splitVisibilityMask = splitVisibilityMask,
                flags = flags,
                sortingPosition = 0,
                activeMeshLod = 0
            };

            BatchFilterSettings filterSettings = new BatchFilterSettings
            {
                renderingLayerMask = uint.MaxValue,
                layer = (byte)Mathf.Clamp(state.Key.Layer, 0, 31)
            };
            filterSettings.shadowCastingMode = state.Key.ShadowCastingMode;
            filterSettings.receiveShadows = state.Key.ReceiveShadows;

            output->drawRanges[drawIndex] = new BatchDrawRange
            {
                drawCommandsType = BatchDrawCommandType.Direct,
                drawCommandsBegin = (uint)drawIndex,
                drawCommandsCount = 1,
                filterSettings = filterSettings
            };

            for (int instanceIndex = 0;
                 instanceIndex < state.InstanceCount;
                 instanceIndex++)
            {
                output->visibleInstances[visibleOffset + instanceIndex] = instanceIndex;
            }

            visibleOffset += state.InstanceCount;
            drawIndex++;
        }

        return default;
    }

    private static unsafe void InitializeEmptyOutput(
        BatchCullingOutputDrawCommands* output)
    {
        output->drawCommands = null;
        output->indirectDrawCommands = null;
        output->proceduralDrawCommands = null;
        output->proceduralIndirectDrawCommands = null;
        output->visibleInstances = null;
        output->drawRanges = null;
        output->instanceSortingPositions = null;
        output->drawCommandPickingEntityIds = null;
        output->drawCommandCount = 0;
        output->indirectDrawCommandCount = 0;
        output->proceduralDrawCommandCount = 0;
        output->proceduralIndirectDrawCommandCount = 0;
        output->visibleInstanceCount = 0;
        output->drawRangeCount = 0;
        output->instanceSortingPositionFloatCount = 0;
    }

    private ushort ResolveSplitVisibilityMask(
        BrgBatchState state,
        BatchCullingContext cullingContext)
    {
        if (!state.HasBatch || state.InstanceCount <= 0)
        {
            return 0;
        }

        if (cullingContext.viewType == BatchCullingViewType.Light
            && state.Key.ShadowCastingMode == ShadowCastingMode.Off)
        {
            return 0;
        }

        if (DisableCameraCulling && cullingContext.viewType == BatchCullingViewType.Camera)
            return ushort.MaxValue;

        NativeArray<CullingSplit> splits = cullingContext.cullingSplits;
        if (!splits.IsCreated || splits.Length == 0)
        {
            return 0xff;
        }

        NativeArray<Plane> planes = cullingContext.cullingPlanes;
        int splitCount = Math.Min(splits.Length, 16);
        ushort visibilityMask = 0;
        for (int splitIndex = 0; splitIndex < splitCount; splitIndex++)
        {
            if (IntersectsSplit(state.WorldBounds, splits[splitIndex], planes))
            {
                visibilityMask |= (ushort)(1 << splitIndex);
            }
        }

        return visibilityMask;
    }

    private static bool IntersectsSplit(
        Bounds bounds,
        CullingSplit split,
        NativeArray<Plane> planes)
    {
        int planeStart = split.cullingPlaneOffset;
        int planeCount = split.cullingPlaneCount;
        if (!planes.IsCreated
            || planeCount <= 0
            || planeStart < 0
            || planeStart >= planes.Length)
        {
            // 비어 있거나 잘못된 컬링 데이터로 전체 배치가 사라지는 것보다
            // 해당 분할을 보이는 것으로 처리하는 편이 안전하다.
            return true;
        }

        int planeEnd = Math.Min(planes.Length, planeStart + planeCount);
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        for (int planeIndex = planeStart; planeIndex < planeEnd; planeIndex++)
        {
            Plane plane = planes[planeIndex];
            Vector3 normal = plane.normal;
            float projectedRadius =
                extents.x * Math.Abs(normal.x)
                + extents.y * Math.Abs(normal.y)
                + extents.z * Math.Abs(normal.z);
            // BatchCullingContext의 평면은 내부에서 양수가 되는 방향이다.
            // AABB의 가장 가까운 점까지 음수일 때만 완전히 밖으로 판정한다.
            if (plane.GetDistanceToPoint(center) + projectedRadius < 0f)
            {
                return false;
            }
        }

        return true;
    }

    private struct PackedMatrix
    {
        public float C0X;
        public float C0Y;
        public float C0Z;
        public float C1X;
        public float C1Y;
        public float C1Z;
        public float C2X;
        public float C2Y;
        public float C2Z;
        public float C3X;
        public float C3Y;
        public float C3Z;

        public PackedMatrix(Matrix4x4 matrix)
        {
            C0X = matrix.m00;
            C0Y = matrix.m10;
            C0Z = matrix.m20;
            C1X = matrix.m01;
            C1Y = matrix.m11;
            C1Z = matrix.m21;
            C2X = matrix.m02;
            C2Y = matrix.m12;
            C2Z = matrix.m22;
            C3X = matrix.m03;
            C3Y = matrix.m13;
            C3Z = matrix.m23;
        }
    }

    private sealed class BrgBatchState
    {
        public readonly Vector4[] ColorProperty = new Vector4[1];
        public VirtualRenderBatchKey Key;
        public BatchID BatchId;
        public BatchMeshID MeshId;
        public BatchMaterialID MaterialId;
        public GraphicsBuffer InstanceDataBuffer;
        public PackedMatrix[] ObjectToWorldMatrices;
        public PackedMatrix[] WorldToObjectMatrices;
        public Vector4[] UvProperties;
        public Bounds WorldBounds;
        public int Capacity;
        public int InstanceCount;
        public int ObjectToWorldByteOffset;
        public int WorldToObjectByteOffset;
        public int UvPropertiesByteOffset;
        public int ColorPropertyByteOffset;
        public int UploadedDataVersion;
        public int LastSyncGeneration;
        public bool HasBatch;

        public void EnsureUploadArrayCapacity(int requiredCapacity)
        {
            if (ObjectToWorldMatrices == null
                || ObjectToWorldMatrices.Length < requiredCapacity)
            {
                ObjectToWorldMatrices = new PackedMatrix[Capacity];
                WorldToObjectMatrices = new PackedMatrix[Capacity];
            }

            if (Key.HasUvScroll
                && (UvProperties == null || UvProperties.Length < requiredCapacity))
            {
                UvProperties = new Vector4[Capacity];
            }
        }
    }
}
