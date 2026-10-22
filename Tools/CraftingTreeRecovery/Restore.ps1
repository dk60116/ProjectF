param(
    [switch]$Apply,
    [string]$ExpectedSourceSha256
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Inspect.ps1') -FunctionsOnly

function Get-BytesHash([byte[]]$bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '') }
    finally { $sha.Dispose() }
}

function ConvertTo-CraftingTreeBytes([object[]]$recipes) {
    $memory = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($memory)
    try {
        $writer.Write([int]6)
        $writer.Write([int]$recipes.Count)
        foreach ($entry in $recipes) {
            $writer.Write([string]$entry.Name)
            $writer.Write([int]$entry.Machines.Count)
            foreach ($machine in $entry.Machines) { $writer.Write([string]$machine) }
            $writer.Write([single]$entry.Output)
            $writer.Write([int]$entry.Ingredients.Count)
            foreach ($ingredient in $entry.Ingredients) {
                $writer.Write([string]$ingredient.Name)
                $writer.Write([single]$ingredient.Amount)
            }
        }
        $writer.Flush()
        return ,$memory.ToArray()
    }
    finally { $writer.Dispose(); $memory.Dispose() }
}

# Only the 19 entries approved for this recovery; no historical blanket merge.
$missingNames = @(
    'Manual - Super computer', 'Book desk', 'Beef', 'Pork steak', 'Beef steak',
    'Horse steak', 'Wooden wheel', 'Iron wheel', 'Handcart', 'Engine',
    'Diesel Engine', 'Railload', 'Bucket', 'Fuel Can', 'Diesel Can',
    'Heavy oil Can', 'Lubricant Can'
)
$emptyNames = @('Train station', 'Steam train')
$sourcePath = Join-Path $repo $relativePath
$resourcePath = Join-Path $repo 'FactorioProject/Assets/Resources/Data/CraftingTree/crafting_tree.bytes'
$sourceBytes = [IO.File]::ReadAllBytes($sourcePath)
$resourceBytes = [IO.File]::ReadAllBytes($resourcePath)
$sourceHash = Get-BytesHash $sourceBytes
if ($Apply -and (!$ExpectedSourceSha256 -or $sourceHash -ne $ExpectedSourceSha256)) {
    throw 'Apply requires the expected current source SHA256; inspect again if the source changed.'
}
$current = Read-CraftingTree $sourceBytes
if ($current.Version -ne 6) { throw 'Recovery expects the current v6 source format.' }
if ((Get-BytesHash (ConvertTo-CraftingTreeBytes $current.Recipes)) -ne $sourceHash) {
    throw 'Binary reader/writer does not reproduce the current source exactly.'
}
$historical = Read-CraftingTree (Read-GitBytes 'b1763bd')
$historyByName = @{}
foreach ($entry in $historical.Recipes) {
    if ($historyByName.ContainsKey($entry.Name)) { throw "Duplicate historical recipe: $($entry.Name)" }
    $historyByName[$entry.Name] = $entry
}
$byName = @{}
$merged = [Collections.Generic.List[object]]::new()
$recovered = [Collections.Generic.List[string]]::new()
foreach ($entry in $current.Recipes) {
    if ($byName.ContainsKey($entry.Name)) { throw "Duplicate current recipe: $($entry.Name)" }
    $byName[$entry.Name] = $entry
    if ($entry.Name -in $emptyNames -and $entry.Ingredients.Count -eq 0) {
        $old = $historyByName[$entry.Name]
        if (!$old -or $old.Ingredients.Count -eq 0) { throw "No historical recipe for $($entry.Name)" }
        $merged.Add([pscustomobject]@{
            Name = $entry.Name; Output = $entry.Output
            Machines = @(if ($entry.Machines.Count -gt 0) { $entry.Machines } else { $old.Machines })
            Ingredients = @($old.Ingredients)
        })
        $recovered.Add($entry.Name)
    }
    else { $merged.Add($entry) }
}
foreach ($name in $missingNames) {
    if ($byName.ContainsKey($name)) { continue }
    $entry = $historyByName[$name]
    if (!$entry -or $entry.Ingredients.Count -eq 0) { throw "No historical recipe for $name" }
    $merged.Add($entry)
    $recovered.Add($name)
}

