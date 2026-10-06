using System.Collections.Generic;
using UnityEngine;
using ProjectF.Simulation;
using ProjectF.MapObjects;

namespace ProjectF.Railway
{
    public interface IRailwayTarget : IMapObjectTarget
    {
        long RuntimePlacementSequence { get; }
        Vector2Int RuntimeAnchorCoordinate { get; }
        IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates { get; }
        bool TryGetPlacementRuntime(out Vector2Int anchor, out int quarterTurns);
    }

    public interface IRailTarget : IRailwayTarget
    {
        IReadOnlyList<Vector2> RuntimeVisualPathPoints { get; }
        bool RuntimeVisualPathExtendsStart { get; }
        bool RuntimeVisualPathExtendsEnd { get; }
        int RequiredItemCount { get; }
        bool TryFindNearestPathPointAndTangent(Vector2 point, out Vector2 pathPoint, out Vector2 tangent, out float sqrDistance);
        bool TryFindNearestRenderedPathSample(Vector2 point, out float distance, out Vector2 pathPoint, out Vector2 tangent, out float sqrDistance);
        bool TrySampleRenderedPath(float distance, out Vector2 point, out Vector2 tangent);
        bool TryGetRenderedPathLength(out float length);
        bool TryGetRenderedEndpointSample(bool start, out float distance, out Vector2 point, out Vector2 tangent);
    }

    public interface ITrainStationTarget : IRailwayTarget
    {
        Sprite StationMarkerIcon { get; }
        string StationName { get; }
        string StoredStationName { get; }
        bool HasAssignedStationName { get; }
        Color StationColor { get; }
        Color32 StoredStationColor { get; }
        bool HasAssignedStationColor { get; }
        bool TryGetRailCoordinate(out Vector2Int coordinate);
        void SetStationName(string name);
        void ApplyStationName(string name);
        void SetStationColor(Color color);
        void ApplyStationColor(Color32 color, bool assigned);
    }

    public abstract class RailwayInstance : IRailwayTarget, IMapObjectSimulationIdentity
    {
        internal RailwayInstance(RailWorld world, BlockStateStore.InstallationSaveState state, InstallationObject prototype)
        {
            World = world; State = state; Prototype = prototype;
            FocusBounds = new Bounds(WorldPosition + Vector3.up * .5f, Vector3.one);
            var coordinates = RuntimeOccupiedCoordinates;
            for (int i = 0; i < coordinates.Count; i++)
            {
                FocusBounds.Encapsulate(new Vector3(coordinates[i].x - .5f, WorldPosition.y, coordinates[i].y - .5f));
                FocusBounds.Encapsulate(new Vector3(coordinates[i].x + .5f, WorldPosition.y + 1, coordinates[i].y + .5f));
            }
            var filters = prototype.GetComponentsInChildren<MeshFilter>(true);
            var root = filters.Length > 0 ? Matrix4x4.TRS(WorldPosition, state.worldRotation, prototype.transform.localScale) : Matrix4x4.identity;
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                var matrix = root * prototype.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var bounds = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                    FocusBounds.Encapsulate(matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
            }
        }
        internal RailWorld World { get; }
        public BlockStateStore.InstallationSaveState State { get; }
        public InstallationObject Prototype { get; }
        public Bounds FocusBounds;
        public Vector2Int StorageKey => BlockStateStore.GetInstallationStorageKey(State);
        public MapObjectHandle Handle { get; internal set; }
        public long SimulationId => State.placementSequence;
        public long RuntimePlacementSequence => SimulationId;
        public Vector2Int RuntimeAnchorCoordinate => State.anchorCoordinate;
        internal bool PresentationSuppressed { get; private set; }
        internal float PresentationScale { get; private set; } = 1;
        internal void SetPresentationSuppressed(bool value)
        { if (PresentationSuppressed == value) return; PresentationSuppressed = value; World.NotifyPresentationChanged(this); }
        internal void SetPresentationScale(float value)
        { value = Mathf.Max(0, value); if (Mathf.Approximately(PresentationScale, value)) return; PresentationScale = value; World.NotifyPresentationChanged(this); }
        public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => State.occupiedCoordinates;
        public MapObject SceneObject => null;
        public bool IsTargetActive => World.Contains(this);
        public Vector3 WorldPosition => State.worldPosition;
        public string ObjectName => Prototype.ObjectName;
        public bool AllowsFocus => Prototype.AllowsFocus;
        public bool AllowsAnimalTraversal => Prototype.AllowsAnimalTraversal;
        public MapObject.MultiFocusMode FocusMode => Prototype.FocusMode;
        public MapObject.MapObjectStatus Status => Prototype.Status;
        public ItemDefinition BoundItemDefinition => InputOutputModule.ResolveItemDefinition(State.itemId);
        public int ResolveItemId() => State.itemId;
        public int ResolvedItemId => State.itemId;
        public int ID => State.itemId;
        public bool TryGetPlacementRuntime(out Vector2Int anchor, out int quarterTurns)
        { anchor = State.anchorCoordinate; quarterTurns = State.quarterTurns; return IsTargetActive; }
    }

