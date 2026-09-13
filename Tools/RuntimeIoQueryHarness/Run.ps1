$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$text = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs'))
function Member([string]$signature) {
    $start = $text.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing member: $signature" }
    $end = $text.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($text[$end] -eq '{') { $depth++ }
        if ($text[$end] -eq '}') { $depth-- }
        $end++
    }
    $text.Substring($start, $end - $start)
}
$source = "using System; using System.Collections.Generic; using UnityEngine; public partial class InputOutputModule {`n"
$source += "private delegate bool RuntimeCoordinateValueCollector<T>(InputOutputModule module, Vector2Int coordinate, ISet<T> values);`n"
foreach ($field in [regex]::Matches($text, 'private static readonly RuntimeCoordinateValueCollector<[^>]+> [^;]+;')) {
    $source += $field.Value + "`n"
}
foreach ($signature in @(
    'public static bool TryGetOutputItemIdsAtRuntimeGridCoordinate(',
    'public static bool TryGetAcceptedInputItemIdsAtRuntimeGridCoordinate(',
    'public static bool TryGetInputEnergyTypesAtRuntimeGridCoordinate(',
    'private static bool TryGetRuntimeCoordinateValues<T>(',
    'private static bool TryAppendRuntimeOutputItemIds(',
    'private static bool TryAppendRuntimeInputItemIds(',
    'private static bool TryAppendAcceptedRuntimeInputItemIds(',
    'private static bool TryAppendRuntimeInputEnergyTypes(',
    'public static bool RuntimeOutputCoordinateProducesItemId(',
    'public static bool CanAddItemToRuntimeIoOverlapCoordinate(',
    'public static bool TryGetRuntimeIoOverlapAllowedItemIds(',
    'private static bool OutputItemMatchesEnergyTypes(',
    'private void RegisterRuntimeAreaCoordinates()',
    'private void RegisterRuntimeAreaCoordinates(IReadOnlyList',
    'private void RegisterRuntimeInputItemAreaCoordinates()',
    'private void UnregisterRuntimeAreaCoordinates()',
    'private void UnregisterRuntimeAreaCoordinates(IReadOnlyList',
    'private void UnregisterRuntimeInputItemAreaCoordinates()',
    'private static void RegisterRuntimeCoordinate(',
    'private static void UnregisterRuntimeCoordinate('
)) { $source += (Member $signature) + "`n" }
$source += '}'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-RuntimeIoQuery-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $source)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
