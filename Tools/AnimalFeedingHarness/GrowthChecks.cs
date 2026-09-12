using System;
using System.Collections.Generic;
using System.IO;

public static class AnimalAISettings { public const float DefaultHerdAreaRadius = 30; }
public partial class Animal
{
    private float DinoAge = 10, growthFoodEnergy;
    private bool growthInitialized, needsInitialized;
    public float Age => DinoAge;
    public bool IsFullyGrown => Age >= AnimalDefinition.MaxSpawnAge;
    public float RequiredGrowthFoodEnergy => animalDefinition?.NeedsSettings.GrowthEnergyPerLevel ?? 100;
    public float CurrentGrowthFoodEnergy => IsFullyGrown ? RequiredGrowthFoodEnergy : growthFoodEnergy;
    public float AppliedGrowthScale;
    public bool EatingAnimation;
    public float RemainingEatingAnimationSeconds;
    public void SetAIAnimation(float speed, bool isEating, bool drinking, bool resting, bool looking, bool fleeing, bool running, float playbackScale)
        => EatingAnimation = isEating;
    private bool InitializeGrowth() => true;
    private void SetGrowth(float growth) => AppliedGrowthScale = growth;
    public void SetGrowthRequirement(float amount) => animalDefinition = new() { NeedsSettings = new() { GrowthEnergyPerLevel = amount } };
    public void RestoreGrowth(AnimalSaveEntry entry) { SetAge(entry.age); RestoreNeedsState(entry); }
}
public static partial class GrowthSaveProbe
{
    private const int MaxSerializedListCount = 1000000;
    private static void WriteIntList(BinaryWriter writer, List<int> values)
    {
        writer.Write(values.Count);
        foreach (int value in values) writer.Write(value);
    }
    private static List<int> ReadIntList(BinaryReader reader)
    {
        int count = reader.ReadInt32(); var result = new List<int>();
        for (int i = 0; i < count; i++) result.Add(reader.ReadInt32());
        return result;
    }
    public static AnimalSaveEntry RoundTrip(AnimalSaveEntry entry, bool legacy = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        WriteAnimalEntry(writer, entry); writer.Flush();
        if (legacy)
        {
            // v52 had one timer/count pair and no growth or per-meal timer list.
            stream.SetLength(stream.Length - sizeof(float) - sizeof(int) - entry.pendingDefecations.Count * sizeof(float));
            stream.Position = stream.Length;
            writer.Write(300f);
            writer.Write(entry.pendingDefecations.Count);
            writer.Flush();
        }
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        var restored = ReadAnimalEntry(reader, legacy ? 52 : 54);
        if (stream.Position != stream.Length) throw new Exception("Animal record length mismatch");
        return restored;
    }
}
public static partial class Checks
{
    private static void RunFoodStackConcurrencyChecks()
    {
        RunArmDeliveryFoodChecks();
        TerrainGenerator.Active = new(); var food = Drop(0); food.Count = 10;
        var first = new AnimalAIController { feedingDuration = 3 }; first.Tick(.1f);
        var others = new AnimalAIController[9];
        for (int i = 0; i < others.Length; i++) { others[i] = new(); others[i].Tick(.1f); }
        Check(food.Count == 9, "ten hungry animals cannot empty one stack in the same tick");
        first.animal.RemainingEatingAnimationSeconds = 2;
        first.Tick(1.1f);
        foreach (var other in others) other.Tick(1.1f);
        Check(food.Count == 9, "shared stack stays reserved throughout a long meal animation");
        first.animal.RemainingEatingAnimationSeconds = 0; first.Tick(2.1f);
        foreach (var other in others) other.Tick(1.1f);
        Check(food.Count == 8, "meal completion releases the stack to exactly one waiting animal");
        var secondStack = Drop(1); secondStack.Count = 3;
        new AnimalAIController().Tick(.1f);
        Check(secondStack.Count == 2 && food.Count == 8, "different food stacks can be eaten independently");
        others[0].animal.IsAlive = false;
        new AnimalAIController().Tick(.1f);
        Check(food.Count == 7, "dead feeder cannot leave a permanent reservation on the remaining stack");
    }

