param(
    [string[]]$Revisions = @('HEAD', '647ea40', 'b9192e0', '3fba313'),
    [string]$ReportDirectory,
    [switch]$FunctionsOnly
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$relativePath = 'FactorioProject/Assets/Data/CraftingTree/crafting_tree.bytes'

function Read-CraftingTree([byte[]]$bytes) {
    $stream = [IO.MemoryStream]::new($bytes, $false)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        $version = $reader.ReadInt32()
        if ($version -lt 4 -or $version -gt 6) { throw "Unsupported binary version $version" }
        $count = $reader.ReadInt32()
        if ($count -lt 0 -or $count -gt 100000) { throw 'Invalid recipe count' }
        $recipes = [Collections.Generic.List[object]]::new()
        for ($i = 0; $i -lt $count; $i++) {
            $name = if ($version -ge 5) { $reader.ReadString() } else { '#' + $reader.ReadInt32() }
            $machineCount = $reader.ReadInt32()
            if ($machineCount -lt 0 -or $machineCount -gt 100000) { throw 'Invalid machine count' }
            $machines = @(
                for ($j = 0; $j -lt $machineCount; $j++) {
                    if ($version -ge 5) { $reader.ReadString() } else { '#' + $reader.ReadInt32() }
                }
            )
            $output = if ($version -ge 6) { $reader.ReadSingle() } else { $reader.ReadInt32() }
            $ingredientCount = $reader.ReadInt32()
            if ($ingredientCount -lt 0 -or $ingredientCount -gt 100000) { throw 'Invalid ingredient count' }
            $ingredients = @(
                for ($j = 0; $j -lt $ingredientCount; $j++) {
                    $ingredient = if ($version -ge 5) { $reader.ReadString() } else { '#' + $reader.ReadInt32() }
                    $amount = if ($version -ge 6) { $reader.ReadSingle() } else { $reader.ReadInt32() }
                    [pscustomobject]@{ Name = $ingredient; Amount = $amount }
                }
            )
            $recipes.Add([pscustomobject]@{ Name = $name; Output = $output; Machines = $machines; Ingredients = $ingredients })
        }
        if ($stream.Position -ne $stream.Length) { throw 'Trailing bytes after recipes' }
        return [pscustomobject]@{ Version = $version; Recipes = @($recipes.ToArray()) }
    }
    finally { $reader.Dispose(); $stream.Dispose() }
}

function Read-GitBytes([string]$revision) {
    if ($revision -notmatch '^[A-Za-z0-9_./~-]+$') { throw 'Invalid revision' }
    $info = [Diagnostics.ProcessStartInfo]::new('git', "show ${revision}:$relativePath")
    $info.WorkingDirectory = $repo
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($info)
    $memory = [IO.MemoryStream]::new()
    try {
        $process.StandardOutput.BaseStream.CopyTo($memory)
        $errorText = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw $errorText }
        return ,$memory.ToArray()
    }
    finally { $memory.Dispose(); $process.Dispose() }
}

if ($FunctionsOnly) { return }

$current = Read-CraftingTree ([IO.File]::ReadAllBytes((Join-Path $repo $relativePath)))
$byName = @{}
foreach ($entry in $current.Recipes) {
    if ($byName.ContainsKey($entry.Name)) { throw "Duplicate current recipe: $($entry.Name)" }
    $byName[$entry.Name] = $entry
}
$summary = [Collections.Generic.List[object]]::new()
$snapshots = [ordered]@{ Current = $current }
foreach ($revision in $Revisions) {
    try { $previous = Read-CraftingTree (Read-GitBytes $revision) }
    catch {
        Write-Warning "${revision}: $($_.Exception.Message)"
        continue
    }
    $snapshots[$revision] = $previous
    $missing = @($previous.Recipes | Where-Object { $_.Ingredients.Count -gt 0 -and !$byName.ContainsKey($_.Name) })
    $emptied = @($previous.Recipes | Where-Object { $_.Ingredients.Count -gt 0 -and $byName.ContainsKey($_.Name) -and $byName[$_.Name].Ingredients.Count -eq 0 })
    $summary.Add([pscustomobject]@{
        Revision = $revision; Version = $previous.Version; Recipes = $previous.Recipes.Count
        Nonempty = @($previous.Recipes | Where-Object { $_.Ingredients.Count -gt 0 }).Count
        Missing = @($missing | ForEach-Object { $_.Name }); Emptied = @($emptied | ForEach-Object { $_.Name })
    })
}
[pscustomobject]@{
    Revision = 'WorkingTree'; Version = $current.Version; Recipes = $current.Recipes.Count
    Nonempty = @($current.Recipes | Where-Object { $_.Ingredients.Count -gt 0 }).Count
} | Format-List
$summary | ConvertTo-Json -Depth 8
if ($ReportDirectory) {
    $directory = [IO.Path]::GetFullPath($ReportDirectory)
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    [IO.File]::WriteAllText((Join-Path $directory 'snapshots.json'), ($snapshots | ConvertTo-Json -Depth 12))
}
