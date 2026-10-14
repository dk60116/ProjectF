using System.Collections.Generic;
using UnityEngine;

// Input devices, camera/plane and UI are ports; brush, spawn and ECS registration are production code.
namespace UnityEngine
{
    public static class Time { public static int frameCount; public static float time, unscaledTime; }
    public enum KeyCode { Escape }
    public static class Input
    {
        public static bool Escape, Click;
        public static Vector2 mousePosition;
        public static bool GetKeyDown(KeyCode key) => Escape;
        public static bool GetMouseButtonDown(int button) => Click;
    }
    public sealed class Camera
    {
        public static Camera main;
        public bool isActiveAndEnabled = true;
        public readonly Transform transform = new GameObject().transform;
        public Rect pixelRect = new Rect();
        public Ray ScreenPointToRay(Vector2 point) => new Ray { Point = new Vector3(point.x, 0, point.y) };
    }
    public struct Rect { public bool Contains(Vector2 point) => point.x >= 0 && point.y >= 0 && point.x < 100 && point.y < 100; }
    public struct Ray { public Vector3 Point; public Vector3 GetPoint(float distance) => Point; }
    public struct Plane
    {
        public Plane(Vector3 normal, Vector3 position) { }
        public bool Raycast(Ray ray, out float distance) { distance = 1; return true; }
    }
    public static class Debug { public static int Warnings; public static void LogWarning(string value, object context) => Warnings++; }
}
namespace UnityEngine.UI { public sealed class Selectable { } }
namespace UnityEngine.EventSystems
{
    public sealed class EventSystem
    {
        public static EventSystem current;
        public GameObject Hit;
        public void RaycastAll(PointerEventData data, List<RaycastResult> results)
        { if (Hit != null) results.Add(new RaycastResult { gameObject = Hit }); }
    }
    public sealed class PointerEventData
    {
        public Vector2 position;
        public PointerEventData(EventSystem events) { }
        public void Reset() { }
    }
    public struct RaycastResult { public GameObject gameObject; }
}
public sealed class AnimalAIController
{
    public float AvoidanceColliderRadius = .5f;
    public Vector3 SimulationPosition;
}
public sealed class AnimalAIWorld
{
    public static AnimalAIWorld Instance;
    public readonly List<AnimalAIController> Animals = new();
    public void CopyControllers(List<AnimalAIController> results, bool living) => results.AddRange(Animals);
}