    private static void RunArmDeliveryFoodChecks()
    {
        static PortableObject Delivery(Block block, Vector3 position, bool settled = true, int itemId = 1)
        {
            var item = new PortableObject { ItemId = itemId, transform = new() { position = position },
                Gate = new() { Settled = settled } };
            block.CenterStack.Add(item);
            return item;
        }
        static bool Food(int id) => GameManager.Instance.ItemManger.TryGetItemDefinitionById(id, out var item)
            && item.Food && item.energyAmount > 0;

        TerrainGenerator.Active = new();
        var block = Drop(0); block.Count = 0;
        Delivery(block, Vector3.zero); Delivery(block, Vector3.zero);
        var incoming = Delivery(block, Vector3.zero, settled: false);
        var animal = new AnimalAIController();
        animal.animal.SetAge(2);
        animal.Tick(.1f); animal.UpdateAnimationForCheck();
        Check(block.CenterStack.Count == 3 && !animal.animal.EatingAnimation,
            "arm delivery in flight cannot be eaten and does not expose lower items in its stack");
        incoming.Gate.Settled = true;
        animal.Tick(1); animal.UpdateAnimationForCheck();
        Check(block.CenterStack.Count == 2 && incoming.Released && block.Releases == 1 && block.Notifications == 1,
            "settled arm delivery removes and releases exactly one central-stack item and notifies storage");
        Check(animal.animal.EatingAnimation && animal.animal.CurrentGrowthFoodEnergy == 25
            && animal.animal.currentHunger == 65 && animal.animal.PendingMeals == 1,
            "central-stack food applies one meal's growth hunger and digestion before eating animation");
        animal.Tick(.1f);
        Check(block.CenterStack.Count == 2, "arm delivery leftovers remain during the current meal");

        TerrainGenerator.Active = new(); block = Drop(0); block.Position = new(.4f, 0, 0);
        Delivery(block, Vector3.zero);
        Check(block.TryGetClosestAnimalFoodWorldPosition(new(-.1f, 0, 0), Food, out var position) && position.sqrMagnitude == 0
            && block.TryTakeAnimalFood(position, Food, out _) && block.CenterStack.Count == 0 && block.Count == 1,
            "nearest central food is consumed without removing a different floor pile in the same block");
        Delivery(block, Vector3.zero);
        Check(block.TryGetClosestAnimalFoodWorldPosition(new(.5f, 0, 0), Food, out position) && position.x == .4f
            && block.TryTakeAnimalFood(position, Food, out _) && block.Count == 0 && block.CenterStack.Count == 1,
            "nearer floor food wins over central food using the same source selection for extraction");
        Check(!block.TryTakeAnimalFood(new(.4f, 0, 0), Food, out _) && block.CenterStack.Count == 1,
            "disappearing target cannot silently consume another pile the animal was not facing");
        block.CenterVisible = false;
        Check(!block.TryGetClosestAnimalFoodWorldPosition(Vector3.zero, Food, out _), "hidden installation storage is not ground food");
        block.CenterVisible = true; block.ContainerContent = true;
        Check(!block.TryGetClosestAnimalFoodWorldPosition(Vector3.zero, Food, out _), "box inventory is not edible ground delivery");
        block.ContainerContent = false; block.CenterStack[0].ItemId = 2;
        Check(!block.TryGetClosestAnimalFoodWorldPosition(Vector3.zero, Food, out _), "central non-food items remain excluded");

        TerrainGenerator.Active = new(); block = Drop(0); block.Count = 0;
        for (int i = 0; i < 10; i++) Delivery(block, Vector3.zero);
        var consumers = new AnimalAIController[10];
        for (int i = 0; i < consumers.Length; i++) { consumers[i] = new(); consumers[i].Tick(.1f); }
        Check(block.CenterStack.Count == 9 && block.Releases == 1,
            "ten animals cannot empty a robot-arm destination stack in the same tick");

        TerrainGenerator.Active = new(); block = Drop(2); block.Count = 0; Delivery(block, new(2, 0, 0));
        for (int i = -1; i <= 1; i++)
        {
            TerrainGenerator.Active.Walls.Add(new(-1, i)); TerrainGenerator.Active.Walls.Add(new(1, i));
            TerrainGenerator.Active.Walls.Add(new(i, -1)); TerrainGenerator.Active.Walls.Add(new(i, 1));
        }
        animal = new(); animal.Tick(.1f);
        Check(block.CenterStack.Count == 1 && animal.animal.PendingMeals == 0,
            "robot-arm destination outside a closed pen cannot be eaten through its wall");
    }

