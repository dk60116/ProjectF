$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$base = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject'
function Read-Member([string]$file, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $base $file))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    return $source.Substring($start, $end - $start)
}
$generated = "using System; using System.Collections.Generic;`npublic partial class Pipe {`n"
foreach ($signature in @(
    'private readonly struct ObjectInfoFluidSearchNode',
    'internal bool TryGetObjectInfoFluidInfoAtCoordinate(',
    'private bool TrySearchFluidNetwork(',
    'private void AppendObjectInfoFluidOutputSourcesAtCoordinate(',
    'private void EnqueueObjectInfoFluidSearchCoordinate(',
    'private static int FreezeObjectInfoPressureDistance(',
    'private static int AddObjectInfoPipeDistance(',
    'private static bool IsBetterObjectInfoPressureDistance(',
    'private static bool IsObjectInfoPressureDistanceFrozen(',
    'private static int ResolveObjectInfoPressureDistance(',
    'public static int AddRemoteTraversalPipeDistance(')) {
    $generated += (Read-Member 'Pipe.cs' $signature) + "`n"
}
$generated += "} public partial class InputOutputModule {`n"
foreach ($signature in @(
    'public static void AppendFluidOutputSourcesAtCoordinate(',
    'internal static bool TryGetSteamGeneratorPipePassAtRuntimeCoordinate(',
    'private static void SelectSteamGeneratorPipePass(')) {
    $generated += (Read-Member 'InputOutputModule.cs' $signature) + "`n"
}
# Include the new edge resolver when present so this harness also runs on the
# unfixed revision and demonstrates its numerical pressure failure.
$moduleSource = [IO.File]::ReadAllText((Join-Path $base 'InputOutputModule.cs'))
if ($moduleSource.Contains('internal static bool TryGetOverlappingSteamSourcePort(')) {
    $generated += (Read-Member 'InputOutputModule.cs' 'internal static bool TryGetOverlappingSteamSourcePort(') + "`n"
}
$generated += "} public partial class SteamGenerator {`n"
foreach ($signature in @(
    'public bool TryGetRuntimeSteamPass(',
    'public bool CanReceiveSteamFromDirectedPortAtRuntime(',
    'internal static bool IsDirectedSteamPortConnection(')) {
    $generated += (Read-Member 'SteamGenerator.cs' $signature) + "`n"
}
$generated += "}"
$worldSource = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/PipeWorld.cs'))
$recordStart = $worldSource.IndexOf('    public bool TryGetObjectInfoFluidInfo(', [StringComparison]::Ordinal)
$recordEnd = $worldSource.IndexOf('    public bool TryGetConnectedFluidItemIdIgnoringStorageCoordinate(', $recordStart, [StringComparison]::Ordinal)
if ($recordStart -lt 0 -or $recordEnd -lt 0) { throw 'Missing runtime pipe object-info entry point' }
$generated += "`npublic partial class PipeRuntimeRecord {`n" + $worldSource.Substring($recordStart, $recordEnd - $recordStart) + "}`n"
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-SteamPressure-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
$checks = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Checks.cs'))
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup><Compile Include="' + $checks + '" /></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
