$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Read-Member([string]$file, [string]$signature, [int]$occurrence = 1) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $file))
    $start = -1
    for ($i = 0; $i -lt $occurrence; $i++) {
        $start = $source.IndexOf($signature, $start + 1, [StringComparison]::Ordinal)
        if ($start -lt 0) { throw "Missing production member: $signature" }
    }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $source.Substring($start, $end - $start)
}
$base = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/'
$integrationChecks = @(
    [pscustomobject]@{ File = 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.PipeSplit.cs'; Text = 'ConnectPipeSplitThroughSteamGenerator(' },
    [pscustomobject]@{ File = $base + 'Pipe.cs'; Text = 'TryGetSteamGeneratorPipePassAtRuntimeCoordinate(' },
    [pscustomobject]@{ File = $base + 'InputOutputModule.cs'; Text = 'TryGetSteamGeneratorPipePassAtRuntimeCoordinate(' }
)
foreach ($check in $integrationChecks) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $check.File))
    if ($source.IndexOf($check.Text, [StringComparison]::Ordinal) -lt 0) {
        throw "Missing production steam-pass integration: $($check.File) :: $($check.Text)"
    }
}
$forbiddenIntegrationChecks = @(
    [pscustomobject]@{ File = $base + 'Pipe.cs'; Text = 'GetObjectInfoFluidPressureConsumptionLitersPerSecond(' },
    [pscustomobject]@{ File = $base + 'InputOutputModule.cs'; Text = 'fluidConsumptionRateMeter' }
)
foreach ($check in $forbiddenIntegrationChecks) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $check.File))
    if ($source.IndexOf($check.Text, [StringComparison]::Ordinal) -ge 0) {
        throw "Pipe pressure must describe connected source pressure, not remaining capacity: $($check.File) :: $($check.Text)"
    }
}
$steamGeneratorSource = $base + 'SteamGenerator.cs'
foreach ($member in @(
    'protected override bool ShouldKeepRuntimeUpdateTickActive()',
    'public bool TryGetAvailableElectricOutputRate(',
    'public void CaptureFacilityFlow(')) {
    $memberSource = Read-Member $steamGeneratorSource $member
    if ($memberSource.Contains('IsInDirectedBoilerSteamChain(')) {
        throw "Stored steam consumption must not depend on a transient directed-chain cache: $member"
    }
}
$generated = "using System; using System.Collections.Generic; using UnityEngine; public partial class InputOutputModule {`n"
$generated += (Read-Member ($base + 'InputOutputModule.cs') 'private readonly struct FluidOutputConnection') + "`n"
$generated += (Read-Member ($base + 'InputOutputModule.cs') 'private readonly struct DirectedSteamPort') + "`n"
foreach ($member in @(
    'protected bool TryEmitFluidOutputToConnectedStorages(',
    'private bool TrySelectFluidOutputStorageWithAnySpaceFromCache(',
    'private bool TrySelectFluidOutputConnectionWithAnySpaceFromCache(',
    'private bool CanUseFluidOutputStorageWithAnySpace(',
    'private static float GetFluidStorageFillRatio(',
    'private bool EnqueueFluidStoragePipePassCoordinatesAt(',
    'private static void AddDirectedBoilerSteamChains(',
    'private static void EnqueueDirectedSteamPort(',
    'private static void TryAppendDirectedSteamGeneratorAtPort(',
    'private static void EnqueueDirectedSteamPipesAtPort(')) {
    $generated += (Read-Member ($base + 'InputOutputModule.cs') $member) + "`n"
}
$generated += "} public partial class Boiler {`n"
foreach ($member in @('public bool TryGetRuntimeWaterPass(', 'private bool IsWaterStorageFull(')) {
    $generated += (Read-Member ($base + 'Boiler.cs') $member) + "`n"
}
$generated += "} public partial class SteamGenerator { private const float FluidEpsilon = .0001f;`n"
$generated += (Read-Member ($base + 'SteamGenerator.cs') 'internal static bool IsDirectedSteamPortConnection(') + "`n"
$generated += (Read-Member ($base + 'SteamGenerator.cs') 'internal static bool TryResolveSteamPassPipeConnectionDirection(') + "`n"
$generated += (Read-Member ($base + 'SteamGenerator.cs') 'internal static float ResolveGenerationOutputScale(') + "`n}"
$generated += " public partial class SteamTrain {`n"
$generated += (Read-Member ($base + 'Vehicle/SteamTrain.cs') 'private bool TryGetActivePipeAtCoordinate(') + "`n}"
$probeDir = Join-Path $env:TEMP ('ProjectF-BoilerWaterPass-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probeDir
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
