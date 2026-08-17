using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[FilePath("ProjectSettings/ProjectFItemDataFolders.asset", FilePathAttribute.Location.ProjectFolder)]
internal sealed class ItemDataFolderSettings : ScriptableSingleton<ItemDataFolderSettings>
{
    internal static event Action Changed;

    [Serializable]
    internal sealed class FolderEntry
    {
        [SerializeField]
        private string id;
        [SerializeField]
        private string displayName;
        [SerializeField]
        private bool expanded = true;
        [SerializeField]
        private string anchorItemGuid;

        internal string Id => id;
        internal string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Folder" : displayName;
        internal bool Expanded => expanded;
        internal string AnchorItemGuid => anchorItemGuid ?? string.Empty;

        internal FolderEntry(string id, string displayName)
        {
            this.id = id;
            this.displayName = displayName;
            expanded = true;
        }

        internal void SetDisplayName(string value)
        {
            displayName = value;
        }

        internal void SetExpanded(bool value)
        {
            expanded = value;
        }

        internal void SetAnchorItemGuid(string value)
        {
            anchorItemGuid = value ?? string.Empty;
        }
    }

    [Serializable]
    private sealed class ItemAssignment
    {
        [SerializeField]
        private string itemGuid;
        [SerializeField]
        private string folderId;

        internal string ItemGuid => itemGuid;
        internal string FolderId => folderId;

        internal ItemAssignment(string itemGuid, string folderId)
        {
            this.itemGuid = itemGuid;
            this.folderId = folderId;
        }
    }

    [SerializeField]
    private List<FolderEntry> folders = new List<FolderEntry>();
    [SerializeField]
    private List<ItemAssignment> itemAssignments = new List<ItemAssignment>();

    [NonSerialized]
    private int revision;
    [NonSerialized]
    private int lookupCacheRevision = -1;
    [NonSerialized]
    private Dictionary<string, FolderEntry> folderById;
    [NonSerialized]
    private Dictionary<string, string> folderIdByItemGuid;
    [NonSerialized]
    private Dictionary<ItemDefinition, string> folderIdByDefinition;
    [NonSerialized]
    private Dictionary<ItemDefinition, string> itemGuidByDefinition;

    internal IReadOnlyList<FolderEntry> Folders
    {
        get
        {
            EnsureCollections();
            return folders;
        }
    }
    internal int Revision => revision;

    internal FolderEntry AddFolder()
    {
        EnsureCollections();
        FolderEntry folder = new FolderEntry(
            Guid.NewGuid().ToString("N"),
            CreateUniqueFolderName("New Folder", null));
        folders.Add(folder);
        SaveChangedSettings();
        return folder;
    }

    internal bool RenameFolder(string folderId, string displayName)
    {
        FolderEntry folder = FindFolder(folderId);
        if (folder == null)
        {
            return false;
        }

        string normalizedName = string.IsNullOrWhiteSpace(displayName)
            ? folder.DisplayName
            : displayName.Trim();
        normalizedName = CreateUniqueFolderName(normalizedName, folderId);
        if (string.Equals(folder.DisplayName, normalizedName, StringComparison.Ordinal))
        {
            return false;
        }

        folder.SetDisplayName(normalizedName);
        SaveChangedSettings();
        return true;
    }

    internal bool RemoveFolder(string folderId)
    {
        EnsureCollections();
        int folderIndex = FindFolderIndex(folderId);
        if (folderIndex < 0)
        {
            return false;
        }

        folders.RemoveAt(folderIndex);
        for (int i = itemAssignments.Count - 1; i >= 0; i--)
        {
            ItemAssignment assignment = itemAssignments[i];
            if (assignment != null
                && string.Equals(assignment.FolderId, folderId, StringComparison.Ordinal))
            {
                itemAssignments.RemoveAt(i);
            }
        }

        SaveChangedSettings();
        return true;
    }

    internal bool SetFolderExpanded(string folderId, bool expanded)
    {
        FolderEntry folder = FindFolder(folderId);
        if (folder == null || folder.Expanded == expanded)
        {
            return false;
        }

        folder.SetExpanded(expanded);
        SaveChangedSettings();
        return true;
    }

