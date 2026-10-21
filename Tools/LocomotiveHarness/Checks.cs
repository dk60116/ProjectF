using System;
using System.Collections.Generic;
using UnityEngine;

public static class Time { public static int frameCount; }
public sealed class Sprite { }
public sealed class SpriteRenderer
{
    public Sprite sprite;
    public bool enabled;
}
public sealed class SceneObject { public bool activeInHierarchy = true; }
public class Train
{
    public readonly SceneObject gameObject = new SceneObject();
    public readonly List<Train> ConnectedTrains = new List<Train>();
}
public sealed class ItemManager { public readonly List<ItemDefinition> ItemDefinitions = new List<ItemDefinition>(); }
public sealed class GameManager
{
    public static GameManager Instance;
    public readonly ItemManager ItemManger = new ItemManager();
}
public sealed class ItemDefinition
{
    public enum EnergyType { None, Burn, Diesel = 6 }
    public EnergyType energyType, useEnergyType;
    public int energyAmount;
    public float useEnergyAmount;
    public bool isFluid;
    public Sprite icon;
    public bool UsesEnergyType(EnergyType type) => useEnergyType == type;
    public static float ResolveUseEnergyRatePerSecond(ItemDefinition item, EnergyType type) =>
        item.UsesEnergyType(type) ? item.useEnergyAmount : 0;
}
public static class InputOutputModule
{
    public static readonly Dictionary<int, ItemDefinition> Items = new Dictionary<int, ItemDefinition>();
    public static ItemDefinition ResolveItemDefinition(int id) => Items.TryGetValue(id, out var item) ? item : null;
}
public sealed class PortableObject { public Vector3 WorldPosition; }
public sealed class Pose { public Vector3 position; }
public sealed class PlayerBag
{
    public readonly List<int> Ids = new List<int>();
    public int SlotCount => Ids.Count;
    public int Refreshes;
    public int GetSlotItemId(int slot) => Ids[slot];
    public bool TryRemoveItemsAtSlot(int slot, int count, out int id, out int removed, out Vector3 start)
    { id = Ids[slot]; Ids.RemoveAt(slot); removed = 1; start = default; return true; }
    public void RefreshExternalStackCounts(bool value) { Refreshes++; }
}
public sealed class Player
{
    public PlayerBag Bag = new PlayerBag(), Hand = new PlayerBag();
    public int CarryRefreshes;
    public PlayerBag GetBag() => Bag;
    public PlayerBag GetHandBag() => Hand;
    public void UpdateCarryState() { CarryRefreshes++; }
}
public sealed partial class FreightCar : Train
{
    private SpriteRenderer fuelRoleIcon = new SpriteRenderer();
    public Sprite RoleIcon => fuelRoleIcon.sprite;
    public bool RoleIconVisible => fuelRoleIcon.enabled;
    public void RefreshIcon() => RefreshFuelRoleIcon();
    public readonly List<int> Items = new List<int>();
    public bool TryTakeOneItem(Vector3 position, Predicate<int> filter, out int id,
        out Vector3 pickup, out PortableObject portable)
    {
        id = -1; pickup = default; portable = null;
        for (int i = 0; i < Items.Count; i++)
            if (filter(Items[i])) { id = Items[i]; Items.RemoveAt(i); return true; }
        return false;
    }
}
public class RailHandcar : Train
{
    public ProjectF.Simulation.VehicleMotionState Motion;
    public float CurrentVehicleSpeed => Math.Abs(Motion.SignedSpeed);
    public void HandleMountedInput(Vector3 direction, float ignored, float dt) =>
        Motion.Advance(direction.z, dt, 6.2f, 2f, 3.2f);
}
public partial class Locomotive
{
    const float BurnEnergyEpsilon = .0001f, BurnEnergyDrivingSpeedThreshold = .0001f, WaterEpsilon = .0001f;
    private long storedBurnEnergyUnits, burnEnergyGaugeCapacityUnits;
    private Predicate<int> cachedFuelItemFilter;
    private ItemDefinition cachedFuelDisplayDefinition;
    private bool waitingForPropulsionFuel;
    private int lastDrivenInputFrame;
    public readonly Pose transform = new Pose();
    public ItemDefinition Installed;
    public FreightCar FuelCar;
    public float StoredWater;
    public int SpentWaterCalls, FuelVisuals;
    public static bool FreeTrain;
    private static bool IsFreeTrainEnabled() => FreeTrain;
    private ItemDefinition ResolveInstalledDefinition() => Installed;
    private float ObjectInfoWaterUseRatePerSecond => UsesWaterSupply ? .8f : 0;
    private int ResolveWaterItemId() => 3;
    private bool CanProvideFluidItem(int id, float amount) => StoredWater >= amount;
    private bool TryConsumeFluidLiters(int id, float amount, out float consumed)
    { SpentWaterCalls++; StoredWater -= amount; consumed = amount; return true; }
    private void RequestWaterPipeRetract() { }
    private void StopMovementParticle(bool clear) { }
    public bool TryGetRearFreightCar(out FreightCar car) { car = FuelCar; return car != null; }
    private bool TryGetTopPortableObjectInSlot(PlayerBag bag, int slot, out PortableObject portable)
    { portable = null; return false; }
    private void DestroyPortableMoveObject(PortableObject portable) { }
    private void PlayBurnEnergyPortableMove(PortableObject portable, int id, Vector3 position, bool source)
    { FuelVisuals++; }
    public bool Drive(Vector3 direction, float dt, Player player = null) =>
        HandleResolvedDriveMotion(direction, 0, dt, player) == DriveMotionOutcome.Applied;
    public bool Accepts(int id) => TryResolveBurnEnergyAmount(id, out _);
    public long EnergyUnits => storedBurnEnergyUnits;
    public float Energy => DeterministicSimulationUnits.ToFloat(storedBurnEnergyUnits);
}
public partial class DieselTrain
{
    private ItemDefinition dieselCan;
    public DieselTrain(ItemDefinition fuel, float rate = 10)
    { dieselCan = fuel; Installed = new ItemDefinition { useEnergyType = ItemDefinition.EnergyType.Diesel, useEnergyAmount = rate }; }
}
public sealed class SteamTrain : Locomotive
{
    public SteamTrain() { Installed = new ItemDefinition { useEnergyType = ItemDefinition.EnergyType.Burn, useEnergyAmount = 10 }; }
}
static class Checks
{
    static int count;
    static readonly ItemDefinition Can = new ItemDefinition { energyType = ItemDefinition.EnergyType.Diesel, energyAmount = 100 };
    static void Check(bool condition, string name)
    { count++; if (!condition) throw new Exception(name); }
    static void Near(float a, float b, string name) => Check(Math.Abs(a - b) < .0001f, name);
    static void Main()
    {
        InputOutputModule.Items[1] = Can;
        InputOutputModule.Items[2] = new ItemDefinition { energyType = ItemDefinition.EnergyType.Burn, energyAmount = 100 };
        InputOutputModule.Items[3] = new ItemDefinition { energyType = ItemDefinition.EnergyType.Diesel, energyAmount = 100, isFluid = true };
        InputOutputModule.Items[4] = new ItemDefinition { energyType = ItemDefinition.EnergyType.Diesel, energyAmount = 100 };
        var engine = new DieselTrain(Can);
        Check(engine.Accepts(1), "Configured Diesel Can accepted");
        Check(!engine.Accepts(2) && !engine.Accepts(3) && !engine.Accepts(4) && !engine.Accepts(-1),
            "Coal, fluid diesel, another diesel item and missing definition rejected");
        Check(!engine.UsesWaterSupply, "Diesel never requires water");
        var player = new Player(); player.Bag.Ids.AddRange(new[] { 2, 3, 4, 1, 1 });
        Check(engine.Drive(Vector3.forward, .1f, player), "Dry diesel accelerates using bag fuel");
        Near(engine.Energy, 99, "One can credits energy then exact tick charge commits");
        Check(player.Bag.Ids.Count == 4 && player.Bag.Ids[0] == 2 && engine.FuelVisuals == 1,
            "Only the diesel can is removed, exactly once");
        Check(engine.SpentWaterCalls == 0, "Dry diesel does not consume water");
        long saved = engine.EnergyUnits;
        Check(engine.Drive(Vector3.zero, .1f, player), "Release input coasts");
        Check(engine.EnergyUnits == saved, "No fuel consumed while coasting/idling");
        Check(engine.Drive(Vector3.back, .1f, player), "Reverse input supported");
        Near(engine.Energy, 98, "Reverse drive consumes configured fuel");
        engine.CaptureBurnEnergyStateUnits(out long units, out long capacity);
        var restored = new DieselTrain(Can); restored.ApplyBurnEnergyStateUnits(units, capacity);
        Check(restored.EnergyUnits == units, "Save/load preserves deterministic remaining fuel");
        restored.ClearBurnEnergyState(); Check(restored.EnergyUnits == 0, "Recovery/pool clear removes spent fuel state");
        var hand = new Player(); hand.Hand.Ids.Add(1);
        Check(restored.Drive(Vector3.forward, .1f, hand), "Hand fuel is usable after bag fallback");
        Check(hand.Hand.Ids.Count == 0 && hand.Hand.Refreshes == 1 && hand.CarryRefreshes == 1,
            "Hand slot and carry state refresh after consumption");
        var car = new FreightCar(); car.Items.AddRange(new[] { 2, 3, 4, 1 });
        var freight = new DieselTrain(Can) { FuelCar = car };
        Check(freight.Drive(Vector3.forward, .1f, player), "Rear fuel car can power unmounted automatic movement");
        Check(car.Items.Count == 3 && player.Bag.Ids.Count == 4, "Freight fuel preferred over player fuel");
        var empty = new DieselTrain(Can); empty.Motion.SignedSpeed = 3;
        Check(!empty.Drive(Vector3.forward, .1f), "Empty diesel blocks propulsion");
        Check(empty.CurrentVehicleSpeed > 0 && empty.CurrentVehicleSpeed < 3, "Fuel exhaustion decelerates, not instantaneous stop");
        for (int i = 0; i < 30; i++) empty.Drive(Vector3.forward, .1f);
        Near(empty.CurrentVehicleSpeed, 0, "Exhausted engine eventually stops");
        var refill = new Player(); refill.Bag.Ids.Add(1);
        Check(empty.Drive(Vector3.forward, .1f, refill) && empty.CurrentVehicleSpeed > 0,
            "Adding a diesel can resumes propulsion without separate fueling action");
        var unconfigured = new DieselTrain(Can, 0);
        var untouched = new Player(); untouched.Bag.Ids.Add(1);
        Check(!unconfigured.Drive(Vector3.forward, .1f, untouched), "Unset zero use rate cannot grant free propulsion");
        Check(untouched.Bag.Ids.Count == 1, "Unconfigured locomotive cannot consume a can");
        Locomotive.FreeTrain = true;
        Check(unconfigured.Drive(Vector3.forward, .1f), "Existing FreeTrain debug option remains supported");
        Locomotive.FreeTrain = false;
        var steam = new SteamTrain(); var coal = new Player(); coal.Bag.Ids.Add(2);
        Check(!steam.Drive(Vector3.forward, .1f, coal) && coal.Bag.Ids.Count == 1, "Dry steam still blocks without wasting coal");
        steam.StoredWater = 1;
        Check(steam.Drive(Vector3.forward, .1f, coal) && coal.Bag.Ids.Count == 0, "Steam still consumes coal and water");
        Near(steam.StoredWater, .92f, "Steam water use unchanged");
        CheckFuelRoleIcons();
        for (int i = 1; i <= 1000; i++)
        {
            float dt = i % 2 == 0 ? 1f / 60 : .125f;
            float rate = .25f + i % 40;
            var test = new DieselTrain(Can, rate);
            test.ApplyBurnEnergyStateUnits(DeterministicSimulationUnits.FromFloat(100), DeterministicSimulationUnits.FromFloat(100));
            Check(test.Drive(i % 2 == 0 ? Vector3.forward : Vector3.back, dt), "Parameterized dry diesel move");
            long expected = DeterministicSimulationUnits.FromFloat(100) - DeterministicSimulationUnits.FromFloat(rate * dt);
            Check(test.EnergyUnits == expected, "Configured rates use deterministic tick accounting");
            Check(test.SpentWaterCalls == 0, "Parameterized move never uses water");
        }
        Console.WriteLine($"PASS {count} locomotive fuel/motion/persistence checks (production methods; boundary inventory doubles).");
    }

