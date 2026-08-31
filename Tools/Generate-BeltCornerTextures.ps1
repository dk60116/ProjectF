param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$assetDirectory = Join-Path $ProjectRoot 'FactorioProject/Assets/MapObject/Belt/Conveyor belt'
$straightTexturePath = Join-Path $assetDirectory 'BeltTop_TB.png'
$cornerTexturePath = Join-Path $assetDirectory 'BeltTop_Corner_TB.png'
$maskTexturePath = Join-Path $assetDirectory 'BodyCornerMask_8.png'
$pathUvTexturePath = Join-Path $assetDirectory 'BeltTop_Corner_PathUV.png'

$textureSize = 128
$straightTopWorldWidth = 10.0 * 0.065948404
$cornerPlaneWorldWidthX = 10.0 * 1.0204512 * 0.083034955
$cornerPlaneWorldWidthY = 10.0 * 0.83570653 * 0.098033614
$innerRadiusX = $textureSize * (1.0 - $straightTopWorldWidth / $cornerPlaneWorldWidthX)
$innerRadiusY = $textureSize * (1.0 - $straightTopWorldWidth / $cornerPlaneWorldWidthY)
$outerRadius = [double]$textureSize
$samplesPerAxis = 4

function Get-BilinearColor {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [double]$X,
        [double]$Y
    )

    $clampedX = [Math]::Max(0.0, [Math]::Min($Bitmap.Width - 1.0, $X))
    $clampedY = [Math]::Max(0.0, [Math]::Min($Bitmap.Height - 1.0, $Y))
    $x0 = [int][Math]::Floor($clampedX)
    $y0 = [int][Math]::Floor($clampedY)
    $x1 = [Math]::Min($Bitmap.Width - 1, $x0 + 1)
    $y1 = [Math]::Min($Bitmap.Height - 1, $y0 + 1)
    $tx = $clampedX - $x0
    $ty = $clampedY - $y0

    $c00 = $Bitmap.GetPixel($x0, $y0)
    $c10 = $Bitmap.GetPixel($x1, $y0)
    $c01 = $Bitmap.GetPixel($x0, $y1)
    $c11 = $Bitmap.GetPixel($x1, $y1)

    function Interpolate-Channel([int]$a, [int]$b, [int]$c, [int]$d) {
        $top = $a + ($b - $a) * $tx
        $bottom = $c + ($d - $c) * $tx
        return [int][Math]::Round($top + ($bottom - $top) * $ty)
    }

    return [System.Drawing.Color]::FromArgb(
        (Interpolate-Channel $c00.A $c10.A $c01.A $c11.A),
        (Interpolate-Channel $c00.R $c10.R $c01.R $c11.R),
        (Interpolate-Channel $c00.G $c10.G $c01.G $c11.G),
        (Interpolate-Channel $c00.B $c10.B $c01.B $c11.B)
    )
}

function Get-CornerCoordinates {
    param(
        [double]$PixelX,
        [double]$PixelY
    )

    $dx = $textureSize - $PixelX
    $dy = $textureSize - $PixelY
    $radius = [Math]::Sqrt($dx * $dx + $dy * $dy)
    $angle = [Math]::Atan2($dy, $dx)
    $cosine = [Math]::Cos($angle)
    $sine = [Math]::Sin($angle)
    $innerRadius = 1.0 / [Math]::Sqrt(
        ($cosine * $cosine) / ($innerRadiusX * $innerRadiusX) +
        ($sine * $sine) / ($innerRadiusY * $innerRadiusY)
    )
    $width = ($radius - $innerRadius) / ($outerRadius - $innerRadius)
    $along = $angle / ([Math]::PI * 0.5)

    return [pscustomobject]@{
        Inside = $radius -ge $innerRadius -and $radius -le $outerRadius
        Width = [Math]::Max(0.0, [Math]::Min(1.0, $width))
        Along = [Math]::Max(0.0, [Math]::Min(1.0, $along))
    }
}