    internal bool SetFolderPlacement(
        string folderId,
        ItemDefinition anchorDefinition,
        string relativeFolderId = null,
        bool insertAfterRelativeFolder = false)
    {
        EnsureCollections();
        int sourceIndex = FindFolderIndex(folderId);
        if (sourceIndex < 0)
        {
            return false;
        }

        FolderEntry folder = folders[sourceIndex];
        string anchorItemGuid = ResolveDefinitionGuid(anchorDefinition);
        bool changed = !string.Equals(
            folder.AnchorItemGuid,
            anchorItemGuid,
            StringComparison.Ordinal);

        if (!string.IsNullOrEmpty(relativeFolderId)
            && !string.Equals(folderId, relativeFolderId, StringComparison.Ordinal))
        {
            int targetIndex = FindFolderIndex(relativeFolderId);
            if (targetIndex >= 0)
            {
                folders.RemoveAt(sourceIndex);
                targetIndex = FindFolderIndex(relativeFolderId);
                if (insertAfterRelativeFolder)
                {
                    targetIndex++;
                }

                targetIndex = Mathf.Clamp(targetIndex, 0, folders.Count);
                folders.Insert(targetIndex, folder);
                changed |= sourceIndex != targetIndex;
            }
        }

        if (!changed)
        {
            return false;
        }

        folder.SetAnchorItemGuid(anchorItemGuid);
        SaveChangedSettings();
        return true;
    }

    internal string GetItemFolderId(ItemDefinition definition)
    {
        if (definition == null)
        {
            return string.Empty;
        }

        EnsureLookupCache();
        if (folderIdByDefinition.TryGetValue(definition, out string cachedFolderId))
        {
            return cachedFolderId;
        }

        string itemGuid = ResolveDefinitionGuid(definition);
        if (string.IsNullOrEmpty(itemGuid))
        {
            folderIdByDefinition[definition] = string.Empty;
            return string.Empty;
        }

        string folderId = folderIdByItemGuid.TryGetValue(itemGuid, out string assignedFolderId)
            ? assignedFolderId
            : string.Empty;
        folderIdByDefinition[definition] = folderId;
        return folderId;
    }

    internal bool SetItemFolder(ItemDefinition definition, string folderId)
    {
        return SetItemsFolder(new[] { definition }, folderId);
    }

    internal bool SetItemsFolder(IReadOnlyList<ItemDefinition> definitions, string folderId)
    {
        if (definitions == null || definitions.Count == 0)
        {
            return false;
        }

        EnsureCollections();
        HashSet<string> itemGuids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < definitions.Count; i++)
        {
            string itemGuid = ResolveDefinitionGuid(definitions[i]);
            if (!string.IsNullOrEmpty(itemGuid))
            {
                itemGuids.Add(itemGuid);
            }
        }

        if (itemGuids.Count == 0)
        {
            return false;
        }