    static void CheckFuelRoleIcons()
    {
        Can.icon = new Sprite();
        ItemDefinition coal = InputOutputModule.Items[2];
        coal.icon = new Sprite();
        var wood = new ItemDefinition { energyType = ItemDefinition.EnergyType.Burn, energyAmount = 20, icon = new Sprite() };
        GameManager.Instance = new GameManager();
        GameManager.Instance.ItemManger.ItemDefinitions.AddRange(new[] { coal, wood, Can });
        var car = new FreightCar();
        var steam = new SteamTrain { FuelCar = car };
        var diesel = new DieselTrain(Can) { FuelCar = car };
        car.ConnectedTrains.Add(steam);
        car.RefreshIcon();
        Check(car.IsFuelSupplyCar && car.RoleIconVisible && car.RoleIcon == coal.icon,
            "Steam supply car uses a valid burn fuel from ItemDefinition");
        car.ConnectedTrains.Clear(); car.ConnectedTrains.Add(diesel);
        car.RefreshIcon();
        Check(car.RoleIconVisible && car.RoleIcon == Can.icon, "Diesel reconnection replaces coal with the configured can icon");
        Check(new DieselTrain(null).FuelDisplayDefinition == null, "Missing diesel configuration cannot fall back to unrelated fuel");
        Can.energyAmount = 0; car.RefreshIcon();
        Check(!car.RoleIconVisible && car.RoleIcon == null, "Zero-energy fuel does not advertise a usable supply");
        Can.energyAmount = 100; Can.isFluid = true; car.RefreshIcon();
        Check(!car.RoleIconVisible, "Fluid diesel cannot be advertised for a can-only locomotive");
        Can.isFluid = false;
        Sprite icon = Can.icon; Can.icon = null; car.RefreshIcon();
        Check(!car.RoleIconVisible, "Missing icon hides the role instead of retaining coal");
        Can.icon = icon; car.RefreshIcon();
        Check(car.RoleIconVisible && car.RoleIcon == icon, "Restoring the configured icon refreshes immediately");
        diesel.gameObject.activeInHierarchy = false; car.RefreshIcon();
        Check(!car.IsFuelSupplyCar && !car.RoleIconVisible, "Inactive engine cannot assign a fuel role");
        diesel.gameObject.activeInHierarchy = true; diesel.FuelCar = new FreightCar(); car.RefreshIcon();
        Check(!car.RoleIconVisible, "A front/non-supplier car cannot retain the fuel icon");
        diesel.FuelCar = car; car.RefreshIcon();
        car.ConnectedTrains.Clear(); car.RefreshIcon();
        Check(!car.IsFuelSupplyCar && !car.RoleIconVisible && car.RoleIcon == null, "Disconnect clears the previous fuel role");
        coal.energyAmount = 0;
        Check(steam.FuelDisplayDefinition == wood, "Burn icon cache revalidates the actual consumption policy");
        coal.energyAmount = 100;
        car.ConnectedTrains.Add(diesel);
        for (int i = 0; i < 100; i++) car.RefreshIcon();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) car.RefreshIcon();
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "Warm fuel role refresh allocates zero managed bytes");
    }
}
