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

$rail = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Railload.cs'
$shader = 'FactorioProject/Assets/Shaders/ToonCharacter.shader'
$railSource = [IO.File]::ReadAllText((Join-Path $repo $rail))
$shaderSource = [IO.File]::ReadAllText((Join-Path $repo $shader))
if (!$shaderSource.Contains('_UseWorldUpLighting("Use World Up Lighting", Float) = 0')) {
    throw 'The shared toon shader must keep world-up lighting disabled by default'
}
if (!$shaderSource.Contains('saturate(_UseWorldUpLighting)')) {
    throw 'The toon lighting normal must use the world-up material option'
}

$generated = "using System.Collections.Generic; using UnityEngine; using UnityEngine.Rendering;`npublic partial class Railload {`n"
foreach ($name in @('ToonCharacterShaderName', 'RailVisualColor', 'SleeperVisualColor', 'railRuntimeMaterial', 'sleeperRuntimeMaterial')) {
    $declaration = [regex]::Match($railSource, '(?m)^\s*private [^\r\n;]*\b' + $name + '\b[^\r\n;]*;')
    if (!$declaration.Success) { throw "Missing production field: $name" }
    $generated += $declaration.Value + "`n"
}
foreach ($signature in @('private static Material ResolveRailRuntimeMaterial(', 'private static Material ResolveSleeperRuntimeMaterial(',
    'private static Material CreateToonRuntimeMaterial(', 'private static void SetMaterialColor(', 'private static void SetMaterialFloat(')) {
    $generated += (Read-Member $rail $signature) + "`n"
}
$generated += "public static Material RailMaterial => ResolveRailRuntimeMaterial(); public static Material SleeperMaterial => ResolveSleeperRuntimeMaterial();`n"
$generated += "public static void ResetMaterials() { railRuntimeMaterial = null; sleeperRuntimeMaterial = null; }`n"
foreach ($signature in @('public void RefreshRailVisual()', 'internal void AppendDataVisual(')) {
    if (!(Read-Member $rail $signature).Contains('AppendVisualGeometry(')) {
        throw 'Native rails, ECS rails and blueprints must share rail and sleeper geometry generation.'
    }
}
foreach ($name in @('RailEndpointCellHalfExtent', 'railLineWidth', 'railHalfSpacing', 'railVisualThickness',
    'sleeperLength', 'sleeperWidth', 'sleeperSpacing', 'sleeperVisualThickness', 'DefaultRailVisualThickness')) {
    $declaration = [regex]::Match($railSource, '(?m)^\s*private [^\r\n;]*\b' + $name + '\b[^\r\n;]*;')
    if (!$declaration.Success) { throw "Missing production field: $name" }
    $generated += $declaration.Value + "`n"
}
foreach ($signature in @('internal void AppendPlacementPreviewMesh(', 'private void AppendVisualGeometry(',
    'private static void BuildVisualCenterPath(', 'private static Vector3 VisualPathPointToLocal(',
    'private static void ExtendPathEndpointsToCellEdges(', 'private static Vector3 FlattenDirection(',
    'private static float ResolveCellEdgeExtension(Vector3', 'private static void AddPathPoint(',
    'private static void AddRailStrips(', 'private static void AddRailStrip(', 'private static void AddRailStripCap(',
    'private static Vector3 ResolvePathTangent(', 'private static void AddRailSleepers(',
    'private static float CalculateFlatPathLength(', 'private static bool TrySampleFlatPath(', 'private static float ResolveSleeperTopHeight(')) {
    $generated += (Read-Member $rail $signature) + "`n"
}
$generated += Read-Member $rail 'private static Vector3 ResolveFlatSide('
$generated += Read-Member $rail 'private static void AddSleeperBox('
$generated += Read-Member $rail 'private static void AddQuad('
$generated += Read-Member $rail 'private static void AddQuadVertices('
$generated += "`npublic static void BuildSleeper(List<Vector3> vertices, List<int> triangles, Vector3 center, Vector3 tangent) => AddSleeperBox(vertices, triangles, center, tangent, 1.2f, 0.28f, 0.12f);`n}"

$controller = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/RailloadInstallationController.cs'
$controllerSource = [IO.File]::ReadAllText((Join-Path $repo $controller))
$generated += "`npublic partial class RailloadInstallationController {`n"
foreach ($name in @('PreviewRailHeight', 'ValidRailColor', 'InvalidRailColor')) {
    $generated += [regex]::Match($controllerSource, '(?m)^\s*private [^\r\n;]*\b' + $name + '\b[^\r\n;]*;').Value + "`n"
}
foreach ($signature in @('private sealed class RailPathPlan', 'private void RefreshPreviewMesh(',
    'private void SetPreviewVisible(', 'private static void ApplyMesh(', 'private static void ApplyMaterialColor(')) {
    $generated += (Read-Member $controller $signature) + "`n"
}
$generated += "}`n"

$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-RailLighting-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PreviewChecks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