        string resolvedFolderId = FindFolder(folderId) != null ? folderId : string.Empty;
        Dictionary<string, int> assignmentCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        HashSet<string> exactAssignments = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < itemAssignments.Count; i++)
        {
            ItemAssignment assignment = itemAssignments[i];
            if (assignment == null || !itemGuids.Contains(assignment.ItemGuid))
            {
                continue;
            }

            assignmentCounts.TryGetValue(assignment.ItemGuid, out int count);
            assignmentCounts[assignment.ItemGuid] = count + 1;
            if (string.Equals(assignment.FolderId, resolvedFolderId, StringComparison.Ordinal))
            {
                exactAssignments.Add(assignment.ItemGuid);
            }
        }

        bool changed = false;
        foreach (string itemGuid in itemGuids)
        {
            assignmentCounts.TryGetValue(itemGuid, out int assignmentCount);
            bool isExact = exactAssignments.Contains(itemGuid);
            if ((string.IsNullOrEmpty(resolvedFolderId) && assignmentCount != 0)
                || (!string.IsNullOrEmpty(resolvedFolderId)
                    && (assignmentCount != 1 || !isExact)))
            {
                changed = true;
                break;
            }
        }

        if (!changed)
        {
            return false;
        }

        for (int i = itemAssignments.Count - 1; i >= 0; i--)
        {
            ItemAssignment assignment = itemAssignments[i];
            if (assignment != null && itemGuids.Contains(assignment.ItemGuid))
            {
                itemAssignments.RemoveAt(i);
            }
        }

        if (!string.IsNullOrEmpty(resolvedFolderId))
        {
            foreach (string itemGuid in itemGuids)
            {
                itemAssignments.Add(new ItemAssignment(itemGuid, resolvedFolderId));
            }
        }

        SaveChangedSettings();
        return true;
    }

    internal FolderEntry FindFolder(string folderId)
    {
        if (string.IsNullOrEmpty(folderId))
        {
            return null;
        }

        EnsureLookupCache();
        return folderById.TryGetValue(folderId, out FolderEntry folder)
            ? folder
            : null;
    }

    private int FindFolderIndex(string folderId)
    {
        if (string.IsNullOrEmpty(folderId))
        {
            return -1;
        }

        EnsureCollections();
        for (int i = 0; i < folders.Count; i++)
        {
            FolderEntry folder = folders[i];
            if (folder != null && string.Equals(folder.Id, folderId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private string CreateUniqueFolderName(string requestedName, string excludedFolderId)
    {
        string baseName = string.IsNullOrWhiteSpace(requestedName) ? "Folder" : requestedName.Trim();
        string candidate = baseName;
        int suffix = 2;
        while (ContainsFolderName(candidate, excludedFolderId))
        {
            candidate = $"{baseName} {suffix}";
            suffix++;
        }

        return candidate;
    }

    private bool ContainsFolderName(string displayName, string excludedFolderId)
    {
        EnsureCollections();
        for (int i = 0; i < folders.Count; i++)
        {
            FolderEntry folder = folders[i];
            if (folder == null
                || string.Equals(folder.Id, excludedFolderId, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(folder.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureCollections()
    {
        folders ??= new List<FolderEntry>();
        itemAssignments ??= new List<ItemAssignment>();
    }

    private void SaveChangedSettings()
    {
        revision++;
        lookupCacheRevision = -1;
        folderIdByDefinition?.Clear();
        Save(true);
        Changed?.Invoke();
    }

    private void EnsureLookupCache()
    {
        EnsureCollections();
        folderById ??= new Dictionary<string, FolderEntry>(StringComparer.Ordinal);
        folderIdByItemGuid ??= new Dictionary<string, string>(StringComparer.Ordinal);
        folderIdByDefinition ??= new Dictionary<ItemDefinition, string>();
        itemGuidByDefinition ??= new Dictionary<ItemDefinition, string>();
        if (lookupCacheRevision == revision)
        {
            return;
        }

        folderById.Clear();
        folderIdByItemGuid.Clear();
        folderIdByDefinition.Clear();
        for (int i = 0; i < folders.Count; i++)
        {
            FolderEntry folder = folders[i];
            if (folder != null && !string.IsNullOrEmpty(folder.Id))
            {
                folderById[folder.Id] = folder;
            }
        }

        for (int i = 0; i < itemAssignments.Count; i++)
        {
            ItemAssignment assignment = itemAssignments[i];
            if (assignment != null
                && !string.IsNullOrEmpty(assignment.ItemGuid)
                && folderById.ContainsKey(assignment.FolderId))
            {
                folderIdByItemGuid[assignment.ItemGuid] = assignment.FolderId;
            }
        }

        lookupCacheRevision = revision;
    }

    private string ResolveDefinitionGuid(ItemDefinition definition)
    {
        if (definition == null)
        {
            return string.Empty;
        }

        itemGuidByDefinition ??= new Dictionary<ItemDefinition, string>();
        if (itemGuidByDefinition.TryGetValue(definition, out string cachedGuid))
        {
            return cachedGuid;
        }

        string assetPath = definition != null ? AssetDatabase.GetAssetPath(definition) : string.Empty;
        string itemGuid = string.IsNullOrEmpty(assetPath)
            ? string.Empty
            : AssetDatabase.AssetPathToGUID(assetPath);
        itemGuidByDefinition[definition] = itemGuid;
        return itemGuid;
    }
}
