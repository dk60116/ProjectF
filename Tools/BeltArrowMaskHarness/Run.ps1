$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assets = Join-Path $repo 'FactorioProject/Assets'
$directory = Join-Path $assets 'MapObject/Belt/Conveyor belt'
$maskPath = Join-Path $directory 'BeltTop_ArrowMask.png'
$maskMeta = [IO.File]::ReadAllText($maskPath + '.meta')
$maskGuid = [regex]::Match($maskMeta, '(?m)^guid: ([a-f0-9]{32})\s*$').Groups[1].Value
if (!$maskGuid -or !$maskMeta.Contains('sRGBTexture: 0') -or !$maskMeta.Contains('textureType: 0') -or
    !$maskMeta.Contains('wrapU: 0') -or !$maskMeta.Contains('wrapV: 0') -or !$maskMeta.Contains('enableMipMap: 1')) {
    throw 'Arrow masks must import as linear repeating textures with mipmaps.'
}
$mask = [Drawing.Bitmap]::new($maskPath)
try {
    if ($mask.Width -ne 128 -or $mask.Height -ne 128) { throw 'Mask must match the source atlas dimensions.' }
    $marked = 0
    for ($y = 0; $y -lt 128; $y++) { for ($x = 0; $x -lt 128; $x++) {
        $pixel = $mask.GetPixel($x, $y)
        if ($pixel.R -ne $pixel.G -or $pixel.R -ne $pixel.B) { throw 'Mask channels must agree.' }
        if ($pixel.R -eq 0) { continue }
        $marked++
        if ($x -lt 40 -or $x -gt 87 -or ($y % 64) -lt 46 -or ($y % 64) -gt 59) { throw 'Mask includes belt plates or metal borders.' }
    } }
    if ($marked -lt 350 -or $marked -gt 650 -or $mask.GetPixel(64, 54).R -lt 250 -or $mask.GetPixel(64, 118).R -lt 250) {
        throw 'Both source arrows must be covered without coloring whole belt plates.'
    }
}
finally { $mask.Dispose() }
$materialCount = 0
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $assets 'MapObject/Belt') -Recurse -Filter '*.mat' -File) {
    $material = [IO.File]::ReadAllText($file.FullName)
    if ($material -notmatch 'm_Shader: .*guid: (9f1fd9f6207b4d38a5cd9b5bbfa34a11|2f7ec34bd3e84f04b0b07a62c91d7ec1)') { continue }
    $materialCount++
    if (!$material.Contains('guid: ' + $maskGuid) -or !$material.Contains('_ArrowColor:') -or
        ($file.Directory.Name -eq 'Fast Conveyor belt' -and !$material.Contains('_ArrowColor: {r: 1, g: 0.8, b: 0, a: 1}'))) {
        throw "Missing arrow mask/color or incorrect fast belt yellow: $($file.Name)"
    }
}
if ($materialCount -ne 8) { throw 'Expected basic/fast straight, corner and both seam materials.' }
$scroll = [IO.File]::ReadAllText((Join-Path $assets 'Shaders/ConveyorBeltScroll.shader'))
$corner = [IO.File]::ReadAllText((Join-Path $assets 'Shaders/ConveyorBeltCornerFlow.shader'))
foreach ($shader in @($scroll, $corner)) {
    if (!$shader.Contains('_ArrowColor("Arrow Color", Color) = (1,1,1,1)') -or
        !$shader.Contains('#include "ConveyorArrowColor.hlsl"') -or !$shader.Contains('half4 _ArrowColor;')) {
        throw 'Both shaders must expose the same per-material color and shared mask helper.'
    }
    if ($shader.IndexOf('ApplyConveyorArrowColor(flowSample, flowUv)') -gt $shader.IndexOf('baseSample = ApplyBlueprintPreview')) {
        throw 'Apply arrow color before preview tint and lighting.'
    }
}
if (!$scroll.Contains('baseSample = ApplyConveyorArrowColor(baseSample, beltUv)') -or
    !$scroll.Contains('SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, beltUv)') -or
    !$scroll.Contains('flowSample = ApplyConveyorArrowColor(flowSample, flowUv)') -or
    !$corner.Contains('flowSample = ApplyConveyorArrowColor(flowSample, flowUv)')) {
    throw 'Straight, slope, seam and both corner flow paths must sample albedo and mask at identical animated UVs.'
}
Write-Output "PASS arrow-only mask ($marked pixels), all $materialCount surface materials, matching animated UVs and preview order"
