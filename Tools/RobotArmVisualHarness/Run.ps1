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
$rendererPath = 'FactorioProject/Assets/Scripts/Map/PortableItemRenderer.cs'
$renderer = [IO.File]::ReadAllText((Join-Path $repo $rendererPath))
$arm = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/RobotArm.cs'))
$portable = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/PortableObject.cs'))
$batch = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/VirtualRenderBatcher.cs'))
$source = "using System; using System.Collections.Generic; using UnityEngine; using UnityEngine.Rendering;`n"
$source += (Member $batch 'public readonly struct VirtualRenderBatchKey') + "`n"
$source += "public partial class PortableItemRenderer {`n"
foreach ($member in @('public void Register(', 'public void Unregister(', 'public void MarkDirty(', 'public void RequestPortableObjectRenderDataRefresh(', 'private void LateUpdate(', 'private void RebuildPortableObjectBatches(', 'private bool RefreshPortableObjectRenderSnapshots(', 'private PortableObjectRenderSnapshot ReadPortableObjectRenderSnapshot(', 'private readonly struct PortableObjectRenderSnapshot', 'private void RenderPortableObjectBatches(', 'private bool HasPortableObjectRenderWork(')) {
    $source += (Member $renderer $member) + "`n"
}
$source += "}`n"
$source += "public partial class RobotArm {`n" + (Member $arm 'internal void AppendInstancedRenderData(') + "`n" + (Member $arm 'private void RefreshHeldItemVisualIfNeeded(') + "`n"
$source += [regex]::Match($arm, '(?m)^\s*private System.Predicate<int> PickupItemFilter[^\r\n]+').Value + "`n"
$source += (Member $arm 'private bool AcceptsPickupItem(') + "`n}"
if ($arm -match 'AcceptsPickupItem\s*,') { throw 'An uncached pickup filter method group remains.' }
$source += "public partial class PortableObject {`n" + (Member $portable 'public void RequestBatchedRenderDataRefresh(') + "`n}"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-RobotVisual-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $source)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LegacyPortableItemRenderer.cs') -Destination $probe
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
