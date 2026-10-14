using System;

namespace ProjectF.Simulation.Monsters
{
    public static class MonsterSpawnSearch
    {
        public const int MinimumRadius = 2;
        public const int MaximumRadius = 8;

        public static bool TryFindNear(int centerX, int centerZ,
            Func<int, int, bool> isFree, out int spawnX, out int spawnZ)
        {
            if (isFree == null) throw new ArgumentNullException(nameof(isFree));
            spawnX = spawnZ = 0;
            for (int radius = MinimumRadius; radius <= MaximumRadius; radius++)
            {
                for (int z = -radius; z <= radius; z++)
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        if ((Math.Abs(x) != radius && Math.Abs(z) != radius)
                            || x * x + z * z > MaximumRadius * MaximumRadius) continue;
                        int candidateX = centerX + x;
                        int candidateZ = centerZ + z;
                        if (!isFree(candidateX, candidateZ)) continue;
                        spawnX = candidateX;
                        spawnZ = candidateZ;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
