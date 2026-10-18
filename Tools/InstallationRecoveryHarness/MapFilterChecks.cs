using System;

public static partial class MapFilterChecks
{
    private static int checks;

    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    public static void Run()
    {
        var installation = new InstallationObject();
        Check(installation.MapFilter == InstallationObject.DefaultMapFilter, "New installations retain their default filter");
        installation.MapFilter = InstallationMapFilter.Water;
        installation.MapFilter = InstallationMapFilter.None;
        for (int repaint = 0; repaint < 5; repaint++)
        {
            installation.MapFilter = installation.MapFilter;
            Check(installation.MapFilter == InstallationMapFilter.None, "None survives edit, validation and repeated reads");
        }
        installation.MapFilter = InstallationObject.DefaultMapFilter & ~InstallationMapFilter.Floor;
        Check((installation.MapFilter & InstallationMapFilter.Floor) == 0, "Floor can be removed from the default filter");
        installation.MapFilter = InstallationMapFilter.Floor;
        installation.MapFilter &= ~InstallationMapFilter.Floor;
        Check(installation.MapFilter == InstallationMapFilter.None, "Removing the final Floor flag preserves None");

        foreach (string text in new[] { "None", "none", "0", "None | None" })
        {
            Check(TryParseInstallationMapFilter(text, out var parsed) && parsed == InstallationMapFilter.None,
                "JSON/import preserves explicit None: " + text);
            installation.MapFilter = parsed;
            Check(installation.MapFilter.ToString() == "None", "Export preserves None after import");
        }

        foreach (var entry in new[] {
            ("Resource | WaterOutlne", InstallationMapFilter.Ore | InstallationMapFilter.WaterOutline),
            ("Ground, Water", InstallationMapFilter.Ground | InstallationMapFilter.Water),
            ("Water", InstallationMapFilter.Water) })
        {
            Check(TryParseInstallationMapFilter(entry.Item1, out var parsed)
                && parsed == entry.Item2, "Import preserves authored flags without adding Floor");
        }
        Check(!TryParseInstallationMapFilter("", out _), "Absent import value stays absent");
        Check(!TryParseInstallationMapFilter("InvalidFilter", out _), "Invalid import value is rejected");

        for (int bits = 0; bits < (1 << 11); bits++)
        {
            var filter = (InstallationMapFilter)bits;
            installation.MapFilter = filter;
            Check(installation.MapFilter == filter, "Runtime getter/setter preserve every authored filter combination");
            Check(TryParseInstallationMapFilter(filter.ToString(), out var parsed) && parsed == filter,
                "Named flags round-trip without forcing Floor");
            Check(TryParseInstallationMapFilter(bits.ToString(), out parsed) && parsed == filter,
                "Numeric flags round-trip without forcing Floor");
        }
        Console.WriteLine($"PASS: {checks} production installation MapFilter edit/import checks");
    }
}
