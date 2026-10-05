$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scripts = Join-Path $repo 'FactorioProject/Assets/Scripts'
foreach ($asset in @('MapObject/Logging machine/Logging machine.prefab', 'MapObject/InputOutputModule/Seed Planter/Seed Planter.prefab')) {
    # Unity text serialization has tagged documents; tie the collider to the script's root GameObject.
    $text = [IO.File]::ReadAllText((Join-Path $repo ('FactorioProject/Assets/' + $asset)))
    $documents = [regex]::Split($text, '(?m)^--- ')
    $script = $documents | Where-Object { $_ -match 'm_EditorClassIdentifier: Assembly-CSharp::(LoggingMachine|SeedPlanter)' }
    if (@($script).Count -ne 1) { throw "Cannot resolve forestry root script in $asset" }
    $root = [regex]::Match($script, 'm_GameObject: \{fileID: (-?\d+)\}').Groups[1].Value
    $collider = $documents | Where-Object { $_ -match '(?m)^SphereCollider:' -and $_.Contains('m_GameObject: {fileID: ' + $root + '}') }
    if (@($collider).Count -ne 1) { throw "Forestry root collider contract changed in $asset; extend runtime and probe shape support together" }
}
Write-Output 'PASS two production forestry prefab root sphere-collider contracts'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ForestryEcs-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
foreach ($relative in @('Map/ForestryWorld.cs', 'Map/ForestryInstance.cs', 'Map/LoggingMachineInstance.cs',
    'Map/SeedPlanterInstance.cs', 'Map/LoggingFilter.cs', 'Map/LoggingHarvest.cs', 'Map/FacilityFuel.cs',
    'Map/ResourceStateSlots.cs', 'Map/IForestryTarget.cs', 'Map/IDataItemProducer.cs', 'Map/IDataElectricConsumer.cs',
    'Map/FacilityRuntimeWakeRegistry.cs', 'Simulation/Core/ForestryProcess.cs', 'Simulation/Core/PipeConnections.cs',
    'Object/MapObj/InstallationObject/LoggingMachine.cs', 'Object/MapObj/InstallationObject/SeedPlanter.cs')) {
    Copy-Item -LiteralPath (Join-Path $scripts $relative) -Destination $probe
}
# Only Unity native geometry is substituted. Entity state, scheduling calls, IO and harvesting are production code.
$path = Join-Path $probe 'ForestryInstance.cs'
$source = [IO.File]::ReadAllText($path).Replace('Matrix4x4.TRS(WorldPosition, WorldRotation, template.Scale)', 'Matrix4x4.Translate(WorldPosition)')
[IO.File]::WriteAllText($path, $source)
$path = Join-Path $probe 'ForestryWorld.cs'
$source = [IO.File]::ReadAllText($path).Replace('members[i].CullBounds.IntersectRay(ray, out float d)', 'HarnessEngine.IntersectRay(members[i].CullBounds, ray, out float d)')
[IO.File]::WriteAllText($path, $source)
$path = Join-Path $probe 'SeedPlanterInstance.cs'
$source = [IO.File]::ReadAllText($path).Replace('Debug.LogError(', 'HarnessEngine.LogError(')
[IO.File]::WriteAllText($path, $source)
$source = [IO.File]::ReadAllText((Join-Path $scripts 'Simulation/Core/SimulationTickContracts.cs'))
$start = $source.IndexOf('public static class DeterministicSimulationUnits')
$depth = 1; $end = $source.IndexOf('{', $start) + 1
while ($depth -gt 0) { if ($source[$end] -eq '{') { $depth++ }; if ($source[$end] -eq '}') { $depth-- }; $end++ }
[IO.File]::WriteAllText((Join-Path $probe 'Units.cs'), "using System; using UnityEngine;`n" + $source.Substring($start, $end - $start).Replace('ProjectF.Simulation.SimulationTickWorld.', 'MapObjectTickManager.'))
$source = [IO.File]::ReadAllText((Join-Path $scripts 'Rendering/CameraRenderCulling.cs'))
$start = $source.IndexOf('    internal struct SpatialRayCellTraversal')
$end = $source.IndexOf('    // View-only state.', $start)
[IO.File]::WriteAllText((Join-Path $probe 'SpatialRayCellTraversal.cs'), "using UnityEngine; namespace ProjectF.Rendering {`n" + $source.Substring($start, $end - $start) + "`n}")
foreach ($name in @('Boundaries.cs', 'Checks.cs')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $probe }
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0414;0649</NoWarn></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
