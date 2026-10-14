using System;
using System.Collections.Generic;
using UnityEngine;

// Canvas/image/camera ports only. Shared bar policy, Animal adapter and monster binding are production code.
namespace UnityEngine
{
    public sealed class DisallowMultipleComponent : Attribute { }
    public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int value) { } }
    public class MonoBehaviour : FixtureComponent { }
    public partial class Transform
    {
        public GameObject gameObject;
        public Transform parent;
        public readonly List<Transform> Children = new();
        public void SetParent(Transform value, bool worldPositionStays)
        {
            parent?.Children.Remove(this);
            parent = value;
            parent?.Children.Add(this);
        }
    }
    public sealed class RectTransform : Transform
    {
        public Vector2 sizeDelta, anchorMin, anchorMax, offsetMin, offsetMax;
    }
    public enum RenderMode { WorldSpace }
    public sealed class Canvas : FixtureComponent
    {
        public RenderMode renderMode;
        public bool overrideSorting;
        public int sortingOrder;
    }
    public readonly record struct Color(float r, float g, float b, float a = 1f);
}
namespace UnityEngine.UI
{
    public sealed class Image : FixtureComponent
    {
        public Color color;
        public bool enabled = true, raycastTarget;
    }
}
public static class MapObjectTickProfiler
{
    public readonly struct Scope : IDisposable { public void Dispose() { } }
    public static Scope SampleLateUpdateCaller<T>() => default;
}
public sealed class TerrainAnimalInstance : MonoBehaviour { }
