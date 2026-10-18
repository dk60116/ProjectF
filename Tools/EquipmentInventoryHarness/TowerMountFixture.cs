using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public sealed class HideInInspector : Attribute { }
    public readonly record struct Vector2Int(int x, int y);
}

public class InstallationObject : MonoBehaviour
{
    protected virtual void OnEnable() { }
    protected virtual void OnDisable() { }
    public virtual void PrepareForPool() { }
}
public class InputOutputModule : InstallationObject
{
    internal readonly Dictionary<(Vector2Int,int),int> Inputs = new();
    private float plannedDelta;
    private bool planned;
    internal bool AwakeForTest;
    public virtual bool IsWorkingForItemLight => false;
    public void ManagedUpdateTick(float deltaTime) { plannedDelta=deltaTime; planned=true; ApplyManagedUpdateTick(); }
    public virtual void ApplyManagedUpdateTick() { }
    protected bool TryBeginPlannedModuleApply(out float deltaTime) { deltaTime=plannedDelta; bool result=planned; planned=false; return result; }
    protected void ApplyPlannedBaseModuleTick(float dt) { TryStartNextCraft(); AwakeForTest=ShouldKeepRuntimeUpdateTickActive(); }
    protected virtual void TryStartNextCraft() { }
    protected virtual bool ShouldKeepRuntimeUpdateTickActive() => false;
    protected virtual bool ShouldKeepRuntimeUpdateTickActiveWithoutOperationalEnergy() => false;
    protected virtual bool ShouldPlayWorkAnimation() => false;
    protected virtual string ResolveObjectInfoStatus(out bool producing) { producing=false; return "No recipe"; }
    public string StatusForTest() => ResolveObjectInfoStatus(out _);
    protected virtual bool TryCollectAdditionalRuntimeInputItemIds(ICollection<int> ids) => false;
    protected virtual bool AppendAcceptedRuntimeInputItemIdsAtCoordinate(Vector2Int cell,ISet<int> ids) => false;
    internal bool AcceptedForTest(Vector2Int cell,ISet<int> ids) => AppendAcceptedRuntimeInputItemIdsAtCoordinate(cell,ids);
    protected bool ContainsRuntimeInputItemArea(Vector2Int coordinate,int id) => Inputs.ContainsKey((coordinate,id));
    protected bool TryResolveRuntimeInputItemBlock(int id,int count,ISet<Vector2Int> excluded,out object block,out Vector2Int coordinate)
    {
        block=null; coordinate=default;
        foreach(var pair in Inputs)
            if(pair.Key.Item2==id && pair.Value>=count) { coordinate=pair.Key.Item1; return true; }
        return false;
    }
    protected int ConsumeRuntimeInputAreaCenterObjects(Vector2Int coordinate,int id,int count,Vector3 destination,float interval)
    {
        if(!Inputs.TryGetValue((coordinate,id),out int available)||available<count) return 0;
        Inputs[(coordinate,id)]-=count;
        return count;
    }
    protected void WakeRuntimeUpdate() => AwakeForTest=true;
    protected void MarkManagedRuntimeVisualsDirty() { }
}
public interface IPlayerMapObjectInteraction
{
    bool CanPlayerInteract(Player player);
    bool TryPlayerInteract(Player player);
    int GetInteractionIconItemId(Player player);
}
public interface IPersistentInstallationItemStorage
{
    int PersistentStoredItemId { get; }
    void ApplyPersistentStoredItemId(int itemId);
}
public interface IPlayerItemMount
{
    bool AcceptsMountedItem(int itemId);
    bool TryMountItemFromSlot(Player player, PlayerBag sourceBag, int slotIndex);
}
public static class ProjectFApplicationLifecycle { public static bool IsQuitting; }
public sealed class TerrainGenerator : MonoBehaviour
{
    public static readonly TerrainGenerator Active = new();
    public int SaveCalls;
    public int SavedItemId;
    public static TerrainGenerator ResolveActive() => Active;
    public void SaveRuntimeInstallationState(InstallationObject owner)
    {
        SaveCalls++;
        SavedItemId = ((IPersistentInstallationItemStorage)owner).PersistentStoredItemId;
    }
}
namespace ProjectF.Monsters
{
    public sealed class MonsterWorld
    {
        public static MonsterWorld Current;
        public readonly List<MonsterInstance> Monsters = new();
        public int Queries;
        public bool TryFindNearestAttackTarget(Vector3 origin,float range,out MonsterInstance target)
        {
            Queries++; target=null; float nearest=range*range;
            foreach(var monster in Monsters)
            {
                float distance=(monster.ClosestCombatPoint(origin)-origin).sqrMagnitude;
                if(!monster.IsActive||monster.CurrentHealth<=0||distance>nearest) continue;
                target=monster; nearest=distance;
            }
            return target!=null;
        }
    }
}
internal sealed class PlayerItemStorageReservation
{
    private readonly PlayerBag bag;
    private readonly PortableObject target;
    internal PlayerItemStorageReservation(PlayerBag bag, PortableObject target) { this.bag=bag; this.target=target; }
    public void Commit() => bag.CommitReservedObject(target);
}
internal static class PlayerItemStorageUtility
{
    public static bool TryReserveHand(Player player, int itemId, out PlayerItemStorageReservation reservation)
        => TryReserveBag(player.GetHandBag(), itemId, out reservation);
    public static bool TryReserveBag(Player player, int itemId, int preferred, bool fallback, out PlayerItemStorageReservation reservation)
        => TryReserveBag(player.GetBag(), itemId, out reservation);
    private static bool TryReserveBag(PlayerBag bag, int id, out PlayerItemStorageReservation reservation)
    {
        reservation = null;
        if (bag == null || !bag.TryReserveObject(id, out var target) || target == null) return false;
        reservation = new(bag, target);
        return true;
    }
}
