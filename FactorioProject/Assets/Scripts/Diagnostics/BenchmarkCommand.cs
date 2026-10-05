using System;
using System.Globalization;

namespace ProjectF.Benchmark
{
    public enum BenchmarkAction { Map, Belts, Fill, ClearItems, Spawn, ClearObjects, Force, Cancel, Catalog, Status, FillRandom, RandomizeProgress }
    public readonly struct BenchmarkCommand
    {
        public readonly BenchmarkAction Action;
        public readonly int ItemId, Count;
        public readonly double Percent;
        public readonly bool Enabled;
        private BenchmarkCommand(BenchmarkAction action, int item = -1, int count = 0, double percent = 0, bool enabled = false)
        { Action = action; ItemId = item; Count = count; Percent = percent; Enabled = enabled; }

        public static bool TryParse(string[] parts, out BenchmarkCommand command, out string error)
        {
            command = default;
            error = "usage: benchmark map|belts <itemId|auto> <rings>|fill <itemId|random> <percent>|clearitems|spawn <itemId> <count>|clearobjects|force <0|1> <fallbackItemId>|randomizeprogress|cancel|catalog|status";
            if (parts == null || parts.Length < 2 || !string.Equals(parts[0], "benchmark", StringComparison.OrdinalIgnoreCase)) return false;
            string action = parts[1].ToLowerInvariant();
            if (parts.Length == 2)
            {
                BenchmarkAction kind;
                switch (action)
                {
                    case "map": kind = BenchmarkAction.Map; break;
                    case "clearitems": kind = BenchmarkAction.ClearItems; break;
                    case "clearobjects": kind = BenchmarkAction.ClearObjects; break;
                    case "cancel": kind = BenchmarkAction.Cancel; break;
                    case "catalog": kind = BenchmarkAction.Catalog; break;
                    case "status": kind = BenchmarkAction.Status; break;
                    case "randomizeprogress": kind = BenchmarkAction.RandomizeProgress; break;
                    default: return false;
                }
                command = new BenchmarkCommand(kind); error = null; return true;
            }
            if (parts.Length != 4) return false;
            if (action == "force")
            {
                if ((parts[2] != "0" && parts[2] != "1") || !int.TryParse(parts[3], out int fallback)
                    || fallback < (parts[2] == "1" ? 0 : -1)) return false;
                command = new BenchmarkCommand(BenchmarkAction.Force, fallback, enabled: parts[2] == "1");
                error = null; return true;
            }
            int item = -1;
            bool auto = action == "belts" && string.Equals(parts[2], "auto", StringComparison.OrdinalIgnoreCase);
            bool random = action == "fill" && string.Equals(parts[2], "random", StringComparison.OrdinalIgnoreCase);
            if (!auto && !random && (!int.TryParse(parts[2], out item) || item < 0)) return false;
            if (action == "fill")
            {
                if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double percentage)
                    || double.IsNaN(percentage) || double.IsInfinity(percentage) || percentage < 0 || percentage > 100) return false;
                command = new BenchmarkCommand(random ? BenchmarkAction.FillRandom : BenchmarkAction.Fill, item, percent: percentage);
            }
            else if (action == "belts" || action == "spawn")
            {
                int limit = action == "belts" ? BenchmarkLayout.MaximumRings : BenchmarkLayout.MaximumObjects;
                if (!int.TryParse(parts[3], out int count) || count < 1 || count > limit) return false;
                command = new BenchmarkCommand(action == "belts" ? BenchmarkAction.Belts : BenchmarkAction.Spawn, item, count);
            }
            else return false;
            error = null; return true;
        }
    }
}
