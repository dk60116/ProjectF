param(
    [ValidateSet('Can', 'Engine', 'DieselEngine')]
    [string]$Asset = 'Can'
)

# Four solid UV swatches keep portable-mesh colors deterministic and cheap to sample.
Add-Type -AssemblyName System.Drawing

switch ($Asset) {
    'Can' {
        $relativeOutput = '..\FactorioProject\Assets\Items\Fuel Can\Fuel Can\Can_P_Palette.png'
        $rgb = @(
            @(242, 242, 242), # plastic body
            @(255, 255, 255), # raised panel
            @(146, 146, 146), # embossed ribs
            @(24, 27, 30)    # screw cap
        )
    }
    'Engine' {
        $relativeOutput = '..\FactorioProject\Assets\Items\Engine\Engine\Engine_P_TB.png'
        $rgb = @(
            @(91, 100, 113), # steel body and shaft
            @(219, 159, 65), # brass caps and rail
            @(43, 49, 57),   # dark flywheel
            @(171, 123, 53)  # hub accent
        )
    }
    'DieselEngine' {
        $relativeOutput = '..\FactorioProject\Assets\Items\Engine\Diesel Engine\Diesel Engine_P_TB.png'
        $rgb = @(
            @(91, 100, 113), # steel body and shaft
            @(235, 111, 22), # orange caps, rail, flywheel rim and side plate
            @(43, 49, 57),   # dark flywheel
            @(190, 73, 10)   # darker orange hub accent
        )
    }
}

$outputPath = Join-Path $PSScriptRoot $relativeOutput
$bitmap = [System.Drawing.Bitmap]::new(64, 16)
try {
    for ($y = 0; $y -lt $bitmap.Height; $y++) {
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            $color = $rgb[[int][Math]::Floor($x / 16)]
            $bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $color[0], $color[1], $color[2]))
        }
    }
    $bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $bitmap.Dispose()
}