    private static void RunDigestionChecks()
    {
        var animal = new Animal { currentHunger = 100 };
        animal.TickNeeds(49);
        Check(animal.currentHunger == 51 && !animal.IsHungry, "hunger increases one per second and forty-nine percent cannot start eating");
        animal.TickNeeds(1);
        Check(animal.currentHunger == 50 && animal.IsHungry, "exactly fifty percent hunger enables eating");
        Check(!animal.IsDefecationDue, "no food means no spontaneous periodic defecation");
        animal.ConsumeDroppedFood(new() { Food = true, energyAmount = 10 });
        Check(animal.currentHunger == 60 && !animal.IsHungry && animal.PendingMeals == 1, "energy ten reduces hunger by ten and schedules one dropping");
        animal.TickNeeds(5);
        animal.ConsumeDroppedFood(new() { Food = true, energyAmount = 10 });
        animal.TickNeeds(4);
        Check(!animal.IsDefecationDue && animal.PendingMeals == 2, "second meal cannot reset or prematurely finish the first meal timer");
        animal.TickNeeds(1);
        Check(animal.IsDefecationDue && animal.CompleteDefecation() && !animal.IsDefecationDue && animal.PendingMeals == 1,
            "first meal produces one dropping at ten seconds while later meal waits");
        animal.TickNeeds(5);
        Check(animal.IsDefecationDue && animal.CompleteDefecation() && animal.PendingMeals == 0,
            "second meal produces its own dropping ten seconds after consumption");
        animal.TickNeeds(1000);
        Check(animal.currentHunger == 0 && !animal.IsDefecationDue && !animal.CompleteDefecation(), "hunger is capped and completed meals cannot produce extra droppings");

        var saved = GrowthSaveProbe.RoundTrip(new() { age = 3, hasNeedsState = true, currentHunger = 60,
            pendingDefecations = new() { 2, 7 } });
        animal.RestoreGrowth(saved); animal.TickNeeds(2);
        Check(animal.IsDefecationDue && animal.CompleteDefecation() && !animal.IsDefecationDue, "save/load retains each meal's remaining digestion time");
        animal.TickNeeds(5);
        Check(animal.IsDefecationDue, "later saved meal matures at its own deadline");
        animal.RestoreGrowth(new() { age = 3, hasNeedsState = false });
        Check(animal.PendingMeals == 0 && !animal.IsDefecationDue, "pooled animal reset clears digestion timers");
        animal.IsAlive = false; animal.TickNeeds(50);
        Check(animal.currentHunger == 100 && !animal.IsDefecationDue, "dead animals neither accumulate hunger nor defecate");
        TerrainGenerator.Active = new(); var food = Drop(0);
        GameManager.Instance.ItemManger.Items[1].energyAmount = 0;
        var controller = new AnimalAIController(); controller.Tick(.1f);
        Check(food.Count == 1 && controller.animal.PendingMeals == 0, "zero-energy items are excluded before extraction");
        GameManager.Instance.ItemManger.Items[1].energyAmount = 25;
    }

