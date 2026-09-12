using UnityEngine;

public sealed partial class RobotArmInstance
{
    private struct InteractionTargetCache
    {
        internal bool HasBlockBinding;
        internal Vector2Int Coordinate;
        internal BlockHandle BlockHandle;
        internal Block Block;
        internal bool HasMapObjectBinding;
        internal IMapObjectTarget MapObject;
        internal BoxObject BoxObject;
        internal FreightCar MapObjectFreightCar;
        internal bool CoordinateFreightCarResolved;
        internal ulong CoordinateInstallationVersion;
        internal FreightCar CoordinateFreightCar;

        internal void Reset()
        {
            HasBlockBinding = false;
            Coordinate = default;
            BlockHandle = default;
            Block = null;
            ResetMapObjectBinding();
            CoordinateFreightCarResolved = false;
            CoordinateInstallationVersion = 0UL;
            CoordinateFreightCar = null;
        }

        internal void ResetMapObjectBinding()
        {
            HasMapObjectBinding = false;
            MapObject = null;
            BoxObject = null;
            MapObjectFreightCar = null;
        }
    }

    private InteractionTargetCache pickupTargetCache;
    private InteractionTargetCache dropTargetCache;

    private void InvalidateInteractionTargetCaches()
    {
        pickupTargetCache.Reset();
        dropTargetCache.Reset();
    }

    internal void ReleaseRuntimeCaches()
    {
        InvalidateInteractionTargetCaches();
        freightCarCoordinateScratch.Clear();
    }

    private bool TryResolvePickupInteractionTargets(
        TerrainGenerator terrainGenerator,
        Vector2Int coordinate,
        out Block block,
        out BoxObject boxObject,
        out FreightCar freightCar)
    {
        return TryResolveInteractionTargets(
            terrainGenerator,
            coordinate,
            ref pickupTargetCache,
            out block,
            out boxObject,
            out freightCar);
    }

    private bool TryResolveDropInteractionTargets(
        TerrainGenerator terrainGenerator,
        Vector2Int coordinate,
        out Block block,
        out BoxObject boxObject,
        out FreightCar freightCar)
    {
        return TryResolveInteractionTargets(
            terrainGenerator,
            coordinate,
            ref dropTargetCache,
            out block,
            out boxObject,
            out freightCar);
    }

    private bool TryResolveInteractionFreightCar(
        TerrainGenerator terrainGenerator,
        Vector2Int coordinate,
        out FreightCar freightCar)
    {
        if (EnsureInteractionCoordinateCache() && coordinate == cachedPickupCoordinate)
        {
            bool hasLoadedBlock = TryResolvePickupInteractionTargets(
                terrainGenerator,
                coordinate,
                out _,
                out _,
                out freightCar);
            if (!hasLoadedBlock && freightCar == null)
            {
                TryResolveCoordinateFreightCar(coordinate, ref pickupTargetCache, out freightCar);
            }
            return freightCar != null;
        }

        if (interactionCoordinateCacheValid && coordinate == cachedDropCoordinate)
        {
            bool hasLoadedBlock = TryResolveDropInteractionTargets(
                terrainGenerator,
                coordinate,
                out _,
                out _,
                out freightCar);
            if (!hasLoadedBlock && freightCar == null)
            {
                TryResolveCoordinateFreightCar(coordinate, ref dropTargetCache, out freightCar);
            }
            return freightCar != null;
        }

        InteractionTargetCache uncachedTarget = default;
        bool hasUncachedBlock = TryResolveInteractionTargets(
            terrainGenerator,
            coordinate,
            ref uncachedTarget,
            out _,
            out _,
            out freightCar);
        if (!hasUncachedBlock && freightCar == null)
        {
            TryResolveCoordinateFreightCar(coordinate, ref uncachedTarget, out freightCar);
        }
        return freightCar != null;
    }

    private bool TryResolveInteractionTargets(
        TerrainGenerator terrainGenerator,
        Vector2Int coordinate,
        ref InteractionTargetCache cache,
        out Block block,
        out BoxObject boxObject,
        out FreightCar freightCar)
    {
        bool hasLoadedBlock = TryResolveInteractionBlock(
            terrainGenerator,
            coordinate,
            ref cache,
            out block);

        if (hasLoadedBlock)
        {
            RefreshMapObjectBinding(block.MapObject, ref cache);
            boxObject = cache.BoxObject;
            freightCar = cache.MapObjectFreightCar;
        }
        else
        {
            boxObject = null;
            freightCar = null;
        }

        if (hasLoadedBlock && freightCar == null)
        {
            TryResolveCoordinateFreightCar(coordinate, ref cache, out freightCar);
        }

        return hasLoadedBlock;
    }

