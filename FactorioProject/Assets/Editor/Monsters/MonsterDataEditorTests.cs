using System;
using System.Collections.Generic;
using ProjectF.Monsters;
using ProjectF.Simulation.Monsters;
using UnityEditor;
using UnityEngine;

namespace ProjectF.Editor.Monsters
{
    internal static class MonsterDataEditorTests
    {
        [MenuItem("Tools/ProjectF/Tests/Monster Data")]
        private static void Run()
        {
            List<MonsterDefinition> sources = MonsterDataEditorUtility.LoadDefinitions();
            List<MonsterDefinition> drafts = new List<MonsterDefinition>();
            List<MonsterDefinition> imported = null;
            MonsterDefinition copy = null;
            int checks = 0;
            void Check(bool condition, string message)
            {
                checks++;
                if (!condition) throw new InvalidOperationException(message);
            }
            try
            {
                CheckListIcons(Check);
                Check(sources.Count >= 4, "Initial species catalog is missing.");
                CheckMonsterFocus(Check, sources[0]);
                List<string> originalJson = new List<string>();
                foreach (MonsterDefinition source in sources)
                {
                    originalJson.Add(EditorJsonUtility.ToJson(source));
                    MonsterDefinition draft = UnityEngine.Object.Instantiate(source);
                    draft.hideFlags = HideFlags.HideAndDontSave;
                    drafts.Add(draft);
                }
                Check(!MonsterDataEditorUtility.HasErrors(MonsterDataEditorUtility.Validate(drafts)),
                    "Catalog has validation errors.");
                MonsterDefinition first = drafts[0];
                MonsterStats stats = first.CommonStats;
                stats.MaxHealth = 321f;
                first.SetAuthoringData(first.Id, first.MonsterName, stats, first.Animations,
                    new List<MonsterDropEntry>(first.CommonDrops), new List<MonsterVariant>(first.Variants));
                first.Variants[0].overrideMask = MonsterStat.AttackDamage;
                first.Variants[0].stats.AttackDamage = 77f;
                string json = MonsterDataJson.Export(sources, drafts);
                imported = MonsterDataJson.Import(json, sources, drafts);
                Check(imported[0].CommonStats.MaxHealth == 321f, "Common stats JSON round trip.");
                Check(imported[0].TryGetRuntimeData(first.Variants[0].id, out MonsterArchetypeData data)
                    && data.Stats.AttackDamage == 77f && data.Stats.MaxHealth == 321f,
                    "Override JSON round trip.");
                for (int i = 0; i < drafts.Count; i++)
                {
                    for (int v = 0; v < drafts[i].Variants.Count; v++)
                    {
                        Check(imported[i].Variants[v].prefab == drafts[i].Variants[v].prefab,
                            "Prefab GUID/local ID round trip.");
                        Check(imported[i].Variants[v].icon == drafts[i].Variants[v].icon,
                            "Portrait Sprite GUID/local ID round trip.");
                    }
                    for (int state = 0; state <= (int)MonsterAnimationState.Death; state++)
                        Check(imported[i].Animations.GetClip((MonsterAnimationState)state)
                            == drafts[i].Animations.GetClip((MonsterAnimationState)state),
                            "FBX clip GUID/local ID round trip.");
                }
                copy = UnityEngine.Object.Instantiate(first);
                copy.name = "Copy Target";
                copy.hideFlags = HideFlags.HideAndDontSave;
                int reserved = first.AllocateVariantId();
                MonsterDataEditorUtility.CopyAuthoring(first, copy);
                Check(copy.name == "Copy Target" && copy.hideFlags == HideFlags.HideAndDontSave,
                    "Copy must not change object identity or hide flags.");
                Check(copy.AllocateVariantId() > reserved, "Copy must preserve the hidden ID counter.");
                bool rejected = false;
                first.SetAuthoringData(drafts[1].Id, first.MonsterName, stats, first.Animations,
                    new List<MonsterDropEntry>(first.CommonDrops), new List<MonsterVariant>(first.Variants));
                try
                {
                    List<MonsterDefinition> invalid = MonsterDataJson.Import(
                        MonsterDataJson.Export(sources, drafts), sources, drafts);
                    foreach (MonsterDefinition item in invalid) UnityEngine.Object.DestroyImmediate(item);
                }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Duplicate IDs must reject the complete import.");
                for (int i = 0; i < sources.Count; i++)
                    Check(EditorJsonUtility.ToJson(sources[i]) == originalJson[i], "Source assets remain unchanged.");
                Debug.Log($"PASS: {checks} Monster Data editor checks. No assets were saved.");
            }
            catch (Exception exception) { Debug.LogError("Monster Data tests failed: " + exception); }
            finally
            {
                foreach (MonsterDefinition draft in drafts) UnityEngine.Object.DestroyImmediate(draft);
                if (imported != null)
                    foreach (MonsterDefinition item in imported) UnityEngine.Object.DestroyImmediate(item);
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        private static void CheckMonsterFocus(Action<bool, string> check, MonsterDefinition source)
        {
            MonsterDefinition definition = ScriptableObject.CreateInstance<MonsterDefinition>();
            GameObject nearRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject farRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            MonsterInstance near = null;
            MonsterInstance far = null;
            try
            {
                Sprite icon = source.Variants[0].icon;
                definition.SetAuthoringData(41, "Focus Test", MonsterStats.Default, null, null,
                    new List<MonsterVariant> { new MonsterVariant
                    {
                        id = 7, variantName = "Blue", icon = icon,
                        overrideMask = MonsterStat.MaxHealth,
                        stats = new MonsterStats { MaxHealth = 321f }
                    } });
                Vector3 origin = new Vector3(45000f, 45000f, 45000f);
                nearRoot.transform.position = origin + Vector3.forward * 5f;
                farRoot.transform.position = origin + Vector3.forward * 10f;
                UnityEngine.Object.DestroyImmediate(nearRoot.GetComponent<Collider>());
                UnityEngine.Object.DestroyImmediate(farRoot.GetComponent<Collider>());
                nearRoot.AddComponent<CapsuleCollider>();
                farRoot.AddComponent<CapsuleCollider>();
                nearRoot.AddComponent<Rigidbody>().isKinematic = true;
                farRoot.AddComponent<Rigidbody>().isKinematic = true;
                near = new MonsterInstance(nearRoot, definition, 7);
                far = new MonsterInstance(farRoot, definition, 7);
                nearRoot.GetComponent<CapsuleCollider>().enabled = false;
                farRoot.GetComponent<CapsuleCollider>().enabled = false;
                check(near.Data.SpeciesId == 41 && near.Data.VariantId == 7
                    && near.Data.Stats.MaxHealth == 321f, "Focus data uses the resolved numeric archetype.");
                check(near.Icon == icon && near.DisplayName == "Focus Test Blue"
                    && near.MaxHealth == 321f && near.CurrentHealth == 321f, "InfoPanel identity and initial full HP.");
                near.SetCurrentHealth(123f);
                check(near.CurrentHealth == 123f, "HP changes remain available to the gauge.");
                near.SetCurrentHealth(float.NaN);
                check(near.CurrentHealth == 123f, "Invalid HP cannot corrupt the gauge.");
                near.SetCurrentHealth(-1f);
                check(near.CurrentHealth == 0f, "HP cannot be negative.");
                near.SetCurrentHealth(999f);
                check(near.CurrentHealth == near.MaxHealth, "HP cannot exceed its maximum.");
                check(nearRoot.GetComponents<MonoBehaviour>().Length == 0, "Focus adds no actor behaviour.");
                Ray ray = new Ray(origin, Vector3.forward);
                check(!MonsterFocusWorld.TryRaycast(ray, 4f, out _, out _), "Clip selection before the view.");
                check(MonsterFocusWorld.TryRaycast(ray, 20f, out MonsterInstance hit, out float distance)
                    && hit == near && Mathf.Abs(distance - 4.5f) < 0.01f, "Nearest collider-free view wins.");
                nearRoot.SetActive(false);
                check(!MapObjectTargetExtensions.IsAliveTarget(near)
                    && MonsterFocusWorld.TryRaycast(ray, 20f, out hit, out _) && hit == far,
                    "Inactive views cannot stay selected.");
                nearRoot.SetActive(true);
                nearRoot.GetComponent<Renderer>().enabled = false;
                check(MonsterFocusWorld.TryRaycast(ray, 20f, out hit, out _) && hit == far,
                    "Hidden renderers cannot be picked.");
                nearRoot.GetComponent<Renderer>().enabled = true;
                nearRoot.transform.position += Vector3.right * 5f;
                check(MonsterFocusWorld.TryRaycast(ray, 20f, out hit, out _) && hit == far,
                    "Selection bounds follow view movement.");
                far.Dispose();
                far.Dispose();
                check(!MapObjectTargetExtensions.IsAliveTarget(far)
                    && !MonsterFocusWorld.TryRaycast(ray, 20f, out _, out _), "Disposal is idempotent and unregisters.");
                nearRoot.transform.position = origin + Vector3.forward * 5f;
                UnityEngine.Object.DestroyImmediate(nearRoot);
                check(!MapObjectTargetExtensions.IsAliveTarget(near)
                    && !MonsterFocusWorld.TryRaycast(ray, 20f, out _, out _), "Destroyed views are pruned safely.");
            }
            finally
            {
                near?.Dispose();
                far?.Dispose();
                if (nearRoot != null) UnityEngine.Object.DestroyImmediate(nearRoot);
                if (farRoot != null) UnityEngine.Object.DestroyImmediate(farRoot);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        private static void CheckListIcons(Action<bool, string> check)
        {
            Texture2D texture = new Texture2D(2, 2);
            Sprite first = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), Vector2.one * 0.5f);
            Sprite second = Sprite.Create(texture, new Rect(0f, 0f, 1f, 2f), Vector2.one * 0.5f);
            try
            {
                List<MonsterVariant> variants = new List<MonsterVariant>();
                check(MonsterDataEditorWindow.GetSpeciesIcon(null) == null, "Null variant list has no portrait.");
                check(MonsterDataEditorWindow.GetSpeciesIcon(variants) == null, "Empty species has no portrait.");
                variants.Add(null);
                variants.Add(new MonsterVariant());
                check(MonsterDataEditorWindow.GetSpeciesIcon(variants) == null, "Missing portraits are allowed.");
                variants.Add(new MonsterVariant { icon = first });
                check(MonsterDataEditorWindow.GetSpeciesIcon(variants) == first, "Skip missing portraits.");
                variants[1].icon = second;
                check(MonsterDataEditorWindow.GetSpeciesIcon(variants) == second, "Use the first assigned draft portrait.");
                variants[1].icon = null;
                check(MonsterDataEditorWindow.GetSpeciesIcon(variants) == first, "Clearing a portrait updates the species row.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