$definitionNames = @{}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Data/Items') -Filter '*.asset') {
    $match = Select-String -LiteralPath $file.FullName -Pattern '^  itemName: (.+)$'
    if ($match) { $definitionNames[$match.Matches[0].Groups[1].Value.Trim()] = $true }
}
foreach ($entry in $merged) {
    $references = @($entry.Name) + @($entry.Machines) + @($entry.Ingredients | ForEach-Object { $_.Name })
    foreach ($name in $references) {
        if (!$definitionNames.ContainsKey($name)) { throw "Unresolved ItemDefinition: $name" }
    }
}
$mergedBytes = ConvertTo-CraftingTreeBytes $merged.ToArray()
$verified = Read-CraftingTree $mergedBytes
if (($verified.Recipes | ConvertTo-Json -Depth 8 -Compress) -cne ($merged.ToArray() | ConvertTo-Json -Depth 8 -Compress)) {
    throw 'Recovery serialization round-trip failed.'
}
foreach ($entry in $verified.Recipes) {
    if ($byName.ContainsKey($entry.Name) -and $entry.Name -notin $recovered) {
        if (($entry | ConvertTo-Json -Depth 8 -Compress) -cne ($byName[$entry.Name] | ConvertTo-Json -Depth 8 -Compress)) {
            throw "Existing recipe changed: $($entry.Name)"
        }
    }
}
$audit = [ordered]@{
    BaseRevision = 'b1763bd'; BeforeSha256 = $sourceHash; AfterSha256 = Get-BytesHash $mergedBytes
    RecordsBefore = $current.Recipes.Count; RecordsAfter = $verified.Recipes.Count
    NonemptyBefore = @($current.Recipes | Where-Object { $_.Ingredients.Count -gt 0 }).Count
    NonemptyAfter = @($verified.Recipes | Where-Object { $_.Ingredients.Count -gt 0 }).Count
    Recovered = @($recovered.ToArray()); Applied = $false
}
if ($Apply -and $recovered.Count -gt 0) {
    $backup = Join-Path $env:USERPROFILE ('.codex/backups/ProjectF/crafting-tree-restore-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
    [IO.Directory]::CreateDirectory($backup) | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $backup 'source-before.bytes'), $sourceBytes)
    [IO.File]::WriteAllBytes((Join-Path $backup 'resources-before.bytes'), $resourceBytes)
    [IO.File]::WriteAllBytes((Join-Path $backup 'recovered.bytes'), $mergedBytes)
    [IO.File]::WriteAllText((Join-Path $backup 'audit.json'), ($audit | ConvertTo-Json -Depth 8))
    if ((Get-BytesHash ([IO.File]::ReadAllBytes($sourcePath))) -ne $sourceHash -or
        (Get-BytesHash ([IO.File]::ReadAllBytes($resourcePath))) -ne (Get-BytesHash $resourceBytes)) {
        throw 'Crafting tree changed during recovery preparation; original files were not replaced.'
    }
    $replaceIndex = 0
    foreach ($path in @($sourcePath, $resourcePath)) {
        $temporary = $path + '.recovery-' + [guid]::NewGuid().ToString('N') + '.tmp'
        [IO.File]::WriteAllBytes($temporary, $mergedBytes)
        try { [IO.File]::Replace($temporary, $path, (Join-Path $backup "replaced-$replaceIndex.bytes")) }
        finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
        $replaceIndex++
    }
    foreach ($path in @($sourcePath, $resourcePath)) {
        if ((Get-BytesHash ([IO.File]::ReadAllBytes($path))) -ne $audit.AfterSha256) { throw "Written file differs: $path" }
    }
    $audit.Applied = $true
    $audit['Backup'] = $backup
    [IO.File]::WriteAllText((Join-Path $backup 'audit.json'), ($audit | ConvertTo-Json -Depth 8))
}
$audit | ConvertTo-Json -Depth 8
