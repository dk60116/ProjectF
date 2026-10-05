using System;
using System.Collections.Generic;
using UnityEngine;
namespace ProjectF.MapObjects
{
internal sealed class LoggingFilter
{
    private static readonly Dictionary<ResourceDefinition, string> definitionKeys = new Dictionary<ResourceDefinition, string>();
    internal static string DefinitionKey(ResourceDefinition definition)
    {
        if (definition == null) return string.Empty;
        if (!definitionKeys.TryGetValue(definition, out string key))
        { key = !string.IsNullOrEmpty(definition.name) ? definition.name : definition.resourceName ?? string.Empty; definitionKeys.Add(definition, key); }
        return key;
    }
    private readonly BlockStateStore.InstallationSaveState state;
    private readonly Action changed;
    internal LoggingFilter(BlockStateStore.InstallationSaveState state, Action changed)
    { this.state = state; this.changed = changed; }
    private bool treeFilterInitialized { get => state.loggingTreeFilterInitialized; set => state.loggingTreeFilterInitialized = value; }
    private List<string> enabledTreeDefinitionKeys { get => state.loggingEnabledTreeDefinitionKeys; set => state.loggingEnabledTreeDefinitionKeys = value; }
    private int minimumGrowth { get => state.loggingMinimumGrowth; set => state.loggingMinimumGrowth = value; }
    private int maximumGrowth { get => state.loggingMaximumGrowth; set => state.loggingMaximumGrowth = value; }
    public int MinimumGrowth => Mathf.Clamp(minimumGrowth, ResourceDefinition.MinGrowth, ResourceDefinition.MaxGrowth);
    public int MaximumGrowth => Mathf.Clamp(maximumGrowth, MinimumGrowth, ResourceDefinition.MaxGrowth);
    public bool IsTreeFilterInitialized => treeFilterInitialized;
    private void InvalidateFilteredTarget() => changed?.Invoke();
    public bool IsTreeTypeEnabled(ResourceDefinition definition)
    {
        if (definition == null)
        {
            return false;
        }

        return !treeFilterInitialized
               || ContainsTreeDefinitionKey(BuildTreeDefinitionKey(definition));
    }
    public void SetTreeTypeEnabled(
        ResourceDefinition definition,
        IReadOnlyList<ResourceDefinition> availableDefinitions,
        bool enabled)
    {
        if (definition == null)
        {
            return;
        }

        EnsureTreeFilterInitialized(availableDefinitions);
        string key = BuildTreeDefinitionKey(definition);
        int existingIndex = IndexOfTreeDefinitionKey(key);
        if (enabled && existingIndex < 0)
        {
            enabledTreeDefinitionKeys.Add(key);
        }
        else if (!enabled && existingIndex >= 0)
        {
            enabledTreeDefinitionKeys.RemoveAt(existingIndex);
        }

        InvalidateFilteredTarget();
    }
    public void SetAllTreeTypes(
        IReadOnlyList<ResourceDefinition> availableDefinitions,
        bool enabled)
    {
        treeFilterInitialized = true;
        enabledTreeDefinitionKeys ??= new List<string>();
        enabledTreeDefinitionKeys.Clear();
        if (enabled && availableDefinitions != null)
        {
            for (int i = 0; i < availableDefinitions.Count; i++)
            {
                ResourceDefinition definition = availableDefinitions[i];
                if (definition == null)
                {
                    continue;
                }

                string key = BuildTreeDefinitionKey(definition);
                if (!ContainsTreeDefinitionKey(key))
                {
                    enabledTreeDefinitionKeys.Add(key);
                }
            }
        }

        InvalidateFilteredTarget();
    }
    public void SetGrowthRange(int minimum, int maximum)
    {
        int clampedValue = Mathf.Clamp(
            minimum,
            ResourceDefinition.MinGrowth,
            ResourceDefinition.MaxGrowth);
        int clampedMaximum = Mathf.Clamp(maximum, clampedValue, ResourceDefinition.MaxGrowth);
        if (minimumGrowth == clampedValue && maximumGrowth == clampedMaximum)
        {
            return;
        }

        minimumGrowth = clampedValue;
        maximumGrowth = clampedMaximum;
        InvalidateFilteredTarget();
    }
    public List<string> CaptureEnabledTreeDefinitionKeys()
    {
        return enabledTreeDefinitionKeys != null
            ? new List<string>(enabledTreeDefinitionKeys)
            : new List<string>();
    }
    public void ApplyTreeFilterState(
        bool initialized,
        IReadOnlyList<string> enabledDefinitionKeys,
        int savedMinimumGrowth,
        int savedMaximumGrowth = LoggingMachine.DefaultMaximumGrowth)
    {
        treeFilterInitialized = initialized;
        minimumGrowth = Mathf.Clamp(
            savedMinimumGrowth,
            ResourceDefinition.MinGrowth,
            ResourceDefinition.MaxGrowth);
        maximumGrowth = Mathf.Clamp(savedMaximumGrowth, minimumGrowth, ResourceDefinition.MaxGrowth);
        enabledTreeDefinitionKeys ??= new List<string>();
        enabledTreeDefinitionKeys.Clear();
        if (enabledDefinitionKeys != null)
        {
            for (int i = 0; i < enabledDefinitionKeys.Count; i++)
            {
                string key = enabledDefinitionKeys[i];
                if (!string.IsNullOrEmpty(key) && !ContainsTreeDefinitionKey(key))
                {
                    enabledTreeDefinitionKeys.Add(key);
                }
            }
        }

        InvalidateFilteredTarget();
    }
    private void EnsureTreeFilterInitialized(IReadOnlyList<ResourceDefinition> availableDefinitions)
    {
        if (treeFilterInitialized)
        {
            enabledTreeDefinitionKeys ??= new List<string>();
            return;
        }

        treeFilterInitialized = true;
        enabledTreeDefinitionKeys ??= new List<string>();
        enabledTreeDefinitionKeys.Clear();
        if (availableDefinitions == null)
        {
            return;
        }

        for (int i = 0; i < availableDefinitions.Count; i++)
        {
            ResourceDefinition definition = availableDefinitions[i];
            if (definition == null)
            {
                continue;
            }

            string key = BuildTreeDefinitionKey(definition);
            if (!ContainsTreeDefinitionKey(key))
            {
                enabledTreeDefinitionKeys.Add(key);
            }
        }
    }
    private bool ContainsTreeDefinitionKey(string key)
    {
        return IndexOfTreeDefinitionKey(key) >= 0;
    }
    private int IndexOfTreeDefinitionKey(string key)
    {
        if (string.IsNullOrEmpty(key) || enabledTreeDefinitionKeys == null)
        {
            return -1;
        }

        for (int i = 0; i < enabledTreeDefinitionKeys.Count; i++)
        {
            if (string.Equals(enabledTreeDefinitionKeys[i], key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
    private static string BuildTreeDefinitionKey(ResourceDefinition definition)
    {
        if (definition == null)
        {
            return string.Empty;
        }

        return DefinitionKey(definition);
    }
}
}
