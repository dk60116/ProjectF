$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Read-Member([string]$file, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $file))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$scripts = 'FactorioProject/Assets/Scripts/'
$loadingScreenSource = [IO.File]::ReadAllText((Join-Path $repo ($scripts + 'UI/GameSceneLoadingScreen.cs')))
foreach ($requiredContract in @(
    'LatestSceneLoadRequest',
    'screen = active;',
    'sceneRequests.HasPending',
    'SaveManager.DiscardPendingRuntimeLoadForSceneReplacement()',
    'SceneManager.GetActiveScene().name == GameSceneName'
)) {
    if (!$loadingScreenSource.Contains($requiredContract, [StringComparison]::Ordinal)) {
        throw "Missing replaceable scene-load contract: $requiredContract"
    }
}
if ($loadingScreenSource.Contains('if (loadStarted)', [StringComparison]::Ordinal)) {
    throw 'Loading screen still rejects replacement scene requests after the first load starts'
}
$generated = "using System; using System.IO; using System.IO.Compression; using System.Text; using UnityEngine;`npublic partial class TerrainGenerator {`n"
foreach ($signature in @('private void BeginWorldFinalization(', 'private void TryFinalizePendingWorldLoad()',
    'private void FailWorldRestoration(', 'private void ClearWorldRestoreReferences()')) {
    $generated += (Read-Member ($scripts + 'Map/TerrainGenerator.cs') $signature) + "`n"
}
$generated += "}`npublic partial class SaveManager {`n"
$generated += (Read-Member ($scripts + 'Manager/SaveManager.cs') 'private static SaveGameData CaptureSaveData(') + "`n}"
$generated += "`npublic static partial class MapObjectTickManager {`n"
$generated += (Read-Member ($scripts + 'Manager/MapObjectTickManager.cs') 'public static bool WaitingForWorldLoad') + "`n}"
$saveManagerSource = [IO.File]::ReadAllText((Join-Path $repo ($scripts + 'Manager/SaveManager.cs')))
$reloadMemberStart = $saveManagerSource.IndexOf('private bool StartSceneReloadForSlot(', [StringComparison]::Ordinal)
$reloadRequestIndex = $saveManagerSource.IndexOf('GameSceneLoadingScreen.TryLoadSceneAsync(', $reloadMemberStart, [StringComparison]::Ordinal)
$pendingPublishIndex = $saveManagerSource.IndexOf('pendingRuntimeLoadSlot = slotIndex;', $reloadMemberStart, [StringComparison]::Ordinal)
if ($reloadMemberStart -lt 0 -or $reloadRequestIndex -lt 0 -or $pendingPublishIndex -lt $reloadRequestIndex) {
    throw 'Save slot payload must be published only after the replacement scene request is accepted'
}
if (!$saveManagerSource.Contains('DiscardPendingRuntimeLoadForSceneReplacement()', [StringComparison]::Ordinal)) {
    throw 'SaveManager cannot discard a superseded pending slot payload'
}
Write-Output 'PASS replaceable loading-screen and pending-slot publication contracts'
$loadingProperty = [regex]::Match($saveManagerSource, 'public bool IsLoading =>[\s\S]+?;')
if (!$loadingProperty.Success) { throw 'Missing SaveManager load lifecycle property' }
$generated += "`npublic partial class SaveManager {`n" + $loadingProperty.Value + "`n}"
$generated += "`npublic static partial class SaveGameBinarySerializer {`n"
$generated += (Read-Member ($scripts + 'Manager/SaveGameBinarySerializer.cs') 'public static void WriteToFile(') + "`n}"
$probeDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-WorldPersistence-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDirectory | Out-Null
[IO.File]::WriteAllText((Join-Path $probeDirectory 'Extracted.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $repo ($scripts + 'Map/TerrainChunkStreamingScheduler.cs')) -Destination $probeDirectory
Copy-Item -Path (Join-Path $repo ($scripts + 'Simulation/Core/*.cs')) -Destination $probeDirectory
foreach ($file in @('Checks.cs', 'BoundaryStubs.cs')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $probeDirectory }
[IO.File]::WriteAllText((Join-Path $probeDirectory 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0414</NoWarn><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDirectory 'Probe.csproj')
exit $LASTEXITCODE
