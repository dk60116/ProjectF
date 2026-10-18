$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
dotnet run --project (Join-Path $PSScriptRoot 'EquipmentInventoryHarness.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Equipment inventory runtime checks failed.' }

function Read-Member([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    return $source.Substring($start, $end - $start)
}
$scripts = Join-Path $root 'FactorioProject/Assets/Scripts'
$serializer = Get-Content -LiteralPath (Join-Path $scripts 'Manager/SaveGameBinarySerializer.cs') -Raw
$data = Get-Content -LiteralPath (Join-Path $scripts 'Manager/SaveGameData.cs') -Raw
$generated = "using System; using System.IO; using System.Collections.Generic; using UnityEngine;`n"
foreach ($type in @('PlayerSaveData', 'PlayerStatSaveData', 'PlayerInventorySlotSaveState', 'PlayerCraftingQueueEntrySaveData', 'PlayerCraftingIngredientSaveData')) {
    $generated += (Read-Member $data "public sealed class $type") + "`n"
}
$generated += "public static partial class SaveGameBinarySerializer { private const int MaxSerializedListCount = 1000000;`n"
foreach ($signature in @('private static void WritePlayer(', 'private static PlayerSaveData ReadPlayer(',
    'private static void WritePlayerStats(', 'private static PlayerStatSaveData ReadPlayerStats(',
    'private static void WritePlayerSlot(', 'private static PlayerInventorySlotSaveState ReadPlayerSlot(',
    'private static void WritePlayerCraftingQueueEntry(', 'private static PlayerCraftingQueueEntrySaveData ReadPlayerCraftingQueueEntry(',
    'private static void WritePlayerCraftingIngredient(', 'private static PlayerCraftingIngredientSaveData ReadPlayerCraftingIngredient(',
    'private static void WriteVector3(', 'private static Vector3 ReadVector3(',
    'private static void WriteQuaternion(', 'private static Quaternion ReadQuaternion(',
    'private static void WriteVector2Int(', 'private static Vector2Int ReadVector2Int(',
    'private static void WriteList<T>(', 'private static List<T> ReadList<T>(')) {
    $generated += (Read-Member $serializer $signature) + "`n"
}
$generated += '}'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-EquipmentSave-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SerializationChecks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><UseAppHost>false</UseAppHost></PropertyGroup></Project>')
dotnet run --project (Join-Path $probe 'Probe.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Equipment binary save checks failed.' }

$hud = Get-Content -LiteralPath (Join-Path $scripts 'HUD/PlayerHUD.EquipmentInventory.cs') -Raw
$bagSlot = Get-Content -LiteralPath (Join-Path $scripts 'HUD/ItemSlot/BagSlot.cs') -Raw
foreach ($contract in @('bag.Changed += RefreshEquipmentInventory', 'boundEquipmentBag.Changed -= RefreshEquipmentInventory', 'slot.Bind(boundEquipmentBag, i, itemId, count, 1)')) {
    if (!$hud.Contains($contract)) { throw "Missing HUD binding: $contract" }
}
if (!$bagSlot.Contains('if (boundBag != null)') -or !$bagSlot.Contains('if (!IsEquipmentSlot) ToggleCraftingSlots();')) { throw 'Inactive-slot initialization or equipment interaction guard missing.' }
Write-Host 'Equipment HUD binding source contracts passed.'

$trailShaderPath = Join-Path $root 'FactorioProject/Assets/Shaders/MapFocusOverlay.shader'
$trailShader = Get-Content -LiteralPath $trailShaderPath -Raw
foreach ($contract in @('Shader "Custom/MapFocusOverlay"', '"RenderPipeline" = "UniversalPipeline"',
    'output.color = input.color * _Color', 'ZWrite Off', 'ZTest [_ZTest]')) {
    if (!$trailShader.Contains($contract)) { throw "Missing trail shader render contract: $contract" }
}
$shaderGuid = [regex]::Match((Get-Content -LiteralPath ($trailShaderPath + '.meta') -Raw), '(?m)^guid: (\w+)').Groups[1].Value
$graphics = Get-Content -LiteralPath (Join-Path $root 'FactorioProject/ProjectSettings/GraphicsSettings.asset') -Raw
if (!$shaderGuid -or !$graphics.Contains("guid: $shaderGuid")) { throw 'Arrow trail shader is not included in player builds.' }
Write-Host 'Arrow trail URP shader and player-build inclusion contracts passed.'

$playerHud = Get-Content -LiteralPath (Join-Path $scripts 'HUD/PlayerHUD.cs') -Raw
$hudUpdate = Read-Member $playerHud 'private void Update()'
if ($hudUpdate.IndexOf('UpdateRangedAttackRange();') -le $hudUpdate.IndexOf('UpdateObjectInfoPanelState();')) {
    throw 'Ranged range preview must update after the actual focus target.'
}
if (!(Read-Member $playerHud 'private void OnDisable()').Contains('ReleaseRangedAttackRange();') -or
    !(Read-Member $playerHud 'private void ClearObjectInfoPanelState()').Contains('rangedAttackRangeView?.Hide();')) {
    throw 'Range preview focus-clear or HUD-disable cleanup is missing.'
}
Write-Host 'Ranged range HUD focus and lifecycle contracts passed.'

$controller = Get-Content -LiteralPath (Join-Path $scripts 'Character/Player/PlayerController.cs') -Raw
$controllerUpdate = Read-Member $controller 'private void Update()'
if (!$controllerUpdate.Contains('TryGetRangedAttackApproachDirection(out Vector3 rangedApproachDirection)') -or
    ![regex]::IsMatch($controllerUpdate, 'else if \(hasManualMovementInput\)\s*\{\s*CancelRangedAttack\(\);\s*HandleManualKnifeMovement\(\);')) {
    throw 'Player movement must drive ranged pursuit and cancel it on manual input.'
}
if (!(Read-Member $controller 'private void CancelKnifeInteraction()').Contains('CancelRangedAttack();') -or
    !(Read-Member $playerHud 'private void ClearObjectInfoPanelState()').Contains('CancelRangedAttack();') -or
    !(Read-Member $playerHud 'private void BindObjectInfoPanel(').Contains('CancelRangedAttackUnlessFocused(target);')) {
    throw 'Load, mount, HUD focus-clear or target-change pursuit cancellation is missing.'
}
foreach ($contract in @('currentPlayer.RequestRangedAttack(currentInteractionMonster)',
    'currentPlayer.RequestRangedAttack(currentInteractionAnimal)',
    'rangedPlayer.RequestRangedAttack((object)currentInteractionMonster ?? currentInteractionAnimal)')) {
    if (!$playerHud.Contains($contract)) { throw "Ranged HUD input still bypasses pursuit: $contract" }
}
Write-Host 'Ranged pursuit movement, cancellation and HUD input contracts passed.'

$interaction = Read-Member $playerHud 'private void UpdateInteractionButtonState()'
$equippedInteraction = Read-Member $playerHud 'private bool TryActivateEquippedMapObjectInteraction('
if ($interaction.IndexOf('TryActivateEquippedMapObjectInteraction(playerController)') -gt $interaction.IndexOf('object target = clickedObjectInfoTarget;') -or
    !$interaction.Contains('TryActivateEquippedMapObjectInteraction(playerController)') -or
    !$equippedInteraction.Contains('target is IPlayerItemMount || target is Vehicle') -or
    !$equippedInteraction.Contains('TryActivateMapObjectInteraction(target)') -or
    !(Read-Member $playerHud 'private void HandleInteractionButtonKeyboardInput()').Contains('currentInteractionMapObject == null')) {
    throw 'Equipped ranged weapons must not block mount interaction buttons or Space input.'
}
$drop = Read-Member $bagSlot 'private void DropItem(int maxDropCount)'
if (!$drop.Contains('focusedMount.TryMountItemFromSlot(player, boundBag, slotIndex)') -or
    $drop.IndexOf('focusedMount.TryMountItemFromSlot') -gt $drop.IndexOf('player.TryPrepareEquipmentTransfer')) {
    throw 'Mount drop must use shared item mounts before generic equipment removal.'
}
$desk = Get-Content -LiteralPath (Join-Path $scripts 'Object/MapObj/InstallationObject/Book desk.cs') -Raw
$tower = Get-Content -LiteralPath (Join-Path $scripts 'Object/MapObj/InstallationObject/Tower.cs') -Raw
if (!$desk.Contains('IPlayerItemMount') -or !$desk.Contains('public bool TryMountItemFromSlot(') -or
    !$tower.Contains('IPersistentInstallationItemStorage') -or $tower.Contains('PortableObject')) {
    throw 'Desk and Tower must share the mount route; Tower must persist its weapon without portable visuals.'
}
$batchRenderer = Get-Content -LiteralPath (Join-Path $scripts 'Rendering/InstallationBatchRenderer.cs') -Raw
if (!$batchRenderer.Contains('owner.GetComponentsInChildren(true, rendererScratch)') -or
    !$batchRenderer.Contains('!renderer.gameObject.activeInHierarchy')) {
    throw 'Installation batching must capture inactive mount models and respect their active state.'
}
Write-Host 'Tower mount HUD, shared Desk route, persistence and batched visibility contracts passed.'

$placement = Get-Content -LiteralPath (Join-Path $scripts 'Object/MapObj/InstallationObject/InstallationPlacementController.cs') -Raw
if (!$placement.Contains('((Tower)inputOutputModule).TryCollectAmmunitionItemIds(dynamicItemIds)') -or
    !$tower.Contains('protected override void TryStartNextCraft() { }') -or
    !$tower.Contains('TryResolveRuntimeInputItemBlock(ammo.id, 1, null, out _, out coordinate)') -or
    !$tower.Contains('ConsumeRuntimeInputAreaCenterObjects(ammoCoordinate, projectile.id, 1, origin, 0f)')) {
    throw 'Tower ammunition must bind through placement, use canonical I/O storage and be spent only by shooting.'
}
$module = Get-Content -LiteralPath (Join-Path $scripts 'Object/MapObj/InstallationObject/InputOutputModule.cs') -Raw
if (!(Read-Member $module 'protected int ConsumeRuntimeInputAreaCenterObjects(').Contains('stateStore.RemoveSavedCenterItems(coordinate, itemId, count)') -or
    !(Read-Member $module 'protected int GetRuntimeInputAreaCenterItemCount(').Contains('GetSavedCenterExtractableItemCount(coordinate, itemId)') -or
    !$tower.Contains('ApplyPlannedBaseModuleTick(deltaTime);') -or !$tower.Contains('WakeRuntimeUpdate();')) {
    throw 'Tower must retain virtualized I/O save storage and the existing facility wake/sleep lifecycle.'
}
Write-Host 'Tower firing, dynamic ammo placement, canonical saved/loaded I/O and scheduler contracts passed.'
