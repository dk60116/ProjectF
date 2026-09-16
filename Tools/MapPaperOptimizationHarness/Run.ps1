param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$mapPaperPath = Join-Path $repo 'FactorioProject/Assets/Scripts/HUD/Map/MapPaper.cs'
$storePath = Join-Path $repo 'FactorioProject/Assets/Scripts/Map/BlockStateStore.cs'
$mapPaperSource = [IO.File]::ReadAllText($mapPaperPath)
$storeSource = [IO.File]::ReadAllText($storePath)

function Read-Member([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        elseif ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced member: $signature" }
    return $source.Substring($start, $end - $start)
}

$staticRefresh = Read-Member $mapPaperSource 'private void RefreshStaticMapMarkers('
$dynamicRefresh = Read-Member $mapPaperSource 'private void RefreshLiveTrainMarkers('
$update = Read-Member $mapPaperSource 'private void Update()'
if (!$staticRefresh.Contains('CollectMapMarkerSamplesAt')) {
    throw 'Static marker refresh must collect static map markers.'
}
if ($dynamicRefresh.Contains('CollectMapMarkerSamplesAt')) {
    throw 'Dynamic train refresh regressed to a full coordinate scan.'
}
if (!$staticRefresh.Contains('Array.Copy(composedPixelBuffer, staticMarkerPixelBuffer')) {
    throw 'Static refresh must preserve its completed layer.'
}
if (!$dynamicRefresh.Contains('Array.Copy(staticMarkerPixelBuffer, composedPixelBuffer')) {
    throw 'Dynamic refresh must restore the cached static layer.'
}
if (!$update.Contains('boundTerrain.MapMarkerVersion') -or
    !$update.Contains('RefreshLiveTrainMarkers(lastCenterCoordinate)')) {
    throw 'Update must use versioned static invalidation and the lightweight periodic path.'
}

$comparison = Read-Member $storeSource 'private static bool HasSameMapMarkerState('
$generated = @'
using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine
{
    public struct Vector2 : IEquatable<Vector2>
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public bool Equals(Vector2 other) => x == other.x && y == other.y;
        public override bool Equals(object value) => value is Vector2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y);
        public static bool operator ==(Vector2 first, Vector2 second) => first.Equals(second);
        public static bool operator !=(Vector2 first, Vector2 second) => !first.Equals(second);
    }
    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Vector2Int other) => x == other.x && y == other.y;
        public override bool Equals(object value) => value is Vector2Int other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y);
        public static bool operator ==(Vector2Int first, Vector2Int second) => first.Equals(second);
        public static bool operator !=(Vector2Int first, Vector2Int second) => !first.Equals(second);
    }
    public struct Color32 : IEquatable<Color32>
    {
        public byte r, g, b, a;
        public bool Equals(Color32 other) => r == other.r && g == other.g && b == other.b && a == other.a;
        public override bool Equals(object value) => value is Color32 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(r, g, b, a);
    }
}
public partial class BlockStateStore
{
    public sealed class InstallationSaveState
    {
        public UnityEngine.Vector2Int anchorCoordinate;
        public int itemId, quarterTurns, unrelatedPayload;
        public bool stationColorAssigned, railVisualPathExtendsStart, railVisualPathExtendsEnd;
        public UnityEngine.Color32 stationColor;
        public List<UnityEngine.Vector2> railVisualPathPoints = new();
    }
'@
$generated += "`n" + $comparison
$generated += @'

    public static bool SameMarker(InstallationSaveState first, InstallationSaveState second) =>
        HasSameMapMarkerState(first, second);
}
'@

$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-MapPaper-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'ProductionMembers.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
