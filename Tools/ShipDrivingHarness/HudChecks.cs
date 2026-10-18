using UnityEngine;

public sealed class ItemDefinition
{
    public enum ItemLightMode { None, Toggle }
    public int id;
    public List<Sprite> interactionButtonList = new();
    public Sprite icon;
    public ItemLightMode lightMode;
}
public sealed class GameManager
{
    public static GameManager Instance = new();
    public Player Player;
}
public interface IPlayerItemMount { }
public sealed class FixtureTower : MapObject, IPlayerItemMount { }
public sealed class BoxObject : MapObject { }
public interface IFenceDoorTarget : IMapObjectTarget { }
public sealed class ResourceDefinition { public enum PlacementCategory { Other, Oil } }
public sealed class ResourceInstance : MapObject { public ResourceDefinition.PlacementCategory PlacementCategory; }
public interface IPlayerMapObjectInteraction
{
    bool CanPlayerInteract(Player player);
    int GetInteractionIconItemId(Player player);
}
public class Animal : MonoBehaviour { public float GetWorldRadius() => .5f; }
namespace ProjectF.Monsters { public sealed class MonsterInstance { } }
namespace ProjectF.Combat
{
    public static class PlayerProjectileWorld
    {
        public static bool IsDamageable(object target) => target is Animal || target is ProjectF.Monsters.MonsterInstance;
    }
}
public sealed partial class InteractionButton : MonoBehaviour
{
    private UnityEngine.UI.Button cachedButton;
    public InteractionButton() { Component = new UnityEngine.UI.Button(); }
    public bool Visible => gameObject.activeSelf;
    public bool Interactable => ((UnityEngine.UI.Button)Component).interactable;
    public Sprite Icon;
    public void SetIcon(Sprite icon) => Icon = icon;
}
public partial class PlayerHUD
{
    private readonly InteractionButton InteractionButton = new();
    private readonly InteractionButton DoorInteractionButton = new();
    private object clickedObjectInfoTarget;
    private IMapObjectTarget currentInteractionMapObject;
    private BoxObject currentInteractionBoxObject;
    private IFenceDoorTarget currentInteractionDoorTarget;
    private ResourceInstance currentInteractionResource;
    private ProjectF.Monsters.MonsterInstance currentInteractionMonster;
    private Animal currentInteractionAnimal;
    private void ClearInteractionTargets()
    {
        currentInteractionMapObject = null;
        currentInteractionMonster = null;
        currentInteractionAnimal = null;
        currentInteractionBoxObject = null;
        currentInteractionDoorTarget = null;
        currentInteractionResource = null;
    }
    private void ClearContextInteractionButtonState() { ClearInteractionTargets(); HideInteractionButton(InteractionButton); }
    private void UpdateInteractionButtonLayout() { }
    private bool TryGetClickedObjectInfoFocusedMapObject(out IMapObjectTarget target)
    { target = clickedObjectInfoTarget as IMapObjectTarget; return target != null; }
    private static bool IsLoggingMachineFilterTarget(IMapObjectTarget target) => false;
    private static bool TryResolveTrainStation(IMapObjectTarget target, out object station) { station = null; return false; }
    private InteractionButton ResolveDoorInteractionButtonForUse() => InteractionButton;
    private void SetTrainConnectionInteractionButtonState(SteamTrain train) { }
    private static ItemDefinition GetItemDefinition(int id) => null;
    private static Sprite ResolveInteractionIcon(BoxObject box) => ResolveInteractionIcon((IMapObjectTarget)box, 0);
    private static Sprite ResolveInteractionIcon(IFenceDoorTarget door) => ResolveInteractionIcon((IMapObjectTarget)door, 0);
    private static Sprite ResolveInteractionIcon(ResourceInstance resource) => ResolveInteractionIcon((IMapObjectTarget)resource, 0);

    public static void CheckEquippedVehicleInteraction()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        var controller = new PlayerController();
        var player = new Player { Component = controller };
        GameManager.Instance.Player = player;
        var hud = new PlayerHUD();
        var boardingIcon = new Sprite();
        var raft = new Raft { BoundItemDefinition = new() { id = 135, interactionButtonList = new() { boardingIcon, new Sprite() } } };
        hud.clickedObjectInfoTarget = raft;
        hud.TickEquippedInteraction();
        Check(hud.InteractionButton.Visible && hud.currentInteractionMapObject == raft && hud.InteractionButton.Icon == boardingIcon,
            "Equipped crossbow must not hide a selected raft's boarding button");

