using System;
using System.Collections.Generic;
using System.IO;
using ProjectF.Monsters;
using ProjectF.Simulation.Monsters;
using UnityEditor;
using UnityEngine;

namespace ProjectF.Editor.Monsters
{
    public sealed class MonsterDataEditorWindow : EditorWindow
    {
        private const float ListRowHeight = 32f;
        private const float ListIconSize = 24f;

        [Serializable]
        private sealed class ReloadSource
        {
            public string guid;
            public string baseline;
        }

        private sealed class Draft : IDisposable
        {
            public readonly MonsterDefinition Source;
            public readonly MonsterDefinition Data;
            public readonly SerializedObject Serialized;
            public string SourceBaseline;
            public string DraftBaseline;
            public bool Dirty;

            public Draft(MonsterDefinition source)
            {
                Source = source;
                Data = Instantiate(source);
                Data.hideFlags = HideFlags.HideAndDontSave;
                Serialized = new SerializedObject(Data);
                CaptureBaseline();
            }

            public void CaptureBaseline()
            {
                SourceBaseline = EditorJsonUtility.ToJson(Source);
                DraftBaseline = EditorJsonUtility.ToJson(Data);
                Dirty = false;
            }

            public void Dispose()
            {
                Serialized.Dispose();
                if (Data != null) DestroyImmediate(Data);
            }
        }

        private static readonly string[] StatFields =
        {
            "MaxHealth", "MoveSpeed", "TurnSpeed", "AttackDamage",
            "AttackRange", "AttackInterval", "DetectionRange", "LeashRange"
        };
        private static readonly string[] StatLabels =
        {
            "Max Health", "Move Speed", "Turn Speed", "Attack Damage",
            "Attack Range", "Attack Interval", "Detection Range", "Leash Range"
        };
        private readonly List<Draft> drafts = new List<Draft>();
        private readonly HashSet<int> collapsedSpecies = new HashSet<int>();
        private readonly ItemDefinitionDropdownGUI itemDropdown = new ItemDefinitionDropdownGUI();
        private readonly MonsterModelPreview preview = new MonsterModelPreview();
        private List<MonsterValidationIssue> issues = new List<MonsterValidationIssue>();
        private Vector2 listScroll;
        private Vector2 detailScroll;
        [SerializeField] private string search = string.Empty;
        [SerializeField] private string selectedPath = string.Empty;
        [SerializeField] private int selectedVariantId = -1;
        [SerializeField] private string reloadDraftJson = string.Empty;
        [SerializeField] private List<ReloadSource> reloadSources = new List<ReloadSource>();
        private bool showValidation;
        private bool reloadQueued;
        private bool wasPainting;
        private GUIStyle rowStyle;
        private GUIStyle headerStyle;

        [MenuItem("Window/ProjectF/Monster Data")]
        public static void ShowWindow()
        {
            MonsterDataEditorWindow window = GetWindow<MonsterDataEditorWindow>("Monster Data");
            window.minSize = new Vector2(840f, 560f);
            window.Show();
        }

        public static void ShowWindowAndSelect(MonsterDefinition definition)
        {
            ShowWindow();
            MonsterDataEditorWindow window = GetWindow<MonsterDataEditorWindow>();
            window.Reload(true);
            window.selectedPath = AssetDatabase.GetAssetPath(definition);
            window.selectedVariantId = -1;
            window.preview.Dispose();
            window.Repaint();
        }

        private void OnEnable()
        {
            saveChangesMessage = "Save Monster Data changes?";
            Undo.undoRedoPerformed += HandleUndo;
            AssemblyReloadEvents.beforeAssemblyReload += CaptureReloadDrafts;
            EditorApplication.update += UpdatePreview;
            ItemDataEditorWindow.DefinitionCatalog.Changed += RefreshItems;
            Reload(false);
            RestoreReloadDrafts();
        }

