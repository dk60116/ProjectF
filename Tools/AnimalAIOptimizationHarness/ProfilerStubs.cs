using System;
using System.Collections.Generic;

// Measurement boundary; all AI scheduling, spatial indexing and search code is production source.
public static class MapObjectTickProfiler
{
    public static bool IsEnabled = true;
    public static readonly Dictionary<string, long> Counters = new();
    public static readonly HashSet<string> Samples = new();
    public static NamedSampleScope SampleNamed(string kind, string type, string name)
    {
        if (IsEnabled) Samples.Add(name);
        return new();
    }
    public readonly struct NamedSampleScope : IDisposable { public void Dispose() { } }
    public static void AddRuntimeCounter(string group, string name, int value, string note = "")
        => Counters[name] = value;
}
