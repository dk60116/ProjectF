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
