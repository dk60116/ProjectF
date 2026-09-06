$ErrorActionPreference = 'Stop'
$pdbPath = 'C:/Git/ProjectF/Build_PC/FactorioProject_BackUpThisFolder_ButDontShipItWithYourGame/Managed/Assembly-CSharp.pdb'
$stream = [IO.File]::OpenRead($pdbPath)
try {
    $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
    $reader = $provider.GetMetadataReader()
    $results = foreach ($handle in $reader.Documents) {
        $document = $reader.GetDocument($handle)
        $path = $reader.GetString($document.Name)
        if ($path -match '(RobotArm|TerrainGenerator\.Conveyors|MapObjectTickManager|GameManager|PortableItemRenderer|InputOutputModule|InstallationPlacementController|VirtualConveyorBeltRenderer)\.cs$') {
            $recorded = [Convert]::ToHexString($reader.GetBlobBytes($document.Hash))
            $current = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            [pscustomobject]@{ Path=$path; PdbHash=$recorded; CurrentHash=$current; Equal=$recorded -eq $current }
        }
    }
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'build-source-verification.json'), (ConvertTo-Json -InputObject @($results)))
    $results | Select-Object Path,Equal | Format-Table -AutoSize
    if (@($results).Count -ne 8 -or @($results | Where-Object { !$_.Equal }).Count) { throw 'Source checksum verification failed.' }
} finally {
    if ($provider) { $provider.Dispose() }
    $stream.Dispose()
}
