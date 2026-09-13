$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$productionPath = Join-Path $repo 'FactorioProject/Assets/Scripts/Map/BlockStateStore.MapObjectItemClear.cs'
$production = [IO.File]::ReadAllText($productionPath)

function Read-Member([string]$signature, [int]$occurrence = 0) {
    $start = -1
    $searchFrom = 0
    for ($i = 0; $i -le $occurrence; $i++) {
        $start = $production.IndexOf($signature, $searchFrom, [StringComparison]::Ordinal)
        if ($start -lt 0) { throw "Missing production member: $signature occurrence=$occurrence" }
        $searchFrom = $start + $signature.Length
    }
    $end = $production.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $production.Length) {
        if ($production[$end] -eq '{') { $depth++ }
        if ($production[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $production.Substring($start, $end - $start)
}

$source = "using System; using System.Collections.Generic;`npublic partial class BlockStateStore {`n"
$source += Read-Member 'public struct MapObjectItemClearResult'
$source += Read-Member 'private static void ClearSavedMapObjectItems('
$source += Read-Member 'private static bool PreservesStoredItemDuringMapObjectClear(' 0
$source += Read-Member 'private static bool PreservesStoredItemDuringMapObjectClear(' 1
$source += Read-Member 'private static void CountInputOutputState('
$source += Read-Member 'private static bool HasProductionState('
$source += Read-Member 'private static int CountValidPersistentItemIds('
$source += @'
public static MapObjectItemClearResult ProbeClear(InstallationSaveState state, bool countBeforeClear)
{
    MapObjectItemClearResult result = default;
    ClearSavedMapObjectItems(state, countBeforeClear, ref result);
    return result;
}
public static bool ProbePreserveLive(InstallationObject installation) =>
    PreservesStoredItemDuringMapObjectClear(installation);
}
'@

$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-MapObjectItemClear-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $source)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
