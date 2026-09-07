using System;
using UnityEngine;

partial class Block
{
    private readonly int[] itemIds = { -1, -1, -1, -1 };
    private readonly Vector3[] positions = new Vector3[4];

    private int GetConveyorLaneCount() => itemIds.Length;
    private int GetConveyorItemIdAtLane(int lane) => itemIds[lane];
    private Vector3 GetConveyorItemVisualWorldPosition(int lane) => positions[lane];

    public void Set(int lane, int itemId, Vector3 position)
    {
        itemIds[lane] = itemId;
        positions[lane] = position;
    }

    public int Pick(Vector3 selectionReference, Vector3 rangeReference, Predicate<int> filter, float radius)
    {
        return TryGetClosestConveyorItemLane(selectionReference, rangeReference, filter, radius, out int lane)
            ? lane
            : -1;
    }
}

static class Program
{
    private static int checks;

    private static void Require(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Main()
    {
        var block = new Block();
        block.Set(0, 10, new Vector3(-0.35f, 0.2f, 0f));
        block.Set(2, 20, new Vector3(0.35f, 0.2f, 0f));

        Require(block.Pick(new Vector3(2f, 5f, 0f), Vector3.zero, null, 1f) == 2,
            "the item nearest the arm body must win, independently of hand height");
        Require(block.Pick(new Vector3(-2f, -4f, 0f), Vector3.zero, null, 1f) == 0,
            "moving the arm body to the other side must change the preferred item");
        Require(block.Pick(Vector3.zero, Vector3.zero, null, 1f) == 0,
            "an exact distance tie must retain the lower lane-index priority");
        Require(block.Pick(new Vector3(2f, 0f, 0f), Vector3.zero, id => id == 10, 1f) == 0,
            "the item filter must still exclude a nearer item");

        var rangeCheck = new Block();
        rangeCheck.Set(0, 30, new Vector3(0.4f, 0f, 0f));
        rangeCheck.Set(2, 40, new Vector3(1.2f, 0f, 0f));
        Require(rangeCheck.Pick(new Vector3(2f, 0f, 0f), Vector3.zero, null, 0.5f) == 0,
            "pickup radius must remain centered on the belt pickup point");
        Require(rangeCheck.Pick(new Vector3(2f, 0f, 0f), Vector3.zero, null, 0.3f) == -1,
            "items outside the original pickup radius must remain ineligible");

        Console.WriteLine($"PASS: {checks} robot-arm belt pickup priority checks.");
    }
}
