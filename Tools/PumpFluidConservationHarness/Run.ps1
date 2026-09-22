$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$base = Join-Path $root 'FactorioProject/Assets/Scripts/Object/MapObj/'
function Member([string]$file, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $base $file))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$generated = "using System; using System.Collections.Generic; public partial class InputOutputModule {`n"
foreach ($signature in @(
 'private readonly struct FluidOutputConnection',
 'protected sealed class FluidTransferPreview',
 'private float PreviewFluidTransfer(',
 'private float LimitFluidOutputTransfer(',
 'protected bool TryTransferFluidFromStorageToConnectedStorage(',
 'private bool TryAddFluidToOutputConnection(',
 'protected bool TryGetConnectedFluidInputAvailableLitersAtCoordinate(',
 'protected bool TryGetFluidOutputAvailableLitersAtCoordinate('
)) { $generated += (Member 'InstallationObject/InputOutputModule.cs' $signature) + "`n" }
$generated += "} public partial class InstallationObject {`n"
$generated += (Member "InstallationObject/InstallationObject.cs" "internal void RestoreUnacceptedFluid(") + "`n"
$generated += "} public partial class Pump {`n"
foreach ($signature in @('internal float LimitTransferVolume(', 'internal void RecordTransferredVolume(')) {
 $generated += (Member 'Pump.cs' $signature) + "`n"
}
$generated += "}"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-PumpConservation-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
Set-Content -LiteralPath (Join-Path $probe 'Production.cs') -Value $generated
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Set-Content -LiteralPath (Join-Path $probe 'Probe.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>'
dotnet run --project (Join-Path $probe 'Probe.csproj') --configuration Release
exit $LASTEXITCODE
