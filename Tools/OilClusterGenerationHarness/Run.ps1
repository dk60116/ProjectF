$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$resourcePath = Join-Path $repo 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.Resources.cs'
$terrainPath = Join-Path $repo 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.cs'
$editorPath = Join-Path $repo 'FactorioProject/Assets/Editor/TerrainDataEditorWindow.cs'
$scenePath = Join-Path $repo 'FactorioProject/Assets/Scenes/GameScene.unity'
$resourceSource = [IO.File]::ReadAllText($resourcePath)
$terrainSource = [IO.File]::ReadAllText($terrainPath)
$editorSource = [IO.File]::ReadAllText($editorPath)
$sceneSource = [IO.File]::ReadAllText($scenePath)

function Require([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Read-Member([string]$source, [string]$signature) {
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

Require ($resourceSource.Contains('TryEvaluateOilClusterResource(worldCoordinate, oilResources[i]')) 'Oil resources must use the dedicated cluster evaluator'
Require ($resourceSource.Contains('Mathf.Clamp(oilClusterMinCount, 1, 4)')) 'Oil cluster count must be clamped to 1-4'
Require ($resourceSource.Contains('Mathf.Clamp(oilClusterMinSpacing, 2, 8)')) 'Oil cluster spacing must be clamped to 2-8 cells'
Require ($resourceSource.Contains('IsOilClusterCandidateValid(')) 'Random oil shapes must validate every pair before placement'
foreach ($property in @('oilClusterMinCount', 'oilClusterMaxCount', 'oilClusterMinSpacing', 'oilClusterMaxSpacing')) {
    Require ($terrainSource.Contains($property)) "Missing terrain setting: $property"
    Require ($editorSource.Contains('"' + $property + '"')) "Terrain Editor does not expose: $property"
    Require ($sceneSource.Contains('  ' + $property + ':')) "GameScene does not serialize: $property"
}

$resolver = Read-Member $resourceSource 'private Vector2Int ResolveOilClusterMemberOffset('
$candidateValidator = Read-Member $resourceSource 'private static bool IsOilClusterCandidateValid('
$spacingValidator = Read-Member $resourceSource 'private static bool IsOilClusterSpacingValid('
$hash = Read-Member $resourceSource 'private float Hash01('
$generated = @'
using System;
using UnityEngine;
namespace UnityEngine
{
    public readonly struct Vector2Int : IEquatable<Vector2Int>
    {
        public readonly int x;
        public readonly int y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public static Vector2Int zero => new Vector2Int(0, 0);
        public bool Equals(Vector2Int other) => x == other.x && y == other.y;
        public override bool Equals(object value) => value is Vector2Int other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y);
        public override string ToString() => $"{x},{y}";
    }

    public static class Mathf
    {
        public static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);
        public static int Max(int first, int second) => Math.Max(first, second);
        public static int Min(int first, int second) => Math.Min(first, second);
        public static int Abs(int value) => Math.Abs(value);
        public static int FloorToInt(float value) => (int)Math.Floor(value);
    }
}
public partial class TerrainGenerator
{
    private readonly int seed;
    public TerrainGenerator(int seed) { this.seed = seed; }
    public UnityEngine.Vector2Int ResolveForTest(int cellX, int cellY, int salt, int index, int minSpacing, int maxSpacing) =>
        ResolveOilClusterMemberOffset(cellX, cellY, salt, index, minSpacing, maxSpacing);
'@
$generated += "`n$resolver`n$candidateValidator`n$spacingValidator`n$hash`n}`n"

$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-OilCluster-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
Set-Content -LiteralPath (Join-Path $probe 'Production.cs') -Value $generated
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Set-Content -LiteralPath (Join-Path $probe 'Probe.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>'
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