    private static void RunFeedingAnimationChecks()
    {
        TerrainGenerator.Active = new();
        var food = Drop(0);
        var controller = new AnimalAIController { currentState = AnimalAIState.Graze };
        controller.UpdateAnimationForCheck();
        Check(!controller.animal.EatingAnimation && food.Count == 1, "decorative grazing never claims to consume food");
        controller.currentState = AnimalAIState.Eat;
        controller.UpdateAnimationForCheck();
        Check(!controller.animal.EatingAnimation, "arrived eat state without a completed meal cannot play eating");
        controller = new(); controller.animal.SetAge(2);
        controller.Tick(.1f); controller.UpdateAnimationForCheck();
        Check(food.Count == 0 && controller.animal.CurrentGrowthFoodEnergy == 25 && controller.animal.EatingAnimation,
            "successful item consumption advances growth before playing the eating animation");
        controller.Tick(1.1f); controller.UpdateAnimationForCheck();
        Check(!controller.animal.EatingAnimation, "eating animation ends after its completed-meal hold");
        TerrainGenerator.Active = new(); food = Drop(0); food.Settled = false;
        controller = new(); controller.Tick(.1f); controller.UpdateAnimationForCheck();
        Check(food.Count == 1 && !controller.animal.EatingAnimation, "failed item extraction never plays a fake eating animation");
        TerrainGenerator.Active = new(); food = Drop(0); food.Count = 6;
        controller = new(); controller.animal.SetAge(9); controller.animal.currentHunger = 100;
        for (int i = 0; i < 90; i++) controller.Tick(.1f);
        controller.UpdateAnimationForCheck();
        Check(controller.animal.Age == 9 && food.Count == 6 && !controller.animal.EatingAnimation,
            "growing animals also wait until hunger reaches fifty percent");

        TerrainGenerator.Active = new(); food = Drop(0); food.Count = 4;
        controller = new() { feedingDuration = 3 }; controller.animal.SetAge(2);
        controller.Tick(.1f);
        Check(food.Count == 3 && controller.animal.CurrentGrowthFoodEnergy == 25,
            "starting a meal consumes exactly one item and applies exactly one item's growth");
        controller.animal.RemainingEatingAnimationSeconds = 2;
        for (int i = 0; i < 20; i++) controller.Tick(.1f);
        Check(food.Count == 3, "baked clip duration holds the meal without reading playback progress");
        controller.animal.RemainingEatingAnimationSeconds = 0;
        controller.Tick(2.1f);
        controller.Tick(.25f);
        Check(food.Count == 3, "completed meal keeps a separate pause before the next item");
        controller.animal.currentHunger = 50;
        controller.Tick(.25f);
        Check(food.Count == 2 && controller.animal.CurrentGrowthFoodEnergy == 50,
            "next meal consumes one more item only after the pause");
    }

    private static void RunGrowthChecks()
    {
        var animal = new Animal(); animal.SetAge(2);
        animal.ConsumeDroppedFood(new() { Food = true, energyAmount = 10 });
        Check(animal.Age == 2 && animal.CurrentGrowthFoodEnergy == 10 && animal.currentHunger == 50, "growth and hunger recovery use exactly the food's displayed energy");
        animal.ConsumeDroppedFood(new() { Food = true, energyAmount = 90 });
        Check(animal.Age == 3 && animal.CurrentGrowthFoodEnergy == 0 && animal.AppliedGrowthScale == .3f, "full growth gauge gains one level and updates model growth");
        animal.ConsumeDroppedFood(new() { Food = true, energyAmount = 250 });
        Check(animal.Age == 5 && animal.CurrentGrowthFoodEnergy == 50, "large food energy gains multiple levels and retains excess");
        var saved = GrowthSaveProbe.RoundTrip(new() { age = animal.Age, hasNeedsState = true, growthFoodEnergy = animal.CurrentGrowthFoodEnergy, currentHunger = 75 });
        var restored = new Animal(); restored.RestoreGrowth(saved);
        restored.ConsumeDroppedFood(new() { Food = true, energyAmount = 50 });
        Check(restored.Age == 6 && restored.CurrentGrowthFoodEnergy == 0 && restored.currentHunger == 100, "saved growth progress resumes with normal hunger recovery");
        saved = GrowthSaveProbe.RoundTrip(new() { age = 4, hasNeedsState = true, growthFoodEnergy = 90, currentHunger = 70 }, true);
        restored.RestoreGrowth(saved);
        Check(restored.Age == 4 && restored.CurrentGrowthFoodEnergy == 0, "version 52 animal records load with an empty growth gauge");
        animal.SetGrowthRequirement(20); animal.SetAge(9);
        animal.ConsumeDroppedFood(new() { Food = true, energyAmount = 1000 });
        Check(animal.Age == 10 && animal.IsFullyGrown && animal.CurrentGrowthFoodEnergy == 20, "maximum growth is capped at ten and displays a full gauge");
        animal.SetAge(2);
        Check(animal.CurrentGrowthFoodEnergy == 0, "excess growth is discarded at maturity instead of leaking into later age changes");
        animal.ConsumeDroppedFood(new() { Food = true, energyAmount = 20 });
        Check(animal.Age == 3, "per-animal growth energy requirement is respected");
        animal.IsAlive = false;
        animal.ConsumeDroppedFood(new() { Food = true, energyAmount = 100 });
        Check(animal.Age == 3, "dead animals gain no food growth");
    }
}

public partial class AnimalAIController
{
    private void SyncBehaviorAnimationActivity() { }
    private static bool IsNightTime() => false;
    public void UpdateAnimationForCheck() => ApplyAnimation(0f);
}
