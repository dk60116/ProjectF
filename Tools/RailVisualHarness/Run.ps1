$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-RailVisual-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$root = Join-Path $repo 'FactorioProject/Assets/Scripts'
foreach ($file in @('RailVisualNetwork.cs', 'RailVisualStyle.cs', 'RailBallastMask.cs')) {
    Copy-Item -LiteralPath (Join-Path $root "Simulation/Railway/$file") -Destination $probe
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
function Read-Member([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1; $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $source.Substring($start, $end - $start)
}
$rail = [IO.File]::ReadAllText((Join-Path $root 'Object/MapObj/InstallationObject/Railload.cs'))
$mesh = "using System.Collections.Generic; using UnityEngine; internal class Railload {`n"
foreach ($signature in @('internal static void AppendContinuousRails(', 'internal static void AppendContinuousSleeper(',
    'private static void AddRailStrips(', 'private static void AddRailStrip(', 'private static void AddRailStripCap(',
    'private static void AddRailSleepers(', 'private static void AddSleeperBox(',
    'private static float CalculateFlatPathLength(', 'private static bool TrySampleFlatPath(',
    'private static float ResolveSleeperTopHeight(', 'private static Vector3 ResolvePathTangent(', 'private static Vector3 SampleRailTangentProbe(',
    'private static Vector3 ResolveFlatSide(', 'private static void AddQuad(', 'private static void AddQuadVertices(')) {
    $mesh += (Read-Member $rail $signature) + "`n"
}
$mesh += 'internal static void PreviewSleepers(List<Vector3> vertices, List<int> indices, IReadOnlyList<Vector3> path) => AddRailSleepers(vertices, indices, path, .035f, .84f, .16f, .55f, .08f); }'
[IO.File]::WriteAllText((Join-Path $probe 'ProductionMesh.cs'), $mesh)
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><LangVersion>9.0</LangVersion></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$view = [IO.File]::ReadAllText((Join-Path $root 'Simulation/Railway/RailWorldView.cs'))
if (!$view.Contains('visualNetwork.Rebuild()') -or !$view.Contains('UpdateContinuousGeometry();') -or
    !$view.Contains('Railload.AppendContinuousRails(') -or !$view.Contains('Railload.AppendContinuousSleeper(') -or
    $view.Contains('AppendDataVisual(') -or $rail.Contains('AppendDataVisual(')) {
    throw 'Installed rendering must use the shared visual network without the old per-source mesh path.'
}
Write-Output 'PASS installed mesh adapter uses continuous runs and shared sleeper positions (source contract)'
$surface = [IO.File]::ReadAllText((Join-Path $root 'Map/TerrainGenerator.Surface.cs'))
$world = [IO.File]::ReadAllText((Join-Path $root 'Simulation/Railway/RailWorld.cs'))
$renderer = [IO.File]::ReadAllText((Join-Path $root 'Simulation/Railway/RailBallastTerrainRenderer.cs'))
if (!$surface.Contains('RailBallastRenderer?.GetProperties(chunk.coordinate)') -or !$surface.Contains('matProps = properties') -or
    !$surface.Contains('subMeshIndex == GeneratedSurfaceWaterRenderSubMeshIndex ? null') -or
    !$view.Contains('ballast.Upsert(rail, rail.Path.Samples') -or !$view.Contains('ballast.Refresh()') -or
    !$view.Contains('ballast.Remove(rail)') -or !$view.Contains('ballast?.Dispose()') -or
    !$world.Contains('presentation?.UpsertNative(rail, path)') -or !$world.Contains('presentation?.RemoveNative(rail)') -or
    $view.Contains('cell.Ballast') -or $rail.Contains('ballastMesh') -or $surface.Contains('GetGeneratedLandSurfaceMaxY')) {
    throw 'Native/data rail masks must reach terrain draws, update on demolition, release textures and exclude water; obsolete gravel geometry must be removed.'
}
if (!$renderer.Contains('Textures/RailBallast') -or !$renderer.Contains('if (!mask.Rebuild()) return;') -or
    $renderer.Contains('RenderMesh(') -or $renderer.Contains('new Mesh')) { throw 'Gravel presentation must cache terrain properties without submitting geometry.' }
foreach ($file in @('TerrainBiomeBlend.shader', 'WorldTriplanar.shader')) {
    $shader = [IO.File]::ReadAllText((Join-Path $repo ('FactorioProject/Assets/Shaders/' + $file)))
    if (!$shader.Contains('#include "RailBallastTerrain.hlsl"') -or !$shader.Contains('ApplyRailBallastTerrain(') -or
        !$shader.Contains('_RailBallastEnabled("Rail Ballast Enabled", Float) = 0')) {
        throw "Land shader must apply gravel before lighting: $file"
    }
}
$texture = Join-Path $repo 'FactorioProject/Assets/Resources/Textures/RailBallast.png'
$meta = [IO.File]::ReadAllText($texture + '.meta')
if (!(Test-Path -LiteralPath $texture) -or !$meta.Contains('enableMipMap: 1') -or !$meta.Contains('wrapU: 0') -or !$meta.Contains('wrapV: 0')) {
    throw 'Runtime gravel texture must exist in Resources with repeating UVs and mipmaps.'
}
Write-Output 'PASS native/data masks, terrain albedo integration, water exclusion, texture lifetime and removal of old gravel meshes'