function Get-Coverage {
    param(
        [int]$X,
        [int]$Y
    )

    $insideSamples = 0
    for ($sampleY = 0; $sampleY -lt $samplesPerAxis; $sampleY++) {
        for ($sampleX = 0; $sampleX -lt $samplesPerAxis; $sampleX++) {
            $pixelX = $X + ($sampleX + 0.5) / $samplesPerAxis
            $pixelY = $Y + ($sampleY + 0.5) / $samplesPerAxis
            if ((Get-CornerCoordinates $pixelX $pixelY).Inside) {
                $insideSamples++
            }
        }
    }

    return $insideSamples / [double]($samplesPerAxis * $samplesPerAxis)
}

function Save-AtomicPng {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [string]$Destination
    )

    $temporary = "$Destination.generated.tmp.png"
    $Bitmap.Save($temporary, [System.Drawing.Imaging.ImageFormat]::Png)
    $validation = [System.Drawing.Bitmap]::FromFile($temporary)
    try {
        if ($validation.Width -ne $textureSize -or $validation.Height -ne $textureSize) {
            throw "Generated texture has invalid dimensions: $temporary"
        }
    }
    finally {
        $validation.Dispose()
    }

    [System.IO.File]::Copy($temporary, $Destination, $true)
    Remove-Item -LiteralPath $temporary
}

$straightTexture = [System.Drawing.Bitmap]::FromFile($straightTexturePath)
try {
    if ($straightTexture.Width -ne $textureSize -or $straightTexture.Height -ne $textureSize) {
        throw 'BeltTop_TB.png must remain 128x128.'
    }

    $cornerTexture = [System.Drawing.Bitmap]::new(
        $textureSize,
        $textureSize,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    )
    $maskTexture = [System.Drawing.Bitmap]::new(
        $textureSize,
        $textureSize,
        [System.Drawing.Imaging.PixelFormat]::Format24bppRgb
    )
    $pathUvTexture = [System.Drawing.Bitmap]::new(
        $textureSize,
        $textureSize,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    )

    try {
        for ($y = 0; $y -lt $textureSize; $y++) {
            for ($x = 0; $x -lt $textureSize; $x++) {
                $coordinates = Get-CornerCoordinates ($x + 0.5) ($y + 0.5)
                # Pin the two connection texel rows to the exact straight-belt phases.
                if ($y -eq $textureSize - 1) {
                    $coordinates.Along = 0.0
                }
                elseif ($x -eq $textureSize - 1) {
                    $coordinates.Along = 1.0
                }
                $coverage = Get-Coverage $x $y
                $coverageByte = [int][Math]::Round($coverage * 255.0)

                $maskColor = [System.Drawing.Color]::FromArgb($coverageByte, $coverageByte, $coverageByte)
                $maskTexture.SetPixel($x, $y, $maskColor)

                if ($coverageByte -eq 0) {
                    $cornerTexture.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, 0, 0, 0))
                    $pathUvTexture.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0))
                    continue
                }

                $sourceX = $coordinates.Width * ($straightTexture.Width - 1)
                # Unity UV v=0 samples the bottom of the PNG.
                $sourceY = (1.0 - $coordinates.Along) * ($straightTexture.Height - 1)
                $sourceColor = Get-BilinearColor $straightTexture $sourceX $sourceY
                $cornerTexture.SetPixel(
                    $x,
                    $y,
                    [System.Drawing.Color]::FromArgb(
                        255,
                        [int][Math]::Round($sourceColor.R * $coverage),
                        [int][Math]::Round($sourceColor.G * $coverage),
                        [int][Math]::Round($sourceColor.B * $coverage)
                    )
                )

                $pathUvTexture.SetPixel(
                    $x,
                    $y,
                    [System.Drawing.Color]::FromArgb(
                        $coverageByte,
                        [int][Math]::Round($coordinates.Width * 255.0),
                        [int][Math]::Round($coordinates.Along * 255.0),
                        0
                    )
                )
            }
        }

        Save-AtomicPng $cornerTexture $cornerTexturePath
        Save-AtomicPng $maskTexture $maskTexturePath
        Save-AtomicPng $pathUvTexture $pathUvTexturePath
    }
    finally {
        $cornerTexture.Dispose()
        $maskTexture.Dispose()
        $pathUvTexture.Dispose()
    }
}
finally {
    $straightTexture.Dispose()
}

Write-Output ('Generated belt corner textures: inner radii {0:F3}px x {1:F3}px' -f $innerRadiusX, $innerRadiusY)
