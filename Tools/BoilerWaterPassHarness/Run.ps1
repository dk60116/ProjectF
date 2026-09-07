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
$generated = "using System.Collections.Generic; using UnityEngine; public partial class Pump {`n"
foreach ($member in @('private bool TryRouteWaterToFluidStorage(', 'private void EnqueueFluidSearchCoordinate(', 'private bool TryGetFluidNetworkConnectionAtCoordinate(', 'private void ConsiderFluidStorageCandidate(', 'private bool TryUseFluidStorage(')) {
    $generated += (Read-Member ($base + 'Pump.cs') $member) + "`n"
}
$generated += "} public partial class Boiler {`n"
foreach ($member in @('public bool TryGetRuntimeWaterPass(', 'private bool IsWaterStorageFull(', 'private bool TryHeatWater(')) {
    $generated += (Read-Member ($base + 'Boiler.cs') $member) + "`n"
}
$generated += "} public partial class InputOutputModule {`n"
$generated += (Read-Member ($base + 'InputOutputModule.cs') 'private bool EnqueueFluidStoragePipePassCoordinatesAt(') + "`n}"
$probeDir = Join-Path $env:TEMP ('ProjectF-BoilerWaterPass-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probeDir
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
