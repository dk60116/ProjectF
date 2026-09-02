param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Args)

$format = $null
$outDir = $null
$inputPath = $null

for ($i = 0; $i -lt $Args.Count; $i++) {
    if ($Args[$i] -eq '--convert-to' -and $i + 1 -lt $Args.Count) {
        $format = $Args[$i + 1]
        $i++
        continue
    }
    if ($Args[$i] -eq '--outdir' -and $i + 1 -lt $Args.Count) {
        $outDir = $Args[$i + 1]
        $i++
        continue
    }
    if (-not $Args[$i].StartsWith('-')) {
        $inputPath = $Args[$i]
    }
}

if ($format -ne 'pdf' -or -not $outDir -or -not $inputPath) {
    Write-Error 'This compatibility wrapper supports DOCX-to-PDF conversion only.'
    exit 2
}

$inputPath = [System.IO.Path]::GetFullPath($inputPath)
$outDir = [System.IO.Path]::GetFullPath($outDir)
$outputPath = Join-Path $outDir ([System.IO.Path]::GetFileNameWithoutExtension($inputPath) + '.pdf')

$word = $null
$document = $null
try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $document = $word.Documents.Open($inputPath, $false, $true)
    $document.ExportAsFixedFormat($outputPath, 17)
    Write-Output "Converted $inputPath -> $outputPath"
}
finally {
    if ($document -ne $null) {
        $document.Close(0)
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($document)
    }
    if ($word -ne $null) {
        $word.Quit()
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($word)
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
