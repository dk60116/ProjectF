$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Member([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) { if ($source[$end] -eq '{') { $depth++ }; if ($source[$end] -eq '}') { $depth-- }; $end++ }
    return $source.Substring($start, $end - $start)
}
$batch = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/VirtualRenderBatcher.cs'))
$portable = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/PortableObject.cs'))
$renderer = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/PortableItemRenderer.cs'))
$source = $batch.Replace('public sealed class VirtualRenderBatchCollection', 'public sealed partial class VirtualRenderBatchCollection')
# Test-only instrumentation of the actual range-copy helper; no runtime counter is added.
$source = $source.Replace('        destination.Clear();', "        HarnessMetrics.RangeCopyCalls++;`n        destination.Clear();")
$source += (Member $renderer 'public readonly struct ConveyorItemGpuMotionData') + "`n"
$source += (Member $portable 'internal static class SleepAwakeDebugVisual') + "`n"
$source += (Member $portable 'internal static class BeltItemLineDebugVisual') + "`n"
$legacyClass = Member $source 'public sealed partial class VirtualRenderBatchCollection'
$legacyClass = $legacyClass.Replace('VirtualRenderBatchCollection', 'LegacyVirtualRenderBatchCollection')
$legacyClass = $legacyClass.Replace((Member $legacyClass 'private MaterialPropertyBlock ResolveBatchPropertyBlock('), '')
$legacyClass = $legacyClass.Replace('public List<DrawPropertyBlockCache> DrawPropertyBlocks;', 'public List<DrawPropertyBlockCache> DrawPropertyBlocks; public MaterialPropertyBlock PropertyBlock;')
$source += $legacyClass + "`n"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-BatchProperties-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $source)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LegacyResolveBatchPropertyBlock.cs') -Destination $probe
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
