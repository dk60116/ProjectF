using System;
using System.Collections.Generic;
using UnityEngine;

// Engine and portable-entity ports. Storage and transfer logic compile directly from production.
namespace UnityEngine
{
    public class SerializeField : Attribute { }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string text) { } }
    public class MinAttribute : Attribute { public MinAttribute(float min) { } }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string text) { } }
    public class MonoBehaviour
    {
        public bool isActiveAndEnabled = true;
        public readonly Transform transform = new();
        public readonly GameObject gameObject = new();
        public Collider BodyCollider;
        public T GetComponentInChildren<T>() where T : class => BodyCollider as T;
    }
    public class GameObject
    {
        public int layer;
        public bool activeInHierarchy = true;
        private readonly Dictionary<Type,object> components = new();
        public readonly Transform transform = new();
        public GameObject(string name = null) { }
        public T AddComponent<T>() where T : new() { var result = new T(); components[typeof(T)] = result; return result; }
        public T GetComponent<T>() where T : class => components.TryGetValue(typeof(T),out var result) ? result as T : null;
        public void SetActive(bool active) => activeInHierarchy = active;
    }
    public class Transform
    {
        public string name = "Storage";
        public Vector3 position;
        public Vector3 localScale = Vector3.one;
        public Quaternion rotation;
        public int childCount => 0;
        public Transform GetChild(int index) => throw new Exception("No authored children in fixture");
        public T[] GetComponentsInChildren<T>(bool inactive) => Array.Empty<T>();
        public void SetParent(Transform parent, bool keepWorld) { }
        public void SetPositionAndRotation(Vector3 p, Quaternion r) { position=p; rotation=r; }
    }
    public readonly record struct Vector3(float x, float y, float z)
    {
        public static Vector3 zero => default;
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 up => new(0,1,0);
        public static Vector3 right => new(1,0,0);
        public static Vector3 forward => new(0,0,1);
        public float sqrMagnitude => x*x+y*y+z*z;
        public Vector3 normalized => this*(1f/MathF.Sqrt(sqrMagnitude));
        public static Vector3 operator +(Vector3 a,Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float value) => new(a.x*value,a.y*value,a.z*value);
    }
    public readonly struct Quaternion
    {
        public readonly Vector3 From, To;
        private Quaternion(Vector3 from, Vector3 to) { From=from; To=to; }
        public static Quaternion identity => default;
        public static Quaternion LookRotation(Vector3 direction) => default;
        public static Quaternion FromToRotation(Vector3 from, Vector3 to) => new(from,to);
        public static Quaternion operator *(Quaternion a, Quaternion b) => b;
    }
    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
        public static float Max(float a, float b) => Math.Max(a,b);
        public static float Min(float a, float b) => Math.Min(a,b);
    }
}
namespace UnityEngine.Serialization { public sealed class FormerlySerializedAsAttribute : Attribute { public FormerlySerializedAsAttribute(string name) { } } }
public sealed partial class ItemDefinition
{
    public int id;
    public Mesh portableMesh;
    public Material portableMat;
    public bool isEquipment, oneItem;
    public static int ResolveStackCapacity(ItemManager manager, int id, int capacity) =>
        manager.TryGetItemDefinitionById(id, out var definition) && definition.oneItem ? Math.Min(1, capacity) : capacity;
}
public sealed class ItemManager
{
    public readonly Dictionary<int, ItemDefinition> Items = new();
    public bool TryGetItemDefinitionById(int id, out ItemDefinition value) => Items.TryGetValue(id, out value);
}
public sealed class GameManager
{
    public static GameManager Instance = new();
    public static bool TextInputFocused;
    public bool PlayerInteractionLocked;
    public readonly ItemManager ItemManger = new();
}
public sealed class PortableTemplateStack { public List<PortableObjectTemplate> stack; }
public sealed class PortableObjectTemplate { public PortableObject CreateEntity(bool authored) => new(); }
public sealed class PortableStack { public List<PortableObject> stack; }
public sealed class PortableObject
{
    public int ItemId = -1;
    public float InstallationHealth;
    public bool IsActive = true, IsVisualRenderingSuppressed, Disposed;
    public Vector3 WorldPosition;
    public int FailNextItemId = -1;
    public static PortableObject Create(Vector3 position, Quaternion rotation, Vector3 scale, int layer, string name) => new() { WorldPosition = position };
    public void SetCachedParent(Transform parent, bool keepWorld) { }
    public void SetCachedActive(bool active) => IsActive = active;
    public void SetVisualRenderingSuppressed(bool hidden) => IsVisualRenderingSuppressed = hidden;
    public bool SetItem(int id)
    {
        if (id == FailNextItemId) { FailNextItemId = -1; return false; }
        if (!GameManager.Instance.ItemManger.Items.ContainsKey(id)) return false;
        ItemId = id; InstallationHealth = 0; IsVisualRenderingSuppressed = false; return true;
    }
    public void SetInstallationHealth(float health) => InstallationHealth = health;
    public void CopyInstallationHealthFrom(PortableObject source) => InstallationHealth = source.InstallationHealth;
    public void Dispose() => Disposed = true;
}
public sealed class PlayerInventorySlotSaveState
{
    public int slotIndex, itemId, count, capacity;
    public List<float> installationHealth;
}
public partial class PlayerBag
{
    internal static PlayerBag Normal(int capacity)
    {
        var bag = new PlayerBag();
        bag.portableStack = new() { new() { stack = new() } };
        for (int i = 0; i < capacity; i++) bag.portableStack[0].stack.Add(new() { IsActive = false });
        bag.currentStack = new() { 0 };
        bag.initialized = true;
        return bag;
    }
    internal PortableObject Object(int index = 0) => portableStack[index].stack[0];
    internal void DestroyForTest() => OnDestroy();
}
public partial class BagSlot
{
    internal static bool Move(PlayerBag source, int sourceIndex, PlayerBag target, int targetIndex, int count, bool swap = false)
    {
        var sourceObjects = new List<PortableObject>();
        var targetObjects = new List<PortableObject>();
        int sourceCount = source.GetSlotCount(sourceIndex), targetCount = target.GetSlotCount(targetIndex);
        source.TryGetSlotObjects(sourceIndex, swap ? 0 : sourceCount - count, count, sourceObjects);
        target.TryGetSlotObjects(targetIndex, swap ? 0 : targetCount, swap ? targetCount : count, targetObjects);
        return ProjectF.Combat.PlayerInventoryTransfer.TryMove(source, sourceIndex, target, targetIndex, count, swap);
    }
}
public partial class Player : MonoBehaviour
{
    internal PlayerBag Bag = PlayerBag.Normal(200), Hand = PlayerBag.Normal(10);
    public PlayerBag GetBag() => Bag;
    public PlayerBag GetHandBag() => Hand;
    private ItemDefinition ResolveItemDefinition(int id) => GameManager.Instance.ItemManger.Items.TryGetValue(id, out var definition) ? definition : null;
    public void UpdateCarryState() { }
    public bool IsDead;
    private PlayerController playerController = new();
    public Transform BodyTransform => transform;
    private Animator animator = new();
    internal GameObject crossBowObject;
    private int GetUpperBodyLayer() => 0;
    internal void TickInventory() => RefreshSelectedEquipmentIdentity();
    internal void RefillAfterShot(int ammoId) => RefillAmmunitionAfterShot(ammoId);
    internal void RestoreAmmo(PlayerSaveData data) => RestoreAmmunition(data);
}
public sealed class PlayerSaveData
{
    public int selectedEquipmentSlot = -1;
    public List<PlayerInventorySlotSaveState> equipmentSlots = new(), ammunitionSlots = new();
}