    public sealed class RailInstance : RailwayInstance, IRailTarget
    {
        internal RailInstance(RailWorld world, BlockStateStore.InstallationSaveState state, Railload prototype, RailPathData path)
            : base(world, state, prototype) { Path = path; }
        public RailPathData Path { get; }
        public IReadOnlyList<Vector2> RuntimeVisualPathPoints => State.railVisualPathPoints;
        public bool RuntimeVisualPathExtendsStart => State.railVisualPathExtendsStart;
        public bool RuntimeVisualPathExtendsEnd => State.railVisualPathExtendsEnd;
        public int RequiredItemCount => State.railRequiredItemCount > 0 ? State.railRequiredItemCount : Railload.ResolveRequiredItemCount(RuntimeOccupiedCoordinates);
        public bool TryFindNearestPathPointAndTangent(Vector2 point, out Vector2 pathPoint, out Vector2 tangent, out float sqrDistance)
            => TryFindNearestRenderedPathSample(point, out _, out pathPoint, out tangent, out sqrDistance);
        public bool TryFindNearestRenderedPathSample(Vector2 point, out float distance, out Vector2 pathPoint, out Vector2 tangent, out float sqrDistance)
        {
            distance = 0; pathPoint = point; tangent = default; sqrDistance = float.MaxValue;
            return IsTargetActive && Path.TryFindNearest(point, out distance, out pathPoint, out tangent, out sqrDistance);
        }
        public bool TrySampleRenderedPath(float distance, out Vector2 point, out Vector2 tangent)
        { point = tangent = default; return IsTargetActive && Path.TrySample(distance, out point, out tangent); }
        public bool TryGetRenderedPathLength(out float length)
        { length = IsTargetActive ? Path.Length : 0; return length > .0001f; }
        public bool TryGetRenderedEndpointSample(bool start, out float distance, out Vector2 point, out Vector2 tangent)
        { distance = start ? 0 : Path.Length; return TrySampleRenderedPath(distance, out point, out tangent); }
    }

    public sealed class TrainStationInstance : RailwayInstance, ITrainStationTarget
    {
        internal TrainStationInstance(RailWorld world, BlockStateStore.InstallationSaveState state, Trainstation prototype)
            : base(world, state, prototype) { }
        public Sprite StationMarkerIcon => ((Trainstation)Prototype).StationMarkerIcon;
        public string StoredStationName => State.stationName ?? string.Empty;
        public string StationName => HasAssignedStationName ? StoredStationName : ObjectName;
        public bool HasAssignedStationName => !string.IsNullOrWhiteSpace(StoredStationName);
        public Color StationColor => HasAssignedStationColor ? (Color)StoredStationColor : Color.white;
        public Color32 StoredStationColor => State.stationColor;
        public bool HasAssignedStationColor => State.stationColorAssigned;
        public bool TryGetRailCoordinate(out Vector2Int coordinate)
        {
            coordinate = default;
            if (!IsTargetActive || !Trainstation.TryGetFacingDirection(State.quarterTurns, out var direction)) return false;
            var occupied = RuntimeOccupiedCoordinates;
            coordinate = (occupied?.Count > 0 ? occupied[0] : State.anchorCoordinate) + direction;
            for (int i = 0; i < (occupied?.Count ?? 0); i++)
                if (World.CoordinateExists(occupied[i] + direction, true)) { coordinate = occupied[i] + direction; break; }
            return true;
        }
        public void SetStationName(string name)
        {
            if (!IsTargetActive) return;
            ApplyStationName(TerrainGenerator.Active?.ResolveUniqueTrainStationName(this, name) ?? name);
        }
        public void ApplyStationName(string name)
        {
            name = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
            if (!IsTargetActive || StoredStationName == name) return;
            State.stationName = name;
            World.NotifyStationChanged(this);
        }
        public void SetStationColor(Color color) => ApplyStationColor((Color32)color, true);
        public void ApplyStationColor(Color32 color, bool assigned)
        {
            color.a = 255;
            if (!IsTargetActive || State.stationColorAssigned == assigned && State.stationColor.Equals(color)) return;
            State.stationColor = color; State.stationColorAssigned = assigned;
            World.NotifyStationChanged(this, false);
        }
    }
}
