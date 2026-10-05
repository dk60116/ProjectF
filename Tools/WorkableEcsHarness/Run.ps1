$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-WorkableEcs-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
foreach ($relative in @('Map/IWorkableTarget.cs', 'Map/WorkableRangeIndex.cs', 'Map/WorkableWorld.cs', 'Map/WorkableInstance.cs', 'Map/ResourceStateSlots.cs', 'Map/TerrainGenerator.Workables.cs')) {
    Copy-Item -LiteralPath (Join-Path $repo ('FactorioProject/Assets/Scripts/' + $relative)) -Destination $probe
}
# Substitute Unity native math only; entity, index, binding and lifetime code are unchanged.
$entityPath = Join-Path $probe 'WorkableInstance.cs'
$source = [IO.File]::ReadAllText($entityPath).Replace('Matrix4x4.TRS(WorldPosition, WorldRotation, template.Scale)', 'Matrix4x4.Translate(WorldPosition)')
[IO.File]::WriteAllText($entityPath, $source)
$worldPath = Join-Path $probe 'WorkableWorld.cs'
$source = [IO.File]::ReadAllText($worldPath).Replace('members[i].CullBounds.IntersectRay(ray, out float d)', 'HarnessEngine.IntersectRay(members[i].CullBounds, ray, out float d)')
[IO.File]::WriteAllText($worldPath, $source)
$source = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Rendering/CameraRenderCulling.cs'))
$start = $source.IndexOf('    internal struct SpatialRayCellTraversal')
$end = $source.IndexOf('    // View-only state.', $start)
[IO.File]::WriteAllText((Join-Path $probe 'SpatialRayCellTraversal.cs'), "using UnityEngine; namespace ProjectF.Rendering {`n" + $source.Substring($start, $end - $start) + "`n}")
$source = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InstallationPlacementController.cs'))
$start = $source.IndexOf('    private bool TryMaterializeDataOnlyWorkableForEditing(')
$end = $source.IndexOf('    private bool TryMaterializeDataOnlyBuildingForEditing(', $start)
[IO.File]::WriteAllText((Join-Path $probe 'EditorMembers.cs'), "using UnityEngine; using ProjectF.MapObjects; public partial class InstallationPlacementController {`n" + $source.Substring($start, $end - $start) + "`n}")
foreach ($name in @('Boundaries.cs', 'Checks.cs')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $probe
}
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
