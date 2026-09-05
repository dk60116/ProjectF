param([string]$UnityManagedDirectory = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
# Roslyn can compile a source that Unity ignores because its .meta is malformed.
# Check the actual script assets before generating the managed probe.
$scriptFiles = Get-ChildItem -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts') -Recurse -File -Filter '*.cs'
$scriptGuids = @{}
foreach ($scriptFile in $scriptFiles) {
    $metaPath = $scriptFile.FullName + '.meta'
    if (-not (Test-Path -LiteralPath $metaPath)) { throw "Missing script metadata: $metaPath" }
    $guidLines = [regex]::Matches([IO.File]::ReadAllText($metaPath), '(?m)^guid:[^\r\n]*')
    if ($guidLines.Count -ne 1 -or $guidLines[0].Value -cnotmatch '^guid: [0-9a-f]{32}$') {
        throw "Invalid script GUID (expected 32 hexadecimal characters): $metaPath"
    }
    $assetGuid = $guidLines[0].Value.Substring(6)
    if ($scriptGuids.ContainsKey($assetGuid)) { throw "Duplicate script GUID: $metaPath and $($scriptGuids[$assetGuid])" }
    $scriptGuids[$assetGuid] = $metaPath
}
Write-Output "PASS: $($scriptFiles.Count) Unity script metadata files have valid, unique GUIDs."
function Read-Member([string]$file, [string]$signature) {
    $source = Get-Content -LiteralPath (Join-Path $repo $file) -Raw
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced member: $signature" }
    $source.Substring($start, $end - $start)
}
$itemFile = 'FactorioProject/Assets/Scripts/Map/PortableItemRenderer.cs'
$beltFile = 'FactorioProject/Assets/Scripts/Map/VirtualConveyorBeltRenderer.cs'
$batchFile = 'FactorioProject/Assets/Scripts/Map/VirtualRenderBatcher.cs'
$backendFile = 'FactorioProject/Assets/Scripts/Rendering/VirtualRenderBatchRendererGroupBackend.cs'
$generated = "using System.Collections.Generic; using UnityEngine; using UnityEngine.Rendering;`n"
$generated += "public sealed partial class PortableItemRenderer {`n"
foreach ($signature in @('private void RefreshVirtualConveyorBlockRenderCache(', 'private BlockRenderCache GetOrCreateVirtualConveyorBlockRenderCache(', 'private void RemoveVirtualConveyorBlockRenderCache(', 'private Bounds CreateDynamicVirtualConveyorBlockCullBounds(')) {
    $generated += (Read-Member $itemFile $signature) + "`n"
}
$generated += "}`n"
$generated += (Read-Member $beltFile 'public readonly struct VirtualConveyorBeltRenderData') + "`n"
$generated += "public partial class BeltProbe {`n"
foreach ($signature in @('private void AddBeltRenderData(', 'private void ClearBeltRenderCache(', 'private int SyncTrackedTransformMatrices()', 'private int SyncTrackedTransformMatrices(BeltRenderCache', 'private sealed class TrackedBeltCell', 'private sealed class BeltRenderCache', 'private struct TrackedTransformEntry')) {
    $generated += (Read-Member $beltFile $signature) + "`n"
}
$generated += "}`npublic partial class VirtualRenderBatchCollection {`n"
$generated += (Read-Member $batchFile 'internal static Bounds CalculateWorldBounds(Mesh') + "`n"
$generated += (Read-Member $batchFile 'internal static Bounds CalculateWorldBounds(Bounds') + "`n}"
$generated += "`npublic partial class BackendProbe {`n"
foreach ($signature in @('public void BeginSync()', 'public void Deactivate(', 'public void EndSync()')) {
    $generated += (Read-Member $backendFile $signature) + "`n"
}
$generated += "}`n"
$animalFile = 'FactorioProject/Assets/Scripts/Object/Animal/Animal.cs'
$animalAiFile = 'FactorioProject/Assets/Scripts/Object/Animal/AnimalAIController.cs'
$generated += "public partial class AnimalAnimationProbe {`n"
foreach ($signature in @('internal void SetBehaviorAnimationActive(', 'public void SetAIAnimation(', 'private void PlayDeathAnimation()', 'private void OnDisable()')) {
    $generated += (Read-Member $animalFile $signature) + "`n"
}
$generated += "}`n" + (Read-Member $animalAiFile 'public enum AnimalAIState') + "`npublic partial class AnimalControllerProbe {`n"
foreach ($signature in @('public void SetBehaviorExecutionActive(', 'private void SyncBehaviorAnimationActivity()', 'private void ApplyAnimation(')) {
    $generated += (Read-Member $animalAiFile $signature) + "`n"
}
$generated += "}`n"
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-CameraCull-' + [Guid]::NewGuid().ToString('N'))
$generated += "public partial class PlayerCameraCullingProbe {`n" + (Read-Member 'FactorioProject/Assets/Scripts/Character/Player/PlayerCamera.cs' 'private void RefreshPlayerCullingView()') + "`n}`n"
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'ProductionMembers.cs') -Value $generated
$files = @(
    (Join-Path $PSScriptRoot 'Checks.cs'),
    (Join-Path $PSScriptRoot 'WorldChecks.cs'),
    (Join-Path $PSScriptRoot 'AnimalAnimationChecks.cs'),
    (Join-Path $PSScriptRoot 'FreeCameraChecks.cs'),
    (Join-Path $repo 'FactorioProject/Assets/Scripts/Rendering/CameraRenderCulling.cs'),
    (Join-Path $repo 'FactorioProject/Assets/Scripts/Rendering/WorldCameraCulling.cs'),
    (Join-Path $repo 'FactorioProject/Assets/Scripts/Map/PortableItemRenderer.CameraCulling.cs'))
$compile = ($files | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }) -join "`n"
$reference = [Security.SecurityElement]::Escape((Join-Path $UnityManagedDirectory 'UnityEngine.CoreModule.dll'))
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0436;0649</NoWarn></PropertyGroup><ItemGroup>' + $compile + '<Reference Include="UnityEngine.CoreModule"><HintPath>' + $reference + '</HintPath></Reference></ItemGroup></Project>'
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value $project
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
