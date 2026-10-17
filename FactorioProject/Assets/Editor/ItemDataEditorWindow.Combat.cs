using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class ItemDataEditorWindow
{
    private static readonly GUIContent ProjectileSpeedLabel = new GUIContent(
        "Projectile Speed (Units/Sec)", "투사체가 1초 동안 이동하는 월드 거리입니다. 탄약을 쓰는 장비는 탄약 ItemData의 값을 사용합니다.");

    private void DrawCombatItemFields(SerializedObject serialized)
    {
        SerializedProperty equipment = serialized.FindProperty("isEquipment");
        SerializedProperty ammunition = serialized.FindProperty("isAmmo");
        SerializedProperty ranged = serialized.FindProperty("isRangedWeapon");
        SerializedProperty required = serialized.FindProperty("requiresAmmo");
        EditorGUILayout.PropertyField(ammunition, new GUIContent("Ammunition Item"));
        if (equipment.boolValue || equipment.hasMultipleDifferentValues)
        {
            EditorGUILayout.PropertyField(ranged, new GUIContent("Ranged Weapon"));
            if (ranged.boolValue || ranged.hasMultipleDifferentValues)
            {
                EditorGUILayout.PropertyField(required, new GUIContent("Requires Ammo"));
                if (required.boolValue || required.hasMultipleDifferentValues)
                    EditorGUILayout.PropertyField(serialized.FindProperty("compatibleAmmo"), new GUIContent("Compatible Ammo"), true);
            }
        }
        if (!equipment.boolValue && !equipment.hasMultipleDifferentValues
            && !ammunition.boolValue && !ammunition.hasMultipleDifferentValues) return;
        EditorGUILayout.PropertyField(serialized.FindProperty("attackDamage"), new GUIContent("Attack Damage", "장비와 탄약의 공격력을 더합니다."));
        EditorGUILayout.PropertyField(serialized.FindProperty("fireRate"), new GUIContent("Fire Rate / Sec", "장비와 탄약 중 낮은 발사속도를 사용합니다."));
        EditorGUILayout.PropertyField(serialized.FindProperty("attackRange"), new GUIContent("Attack Range", "장비와 탄약 중 짧은 사거리를 사용합니다."));
        if (ammunition.boolValue || ammunition.hasMultipleDifferentValues
            || (ranged.boolValue || ranged.hasMultipleDifferentValues)
                && (!required.boolValue || required.hasMultipleDifferentValues))
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Projectile", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("projectileSpeed"), ProjectileSpeedLabel);
        }
    }

    private static List<InputOutputJsonEntry> BuildCompatibleAmmoJson(ItemDefinition definition)
    {
        var result = new List<InputOutputJsonEntry>();
        if (definition.compatibleAmmo != null)
            for (int i = 0; i < definition.compatibleAmmo.Count; i++)
                if (definition.compatibleAmmo[i] != null)
                    result.Add(BuildDefinitionReferenceJsonEntry(definition.compatibleAmmo[i]));
        return result;
    }
}
