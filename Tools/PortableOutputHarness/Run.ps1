$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Member([string]$path, [string]$signature) {
    $text = [IO.File]::ReadAllText((Join-Path $repo $path))
    $start = $text.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing member: $signature" }
    $brace = $text.IndexOf('{', $start)
    $semicolon = $text.IndexOf(';', $start)
    if ($semicolon -ge 0 -and ($brace -lt 0 -or $semicolon -lt $brace)) {
        return $text.Substring($start, $semicolon - $start + 1)
    }
    $end = $brace + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($text[$end] -eq '{') { $depth++ }
        elseif ($text[$end] -eq '}') { $depth-- }
        $end++
    }
    return $text.Substring($start, $end - $start)
}
$portable = 'FactorioProject/Assets/Scripts/Object/PortableObject.cs'
$block = 'FactorioProject/Assets/Scripts/Map/Block.cs'
$generated = "using System; using System.Collections.Generic; using UnityEngine; using ProjectF.Simulation; using ProjectF.Rendering; using Transform = EngineTransform; using Time = TestClock; using DroppedItemPickupGate = Gate; public partial class PortableObject {`n"
foreach ($signature in @('public void SetConveyorOwnership(', 'public void SetSleepAwakeSleeping(',
    'public const float MoveToDuration =', 'public void SetCachedParent(',
    'private PortableObjectComponent Read()', 'private void Write(', 'private void SetMoving(',
    'public void SetWorldPosition(', 'public void SetWorldPose(', 'public void SetWorldScale(',
    'public void SetLocalPose(', 'public void SetCachedActive(', 'public void SetBatchedRendering(',
    'public void SetVisualRenderingSuppressed(', 'internal void MoveToBlockStack(', 'internal void SampleScheduledMoveNow()', 'private void StartMove(',
    'internal bool UpdateScheduledMove(', 'private void CancelScheduledMove()',
    'private void CompleteMoveImmediately(', 'private Vector3 ResolveMoveTarget(', 'internal bool CanCullMoveIntermediateUpdates(')) {
    $member = Member $portable $signature
    # Instrument only the math boundary to prove culled updates never interpolate.
    if ($signature -eq 'internal bool UpdateScheduledMove(') {
        $member = $member.Replace('Vector3.LerpUnclamped(', 'InterpolationProbe.LerpUnclamped(')
    }
    $generated += $member + "`n"
}
$generated += "}`npublic partial class PortableMoveScheduler {`n"
foreach ($signature in @('internal sealed class MoveState', 'internal MoveState Schedule(', 'private void RemoveAndPool(',
    'internal bool ShouldSkipIntermediateUpdate(', 'private static Bounds CalculateMoveCullBounds(', 'private void RecordCulledUpdate()')) {
    $generated += (Member $portable $signature) + "`n"
}
$generated += "}`npublic partial class Block {`n"
foreach ($signature in @('internal Vector3 GetItemStackPlacementPosition(', 'internal void CompleteItemStackPlacement(',
    'public bool CanAddInputAreaCenterObjects(int count, int itemId)', 'public int GetInputAreaCenterItemCount(',
    'public int GetInputAreaCenterItemId()', 'public bool TryConsumeOneInputAreaCenterObject(', 'public int ConsumeInputAreaCenterObjects(',
    'public int CountFloorObjects(', 'public int RemoveFloorObjects(', 'public List<int> CaptureFloorObjectState()',
    'private int ResolveFloorStackCapacity(', 'private int ResolveInputAreaCenterCapacity(', 'public int GetInputAreaCenterCapacity(',
    'public bool HasVirtualizableFloorObjectState()')) {
    $generated += (Member $block $signature) + "`n"
}
$generated += "}`n"
$generated += "public partial class TerrainGenerator {`n"
foreach ($signature in @('private readonly HashSet<Vector2Int> workableAreaCoordinateScratch', 'private readonly List<Block> workableAreaBlockScratch')) {
    $generated += (Member 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.Items.cs' $signature) + "`n"
}
foreach ($signature in @('public int GetDroppedItemCountAround(', 'public int RemoveDroppedItemsAround(',
    'public int GetWorkableAreaItemCount(', 'public int RemoveWorkableAreaItems(', 'private static bool IsWorkableOutputStack(',
    'private void CollectWorkableAreaBlocks(', 'private static int CompareWorkableAreaBlocks(',
    'private static bool IsInsideSquareRadius(')) {
    $generated += (Member 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.Items.cs' $signature) + "`n"
}
$generated += "}`n"
$generated += (Member 'FactorioProject/Assets/Scripts/Map/Block.DeferredOutputs.cs' 'public partial class Block') + "`n"
$generated += "namespace ProjectF.Diagnostics {`n" + (Member 'FactorioProject/Assets/Scripts/Map/Block.DeferredOutputs.cs' 'internal static class DeferredOutputTiming') + "`n}`n"
$generated += (Member 'FactorioProject/Assets/Scripts/Map/PortableItemRenderer.DeferredOutputs.cs' 'public sealed partial class PortableItemRenderer') + "`n"
$generated += "public static partial class InputOutputModule {`n" + (Member 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs' 'internal static bool TryEmitOutputItemToBlock(') + "`n}`n"
$generated += "public static partial class SavedCapacityProbe {`n" + (Member 'FactorioProject/Assets/Scripts/Map/BlockStateStore.FloorAreaItems.cs' 'private static int ResolveSavedCenterStackCapacity(') + "`n}`n"
$io = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs'
$generated += "public partial class IoCapacityProbe {`n"
foreach ($signature in @('public int ResolveRuntimeAreaCapacity(', 'private int ResolveRuntimeBlockCenterCapacity(',
    'private static int ResolveItemStackCapacity(', 'protected readonly struct RuntimeAreaOutputTarget',
    'protected bool TryResolveOutputTarget(')) {
    $generated += (Member $io $signature) + "`n"
}
$generated += "}`n"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-PortableOutput-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'ProductionMembers.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Map/PortableObjectWorld.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DeferredChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WorkableChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Simulation/Core/OutputStackBatch.cs') -Destination $probe
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
