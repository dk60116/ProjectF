param([switch]$FluidBoundary, [switch]$FurnaceIntegration)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ProductionEcs-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
foreach ($relative in @('Simulation/Core/ProductionProcess.cs', 'Simulation/Core/MiningProcess.cs',
    'Simulation/Core/SimulationTickContracts.cs', 'Map/ProductionFacilityInstance.cs', 'Map/ProductionFacilityInstance.Info.cs',
    'Map/ResourceStateSlots.cs', 'Map/MiningItemOutput.cs', 'Map/IDataItemProducer.cs', 'Map/IDataElectricConsumer.cs', 'Map/IProductionFacilityInfo.cs')) {
    Copy-Item -LiteralPath (Join-Path $repo ('FactorioProject/Assets/Scripts/' + $relative)) -Destination $probe
}
# Matrix4x4.TRS is native to Unity. Replace only that engine boundary, never the production state logic.
$entity = Join-Path $probe 'ProductionFacilityInstance.cs'
$source = [IO.File]::ReadAllText($entity).Replace('Matrix4x4.TRS(WorldPosition, WorldRotation, Template.Scale)', 'Matrix4x4.identity')
[IO.File]::WriteAllText($entity, $source)
$boundary = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../MiningEcsHarness/BoundaryStubs.cs'))
foreach ($name in @('ItemDefinition', 'Block', 'BoxObject', 'TerrainGenerator', 'VirtualObjectWorld')) {
    $boundary = $boundary.Replace("public class $name`n", "public partial class $name`n").Replace("public class $name`r`n", "public partial class $name`r`n")
    $boundary = $boundary.Replace("public class $name {", "public partial class $name {")
    $boundary = $boundary.Replace("public class $name :", "public partial class $name :")
}
if ($FluidBoundary -or $FurnaceIntegration) { $boundary = $boundary.Replace('public partial class InputOutputModule', 'public partial class InputOutputModule : InstallationObject') }
if ($FurnaceIntegration) {
    $boundary = [regex]::Replace($boundary, 'public static class MapObjectTickManager[^\r\n]*', '')
    $boundary = [regex]::Replace($boundary, 'public interface IMapObjectUpdateTickDeadline[^\r\n]*', '')
    $boundary = [regex]::Replace($boundary, 'namespace ProjectF.Simulation\s*\{\s*public static class SimulationTickWorld.*?\}\s*\}', '', 'Singleline')
    $boundary = [regex]::Replace($boundary, 'public static class FacilitySimulationWorld\s*\{.*?\r?\n\}', '', 'Singleline')
    $boundary = $boundary.Replace('public static class UtilityPole', 'public static partial class UtilityPole')
    $boundary = $boundary.Replace('SuccessfulEmitsBeforeFailure--; block.Count++; block.Item = item; return true;', 'SuccessfulEmitsBeforeFailure--; block.Count++; block.Item = item; PublishOutputMutation(); return true;')
    foreach ($relative in @('Map/FacilitySimulationWorld.cs', 'Simulation/Core/SimulationTickWorld.cs',
        'Simulation/Core/FacilityFlowBatch.cs', 'Simulation/Core/FacilityFlowStateWorld.cs')) {
        $path = Join-Path $repo ('FactorioProject/Assets/Scripts/' + $relative)
        $content = [IO.File]::ReadAllText($path).Replace('Application.isPlaying', 'FurnaceHarnessHost.IsPlaying')
        [IO.File]::WriteAllText((Join-Path $probe ([IO.Path]::GetFileName($path))), $content)
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FurnaceBoundaries.cs') -Destination $probe
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FurnaceChecks.cs') -Destination $probe
}
[IO.File]::WriteAllText((Join-Path $probe 'SharedBoundaries.cs'), $boundary)
if (!$FluidBoundary -and !$FurnaceIntegration) {
    $sharedPath = Join-Path $probe 'SharedBoundaries.cs'
    $sharedText = [IO.File]::ReadAllText($sharedPath).Replace('public interface IMapObjectTarget { }',
        'public interface IMapObjectTarget { MapObject SceneObject => null; int ResolveItemId() => -1; }')
    [IO.File]::WriteAllText($sharedPath, $sharedText)
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FilterBoundaries.cs') -Destination $probe
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Boundaries.cs') -Destination $probe
if ($FluidBoundary) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FluidChecks.cs') -Destination $probe
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FluidBoundaries.cs') -Destination $probe
    Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Map/ProductionWorld.Fluid.cs') -Destination $probe
} elseif (!$FurnaceIntegration) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe }
function Member([string]$relative, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $repo ('FactorioProject/Assets/Scripts/' + $relative)))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1; $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }; if ($source[$end] -eq '}') { $depth-- }; $end++
    }
    $source.Substring($start, $end - $start)
}
$generated = "using System; using System.IO; using System.Collections.Generic; using UnityEngine; using ProjectF.Simulation; public partial class InputOutputModule {`n"
foreach ($signature in @('public struct PersistentInputItemAreaState', 'public sealed class RefineryOutputState', 'public sealed class PersistentState',
    'public static void WakeRuntimeModulesAtCoordinate(Vector2Int coordinate)',
    'public static void WakeRuntimeOutputModulesAtCoordinate(Vector2Int coordinate)',
    'private static void WakeRuntimeModulesAtCoordinate(Vector2Int coordinate, bool outputOnly)')) {
    $generated += (Member 'Object/MapObj/InstallationObject/InputOutputModule.cs' $signature) + "`n"
}
$generated += "}`ninternal partial class ProductionRenderTemplate {`n" + (Member 'Map/ProductionRenderTemplate.cs' 'internal sealed class Recipe') + "`n}`n"
$generated += "public partial class ProductionWorld {`n" + (Member 'Map/ProductionWorld.cs' 'internal struct State') + "`n" + (Member 'Map/ProductionWorld.cs' 'private void Observe(') + "`n" + (Member 'Map/ProductionWorld.cs' 'public void Wake(Vector2Int coordinate)') + "`n}`n"
if ($FurnaceIntegration) {
    $generated += "public partial class ProductionWorld {`n" + (Member 'Map/ProductionWorld.cs' 'public ProductionFacilityInstance Register(') + "`n" + (Member 'Map/ProductionWorld.cs' 'internal void Bind(') + "`n" + (Member 'Map/ProductionWorld.cs' 'internal void RefreshRecipeAvailability()') + "`n" + (Member 'Map/ProductionWorld.cs' 'public bool AppendInputItemIds(') + "`n}`n"
    $generated += "public partial class InputOutputModule {`n" + (Member 'Object/MapObj/InstallationObject/InputOutputModule.cs' 'public struct ItemIoEntry') + "`n" + (Member 'Object/MapObj/InstallationObject/InputOutputModule.cs' 'public sealed class InputOutputPair') + "`n" + (Member 'Object/MapObj/InstallationObject/InputOutputModule.cs' 'public static float ResolveCompleteEnergy(') + "`n" + (Member 'Object/MapObj/InstallationObject/InputOutputModule.cs' 'protected static bool RequiresOperationalEnergy(') + "`n}`n"
    # Use the exact production recipe-building block; only mesh/collider/particle setup is omitted.
    $templateSource = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/ProductionRenderTemplate.cs'))
    $recipeStart = $templateSource.IndexOf('        var recipes = new List<Recipe>();', [StringComparison]::Ordinal)
    $recipeEnd = $templateSource.IndexOf('        foreach (var placement', $recipeStart, [StringComparison]::Ordinal)
    if ($recipeStart -lt 0 -or $recipeEnd -lt 0) { throw 'Missing production recipe-building boundary' }
    $generated += "internal partial class ProductionRenderTemplate { internal void BuildRecipes(InputOutputModule source) {`n" + $templateSource.Substring($recipeStart, $recipeEnd - $recipeStart) + "`n} }`n"
}
$generated += "public partial class ProductionMachine {`n" + (Member 'Object/MapObj/InstallationObject/ProductionMachine.cs' 'public readonly struct FluidGaugeState') + "`n}`n"
if (!$FluidBoundary -and !$FurnaceIntegration) {
    $generated += "public static partial class MapObjectTargetExtensions {`n" + (Member 'Map/IMapObjectTarget.cs' 'public static bool TryGetProductionTargetSelection(') + "`n}`n"
    $generated += "public partial class PlayerFilterProbe {`n" + (Member 'Character/Player/PlayerController.cs' 'private static bool SupportsItemFilter(') + "`n" + (Member 'Character/Player/PlayerController.cs' 'private static bool IsItemFilterEnabled(') + "`n}`n"
    $generated += "public partial class FilterSelectUI {`n" + (Member 'HUD/ItemSlot/FilterSelectUI.cs' 'private bool TryApplyProductionTargetSelection(') + "`n}`n"
}
$generated += "public static class SaveProbe { public const int MaxSerializedListCount = 1000000;`n"
foreach ($signature in @('private static void WriteInputOutputState(', 'private static InputOutputModule.PersistentState ReadInputOutputState(',
    'private static void WriteVector2Int(', 'private static Vector2Int ReadVector2Int(', 'private static void WriteVector2IntList(',
    'private static List<Vector2Int> ReadVector2IntList(', 'private static void WriteInputItemAreaList(',
    'private static List<InputOutputModule.PersistentInputItemAreaState> ReadInputItemAreaList(',
    'private static void WriteIntList(', 'private static List<int> ReadIntList(', 'private static void WriteLongList(',
    'private static List<long> ReadLongList(', 'private static void WriteList<T>(', 'private static List<T> ReadList<T>(')) {
    $generated += (Member 'Manager/SaveGameBinarySerializer.cs' $signature) + "`n"
}
$generated += @'
public static InputOutputModule.PersistentState Roundtrip(InputOutputModule.PersistentState state) {
    using var memory = new MemoryStream(); using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true)) WriteInputOutputState(writer, state);
    memory.Position = 0; using var reader = new BinaryReader(memory); var restored = ReadInputOutputState(reader, 70);
    if (memory.Position != memory.Length) throw new Exception("Unexpected trailing save bytes"); return restored;
}
}
'@
[IO.File]::WriteAllText((Join-Path $probe 'ExtractedProduction.cs'), $generated)
$constants = if ($FluidBoundary) { '<DefineConstants>PRODUCTION_FLUID_BRIDGE</DefineConstants>' } elseif ($FurnaceIntegration) { '<DefineConstants>FURNACE_INTEGRATION</DefineConstants>' } else { '' }
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649;0169</NoWarn>' + $constants + '</PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj') -- $repo
exit $LASTEXITCODE
