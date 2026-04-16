param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$InputPaths,
    [switch]$ShowDialog
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

function Resolve-UniquePngPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath
    )

    $directory = [System.IO.Path]::GetDirectoryName($SourcePath)
    $fileNameWithoutExtension = [System.IO.Path]::GetFileNameWithoutExtension($SourcePath)
    $candidatePath = Join-Path $directory ($fileNameWithoutExtension + ".png")
    $suffix = 1

    while (Test-Path -LiteralPath $candidatePath) {
        $candidatePath = Join-Path $directory ("{0}_{1}.png" -f $fileNameWithoutExtension, $suffix)
        $suffix++
    }

    return $candidatePath
}

function Convert-JpegFileToPng {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath
    )

    $resolvedPath = (Resolve-Path -LiteralPath $SourcePath).Path
    $extension = [System.IO.Path]::GetExtension($resolvedPath)
if ([string]::IsNullOrWhiteSpace($extension)) {
        throw "Cannot convert a file without an extension: $resolvedPath"
    }

    $normalizedExtension = $extension.ToLowerInvariant()
    if ($normalizedExtension -notin @(".jpg", ".jpeg")) {
        throw "Only JPEG files can be converted: $resolvedPath"
    }

    $targetPath = Resolve-UniquePngPath -SourcePath $resolvedPath

    $image = $null
    try {
        $image = [System.Drawing.Image]::FromFile($resolvedPath)
        $image.Save($targetPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        if ($null -ne $image) {
            $image.Dispose()
        }
    }

    Write-Output $targetPath
}

if ($null -eq $InputPaths -or $InputPaths.Count -eq 0) {
    throw "A JPEG file path is required."
}

$convertedPaths = New-Object System.Collections.Generic.List[string]
foreach ($inputPath in $InputPaths) {
    if ([string]::IsNullOrWhiteSpace($inputPath)) {
        continue
    }

    $convertedPaths.Add((Convert-JpegFileToPng -SourcePath $inputPath))
}

if ($convertedPaths.Count -eq 0) {
    throw "No files were converted."
}

if ($ShowDialog) {
    [void][System.Reflection.Assembly]::LoadWithPartialName("System.Windows.Forms")
    [System.Windows.Forms.MessageBox]::Show(
        ("PNG conversion complete.`r`n`r`n" + ($convertedPaths -join "`r`n")),
        "JPEG -> PNG",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    )
}
