param([switch]$BeforeFix)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$root = 'FactorioProject/Assets/Scripts'
$moduleFile = "$root/Object/MapObj/InstallationObject/InputOutputModule.cs"
$module = [IO.File]::ReadAllText((Join-Path $repo $moduleFile))
$placement = [IO.File]::ReadAllText((Join-Path $repo "$root/Object/MapObj/InstallationObject/InstallationPlacementController.cs"))
$world = [IO.File]::ReadAllText((Join-Path $repo "$root/Map/ProductionWorld.cs"))
function Member([string]$text, [string]$signature) {
    $start = $text.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $text.IndexOf('{', $start) + 1; $depth = 1
    while ($depth -gt 0 -and $end -lt $text.Length) {
        if ($text[$end] -eq '{') { $depth++ }
        if ($text[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced member: $signature" }
    $text.Substring($start, $end - $start)
}
$generated = "using System; using System.Collections.Generic; using UnityEngine; public partial class InputOutputModule {`n"
foreach ($signature in @('public enum RectGridBlockType', 'public static Vector2Int RotateRectGridOffset(',
    'public static bool TryGetModuleAtRuntimeRectGridBlockType(',
    'public static bool CoordinateIsRuntimeInputEnergyBlock(', 'public static bool CoordinateIsRuntimeInputItemBlock(',
    'public static bool CoordinateIsRuntimeOutputBlock(', 'public static bool CoordinateAllowsRuntimePipeBlock(',
    'public static bool AllowsDirectAreaInteraction(', 'public static bool AllowsPipeAreaInteraction(')) {
    $generated += (Member $module $signature) + "`n"
}
$querySource = if ($BeforeFix) { (git -C $repo show "HEAD:$moduleFile") -join "`n" } else { $module }
$generated += (Member $querySource 'public static bool CoordinateIsRuntimeRectGridBlockType(') + "`n}`n"
$generated += "public partial class InstallationPlacementController {`n"
foreach ($signature in @('private bool CanPlacePreviewOnTargetBlockType(', 'private static bool IsDuplicatePipePlacement(',
    'private static bool IsNormalInputOutputAreaBlockType(', 'private static bool CanPlaceInstallationOnInputOutputAreaBlock(',
    'private bool TryResolveTrainStationSelectedRailCoordinate(', 'private bool IsRailloadAllowedForPlacement(',
    'private static bool IsInstallationObjectAllowedForPlacement(')) {
    $generated += (Member $placement $signature) + "`n"
}
$generated += "}`npublic partial class ProductionWorld {`n" + (Member $world 'public bool CoordinateIsBlockType(') + "`n}`n"
$station = [IO.File]::ReadAllText((Join-Path $repo "$root/Object/MapObj/InstallationObject/Train station.cs"))
$generated += "public partial class Trainstation {`n" + (Member $station 'public static bool TryGetFacingDirection(') + "`n}`n"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-PipeArea-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AreaChecks.cs') -Destination $probe
$diesel = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/MapObject/Train/Diesel train/Diesel Train.prefab'))
$filter = [regex]::Match($diesel, '(?m)^  mapFilter: (\d+)')
$center = [regex]::Match($diesel, '(?m)^    centerCellY: (\d+)')
if (!$filter.Success -or !$center.Success) { throw 'Missing diesel placement settings' }
[IO.File]::WriteAllText((Join-Path $probe 'DieselAsset.cs'), ('static class DieselAsset { public const int MapFilter = ' + $filter.Groups[1].Value + '; public const int CenterCellY = ' + $center.Groups[1].Value + '; }'))
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
