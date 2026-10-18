using System.Collections.Generic;
using System.Reflection;
using ProjectF.Combat;
using ProjectF.Monsters;
using UnityEngine;

static partial class Checks
{
    static void CheckTowerCombat()
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var weapon = GameManager.Instance.ItemManger.Items[132];
        var arrow = GameManager.Instance.ItemManger.Items[133];
        weapon.attackDamage=14; weapon.fireRate=2; weapon.attackRange=8;
        arrow.attackDamage=6; arrow.fireRate=1; arrow.attackRange=6; arrow.projectileSpeed=30;
        arrow.portableMesh=new Mesh(); arrow.portableMat=new Material();
        var visual = new GameObject();
        var tower = new Tower();
        typeof(Tower).GetField("weaponItem",flags).SetValue(tower,weapon);
        typeof(Tower).GetField("weaponVisual",flags).SetValue(tower,visual);
        var cell = new Vector2Int(1,0);
        tower.Inputs[(cell,133)]=5;
        tower.Inputs[(cell,3)]=9;
        var ids = new HashSet<int>();
        Check(tower.TryCollectAmmunitionItemIds(ids) && ids.SetEquals(new[]{133}), "Placement binds only compatible ammunition to blank tower I/O inputs");
        ids.Clear();
        Check(tower.AcceptedForTest(cell,ids) && ids.SetEquals(new[]{133}), "Runtime input filters accept the compatible projectile, not unrelated items");
        ids.Clear();
        Check(!tower.AcceptedForTest(new Vector2Int(99,99),ids), "Unrelated coordinates are not ammunition inputs");
        var world = MonsterWorld.Current = new MonsterWorld();
        var target = new MonsterInstance { Position=new(0,.24f,3) };
        world.Monsters.Add(target);
        MapObjectTickManager.SimulationPaused=MapObjectTickManager.WaitingForWorldLoad=SaveManager.GameplayInputBlocked=false;
        tower.ManagedUpdateTick(.1f);
        Check(tower.Inputs[(cell,133)]==5 && world.Queries==0 && tower.StatusForTest()=="No weapon", "Unarmed tower neither targets nor consumes ammunition");
        tower.ApplyPersistentStoredItemId(132);
        Time.time=0; Time.frameCount=100;
        tower.ManagedUpdateTick(.1f);
        var projectiles = TerrainGenerator.Active.gameObject.GetComponent<PlayerProjectileWorld>();
        Check(projectiles!=null && tower.Inputs[(cell,133)]==4 && tower.Inputs[(cell,3)]==9 && world.Queries==1,
            "Armed tower launches one actual projectile and consumes exactly one compatible I/O item");
        Check(target.CurrentHealth==100 && tower.StatusForTest()=="Attacking", "Tower damage is delayed until projectile arrival rather than instant on firing");
        var update = typeof(PlayerProjectileWorld).GetMethod("Update",flags);
        Time.frameCount++; Time.deltaTime=.2f; Time.time=.2f;
        update.Invoke(projectiles,null);
        Check(target.CurrentHealth==80, "Tower projectile arrival applies combined weapon and ammo damage");
        tower.ManagedUpdateTick(.9f);
        Check(tower.Inputs[(cell,133)]==4, "The slower weapon/ammo fire rate sets tower cooldown");
        tower.ManagedUpdateTick(.11f);
        Check(tower.Inputs[(cell,133)]==3 && world.Queries==1, "Cooldown completion fires again while retaining a valid target without another spatial scan");
        projectiles.ClearForLoad();
        target.CurrentHealth=0;
        var next = new MonsterInstance { Position=new(1,.24f,3) }; world.Monsters.Add(next);
        tower.ManagedUpdateTick(1.1f);
        Check(tower.Inputs[(cell,133)]==2 && world.Queries==2, "A destroyed enemy is replaced by another live enemy");
        projectiles.ClearForLoad();
        next.Position=new(100,.24f,0);
        tower.ManagedUpdateTick(1.1f);
        Check(tower.Inputs[(cell,133)]==2 && tower.StatusForTest()=="Waiting for target", "Out-of-range enemies never consume ammunition");
        int queries=world.Queries;
        tower.ManagedUpdateTick(.1f); tower.ManagedUpdateTick(.1f);
        Check(world.Queries==queries, "Idle target acquisition is throttled rather than rescanning every tick");
        tower.Inputs[(cell,133)]=0;
        tower.ManagedUpdateTick(1f);
        Check(world.Queries==queries && !tower.AwakeForTest && tower.StatusForTest()=="Waiting for ammunition",
            "Empty ammunition stops scans, sleeps the tower and reports ammunition standby");
        tower.Inputs[(cell,133)]=3; next.Position=new(1,.24f,0);
        MapObjectTickManager.SimulationPaused=true; tower.ManagedUpdateTick(1f);
        MapObjectTickManager.SimulationPaused=false; SaveManager.GameplayInputBlocked=true; tower.ManagedUpdateTick(1f);
        SaveManager.GameplayInputBlocked=false; MapObjectTickManager.WaitingForWorldLoad=true; tower.ManagedUpdateTick(1f);
        MapObjectTickManager.WaitingForWorldLoad=false;
        Check(tower.Inputs[(cell,133)]==3, "Pause and world-load gates cannot spend ammunition or fire");
        arrow.portableMesh=null; tower.ManagedUpdateTick(1f);
        Check(tower.Inputs[(cell,133)]==3, "Missing projectile visuals cannot silently consume ammunition");
        arrow.portableMesh=new Mesh(); tower.ManagedUpdateTick(1f);
        Check(tower.Inputs[(cell,133)]==2, "Supplying valid ammo and a target resumes shooting");
        var second = new Tower();
        typeof(Tower).GetField("weaponItem",flags).SetValue(second,weapon);
        typeof(Tower).GetField("weaponVisual",flags).SetValue(second,new GameObject());
        second.ApplyPersistentStoredItemId(132); second.Inputs[(cell,133)]=1; second.ManagedUpdateTick(.1f);
        Check(ReferenceEquals(typeof(Tower).GetField("projectileWorld",flags).GetValue(second),projectiles),
            "All towers share the terrain projectile pool instead of creating one update loop per tower");
        MapObjectTickManager.WaitingForWorldLoad=true; update.Invoke(projectiles,null); MapObjectTickManager.WaitingForWorldLoad=false;
        Time.frameCount++; Time.time+=1; update.Invoke(projectiles,null);
        Check(next.CurrentHealth==100, "Loading clears in-flight tower shots before they can hit after load");
        tower.ApplyPersistentStoredItemId(-1); tower.ManagedUpdateTick(10f);
        Check(tower.Inputs[(cell,133)]==2 && !tower.AwakeForTest, "Removing the weapon stops shooting and lets the empty tower sleep");
        projectiles.ClearForLoad(); MonsterWorld.Current=null;
    }
}
