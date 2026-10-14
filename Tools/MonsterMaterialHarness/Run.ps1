param([string]$SourceBackup)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assets = Join-Path $repo 'FactorioProject/Assets'
$pack = Join-Path $assets 'Level 1 Monster Pack'
$shaderPath = Join-Path $assets 'Shaders/ToonCharacter.shader'
$shader = [IO.File]::ReadAllText($shaderPath)
$shaderGuid = [regex]::Match([IO.File]::ReadAllText("$shaderPath.meta"), '(?m)^guid: (\w+)').Groups[1].Value
$script:checks = 0
function Require([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
    $script:checks++
}
function Property([string]$source, [string]$name) {
    $match = [regex]::Match($source, '(?m)^    - ' + [regex]::Escape($name) + ': ([^\r\n]+)')
    if (!$match.Success) { throw "Missing property: $name" }
    $match.Groups[1].Value
}
function TextureEntry([string]$source, [string]$name) {
    $match = [regex]::Match($source.Replace("`r`n", "`n"),
        '(?s)    - ' + [regex]::Escape($name) + ':\n(.*?)(?=\n    -|\n    m_Ints:)')
    if (!$match.Success) { throw "Missing texture entry: $name" }
    $match.Groups[1].Value
}
Require ($shader.StartsWith('Shader "Custom/ToonCharacter"')) 'Unexpected target shader'
Require ($shader.Contains('"RenderPipeline" = "UniversalPipeline"')) 'Shader is not URP-compatible'
Require ($shader.Contains('return half4(finalColor, baseSample.a);')) 'Alpha blend assumptions need review'
$assetPaths = @{}
Get-ChildItem -LiteralPath $pack -Filter '*.meta' -Recurse | ForEach-Object {
    $guid = [regex]::Match([IO.File]::ReadAllText($_.FullName), '(?m)^guid: (\w+)').Groups[1].Value
    if ($guid) { $assetPaths[$guid] = $_.FullName.Substring(0, $_.FullName.Length - 5) }
}
$materials = @(Get-ChildItem -LiteralPath (Join-Path $pack 'Materials') -Filter '*.mat' -Recurse)
Require ($materials.Count -eq 20) 'Expected all 20 monster materials'
$materialGuids = @{}
$transparentCount = 0
foreach ($file in $materials) {
    $source = [IO.File]::ReadAllText($file.FullName)
    $guid = [regex]::Match([IO.File]::ReadAllText("$($file.FullName).meta"), '(?m)^guid: (\w+)').Groups[1].Value
    $materialGuids[$guid] = $file.FullName
    Require ($source.Contains("m_Shader: {fileID: 4800000, guid: $shaderGuid, type: 3}")) "$($file.Name): wrong shader"
    Require ($source.Contains('m_Parent: {fileID: 0}')) "$($file.Name): unexpected inherited properties"
    Require ($source.Contains('m_ValidKeywords: []') -and $source.Contains('m_InvalidKeywords: []')) 'Obsolete shader keywords remain'
    $baseMap = TextureEntry $source '_BaseMap'
    $textureGuid = [regex]::Match($baseMap, 'guid: (\w+)').Groups[1].Value
    if ($textureGuid) { Require ($assetPaths.ContainsKey($textureGuid)) "$($file.Name): missing albedo texture" }
    $ghost = $file.Directory.Name -eq 'Ghost'
    Require ((Property $source '_Surface') -eq $(if ($ghost) { '1' } else { '0' })) 'Wrong surface mode'
    Require ((Property $source '_SrcBlend') -eq $(if ($ghost) { '5' } else { '1' })) 'Wrong RGB blend source'
    Require ((Property $source '_DstBlend') -eq $(if ($ghost) { '10' } else { '0' })) 'Wrong RGB blend destination'
    Require ((Property $source '_SrcBlendAlpha') -eq '1') 'Wrong alpha blend source'
    Require ((Property $source '_DstBlendAlpha') -eq $(if ($ghost) { '10' } else { '0' })) 'Wrong alpha blend destination'
    Require ((Property $source '_ZWrite') -eq $(if ($ghost) { '0' } else { '1' })) 'Wrong depth write mode'
    Require ((Property $source '_AlphaClip') -eq '0') 'Continuous ghost alpha must not be clipped'
    Require ($source.Contains('m_CustomRenderQueue: ' + $(if ($ghost) { '3000' } else { '2000' }))) 'Wrong render queue'
    Require ($source.Contains('RenderType: ' + $(if ($ghost) { 'Transparent' } else { 'Opaque' }))) 'Wrong render type'
    if ($ghost) {
        $transparentCount++
        foreach ($pass in @('ShadowCaster', 'DepthOnly', 'DepthNormals')) {
            Require ($source.Contains("  - $pass")) "Transparent monster must disable $pass"
        }
    } else { Require ($source.Contains('disabledShaderPasses: []')) 'Opaque monster passes must remain enabled' }
    if ($SourceBackup) {
        $originalPath = Join-Path $SourceBackup ([IO.Path]::GetRelativePath($pack, $file.FullName))
        $original = [IO.File]::ReadAllText($originalPath)
        Require ($baseMap -eq (TextureEntry $original '_MainTex')) 'Texture/tiling/offset changed during conversion'
        Require ((Property $source '_BaseColor') -eq (Property $original '_Color')) 'Source tint changed during conversion'
        Require ((Get-FileHash -LiteralPath "$originalPath.meta").Hash -eq
            (Get-FileHash -LiteralPath "$($file.FullName).meta").Hash) 'Material GUID/import settings changed'
    }
}
Require ($transparentCount -eq 5) 'Expected five transparent ghost materials'
$prefabs = @(Get-ChildItem -LiteralPath (Join-Path $pack 'Prefabs') -Filter '*.prefab' -Recurse)
Require ($prefabs.Count -eq 16) 'Expected sixteen monster variants'
foreach ($prefab in $prefabs) {
    $source = [IO.File]::ReadAllText($prefab.FullName)
    $references = [regex]::Matches($source, 'fileID: 2100000, guid: (\w+)')
    Require ($references.Count -gt 0) "$($prefab.Name): no external material assignment"
    foreach ($reference in $references) {
        Require ($materialGuids.ContainsKey($reference.Groups[1].Value)) "$($prefab.Name): unresolved material"
    }
}
Write-Output "Monster material checks passed: $script:checks (20 ToonCharacter materials, 16 prefabs)"
