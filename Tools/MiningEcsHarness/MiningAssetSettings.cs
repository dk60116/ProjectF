using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

internal static class MiningAssetSettings
{
    internal static (float rate, long energy) Read(string repo, string filename, int energyType)
    {
        string text = File.ReadAllText(Path.Combine(repo, "FactorioProject", "Assets", "Data", "Items", filename));
        var match = Regex.Match(text, @"- energyType:\s*" + energyType + @"\s+useEnergyAmount:\s*([0-9.]+)\s+completeEnergy:\s*([0-9.]+)");
        if (!match.Success) throw new Exception("Missing mining energy requirement: " + filename);
        float rate = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return (rate, DeterministicSimulationUnits.FromFloat(float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)));
    }
}
