using System;
using System.Collections.Generic;
using ProjectF.Animals;
using UnityEngine;

namespace UnityEngine
{
    public sealed class DisallowMultipleComponent : Attribute { }
    public class GameObject
    {
        public bool activeInHierarchy = true;
        public int layer;
        public T GetComponent<T>() where T : class => null;
        public T AddComponent<T>() where T : new() => new T();
    }
    public class MonoBehaviour
    {
        public GameObject gameObject = new();
        public Transform transform = new();
        protected static void Destroy(object value) { }
    }
    public static class Time
    {
        public static float deltaTime = 1f / 60f, timeScale = 1;
        public static double realtimeSinceStartupAsDouble;
    }
    public class Camera
    {
        public static Camera main = new();
        public bool isActiveAndEnabled = true;
    }
    public static class Mathf
    {
        public const float PI = MathF.PI;
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Abs(float value) => Math.Abs(value);
        public static int Abs(int value) => Math.Abs(value);
        public static float Clamp(float v, float min, float max) => Math.Clamp(v, min, max);
        public static float Clamp01(float v) => Math.Clamp(v, 0, 1);
        public static int FloorToInt(float v) => (int)MathF.Floor(v);
        public static int CeilToInt(float v) => (int)MathF.Ceiling(v);
        public static int RoundToInt(float v) => (int)MathF.Round(v);
        public static float Sqrt(float v) => MathF.Sqrt(v);
        public static float Sin(float v) => MathF.Sin(v);
        public static float Cos(float v) => MathF.Cos(v);
        public static bool Approximately(float a, float b) => MathF.Abs(a - b) < .000001f;
    }
    public readonly record struct Vector2Int(int x, int y);
    public struct Vector3 : IEquatable<Vector3>
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude > 0 ? this / magnitude : zero;
        public static Vector3 zero => default;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => a * -1;
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
        public static Vector3 operator /(Vector3 a, float b) => new(a.x / b, a.y / b, a.z / b);
        public static Vector3 ClampMagnitude(Vector3 a, float b) => a.magnitude > b ? a.normalized * b : a;
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) => Lerp(a, b, t);
        public bool Equals(Vector3 b) => x.Equals(b.x) && y.Equals(b.y) && z.Equals(b.z);
        public override bool Equals(object b) => b is Vector3 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
    }
    public struct Quaternion
    {
        public static float Angle(Quaternion a, Quaternion b) => 0f;
        public static Quaternion SlerpUnclamped(Quaternion a, Quaternion b, float t) => default;
    }
    public class Transform
    {
        public Vector3 position;
        public Quaternion rotation;
        public int Writes;
        public void SetPositionAndRotation(Vector3 p, Quaternion q) { position = p; rotation = q; Writes++; }
    }
}
namespace ProjectF.Rendering
{
    public class CameraRenderCulling { public void Update(Camera c) { } }
}
public interface IMapObjectUpdateTick { void ManagedUpdateTick(float dt); }
public interface IMapObjectSimulationIdentity { long SimulationId { get; } }
public static class DeterministicSimulationUnits
{
    public static long DeltaTimeToTicks(float dt) => (long)MathF.Round(dt * 60);
}
public class Player { public Transform transform = new(); }
public class GameManager { public static GameManager Instance = new(); public Player Player = new(); public float AnimalAIActiveRadius = 60; }
public class TerrainAnimalInstance { public long DeterministicId; }
public class TerrainGenerator
{
    public static TerrainGenerator Active;
    public bool IsWorldReadyForPresentation, IsChunkStreamingBusy;
    public HashSet<Vector2Int> Walls = new();
    public bool CanAnimalMoveTo(Vector3 p, bool loaded) => !Walls.Contains(new(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z)));
    public bool IsAnimalDrinkLocation(Vector3 p) => false;
    public long AnimalNavigationRevision;
    public HashSet<Vector2Int> Shores = new();
    internal bool IsAnimalShoreCell(Vector2Int p) => Shores.Contains(p);
}
public class Animal
{
    public bool IsAlive = true;
    public float NeedsElapsed;
    public int NeedsUpdates;
    public void TickNeeds(float dt) { NeedsElapsed += dt; NeedsUpdates++; }
}
public partial class AnimalAIController : MonoBehaviour
{
    public Animal Animal => animal;
    public Animal animal = new();
    public TerrainAnimalInstance TerrainInstance = new();
    public long SimulationId => TerrainInstance.DeterministicId;
    public long HerdId;
    public bool IsConfigured = true, IsFleeing, IsExecuting, HasPendingPresentation;
    public float AvoidanceColliderRadius = .5f;
    public Vector3 SimulationPosition, CrowdSnapshotPosition;
    public bool Due, ExpensiveSearch;
    public int Executions, CrowdCaptures;
    private AnimalNeedsSchedule needsSchedule;
    public bool IsExternallyControlled, presentationActive;
    public float presentationElapsed, presentationDuration = .1f;
    public Vector3 presentationStartPosition, presentationTargetPosition;
    public Quaternion presentationStartRotation, presentationTargetRotation;
    public void InitializeNeedsSchedule(long tick) => needsSchedule.Initialize(tick, SimulationId);
    public bool TickDormant() => false;
    public void CaptureCrowdSnapshot() { CrowdSnapshotPosition = SimulationPosition; CrowdCaptures++; }
    public void SetBehaviorExecutionActive(bool value) => IsExecuting = value;
    public void SetDetailedVisuals(bool value) { }
    public bool QueueScheduledTick(float dt, float interval) => Due;
    public bool ExecuteScheduledTick()
    {
        Executions++;
        if (ExpensiveSearch)
        {
            var path = new Vector3[96];
            AnimalGridPathfinder.FindReachableTargetPath(new(), SimulationPosition, SimulationPosition, 30,
                true, false, 1, 123, path, out _);
        }
        return true;
    }
    public bool IsPresentationVisible(ProjectF.Rendering.CameraRenderCulling c) => true;
    public void NotifyThreat(Vector3 p) => IsFleeing = true;
    public void NotifyForcedThreat(Vector3 p) => IsFleeing = true;
    public bool TryApplyPlayerPush(Vector3 p, Vector3 direction, float distance) => false;
    private void SnapToSimulationPose() => transform.SetPositionAndRotation(SimulationPosition, default);
    private void ResetPresentation() => presentationActive = false;
}
public partial class MapObjectTickManager
{
    public const float FixedSimulationDeltaSeconds = 1f / 60f;
    public static bool SimulationPaused;
    public static void RegisterUpdateTick(object o) { }
    public static void UnregisterUpdateTick(object o) { }
    private bool simulationPaused, waitingForWorldLoad, hasSimulationUpsSample;
    private double simulationTimeAccumulator, simulationUpsSampleStartTime;
    private long simulationTick, simulationUpsSampleStartTick;
    private int simulationTicksLastFrame, maximumSimulationStepsPerFrame = 8;
    private float currentSimulationUps;
    private const double SimulationUpsSampleIntervalSeconds = .5;
    public long Tick => simulationTick;
    public double Backlog => simulationTimeAccumulator;
    public int Executions;
    public void Frame(float dt) { Time.deltaTime = dt; Time.realtimeSinceStartupAsDouble += dt; Update(); }
    public void Pause(bool value) { simulationPaused = value; Time.timeScale = value ? 0 : 1; }
    private bool RequestPeriodicAliveValidation() => false;
    private void ReconcileRequestedUpdateTicks(bool full) { }
    private void TickUpdateObjects() => Executions++;
}
public sealed partial class AnimalAIWorld
{
    internal static void NotifySpatialChanged(ActorScheduleProbe actor) { }
    public void AttachForCheck(AnimalAIController c) => AddController(c);
    public void RemoveForCheck(AnimalAIController c) => RemoveController(c);
    public void DirtyForCheck(AnimalAIController c) => MarkSpatialDirty(c);
    public void RefreshForCheck() => RefreshSpatialCaches();
    public void CompleteForCheck() => LateUpdate();
    public void SelectForCheck() => Instance = this;
    public int CellCountForCheck => controllersBySpatialCell.Count;
    public int IndexedForCheck => spatialEntries.Count;
}
