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
$fixture = $fixture.Replace('// REFINERY_PRESSURE',
    (Read-Member ($base + 'CrudeOilRefinery.cs') 'public override float GetObjectInfoFluidPressureLitersPerSecond('))

$fixture = $fixture.Replace('// REFINERY_TICK',
    (Read-Member ($base + 'CrudeOilRefinery.cs') 'private void UpdateContinuousRefining('))
$fixture = $fixture.Replace('// REFINERY_DEMAND',
    (Read-Member ($base + 'CrudeOilRefinery.cs') 'public override bool TryGetElectricPowerDemand('))
$fixture = $fixture.Replace('// REFINERY_CAPACITY',
    (Read-Member ($base + 'CrudeOilRefinery.cs') 'private float GetInputBufferCapacityLiters('))
$fixture = $fixture.Replace('// REFINERY_OPERATIONAL_PRESSURE',
    (Read-Member ($base + 'CrudeOilRefinery.cs') 'private float GetOperationalInputPressure('))
$fixture = $fixture.Replace('// REFINERY_STARTUP_VOLUME',
    (Read-Member ($base + 'CrudeOilRefinery.cs') 'private float GetStartupInputLiters('))
$fixture = $fixture.Replace('// REFINERY_RECORD_DELIVERY',
    (Read-Member ($base + 'CrudeOilRefinery.cs') 'private static void RecordInputDelivery('))
$fixture = $fixture.Replace('// REFINERY_DIRECT_PRESSURE',
    (Read-Member ($base + 'CrudeOilRefinery.cs') 'private float GetDirectInputPressure('))

$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-RefineryTransport-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $fixture
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>'
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