    private bool TryResolveInteractionBlock(
        TerrainGenerator terrainGenerator,
        Vector2Int coordinate,
        ref InteractionTargetCache cache,
        out Block block)
    {
        block = null;
        if (terrainGenerator == null)
        {
            cache.Reset();
            World.RecordInteractionBlockCache(false);
            return false;
        }

        if (cache.HasBlockBinding
            && cache.Coordinate == coordinate
            && cache.Block != null
            && cache.BlockHandle.IsValid
            && terrainGenerator.TryResolveLoadedBlock(cache.BlockHandle, out Block resolvedBlock)
            && ReferenceEquals(cache.Block, resolvedBlock))
        {
            block = resolvedBlock;
            World.RecordInteractionBlockCache(true);
            return true;
        }

        cache.Reset();
        cache.Coordinate = coordinate;
        if (!TryGetLoadedInteractionBlock(terrainGenerator, coordinate, out block))
        {
            World.RecordInteractionBlockCache(false);
            return false;
        }

        cache.HasBlockBinding = true;
        cache.BlockHandle = block.RuntimeHandle;
        cache.Block = block;
        World.RecordInteractionBlockCache(false);
        return true;
    }

    private void RefreshMapObjectBinding(
        IMapObjectTarget mapObject,
        ref InteractionTargetCache cache)
    {
        if (cache.HasMapObjectBinding && ReferenceEquals(cache.MapObject, mapObject))
        {
            World.RecordInteractionTargetCache(true);
            return;
        }

        cache.ResetMapObjectBinding();
        cache.HasMapObjectBinding = true;
        cache.MapObject = mapObject;
        if (mapObject != null)
        {
            cache.BoxObject = mapObject as BoxObject;
            if (cache.BoxObject == null)
            {
                mapObject.TryGetComponent(out cache.BoxObject);
            }

            TryResolveFreightCar(mapObject, out cache.MapObjectFreightCar);
        }

        World.RecordInteractionTargetCache(false);
    }

    private bool TryResolveCoordinateFreightCar(
        Vector2Int coordinate,
        ref InteractionTargetCache cache,
        out FreightCar freightCar)
    {
        ulong installationVersion =
            InstallationObject.GetActiveInstanceVersionAtRuntimeGridCoordinate(coordinate);
        if (cache.CoordinateFreightCarResolved
            && cache.Coordinate == coordinate
            && cache.CoordinateInstallationVersion == installationVersion)
        {
            freightCar = cache.CoordinateFreightCar;
            if (ReferenceEquals(freightCar, null)
                || freightCar != null && freightCar.gameObject.activeInHierarchy)
            {
                World.RecordInteractionFreightCache(true);
                return freightCar != null;
            }
        }

        cache.Coordinate = coordinate;
        cache.CoordinateFreightCarResolved = true;
        cache.CoordinateInstallationVersion = installationVersion;
        cache.CoordinateFreightCar = null;
        freightCarCoordinateScratch.Clear();
        InstallationObject.CollectActiveInstallationsAtRuntimeGridCoordinate(
            coordinate,
            freightCarCoordinateScratch);
        for (int i = 0; i < freightCarCoordinateScratch.Count; i++)
        {
            InstallationObject candidate = freightCarCoordinateScratch[i];
            if (!TryResolveFreightCar(candidate, out freightCar))
            {
                continue;
            }

            cache.CoordinateFreightCar = freightCar;
            freightCarCoordinateScratch.Clear();
            World.RecordInteractionFreightCache(false);
            return true;
        }

        freightCarCoordinateScratch.Clear();
        freightCar = null;
        World.RecordInteractionFreightCache(false);
        return false;
    }

    private bool IsDropMapObjectBlocking(Block dropBlock)
    {
        IMapObjectTarget mapObject = dropBlock != null ? dropBlock.MapObject : null;
        if (mapObject == null
            || IsOreMapObject(mapObject)
            || IsConveyorBeltMapObject(mapObject))
        {
            return false;
        }

        if (dropTargetCache.HasMapObjectBinding
            && ReferenceEquals(dropTargetCache.MapObject, mapObject))
        {
            return dropTargetCache.BoxObject == null
                   && dropTargetCache.MapObjectFreightCar == null;
        }

        BoxObject boxObject = mapObject as BoxObject;
        if (boxObject == null)
        {
            mapObject.TryGetComponent(out boxObject);
        }

        return boxObject == null && !TryResolveFreightCar(mapObject, out _);
    }
}
