$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Member([string]$file, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $file))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        elseif ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end-$start)
}
$renderer = 'FactorioProject/Assets/Scripts/Map/PortableItemRenderer.cs'
$generated = "using System; using System.Collections.Generic; using ProjectF.Rendering; using ProjectF.Conveyors; using UnityEngine;`n"
foreach ($signature in @('public readonly struct ConveyorItemGpuMotionData', 'public readonly struct VirtualConveyorItemRenderData')) {
    $generated += (Member $renderer $signature) + "`n"
}
$generated += "public partial class Block {`n"
foreach ($signature in @('internal void AppendDynamicVirtualConveyorItemRenderData(', 'internal BeltItemVisualPath CaptureBeltJobVisualPath(')) {
    $generated += (Member 'FactorioProject/Assets/Scripts/Map/Block.ConveyorJobs.cs' $signature) + "`n"
}
$generated += "}`n"
$generated += "public partial class PortableItemRenderer {`n"
foreach ($signature in @('private int SyncDynamicVirtualConveyorBlockRenderItems(',
    'private bool TryUpdateDynamicVirtualConveyorRenderItem(', 'private int RebuildDynamicVirtualConveyorBlockRenderItems(',
    'private bool TryGetResolvedDynamicVirtualConveyorRenderData(', 'private bool TryGetCachedDynamicVirtualConveyorBatchKey(',
    'private bool TryGetDynamicVirtualConveyorBatchKey(', 'private sealed class DynamicBlockRenderCache',
    'private readonly struct DynamicItemRenderKeyCache', 'private void ResolveBatchCell(', 'private static int GetBatchCell(',
    'private static Vector3 ExtractWorldPosition(')) {
    $generated += (Member $renderer $signature) + "`n"
}
$generated += "}`n"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-BeltItemRender-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
$paths = @('FactorioProject/Assets/Scripts/Rendering/BeltItemVisualPath.cs', 'FactorioProject/Assets/Scripts/Rendering/ConveyorItemTransformJobProcessor.cs')
$compile = ($paths | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape((Join-Path $repo $_)) + '" />' }) -join "`n"
$core = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
$math = Join-Path $repo 'FactorioProject/Library/ScriptAssemblies/Unity.Mathematics.dll'
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><TieredCompilation>false</TieredCompilation><NoWarn>0436;0649</NoWarn></PropertyGroup><ItemGroup>' + $compile +
    '<Reference Include="UnityEngine.CoreModule"><HintPath>' + $core + '</HintPath></Reference><Reference Include="Unity.Mathematics"><HintPath>' + $math + '</HintPath></Reference></ItemGroup></Project>'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), $project)
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
