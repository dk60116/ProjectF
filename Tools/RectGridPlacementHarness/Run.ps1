$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$editorSourcePath = Join-Path $repo 'FactorioProject/Assets/Editor/ItemDataEditorWindow.cs'
$editorSource = [IO.File]::ReadAllText($editorSourcePath)

function Read-Member([string]$signature) {
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

function Read-EditorMember([string]$signature) {
    $start = $editorSource.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing editor member: $signature" }
    $end = $editorSource.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $editorSource.Length) {
        if ($editorSource[$end] -eq '{') { $depth++ }
        if ($editorSource[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced editor member: $signature" }
    $editorSource.Substring($start, $end - $start)
}

$generated = @'
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class InputOutputModule
{
    private SlotLayoutType slotLayoutType = SlotLayoutType.RectGrid;
    private int rectGridWidth = 5;
    private int rectGridHeight = 5;
    private bool rectGridPlacementDataInitialized;
    private List<RectGridBlockPlacement> rectGridPlacements = new List<RectGridBlockPlacement>();

    private void EnsureRectGridData() { }
    private int GetMaxObjectBlockCount() => rectGridWidth * rectGridHeight;

    public IReadOnlyList<RectGridBlockPlacement> Snapshot()
    {
        rectGridPlacementDataInitialized = false;
        EnsureRectGridPlacementData();
        return rectGridPlacements;
    }

    public IReadOnlyList<RectGridBlockPlacement> RectGridPlacements => Snapshot();

    public void LoadRaw(params RectGridBlockPlacement[] placements)
    {
        rectGridPlacements = new List<RectGridBlockPlacement>(placements);
        rectGridPlacementDataInitialized = false;
}
'@

foreach ($signature in @(
    'public enum SlotLayoutType',
    'public enum RectGridBlockType',
    'public struct RectGridBlockPlacement',
    'public void SetRectGridBlock(',
    'public void RemoveRectGridBlockAt(',
    'private void EnsureRectGridPlacementData()',
    'private bool IsValidRectGridCell(',
    'private int FindRectGridPlacementIndex(',
    'private void RemoveUniqueRectGridBlockGroup(',
    'private void RemoveRectGridBlocks(',
    'private static bool RequiresUniqueRectGridPlacement(',
    'private static bool IsUniqueOutputRectGridBlockType(',
    'private int GetRectGridObjectCount()',
    'public static bool IsInputEnergyBlockType(',
    'public static bool IsInputItemBlockType('
)) {
    $generated += "`n" + (Read-Member $signature) + "`n"
}
$generated += "}`n"
$generated += @'
public static class RectGridEditorNumbering
{
    public static int InputIndex(InputOutputModule module, Vector2Int cell) =>
        GetNumberedRectGridBlockIndex(module, cell, InputOutputModule.IsInputItemBlockType);

    public static int PipeOutputIndex(InputOutputModule module, Vector2Int cell) =>
        GetNumberedRectGridBlockIndex(module, cell, IsPipeOutputRectGridBlockType);

'@
$generated += (Read-EditorMember 'private static int GetNumberedRectGridBlockIndex(') + "`n"
$generated += (Read-EditorMember 'private static bool IsPipeOutputRectGridBlockType(') + "`n}`n"
# Unity's Application.isPlaying is an engine-internal call. The standalone probe
# always exercises edit-time normalization, so pin that branch to false.
$generated = $generated.Replace('Application.isPlaying', 'false')

$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-RectGridPlacement-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probeDir
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