        private void OnDisable()
        {
            MonsterSpawnBrush.Stop();
            Undo.undoRedoPerformed -= HandleUndo;
            AssemblyReloadEvents.beforeAssemblyReload -= CaptureReloadDrafts;
            EditorApplication.update -= UpdatePreview;
            EditorApplication.delayCall -= DelayedReload;
            ItemDataEditorWindow.DefinitionCatalog.Changed -= RefreshItems;
            reloadQueued = false;
            preview.Dispose();
            foreach (Draft draft in drafts) draft.Dispose();
            drafts.Clear();
        }

        private void CaptureReloadDrafts()
        {
            if (!hasUnsavedChanges) return;
            List<MonsterDefinition> sources = new List<MonsterDefinition>();
            List<MonsterDefinition> data = new List<MonsterDefinition>();
            List<ReloadSource> versions = new List<ReloadSource>();
            foreach (Draft draft in drafts)
            {
                if (!draft.Dirty) continue;
                sources.Add(draft.Source);
                data.Add(draft.Data);
                versions.Add(new ReloadSource
                {
                    guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(draft.Source)),
                    baseline = draft.SourceBaseline
                });
            }
            reloadDraftJson = MonsterDataJson.Export(sources, data);
            reloadSources = versions;
        }

        private void RestoreReloadDrafts()
        {
            if (string.IsNullOrEmpty(reloadDraftJson)) return;
            List<MonsterDefinition> restored = null;
            try
            {
                List<MonsterDefinition> sources = new List<MonsterDefinition>();
                List<MonsterDefinition> data = new List<MonsterDefinition>();
                foreach (Draft draft in drafts) { sources.Add(draft.Source); data.Add(draft.Data); }
                // Incomplete authoring is valid recovery data, but never valid Save/JSON import data.
                restored = MonsterDataJson.Import(reloadDraftJson, sources, data, validate: false);
                for (int i = 0; i < drafts.Count; i++)
                {
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(drafts[i].Source));
                    ReloadSource version = reloadSources.Find(candidate => candidate.guid == guid);
                    if (version == null) continue;
                    MonsterDataEditorUtility.CopyAuthoring(restored[i], drafts[i].Data);
                    drafts[i].SourceBaseline = version.baseline;
                    drafts[i].Dirty = true;
                }
                reloadDraftJson = string.Empty;
                reloadSources.Clear();
                RefreshIssues();
            }
            catch (Exception exception) { Debug.LogError("Monster Data draft recovery failed: " + exception.Message); }
            finally
            {
                if (restored != null)
                    foreach (MonsterDefinition copy in restored) DestroyImmediate(copy);
            }
        }

        private void OnFocus() => itemDropdown.Refresh(false);

        private void RefreshItems()
        {
            itemDropdown.Refresh();
            Repaint();
        }

        private void OnProjectChange()
        {
            if (reloadQueued) return;
            reloadQueued = true;
            EditorApplication.delayCall += DelayedReload;
        }

        private void DelayedReload()
        {
            EditorApplication.delayCall -= DelayedReload;
            reloadQueued = false;
            if (this != null) Reload(true);
        }

        private void UpdatePreview()
        {
            bool painting = MonsterSpawnBrush.IsActive;
            if (preview.Tick() || painting != wasPainting) Repaint();
            wasPainting = painting;
        }

        private void HandleUndo()
        {
            foreach (Draft draft in drafts)
                draft.Dirty = EditorJsonUtility.ToJson(draft.Data) != draft.DraftBaseline;
            Reload(true);
            preview.Dispose();
            RefreshIssues();
            Repaint();
        }

        private void Reload(bool preserveDirty)
        {
            List<MonsterDefinition> sources = MonsterDataEditorUtility.LoadDefinitions();
            List<Draft> previous = new List<Draft>(drafts);
            drafts.Clear();
            foreach (MonsterDefinition source in sources)
            {
                Draft old = previous.Find(candidate => candidate.Source == source);
                if (old != null && preserveDirty && old.Dirty)
                {
                    drafts.Add(old);
                    previous.Remove(old);
                }
                else drafts.Add(new Draft(source));
            }
            foreach (Draft old in previous) old.Dispose();
            if (drafts.Find(candidate => AssetDatabase.GetAssetPath(candidate.Source) == selectedPath) == null)
            {
                selectedPath = drafts.Count > 0 ? AssetDatabase.GetAssetPath(drafts[0].Source) : string.Empty;
                selectedVariantId = -1;
                preview.Dispose();
            }
            RefreshIssues();
            Repaint();
        }

        private void RefreshIssues()
        {
            List<MonsterDefinition> catalog = new List<MonsterDefinition>(drafts.Count);
            hasUnsavedChanges = false;
            foreach (Draft draft in drafts)
            {
                catalog.Add(draft.Data);
                hasUnsavedChanges |= draft.Dirty;
            }
            issues = MonsterDataEditorUtility.Validate(catalog);
        }

        private void Changed(Draft draft)
        {
            MonsterSpawnBrush.Stop();
            draft.Dirty = true;
            RefreshIssues();
            Repaint();
        }

        private void OnGUI()
        {
            Color previousColor = GUI.color;
            Color previousContentColor = GUI.contentColor;
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            try
            {
                DrawWindowContent();
            }
            finally
            {
                GUI.contentColor = previousContentColor;
                GUI.color = previousColor;
            }
        }

        private void DrawWindowContent()
        {
            if (MonsterSpawnBrush.IsActive && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                MonsterSpawnBrush.Stop();
                Event.current.Use();
            }
            EnsureStyles();
            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawList();
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(540f), GUILayout.ExpandWidth(true));
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            Draft selected = drafts.Find(candidate => AssetDatabase.GetAssetPath(candidate.Source) == selectedPath);
            if (selected != null) DrawDetails(selected);
            if (showValidation)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
                if (issues.Count == 0) EditorGUILayout.HelpBox("No issues.", MessageType.Info);
                foreach (MonsterValidationIssue issue in issues) EditorGUILayout.HelpBox(issue.Message, issue.Type);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void EnsureStyles()
        {
            if (rowStyle != null && headerStyle != null) return;
            rowStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            headerStyle = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };
            ProjectFEditorGUIUtility.SetGuiStyleTextColor(rowStyle, Color.white);
            ProjectFEditorGUIUtility.SetGuiStyleTextColor(headerStyle, Color.white);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(52f))) SaveAll();
            if (GUILayout.Button("Load", EditorStyles.toolbarButton, GUILayout.Width(52f)) && ConfirmDiscard()) Reload(false);
            if (GUILayout.Button(new GUIContent("Rebuild", "Add missing pack prefabs without resetting data or IDs"),
                EditorStyles.toolbarButton, GUILayout.Width(65f)))
            {
                if (!hasUnsavedChanges || SaveAll())
                    RunOperation(() => { MonsterDataEditorUtility.RebuildPack(); Reload(false); });
            }
            if (GUILayout.Button("Export JSON", EditorStyles.toolbarButton, GUILayout.Width(92f))) ExportJson();
            if (GUILayout.Button("Load JSON", EditorStyles.toolbarButton, GUILayout.Width(85f))) ImportJson();
            if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(65f)))
            {
                RefreshIssues();
                showValidation = !showValidation;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(MonsterModelPreview.Icon("Toolbar Plus", "Create species"),
                EditorStyles.toolbarButton, GUILayout.Width(28f))) CreateSpecies();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(250f));
            search = EditorGUILayout.TextField("Search", search, EditorStyles.toolbarSearchField);
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            foreach (Draft draft in drafts)
            {
                bool speciesMatches = Matches(draft.Data.MonsterName);
                bool anyMatches = speciesMatches;
                foreach (MonsterVariant variant in draft.Data.Variants)
                    anyMatches |= variant != null && Matches(variant.variantName);
                if (!anyMatches) continue;
                string path = AssetDatabase.GetAssetPath(draft.Source);
                EditorGUILayout.BeginHorizontal();
                bool expanded = !collapsedSpecies.Contains(draft.Source.GetInstanceID());
                Rect foldoutRect = GUILayoutUtility.GetRect(14f, ListRowHeight, GUILayout.Width(14f));
                bool next = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);
                if (next != expanded)
                {
                    if (next) collapsedSpecies.Remove(draft.Source.GetInstanceID());
                    else collapsedSpecies.Add(draft.Source.GetInstanceID());
                }
                if (DrawListRow(selectedPath == path && selectedVariantId < 0,
                    new GUIContent($"{(draft.Dirty ? "* " : "")}[{draft.Data.Id}] {draft.Data.MonsterName}", path),
                    GetSpeciesIcon(draft.Data.Variants))) Select(path, -1);
                EditorGUILayout.EndHorizontal();
                if (!next && string.IsNullOrEmpty(search)) continue;
                foreach (MonsterVariant variant in draft.Data.Variants)
                {
                    if (variant == null || (!speciesMatches && !Matches(variant.variantName))) continue;
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(22f);
                    if (DrawListRow(selectedPath == path && selectedVariantId == variant.id,
                        new GUIContent(variant.variantName, $"Variant {variant.id}"), variant.icon)) Select(path, variant.id);
                    DrawSpawnButton(draft, variant.id);
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        internal static Sprite GetSpeciesIcon(IReadOnlyList<MonsterVariant> variants)
        {
            for (int i = 0; variants != null && i < variants.Count; i++)
                if (variants[i] != null && variants[i].icon != null) return variants[i].icon;
            return null;
        }

        private bool DrawListRow(bool selected, GUIContent content, Sprite icon)
        {
            Rect rect = GUILayoutUtility.GetRect(1f, ListRowHeight, GUILayout.ExpandWidth(true));
            if (selected && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, new Color(0.2f, 0.5f, 0.8f, 0.35f));
            bool next = GUI.Toggle(rect, selected, GUIContent.none, rowStyle);
            Rect iconRect = new Rect(rect.x + 4f, rect.y + (rect.height - ListIconSize) * 0.5f,
                ListIconSize, ListIconSize);
            if (icon != null && Event.current.type == EventType.Repaint)
            {
                float scale = ListIconSize / Mathf.Max(icon.rect.width, icon.rect.height);
                Vector2 size = icon.rect.size * scale;
                ProjectFEditorGUIUtility.DrawSprite(new Rect(iconRect.center - size * 0.5f, size), icon);
            }
            Rect labelRect = new Rect(iconRect.xMax + 4f, rect.y,
                Mathf.Max(0f, rect.xMax - iconRect.xMax - 8f), rect.height);
            GUI.Label(labelRect, content, rowStyle);
            return next;
        }

        private bool Matches(string value) => string.IsNullOrEmpty(search)
            || (!string.IsNullOrEmpty(value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

        private void Select(string path, int variantId)
        {
            if (selectedPath == path && selectedVariantId == variantId) return;
            MonsterSpawnBrush.Stop();
            ProjectFEditorGUIUtility.CommitAndReleaseKeyboardFocus();
            selectedPath = path;
            selectedVariantId = variantId;
            detailScroll = Vector2.zero;
            preview.Dispose();
        }

        private void DrawDetails(Draft draft)
        {
            SerializedObject serialized = draft.Serialized;
            serialized.Update();
            EditorGUILayout.LabelField(draft.Data.MonsterName, headerStyle);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(selectedPath, selectedPath), EditorStyles.miniLabel);
            if (GUILayout.Button(MonsterModelPreview.Icon("d_Search Icon", "Ping definition"), GUILayout.Width(28f)))
                EditorGUIUtility.PingObject(draft.Source);
            DrawSpawnButton(draft, selectedVariantId);
            DrawPaintButton(draft, selectedVariantId);
            EditorGUILayout.EndHorizontal();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serialized.FindProperty("id"), new GUIContent("Species ID"));
            EditorGUILayout.PropertyField(serialized.FindProperty("monsterName"), new GUIContent("Species Name"));
            int variantIndex = -1;
            SerializedProperty variants = serialized.FindProperty("variants");
            for (int i = 0; i < variants.arraySize; i++)
                if (variants.GetArrayElementAtIndex(i).FindPropertyRelative("id").intValue == selectedVariantId) { variantIndex = i; break; }
            if (variantIndex < 0)
            {
                selectedVariantId = -1;
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Common Stats", EditorStyles.boldLabel);
                DrawStats(serialized.FindProperty("commonStats"), null, null);
                EditorGUILayout.Space(6f);
                DrawDrops(draft, serialized.FindProperty("commonDrops"), true);
                EditorGUILayout.Space(6f);
                EditorGUILayout.PropertyField(serialized.FindProperty("animations"), new GUIContent("Animations"), true);
            }
            else DrawVariant(draft, variants.GetArrayElementAtIndex(variantIndex));
            bool changed = EditorGUI.EndChangeCheck();
            if (serialized.ApplyModifiedProperties() || changed) Changed(draft);
            EditorGUILayout.Space(8f);
            DrawVariantCommands(draft, variantIndex);
            GameObject prefab = null;
            if (draft.Data.TryGetVariant(selectedVariantId, out MonsterVariant selected)) prefab = selected.prefab;
            else if (draft.Data.Variants.Count > 0) prefab = draft.Data.Variants[0]?.prefab;
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("3D Preview", EditorStyles.boldLabel);
            preview.Draw(prefab, draft.Data.Animations);
        }

        private static void DrawPaintButton(Draft draft, int variantId)
        {
            bool active = MonsterSpawnBrush.IsPainting(draft.Source, variantId);
            bool valid = draft.Source.TryGetVariant(variantId, out var variant) && variant.prefab != null
                && PrefabUtility.IsPartOfPrefabAsset(variant.prefab);
            using (new EditorGUI.DisabledScope(!active && (!EditorApplication.isPlaying || draft.Dirty || !valid)))
            {
                bool next = GUILayout.Toggle(active, new GUIContent("몬스터 뿌리기"), "Button", GUILayout.Width(112f), GUILayout.Height(22f));
                if (next == active) return;
                ProjectFEditorGUIUtility.CommitAndReleaseKeyboardFocus();
                if (active) MonsterSpawnBrush.Stop();
                else MonsterSpawnBrush.Begin(draft.Source, variantId);
            }
        }

        private static void DrawSpawnButton(Draft draft, int variantId)
        {
            bool valid = draft.Source.TryGetVariant(variantId, out MonsterVariant variant) && variant.prefab != null;
            string tooltip = !EditorApplication.isPlaying ? "Play Mode only"
                : draft.Dirty ? "Save changes before spawning"
                : !valid ? "Select a variant with a saved prefab"
                : "Spawn one " + variant.variantName + " near the player";
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || draft.Dirty || !valid))
            {
                if (GUILayout.Button(new GUIContent("Spawn", tooltip), GUILayout.Width(52f), GUILayout.Height(22f)))
                {
                    ProjectFEditorGUIUtility.CommitAndReleaseKeyboardFocus();
                    if (MonsterEditorSpawner.TrySpawn(draft.Source, variantId, out GameObject spawned, out string error))
                    {
                        Selection.activeGameObject = spawned;
                        EditorGUIUtility.PingObject(spawned);
                    }
                    else Debug.LogWarning("Monster Data: " + error, draft.Source);
                }
            }
        }

        private void DrawVariant(Draft draft, SerializedProperty variant)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Variant", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(variant.FindPropertyRelative("id"), new GUIContent("Variant ID"));
            EditorGUILayout.PropertyField(variant.FindPropertyRelative("variantName"), new GUIContent("Name"));
            EditorGUILayout.PropertyField(variant.FindPropertyRelative("prefab"), new GUIContent("Prefab"));
            EditorGUILayout.PropertyField(variant.FindPropertyRelative("icon"), new GUIContent("Icon"));
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Stat Overrides", EditorStyles.boldLabel);
            DrawStats(variant.FindPropertyRelative("stats"),
                draft.Serialized.FindProperty("commonStats"), variant.FindPropertyRelative("overrideMask"));
            EditorGUILayout.Space(6f);
            SerializedProperty overrideDrops = variant.FindPropertyRelative("overrideDrops");
            bool nextOverride = EditorGUILayout.Toggle("Override Drops", overrideDrops.boolValue);
            SerializedProperty drops = variant.FindPropertyRelative("drops");
            if (nextOverride != overrideDrops.boolValue)
            {
                overrideDrops.boolValue = nextOverride;
                if (nextOverride && drops.arraySize == 0)
                {
                    SerializedProperty common = draft.Serialized.FindProperty("commonDrops");
                    drops.arraySize = common.arraySize;
                    for (int i = 0; i < drops.arraySize; i++)
                    {
                        SerializedProperty source = common.GetArrayElementAtIndex(i);
                        SerializedProperty target = drops.GetArrayElementAtIndex(i);
                        target.FindPropertyRelative("item").objectReferenceValue = source.FindPropertyRelative("item").objectReferenceValue;
                        target.FindPropertyRelative("amount").intValue = source.FindPropertyRelative("amount").intValue;
                        target.FindPropertyRelative("chance").floatValue = source.FindPropertyRelative("chance").floatValue;
                    }
                }
            }
            using (new EditorGUI.DisabledScope(!overrideDrops.boolValue))
                DrawDrops(draft, overrideDrops.boolValue ? drops : draft.Serialized.FindProperty("commonDrops"), overrideDrops.boolValue);
        }

        private static void DrawStats(SerializedProperty stats, SerializedProperty common, SerializedProperty mask)
        {
            for (int i = 0; i < StatFields.Length; i++)
            {
                SerializedProperty value = stats.FindPropertyRelative(StatFields[i]);
                int flag = 1 << i;
                bool overridden = mask == null || (mask.intValue & flag) != 0;
                EditorGUILayout.BeginHorizontal();
                if (mask != null)
                {
                    bool next = GUILayout.Toggle(overridden, new GUIContent("", "Override " + StatLabels[i]), GUILayout.Width(18f));
                    if (next != overridden)
                    {
                        mask.intValue = next ? mask.intValue | flag : mask.intValue & ~flag;
                        if (next) value.floatValue = common.FindPropertyRelative(StatFields[i]).floatValue;
                        overridden = next;
                    }
                }
                using (new EditorGUI.DisabledScope(!overridden))
                {
                    float current = overridden ? value.floatValue : common.FindPropertyRelative(StatFields[i]).floatValue;
                    float next = EditorGUILayout.FloatField(StatLabels[i], current);
                    if (overridden && next != current)
                        value.floatValue = Mathf.Max(i == 0 ? 1f : i == 5 ? 0.01f : 0f, next);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawDrops(Draft draft, SerializedProperty drops, bool editable)
        {
            EditorGUILayout.LabelField("Drop Items", EditorStyles.boldLabel);
            for (int i = 0; i < drops.arraySize; i++)
            {
                SerializedProperty entry = drops.GetArrayElementAtIndex(i);
                string path = entry.propertyPath;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Drop " + (i + 1), EditorStyles.miniBoldLabel);
                bool remove = GUILayout.Button(MonsterModelPreview.Icon("Toolbar Minus", "Remove drop"), GUILayout.Width(26f));
                EditorGUILayout.EndHorizontal();
                if (remove && editable) { drops.DeleteArrayElementAtIndex(i); break; }
                itemDropdown.Draw("Item", (ItemDefinition)entry.FindPropertyRelative("item").objectReferenceValue,
                    item =>
                    {
                        if (draft.Data == null) return;
                        draft.Serialized.Update();
                        SerializedProperty property = draft.Serialized.FindProperty(path + ".item");
                        if (property == null) return;
                        property.objectReferenceValue = item;
                        draft.Serialized.ApplyModifiedProperties();
                        Changed(draft);
                    });
                SerializedProperty amount = entry.FindPropertyRelative("amount");
                EditorGUI.BeginChangeCheck();
                int nextAmount = EditorGUILayout.IntField("Amount", amount.intValue);
                if (EditorGUI.EndChangeCheck() && editable) amount.intValue = Mathf.Max(1, nextAmount);
                SerializedProperty chance = entry.FindPropertyRelative("chance");
                EditorGUI.BeginChangeCheck();
                float nextChance = EditorGUILayout.Slider("Chance (%)", chance.floatValue * 100f, 0f, 100f);
                if (EditorGUI.EndChangeCheck() && editable) chance.floatValue = nextChance / 100f;
            }
            if (editable && GUILayout.Button("Add Drop Item"))
            {
                int index = drops.arraySize++;
                SerializedProperty entry = drops.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("item").objectReferenceValue = null;
                entry.FindPropertyRelative("amount").intValue = 1;
                entry.FindPropertyRelative("chance").floatValue = 1f;
            }
        }

        private void DrawVariantCommands(Draft draft, int index)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Variant"))
            {
                Undo.RecordObject(draft.Data, "Add Monster Variant");
                List<MonsterVariant> variants = new List<MonsterVariant>(draft.Data.Variants);
                MonsterVariant variant = new MonsterVariant
                {
                    id = draft.Data.AllocateVariantId(), variantName = "Variant " + variants.Count
                };
                variants.Add(variant);
                draft.Data.SetAuthoringData(draft.Data.Id, draft.Data.MonsterName,
                    draft.Data.CommonStats, draft.Data.Animations,
                    new List<MonsterDropEntry>(draft.Data.CommonDrops), variants);
                selectedVariantId = variant.id;
                Changed(draft);
                GUIUtility.ExitGUI();
            }
            using (new EditorGUI.DisabledScope(index < 0))
            {
                if (GUILayout.Button("Remove Variant") && EditorUtility.DisplayDialog("Remove Variant",
                    "Remove this variant? Its ID will not be reassigned to another existing variant.", "Remove", "Cancel"))
                {
                    draft.Serialized.Update();
                    draft.Serialized.FindProperty("variants").DeleteArrayElementAtIndex(index);
                    draft.Serialized.ApplyModifiedProperties();
                    selectedVariantId = -1;
                    preview.Dispose();
                    Changed(draft);
                    GUIUtility.ExitGUI();
                }
            }
            if (GUILayout.Button("Clear All Overrides") && EditorUtility.DisplayDialog("Clear Overrides",
                "Make every variant inherit the species stats and drops?", "Clear", "Cancel"))
            {
                draft.Serialized.Update();
                SerializedProperty variants = draft.Serialized.FindProperty("variants");
                for (int i = 0; i < variants.arraySize; i++)
                {
                    SerializedProperty variant = variants.GetArrayElementAtIndex(i);
                    variant.FindPropertyRelative("overrideMask").intValue = 0;
                    variant.FindPropertyRelative("overrideDrops").boolValue = false;
                }
                draft.Serialized.ApplyModifiedProperties();
                Changed(draft);
            }
            EditorGUILayout.EndHorizontal();
        }

        private bool SaveAll()
        {
            ProjectFEditorGUIUtility.CommitAndReleaseKeyboardFocus();
            RefreshIssues();
            if (MonsterDataEditorUtility.HasErrors(issues))
            {
                showValidation = true;
                EditorUtility.DisplayDialog("Monster Data", "Fix validation errors before saving.", "OK");
                return false;
            }
            foreach (Draft draft in drafts)
            {
                if (draft.Dirty && EditorJsonUtility.ToJson(draft.Source) != draft.SourceBaseline)
                {
                    EditorUtility.DisplayDialog("Monster Data", "Asset changed outside this window: "
                        + draft.Source.name + ". Load it again before saving.", "OK");
                    return false;
                }
            }
            foreach (Draft draft in drafts)
            {
                if (!draft.Dirty) continue;
                Undo.RecordObject(draft.Source, "Save Monster Data");
                MonsterDataEditorUtility.CopyAuthoring(draft.Data, draft.Source);
                EditorUtility.SetDirty(draft.Source);
            }
            AssetDatabase.SaveAssets();
            foreach (Draft draft in drafts) draft.CaptureBaseline();
            RefreshIssues();
            return true;
        }

        public override void SaveChanges()
        {
            if (SaveAll()) base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            Reload(false);
            base.DiscardChanges();
        }

        private bool ConfirmDiscard() => !hasUnsavedChanges || EditorUtility.DisplayDialog(
            "Monster Data", "Discard unsaved draft changes?", "Discard", "Cancel");

        private void CreateSpecies()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Monster Definition",
                "Monster_New", "asset", "", MonsterDataEditorUtility.DefinitionRoot);
            if (string.IsNullOrEmpty(path)) return;
            List<MonsterDefinition> catalog = new List<MonsterDefinition>();
            foreach (Draft draft in drafts) catalog.Add(draft.Data);
            MonsterDefinition definition = CreateInstance<MonsterDefinition>();
            definition.SetAuthoringData(MonsterDataEditorUtility.NextSpeciesId(catalog),
                Path.GetFileNameWithoutExtension(path), MonsterStats.Default, new MonsterAnimations(),
                new List<MonsterDropEntry>(), new List<MonsterVariant>());
            AssetDatabase.CreateAsset(definition, path);
            Undo.RegisterCreatedObjectUndo(definition, "Create Monster Definition");
            AssetDatabase.SaveAssets();
            Reload(true);
            Select(path, -1);
        }

        private void ExportJson()
        {
            RefreshIssues();
            if (MonsterDataEditorUtility.HasErrors(issues)) { showValidation = true; return; }
            string path = EditorUtility.SaveFilePanel("Export Monster Data", "", "monster_data", "json");
            if (string.IsNullOrEmpty(path)) return;
            RunOperation(() =>
            {
                List<MonsterDefinition> sources = new List<MonsterDefinition>();
                List<MonsterDefinition> data = new List<MonsterDefinition>();
                foreach (Draft draft in drafts) { sources.Add(draft.Source); data.Add(draft.Data); }
                File.WriteAllText(path, MonsterDataJson.Export(sources, data));
            });
        }

        private void ImportJson()
        {
            string path = EditorUtility.OpenFilePanel("Load Monster Data", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            RunOperation(() =>
            {
                List<MonsterDefinition> sources = new List<MonsterDefinition>();
                List<MonsterDefinition> data = new List<MonsterDefinition>();
                foreach (Draft draft in drafts) { sources.Add(draft.Source); data.Add(draft.Data); }
                List<MonsterDefinition> imported = MonsterDataJson.Import(File.ReadAllText(path), sources, data);
                try
                {
                    for (int i = 0; i < drafts.Count; i++)
                    {
                        Undo.RecordObject(drafts[i].Data, "Load Monster Data JSON");
                        MonsterDataEditorUtility.CopyAuthoring(imported[i], drafts[i].Data);
                        drafts[i].Dirty = true;
                    }
                }
                finally { foreach (MonsterDefinition copy in imported) DestroyImmediate(copy); }
                preview.Dispose();
                RefreshIssues();
                Repaint();
            });
        }

        private static void RunOperation(Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Monster Data", exception.Message, "OK");
            }
        }
    }
}
