using System;

namespace UnityEngine
{
    public class ScriptableObject { }
    public class GameObject { }
    public class Sprite { }
    public class AnimationClip { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class HideInInspector : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute : Attribute
    { public MinAttribute(float value) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute
    { public RangeAttribute(float min, float max) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class CreateAssetMenuAttribute : Attribute
    { public string menuName; public string fileName; }
    public static class Mathf
    {
        public static int Max(int left, int right) => Math.Max(left, right);
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
    }
}

public sealed class ItemDefinition { public int id; }
