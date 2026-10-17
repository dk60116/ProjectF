$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$definition = Get-Content -LiteralPath (Join-Path $root 'FactorioProject/Assets/Scripts/Object/ItemDefinition.cs') -Raw
$editor = Get-Content -LiteralPath (Join-Path $root 'FactorioProject/Assets/Editor/ItemDataEditorWindow.cs') -Raw
$combatEditor = Get-Content -LiteralPath (Join-Path $root 'FactorioProject/Assets/Editor/ItemDataEditorWindow.Combat.cs') -Raw
$checks = 0
function Require([bool]$condition, [string]$message) {
    $script:checks++
    if (-not $condition) { throw $message }
}

# Source contracts complement the runtime/editor compilation without launching Unity.
Require ([regex]::Matches($definition, 'public bool isEquipment;').Count -eq 1) 'ItemDefinition owns one serialized equipment flag, defaulting to false'
Require ($editor.Contains('GetSelectedDefinitionProperty(serializedObject, "isEquipment")')) 'Single selection binds the equipment flag'
Require ($editor.Contains('GetMultiSelectedDefinitionProperty(serializedObject, "isEquipment")')) 'Multi selection binds the same equipment flag'
Require ([regex]::Matches($editor, 'DrawMultiPropertyField\(isEquipmentProperty, new GUIContent\("Equipment Item"').Count -eq 2) 'Both editors draw the toggle through the existing serialized-property helper'
Require ($editor.Contains('public bool hasEquipment;') -and $editor.Contains('public bool isEquipment;')) 'JSON tracks presence separately from the boolean value'
Require ([regex]::IsMatch($editor, 'hasEquipment = true,\s*isEquipment = definition\.isEquipment,')) 'Export includes true and false equipment values'
Require ([regex]::IsMatch($editor, 'if \(entry\.hasEquipment\)\s*\{\s*definition\.isEquipment = entry\.isEquipment;\s*\}')) 'Import restores explicit false while preserving values for legacy JSON without the field'
Require ($editor.Contains('public int version = 23;')) 'Export format version identifies equipment and ammunition authoring support'
Require ($combatEditor.Contains('"Projectile Speed (Units/Sec)"') -and $combatEditor.Contains('serialized.FindProperty("projectileSpeed"), ProjectileSpeedLabel')) 'Projectile authoring exposes the existing serialized speed with explicit travel units'
Require ($combatEditor.Contains('ammunition.boolValue || ammunition.hasMultipleDifferentValues') -and $combatEditor.Contains('(!required.boolValue || required.hasMultipleDifferentValues)')) 'Speed is authored on ammunition or self-contained ranged weapons, not ignored on ammunition-consuming weapons'
Require ($editor.Contains('projectileSpeed = definition.projectileSpeed,') -and $editor.Contains('definition.projectileSpeed = Mathf.Max(.01f, entry.projectileSpeed);')) 'Projectile speed is preserved by ItemData JSON export/import'
Write-Host "Item equipment source contracts passed: $checks"
