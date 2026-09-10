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
$manager = 'FactorioProject/Assets/Scripts/Manager/MapObjectTickManager.cs'
$generated = "using System; using System.Collections.Generic;`n"
$generated += (Read-Member $manager 'public static class DeterministicSimulationUnits') + "`n"
$generated += "public partial class InputOutputModule {`n"
foreach ($signature in @(
    'protected void RecordFluidNetworkOutput(',
    'public float GetObjectInfoFluidOutputLitersPerSecond(',
    'public static void AppendFluidOutputSourcesAtCoordinate(',
    'protected bool TryEmitFluidOutputToConnectedStorages(')) {
    $generated += (Read-Member ($base + 'InputOutputModule.cs') $signature) + "`n"
}
$generated += "}`npublic partial class Pump {`n"
foreach ($signature in @('private void ProduceWater(', 'private void RefreshWaterOutputBudget(')) {
    $generated += (Read-Member ($base + 'Pump.cs') $signature) + "`n"
}
$generated += "}`npublic partial class Pipe {`n"
$generated += (Read-Member ($base + 'Pipe.cs') 'public bool TryGetObjectInfoFluidInfo(' 2) + "`n"
foreach ($signature in @('private bool TrySearchFluidNetwork(', 'private void EnqueueObjectInfoFluidSearchCoordinate(')) {
    $generated += (Read-Member ($base + 'Pipe.cs') $signature) + "`n"
}
$generated += "}`n"
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-PipeFlow-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
$checksPath = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Checks.cs'))
$meterPath = [Security.SecurityElement]::Escape((Join-Path $repo ($base + 'FluidOutputRateMeter.cs')))
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0414;0649</NoWarn></PropertyGroup><ItemGroup><Compile Include="' + $checksPath + '" /><Compile Include="' + $meterPath + '" /></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