        controller.Candidates.Add((new BoxObject { BoundItemDefinition = raft.BoundItemDefinition }, 0));
        controller.Candidates.Add((raft, 1));
        hud.clickedObjectInfoTarget = null;
        hud.TickEquippedInteraction();
        Check(hud.InteractionButton.Visible && hud.currentInteractionMapObject == raft,
            "A nearby raft remains an automatic boarding target with a ranged weapon equipped; boxes do not hijack it");
        var other = new Train { BoundItemDefinition = raft.BoundItemDefinition };
        controller.Candidates.Add((other, .5f));
        hud.TickEquippedInteraction();
        Check(hud.currentInteractionMapObject == other, "Automatic boarding preserves nearest valid vehicle selection");
        other.isActiveAndEnabled = false;
        hud.TickEquippedInteraction();
        Check(hud.currentInteractionMapObject == raft, "Inactive vehicles cannot steal the boarding button");
        raft.InRange = false;
        hud.clickedObjectInfoTarget = raft;
        hud.TickEquippedInteraction();
        Check(!hud.InteractionButton.Visible && hud.currentInteractionMapObject == null, "Out-of-range boarding stays hidden");
        raft.InRange = true;

        var monster = new ProjectF.Monsters.MonsterInstance();
        hud.clickedObjectInfoTarget = monster;
        hud.TickEquippedInteraction();
        Check(hud.currentInteractionMonster == monster && hud.InteractionButton.Icon == player.SelectedEquipment.icon
            && hud.currentInteractionMapObject == null, "A selected monster retains ranged attack priority over nearby vehicles");
        var animal = new Animal();
        hud.clickedObjectInfoTarget = animal;
        hud.TickEquippedInteraction();
        Check(hud.currentInteractionAnimal == animal && hud.currentInteractionMapObject == null,
            "A selected animal retains ranged attack behavior");

        var tower = new FixtureTower { BoundItemDefinition = raft.BoundItemDefinition };
        hud.clickedObjectInfoTarget = tower;
        hud.TickEquippedInteraction();
        Check(hud.currentInteractionMapObject == tower, "Existing selected Tower/Desk item mounting still wins over automatic vehicles");
        hud.clickedObjectInfoTarget = raft;
        raft.BoundItemDefinition.interactionButtonList.Clear();
        hud.TickEquippedInteraction();
        Check(!hud.InteractionButton.Visible, "Missing icons still fail closed instead of showing the equipment icon as boarding");

        raft.BoundItemDefinition.interactionButtonList.Add(boardingIcon);
        var alightingIcon = new Sprite();
        raft.BoundItemDefinition.interactionButtonList.Add(alightingIcon);
        var hull = new BoxCollider { transform = raft.transform };
        typeof(Ship).GetField("drivingCollider", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(raft, hull);
        raft.AddPlayerPoint(new Transform());
        Check(raft.TryDockPlayer(player), "Mounted HUD test enters the real vehicle docking path");
        TerrainGenerator.Active = new();
        Check(!controller.CanDismountFromVehicle(), "Shared dismount query rejects open water");
        hud.TickMountedInteraction();
        Check(!hud.InteractionButton.Visible && hud.currentInteractionMapObject == raft,
            "Open-water raft hides Alighting but keeps mounted context for keyboard routing");
        Check(!hud.InteractionButton.gameObject.activeSelf && !hud.InteractionButton.Interactable,
            "Hidden dismount removes the complete button object and its clickable state, not just the icon");
        raft.transform.position = new(.8f, 0, 0);
        TerrainGenerator.Active.Water = point => point.x >= 2 ? -1 : 1;
        hud.TickMountedInteraction();
        Check(controller.CanDismountFromVehicle() && hud.InteractionButton.Visible && hud.InteractionButton.Icon == alightingIcon,
            "Alighting appears when the same actual exit resolver finds reachable dry shore");
        Check(hud.InteractionButton.Interactable, "Returning to shore restores the complete button and its clickability");
        TerrainGenerator.Active.Clear = _ => false;
        hud.TickMountedInteraction();
        Check(!hud.InteractionButton.Visible, "Blocked shore hides Alighting immediately");
        TerrainGenerator.Active.Clear = _ => true;
        raft.transform.position = Vector3.zero;
        hud.TickMountedInteraction();
        Check(!hud.InteractionButton.Visible, "Leaving the shore hides the button without clearing the mount");
        raft.transform.position = new(.8f, 0, 0);
        PlayerController.InputLocked = true;
        hud.TickMountedInteraction();
        Check(!hud.InteractionButton.Visible, "Input-locked dismount cannot advertise an actionable button");
        PlayerController.InputLocked = false;
        TerrainGenerator.Active = null;
        hud.TickMountedInteraction();
        Check(!hud.InteractionButton.Visible, "Unavailable terrain hides Alighting safely");
        Console.WriteLine($"PASS: {checks} production equipped-vehicle HUD selection/icon checks");
    }
}
