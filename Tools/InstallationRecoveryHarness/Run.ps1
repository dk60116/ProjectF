$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scripts = Join-Path $root 'FactorioProject/Assets/Scripts'
$placement = Get-Content -LiteralPath (Join-Path $scripts 'Object/MapObj/InstallationObject/InstallationPlacementController.cs') -Raw
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
$module = Get-Content -LiteralPath (Join-Path $scripts 'Object/MapObj/InstallationObject/InputOutputModule.cs') -Raw
$contracts = Get-Content -LiteralPath (Join-Path $scripts 'Object/MapObj/InstallationObject/InstallationObject.cs') -Raw
$generated = "using System; using System.Collections.Generic; using UnityEngine;`n"
foreach ($contract in @('public interface IPersistentInstallationItemStorage', 'public interface IPersistentInstallationItemCollectionStorage')) {
    $generated += (Read-Member $contracts $contract) + "`n"
}
$generated += "[Flags] " + (Read-Member $contracts 'public enum InstallationMapFilter') + "`n"
$defaultMapFilter = [regex]::Match($contracts, 'public const InstallationMapFilter DefaultMapFilter\s*=\s*[^;]+;')
if (!$defaultMapFilter.Success) { throw 'Missing installation default map filter.' }
$generated += "public partial class InstallationObject {`n" + $defaultMapFilter.Value + "`n"
$generated += "private InstallationMapFilter mapFilter = DefaultMapFilter;`n"
$generated += (Read-Member $contracts 'public InstallationMapFilter MapFilter') + "`n"
$generated += "}`n"
$itemEditor = Get-Content -LiteralPath (Join-Path $root 'FactorioProject/Assets/Editor/ItemDataEditorWindow.cs') -Raw
$generated += "public static partial class MapFilterChecks {`n"
$generated += (Read-Member $itemEditor 'private static bool TryParseInstallationMapFilter(') + "`n}`n"
$generated += "public partial class InstallationPlacementController {`n"
$generated += Read-Member $placement 'private void TransferPackedStoredInstallationItemsToPlayerStorage('
$generated += Read-Member $placement 'private static bool HasPendingStoredInstallationItems('
$generated += "}`npublic partial class InputOutputModule {`n"
$generated += Read-Member $module 'public bool TryGetPrimaryObjectCell('
$generated += "}`n"
$prop = Get-Content -LiteralPath (Join-Path $scripts 'Object/PropObj.cs') -Raw
$generated += "public partial class PropObj {`n"
foreach ($member in @('public ItemDefinition BoundItemDefinition', 'public int ResolveItemId()',
    'private static bool TryResolveInstallationItemDefinition(', 'private static bool MatchesLookupName(',
    'private static string NormalizeLookupName(')) {
    $generated += (Read-Member $prop $member) + "`n"
}
$generated += "}`n"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-InstallationRecovery-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MapFilterChecks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><UseAppHost>false</UseAppHost><NoWarn>0649</NoWarn></PropertyGroup></Project>')
dotnet run --project (Join-Path $probe 'Probe.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Installation content recovery checks failed.' }

$restore = Read-Member $placement 'private MapObject RestoreEditedInstallation('
$snapshotRestore = $restore.IndexOf('if (restoredObject is IPersistentInstallationItemStorage restoredItemStorage)')
$blockRestore = $restore.IndexOf('ApplyEditedInstallationBlockStates(editSession, anchorCoordinate, quarterTurns);')
$runtimeRestore = Read-Member $restore 'if (!reusesUnchangedRuntimeState)'
$runtimeRestoreEnd = $restore.IndexOf($runtimeRestore) + $runtimeRestore.Length
if ($snapshotRestore -lt 0 -or $snapshotRestore -gt $blockRestore -or
    $snapshotRestore -lt $runtimeRestoreEnd) {
    throw 'Stored item restoration must run outside the unchanged-transform optimization.'
}
$finalize = Read-Member $placement 'private void FinalizePackedInstallationHistory()'
if ($finalize.IndexOf('HasPendingStoredInstallationItems(packedSession)') -gt $finalize.IndexOf('ReleaseInstalledObjectInstance(') -or
    !$finalize.Contains('packedInstallationHistory.Push(packedSession)')) {
    throw 'Packing must retain unreturned items instead of pooling their owner.'
}
Write-Host 'Packing release and unchanged vehicle undo restoration contracts passed.'

$node = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe'
& $node (Join-Path $PSScriptRoot 'TowerLayoutChecks.cjs') $root
if ($LASTEXITCODE -ne 0) { throw 'Tower prefab I/O layout checks failed.' }
