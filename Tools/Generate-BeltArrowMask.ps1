param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$directory = Join-Path $ProjectRoot 'FactorioProject/Assets/MapObject/Belt/Conveyor belt'
if (!$OutputPath) { $OutputPath = Join-Path $directory 'BeltTop_ArrowMask.png' }
$source = [Drawing.Bitmap]::new((Join-Path $directory 'BeltTop_TB.png'))
$mask = [Drawing.Bitmap]::new($source.Width, $source.Height, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
try {
    if ($source.Width -ne 128 -or $source.Height -ne 128) { throw 'Update the arrow regions when the source atlas layout changes.' }
    for ($y = 0; $y -lt $source.Height; $y++) {
        for ($x = 0; $x -lt $source.Width; $x++) {
            $value = 0
            # The two printed triangles occupy these regions of the 128px atlas.
            # Extract their anti-aliased ink, excluding bright metal borders.
            $row = $y % 64
            if ($x -ge 40 -and $x -le 87 -and $row -ge 46 -and $row -le 59) {
                $pixel = $source.GetPixel($x, $y)
                $luma = $pixel.R * .2126 + $pixel.G * .7152 + $pixel.B * .0722
                $value = [int][Math]::Round([Math]::Clamp(($luma - 78.0) / 30.0, 0.0, 1.0) * 255.0)
            }
            $mask.SetPixel($x, $y, [Drawing.Color]::FromArgb($value, $value, $value))
        }
    }
    $mask.Save([IO.Path]::GetFullPath($OutputPath), [Drawing.Imaging.ImageFormat]::Png)
}
finally { $source.Dispose(); $mask.Dispose() }
Write-Output "Generated belt arrow mask: $OutputPath"
