$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$handcartPath = Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/Handcart.cs'
$handcartSource = [IO.File]::ReadAllText($handcartPath)

function Read-Member([string]$signature, [int]$occurrence = 0) {
    $start = -1
    for ($i = 0; $i -le $occurrence; $i++) {
        $start = $handcartSource.IndexOf($signature, $start + 1, [StringComparison]::Ordinal)
        if ($start -lt 0) { throw "Missing production member: $signature occurrence $occurrence" }
    }
    $end = $handcartSource.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $handcartSource.Length) {
        if ($handcartSource[$end] -eq '{') { $depth++ }
        elseif ($handcartSource[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $handcartSource.Substring($start, $end - $start)
}

$preview = Read-Member 'public bool TryPreviewPickupItems(' 1
if ((-not $preview.Contains('TryGetCargoFocusStack(cargoIndex', [StringComparison]::Ordinal)) -or
    (-not $preview.Contains('previewPortableObject.SetFocusStack(focusStack);', [StringComparison]::Ordinal))) {
    throw 'Handcart pickup preview does not assign the selected cargo stack to the outline target.'
}

foreach ($signature in @('public bool TryAddItemStack(', 'private bool TryPickupOneItem(', 'private void RebuildCargoVisuals()')) {
    $member = Read-Member $signature
    if (-not $member.Contains('RebuildCargoFocusStacks();', [StringComparison]::Ordinal)) {
        throw "Cargo mutation does not rebuild focus stacks: $signature"
    }
}

$generated = @'
using System;
using System.Collections.Generic;
public static class Mathf
{
    public static int Min(int a, int b) => Math.Min(a, b);
    public static int Max(int a, int b) => Math.Max(a, b);
}
public sealed class PortableObject
{
    public readonly int Id;
    public PortableObject(int id) { Id = id; }
}
public partial class Handcart
{
    private readonly List<PortableObject> itemVisuals = new();
    private readonly List<int> storedItemIds = new();
    private readonly List<int> cargoStackItemIds = new();
    private readonly List<int> cargoStackCounts = new();
    private List<List<PortableObject>> cargoFocusStacks = new();
    private readonly Dictionary<int, int> capacities = new();
    private int usableItemPointCount;
    private int GetStackCapacityForItem(int itemId) => capacities[itemId];
    private int GetUsableItemPointCount() => usableItemPointCount;
    public void Configure(int pointCount, params (int ItemId, int Capacity)[] definitions)
    {
        usableItemPointCount = pointCount;
        foreach (var definition in definitions) capacities[definition.ItemId] = definition.Capacity;
    }
    public void Add(int itemId, PortableObject visual)
    {
        storedItemIds.Add(itemId);
        itemVisuals.Add(visual);
    }
    public void Rebuild() => RebuildCargoFocusStacks();
    public bool TryGet(int cargoIndex, out List<PortableObject> stack) => TryGetCargoFocusStack(cargoIndex, out stack);
}
'@
$generated += "`npublic partial class Handcart {`n"
$generated += (Read-Member 'private bool TryResolveCargoStack(') + "`n"
$generated += (Read-Member 'private void BuildCargoStackLayout(' 0) + "`n"
$generated += (Read-Member 'private void BuildCargoStackLayout(' 1) + "`n"
$generated += (Read-Member 'private void RebuildCargoFocusStacks()') + "`n"
$generated += (Read-Member 'private bool TryGetCargoFocusStack(') + "`n}`n"

$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-HandcartFocus-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText(
    (Join-Path $probe 'Probe.csproj'),
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
