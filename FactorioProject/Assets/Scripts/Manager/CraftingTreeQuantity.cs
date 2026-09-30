using System.IO;
using UnityEngine;

namespace ProjectF.Crafting
{
    public static class CraftingTreeQuantity
    {
        // v6 stores every recipe quantity as Float32. Record counts and IDs remain Int32.
        public const int FileVersion = 6;
        public const float MinimumFluidAmount = 0.0001f;

        public static float Read(BinaryReader reader, int version) =>
            Normalize(version >= FileVersion ? reader.ReadSingle() : Mathf.Max(1, reader.ReadInt32()));

        public static float Normalize(float amount, ItemDefinition definition = null)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount))
            {
                return 1f;
            }

            return definition != null && !InputOutputModule.IsFluidItemDefinition(definition)
                ? Mathf.Max(1, Mathf.RoundToInt(amount))
                : Mathf.Max(MinimumFluidAmount, amount);
        }

        // Inventory crafting still consumes whole item stacks.
        public static int ToItemCount(float amount) => Mathf.Max(0, Mathf.CeilToInt(amount));
    }
}
