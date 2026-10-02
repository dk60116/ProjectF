$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

function Read-Member([string]$path, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $repositoryRoot $path))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    return $source.Substring($start, $end - $start)
}

$base = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/'
$fixture = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Fixture.cs'))
$fixture = $fixture.Replace('// INSTALLATION_RETENTION',
    (Read-Member ($base + 'InstallationObject.cs') 'protected static float CalculateFluidPressureRetention('))
$fixture = $fixture.Replace('// MODULE_RETENTION',
    (Read-Member ($base + 'InputOutputModule.cs') 'protected float ResolveFluidOutputTransportRetentionAtCoordinate('))
$fixture = $fixture.Replace('// PUMP_RATIO', (Read-Member ($base + 'InputOutputModule.cs') 'private static float ResolvePumpTransportRatio('))
$fixture = $fixture.Replace('// PUMP_LIMIT', (Read-Member ($base + '../Pump.cs') 'internal static float LimitTransportRate('))
$fixture = $fixture.Replace('// MODULE_PRESSURE',
    (Read-Member ($base + 'InputOutputModule.cs') 'public virtual float GetObjectInfoFluidPressureLitersPerSecond('))
$fixture = $fixture.Replace('// MODULE_DEMAND',
    (Read-Member ($base + 'InputOutputModule.cs') 'public virtual bool TryGetElectricPowerDemand('))

$fixture = $fixture.Replace('// REFINERY_OUTPUT_STATE',
    (Read-Member ($base + 'InputOutputModule.cs') 'public sealed class RefineryOutputState'))
$fixture = $fixture.Replace('// ADVANCE_CRAFT',
    (Read-Member ($base + 'InputOutputModule.cs') 'protected void UpdateActiveCraft('))
$refinery = [IO.File]::ReadAllText((Join-Path $repositoryRoot ($base + 'CrudeOilRefinery.cs')))
$fixture = $fixture.Replace('// REFINERY_IMPLEMENTATION', $refinery.Substring($refinery.IndexOf('public class CrudeOilRefinery')))
$fixture += [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Checks.cs'))

$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-RefineryTransport-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $fixture
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'FactorioProject/Assets/Scripts/Simulation/Core/ProductionProcess.cs') -Destination (Join-Path $probeDir 'ProductionProcess.cs')
$units = (Read-Member 'FactorioProject/Assets/Scripts/Simulation/Core/SimulationTickContracts.cs' 'public static class DeterministicSimulationUnits')
Set-Content -LiteralPath (Join-Path $probeDir 'Units.cs') -Value ('using System; using UnityEngine; namespace ProjectF.Simulation { public static class SimulationTickWorld { public const int DefaultSimulationTicksPerSecond = 60; public const float FixedSimulationDeltaSeconds = 1f / 60f; } }' + $units)
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>'
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
