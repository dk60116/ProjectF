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
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $source.Substring($start, $end - $start)
}

$loggingFile = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/LoggingMachine.cs'
$loggingSource = [IO.File]::ReadAllText((Join-Path $repo $loggingFile))
if ($loggingSource.Contains('CanDropTreeLogs') -or $loggingSource.Contains('No output space')) {
    throw 'Logging output space must not gate tree cutting.'
}
$generated = "using UnityEngine;`npublic partial class LoggingMachine {`n"
foreach ($signature in @(
    'public static Vector2Int GetHarvestCoordinate(',
    'private bool HasAnyAdjacentTree()',
    'private bool TryResolveAdjacentTree(',
    'private bool IsHarvestableTree(',
    'public void SetGrowthRange(',
    'private static int NormalizeDirectionIndex(')) {
    $generated += (Read-Member $loggingFile $signature) + "`n"
}
$generated += "}`n"

$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-LoggingTarget-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
$checksPath = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Checks.cs'))
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup><Compile Include="' + $checksPath + '" /></ItemGroup></Project>'
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value $project
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
