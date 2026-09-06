$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Member([string]$path, [string]$signature) {
    $text = [IO.File]::ReadAllText((Join-Path $repo $path))
    $start = $text.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing member: $signature" }
    $end = $text.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) { if ($text[$end] -eq '{') { $depth++ }; if ($text[$end] -eq '}') { $depth-- }; $end++ }
    return $text.Substring($start, $end - $start)
}
$arm = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/RobotArm.cs'
$io = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs'
$serializer = 'FactorioProject/Assets/Scripts/Manager/SaveGameBinarySerializer.cs'
$source = "using System; using System.IO; using System.Collections.Generic; using UnityEngine; public partial class RobotArm {`n"
foreach ($member in @('public enum RobotArmState', 'public sealed class TransferState', 'private bool EnsureInteractionCoordinateCache(', 'private bool TryResolvePickupCoordinate(', 'private bool TryResolveDropCoordinate(', 'private void InvalidateInteractionCoordinateCache(', 'public bool TryCollectTransferItemIds(', 'private void RefreshRegisteredWakeCoordinates(', 'private void RegisterWakeCoordinatesAround(', 'private void RegisterWakeCoordinate(', 'private void UnregisterWakeCoordinates(')) {
    $source += (Member $arm $member) + "`n"
}
$source += "} public partial class InputOutputModule {`n"
foreach ($member in @('public enum SlotLayoutType', 'public enum RectGridBlockType', 'public struct RectGridBlockPlacement', 'public bool TryGetRectGridPlacementCoordinate(', 'public static Vector2Int RotateRectGridOffset(', 'public static bool IsFluidItemDefinition(')) { $source += (Member $io $member) + "`n" }
$source += "} public static partial class Checks {`n"
foreach ($member in @('private static void WriteRobotArmState(', 'private static RobotArm.TransferState ReadRobotArmState(')) { $source += (Member $serializer $member) + "`n" }
$source += "}`n"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-RobotIO-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $source)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj') -- $repo
exit $LASTEXITCODE
