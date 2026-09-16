param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = Join-Path $repo 'FactorioProject/Assets/Scripts/Map/PortableItemRenderer.cs'
$source = [IO.File]::ReadAllText($sourcePath)

function Read-Member([string]$signature) {
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
    $source.Substring($start, $end - $start)
}

$generated = @'
using System;
using System.Collections.Generic;
using UnityEngine;
public sealed partial class PortableItemRenderer
{
    private float portableObjectBatchCellSize = 8f;
    private readonly VirtualRenderBatchCollection portableObjectBatches = new();
    private readonly Dictionary<PortableObject, PortableObjectBatchCache> portableObjectBatchCaches = new(512);
    private readonly List<PortableObject> dirtyPortableObjects = new(128);
    private readonly HashSet<PortableObject> dirtyPortableObjectLookup = new();
    private int pendingPortableObjectDirtyRequests;
    private int lastPortableObjectDirtyRequests;
    private int lastPortableObjectDirtyObjects;
    private int lastPortableObjectSnapshotReads;
    private int lastPortableObjectMatrixUpdates;
    private int lastPortableObjectBatchRebuilds;
'@
foreach ($signature in @(
    'public void Register(PortableObject portableObject)',
    'public void Unregister(PortableObject portableObject)',
    'public void MarkDirty(PortableObject portableObject)',
    'private void RefreshDirtyPortableObjectBatches()',
    'private PortableObjectRenderSnapshot ReadPortableObjectRenderSnapshot(',
    'private readonly struct PortableObjectRenderSnapshot',
    'private sealed class PortableObjectBatchCache')) {
    $generated += "`n" + (Read-Member $signature)
}
$generated += "`n}`n"

$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-PortableItemRenderer-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'ProductionMembers.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
