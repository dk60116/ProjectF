$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Fluid tank.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$pipeSourcePath = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Pipe.cs'
$pipeSource = [IO.File]::ReadAllText($pipeSourcePath)

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

function Read-PipeMember([string]$signature) {
    $start = $pipeSource.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $pipeSource.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $pipeSource.Length) {
        if ($pipeSource[$end] -eq '{') { $depth++ }
        if ($pipeSource[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $pipeSource.Substring($start, $end - $start)
}

$generated = @'
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public readonly struct Vector2Int : IEquatable<Vector2Int>
    {
        public readonly int x;
        public readonly int y;

        public Vector2Int(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2Int zero => new Vector2Int(0, 0);
        public static Vector2Int up => new Vector2Int(0, 1);
        public static Vector2Int down => new Vector2Int(0, -1);
        public static Vector2Int left => new Vector2Int(-1, 0);
        public static Vector2Int right => new Vector2Int(1, 0);
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new Vector2Int(a.x + b.x, a.y + b.y);
        public static Vector2Int operator -(Vector2Int value) => new Vector2Int(-value.x, -value.y);
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.Equals(b);
        public static bool operator !=(Vector2Int a, Vector2Int b) => !a.Equals(b);
        public bool Equals(Vector2Int other) => x == other.x && y == other.y;
        public override bool Equals(object value) => value is Vector2Int other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y);
    }
}

public partial class Fluidtank
{
    private static readonly Vector2Int[] FluidCardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private readonly Dictionary<Vector2Int, int> connectedFluidItemIds = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Vector2Int, Fluidtank> adjacentTanks = new Dictionary<Vector2Int, Fluidtank>();
    private bool networkConnectionAllowed = true;

    public int StoredFluidItemId { get; set; } = -1;
    public bool IsFlatCarMounted { get; set; }

    public void SetConnectedFluid(Vector2Int direction, int fluidItemId)
    {
        connectedFluidItemIds[direction] = fluidItemId;
    }

    public void SetAdjacentTank(Vector2Int direction, Fluidtank tank)
    {
        adjacentTanks[direction] = tank;
    }

    public bool CanConnect(Vector2Int direction, Fluidtank neighbor)
    {
        return CanConnectAdjacentFixedTank(Vector2Int.zero, direction, neighbor);
    }

    public bool CanConnectPipe(Vector2Int direction, int fluidItemId)
    {
        return CanConnectFixedTankPipe(Vector2Int.zero, direction, fluidItemId);
    }

    public void SetNetworkConnectionAllowed(bool allowed)
    {
        networkConnectionAllowed = allowed;
    }

    public bool HasFluidNetworkConnectionTowards(Vector2Int coordinate, Vector2Int direction)
    {
        return networkConnectionAllowed;
    }

    private bool TryResolveConnectionTowards(
        Vector2Int tankCoordinate,
        Vector2Int directionFromTank,
        Vector2Int ignoredStorageCoordinate,
        out Fluidtank neighborTank,
        out int neighborFluidItemId)
    {
        if (adjacentTanks.TryGetValue(directionFromTank, out neighborTank))
        {
            neighborFluidItemId = neighborTank.StoredFluidItemId;
            return true;
        }

        return connectedFluidItemIds.TryGetValue(directionFromTank, out neighborFluidItemId);
    }
'@
$generated += "`n" + (Read-Member 'private bool CanConnectAdjacentFixedTank(')
$generated += "`n" + (Read-Member 'private bool CanConnectFixedTankPipe(')
$generated += "`n" + (Read-Member 'private int ResolveFixedTankNetworkFluidItemId(')
$generated += "`n}`n"
$generated += @'

public partial class Pipe
{
    public static bool CanTraverse(Fluidtank current, Vector2Int direction, Fluidtank neighbor)
    {
        return CanTraverseFluidTankBoundary(current, Vector2Int.zero, direction, neighbor);
    }

    public static bool CanTraverseFromPipeToTank(Vector2Int direction, Fluidtank neighbor)
    {
        return CanTraverseFluidTankBoundary(null, Vector2Int.zero, direction, neighbor);
    }

    public static bool CanTraverseDuringFluidIdentitySearch(
        Fluidtank current,
        Vector2Int direction,
        Fluidtank neighbor)
    {
        return CanTraverseFluidTankBoundary(
            current,
            Vector2Int.zero,
            direction,
            neighbor,
            false);
    }
'@
$generated += "`n" + (Read-PipeMember 'private static bool CanTraverseFluidTankBoundary(')
$generated += "`n}`n"

$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-FluidTankNetwork-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probeDir
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>'
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
