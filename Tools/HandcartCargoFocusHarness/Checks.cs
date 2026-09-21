using System;
using System.Collections.Generic;

public static class Checks
{
    private static int checks;

    private static void Require(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Main()
    {
        var cart = new Handcart();
        cart.Configure(4, (1, 2), (2, 3));
        var a0 = new PortableObject(0);
        var a1 = new PortableObject(1);
        var b0 = new PortableObject(2);
        var a2 = new PortableObject(3);
        var b1 = new PortableObject(4);
        cart.Add(1, a0);
        cart.Add(1, a1);
        cart.Add(2, b0);
        cart.Add(1, a2);
        cart.Add(2, b1);
        cart.Rebuild();

        Require(cart.TryGet(0, out List<PortableObject> firstA)
                && ReferenceEquals(firstA[0], a0) && ReferenceEquals(firstA[1], a1)
                && firstA.Count == 2,
            "the first item stack must contain all compatible visuals up to capacity");
        Require(cart.TryGet(3, out List<PortableObject> secondA)
                && secondA.Count == 1 && ReferenceEquals(secondA[0], a2)
                && !ReferenceEquals(firstA, secondA),
            "overflow items must focus only their physical stack");
        Require(cart.TryGet(2, out List<PortableObject> bStack)
                && cart.TryGet(4, out List<PortableObject> sameBStack)
                && ReferenceEquals(bStack, sameBStack)
                && bStack.Count == 2
                && ReferenceEquals(bStack[0], b0) && ReferenceEquals(bStack[1], b1),
            "interleaved compatible items must resolve to the same visual stack");

        List<PortableObject> previousBStack = bStack;
        var b2 = new PortableObject(5);
        cart.Add(2, b2);
        cart.Rebuild();
        Require(previousBStack.Count == 2,
            "rebuilding must preserve the previous focus generation for safe outline release");
        Require(cart.TryGet(5, out List<PortableObject> rebuiltBStack)
                && rebuiltBStack.Count == 3 && ReferenceEquals(rebuiltBStack[2], b2)
                && !ReferenceEquals(previousBStack, rebuiltBStack),
            "a cargo mutation must publish a new complete focus-stack generation");
        Require(!cart.TryGet(-1, out _) && !cart.TryGet(99, out _),
            "invalid cargo indices must not expose a focus stack");

        Console.WriteLine($"PASS: {checks} handcart cargo stack focus checks.");
    }
}
