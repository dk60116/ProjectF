param(
    [ValidateRange(0.1, 1.0)][double]$Exponent = 0.65,
    [switch]$Apply,
    [string]$ProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')),
    [string]$BackupRoot = (Join-Path $env:USERPROFILE '.codex/asset-backups/ProjectF/monster-texture-brightness')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies @([System.Drawing.Bitmap].Assembly.Location, 'System.Drawing.Primitives',
    (Join-Path $PSHOME 'System.Private.Windows.GdiPlus.dll'), (Join-Path $PSHOME 'System.Private.Windows.Core.dll')) -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class MonsterTextureBrightness
{
    public static double[] Convert(string source, string destination, double exponent)
    {
        using (var input = new Bitmap(source))
        using (var image = input.Clone(new Rectangle(0, 0, input.Width, input.Height), PixelFormat.Format32bppArgb))
        {
            var bounds = new Rectangle(0, 0, image.Width, image.Height);
            var data = image.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            byte[] original = new byte[Math.Abs(data.Stride) * image.Height];
            byte[] corrected;
            double before = 0, after = 0;
            long samples = 0;
            try
            {
                Marshal.Copy(data.Scan0, original, 0, original.Length);
                corrected = (byte[])original.Clone();
                double[] scales = new double[256];
                // Map brightness, not each channel independently, to retain the original hue/saturation.
                for (int value = 1; value < 256; value++)
                    scales[value] = Math.Pow(value / 255.0, exponent) * 255.0 / value;
                for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    int i = y * Math.Abs(data.Stride) + x * 4;
                    int maximum = Math.Max(original[i], Math.Max(original[i + 1], original[i + 2]));
                    if (original[i + 3] == 0 || maximum == 0) continue;
                    for (int channel = 0; channel < 3; channel++)
                        corrected[i + channel] = (byte)Math.Min(255, Math.Round(original[i + channel] * scales[maximum]));
                    if (maximum > 16)
                    {
                        before += .0722 * original[i] + .7152 * original[i + 1] + .2126 * original[i + 2];
                        samples++;
                    }
                }
                Marshal.Copy(corrected, 0, data.Scan0, corrected.Length);
            }
            finally { image.UnlockBits(data); }

            bool jpeg = destination.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);
            if (jpeg)
            {
                ImageCodecInfo encoder = null;
                foreach (var codec in ImageCodecInfo.GetImageEncoders())
                    if (codec.FormatID == ImageFormat.Jpeg.Guid) encoder = codec;
                using (var parameters = new EncoderParameters(1))
                {
                    parameters.Param[0] = new EncoderParameter(Encoder.Quality, 100L);
                    image.Save(destination, encoder, parameters);
                }
            }
            else image.Save(destination, ImageFormat.Png);

            using (var saved = new Bitmap(destination))
            using (var verified = saved.Clone(bounds, PixelFormat.Format32bppArgb))
            {
                if (saved.Width != input.Width || saved.Height != input.Height)
                    throw new InvalidOperationException("Texture dimensions changed: " + destination);
                var read = verified.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] pixels = new byte[Math.Abs(read.Stride) * verified.Height];
                    Marshal.Copy(read.Scan0, pixels, 0, pixels.Length);
                    for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++)
                    {
                        int i = y * Math.Abs(data.Stride) + x * 4;
                        int j = y * Math.Abs(read.Stride) + x * 4;
                        if (original[i + 3] != pixels[j + 3])
                            throw new InvalidOperationException("Texture alpha changed: " + destination);
                        for (int c = 0; !jpeg && c < 3; c++)
                            if (pixels[j + c] != corrected[i + c])
                                throw new InvalidOperationException("Lossless pixel validation failed: " + destination);
                        int maximum = Math.Max(original[i], Math.Max(original[i + 1], original[i + 2]));
                        if (original[i + 3] != 0 && maximum > 16)
                            after += .0722 * pixels[j] + .7152 * pixels[j + 1] + .2126 * pixels[j + 2];
                    }
                }
                finally { verified.UnlockBits(read); }
            }
            if (samples == 0 || after <= before) throw new InvalidOperationException("Texture did not brighten: " + source);
            return new[] { (double)input.Width, input.Height, before / samples, after / samples };
        }
    }
}
'@

$pack = Join-Path $ProjectRoot 'FactorioProject/Assets/Level 1 Monster Pack'
$images = Join-Path $pack 'Images'
$files = @(Get-ChildItem -LiteralPath $images -Recurse -File | Where-Object { $_.Extension -in '.jpg', '.png' } | Sort-Object FullName)
if ($files.Count -ne 16) { throw "Expected 16 monster textures, found $($files.Count)." }
$protected = @(Get-ChildItem -LiteralPath (Join-Path $pack 'Materials') -Recurse -File)
$protected += @(Get-ChildItem -LiteralPath $images -Recurse -File -Filter '*.meta')
$protectedHashes = @{}
foreach ($file in $protected) { $protectedHashes[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash }
$manifestPath = Join-Path $BackupRoot 'manifest.json'
$previous = @{}
if (Test-Path -LiteralPath $manifestPath) {
    foreach ($entry in (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json)) { $previous[$entry.RelativePath] = $entry }
}
$entries = @()
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($images, $file.FullName)
    $original = Join-Path (Join-Path $BackupRoot 'Originals') $relative
    $corrected = Join-Path (Join-Path $BackupRoot 'Corrected') $relative
    if (Test-Path -LiteralPath $original) {
        $currentHash = (Get-FileHash -LiteralPath $file.FullName).Hash
        $originalHash = (Get-FileHash -LiteralPath $original).Hash
        if ($previous.ContainsKey($relative) -and $originalHash -ne $previous[$relative].OriginalHash) {
            throw "Backup was modified: $original"
        }
        $appliedHash = if ($previous.ContainsKey($relative)) {
            if ($previous[$relative].PSObject.Properties['AppliedHash']) { $previous[$relative].AppliedHash }
            else { $previous[$relative].ResultHash }
        } else { $null }
        if ($currentHash -ne $originalHash -and $currentHash -ne $appliedHash) {
            throw "Texture changed outside this correction. Preserve it before starting a new baseline: $($file.FullName)"
        }
    }
    $entries += [pscustomobject]@{ RelativePath = $relative; Source = $file.FullName; Original = $original; Corrected = $corrected }
}

$report = @()
foreach ($entry in $entries) {
    foreach ($path in @($entry.Original, $entry.Corrected)) {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    }
    if (!(Test-Path -LiteralPath $entry.Original)) { Copy-Item -LiteralPath $entry.Source -Destination $entry.Original }
    $metrics = [MonsterTextureBrightness]::Convert($entry.Original, $entry.Corrected, $Exponent)
    $report += [pscustomobject]@{
        RelativePath = $entry.RelativePath; Exponent = $Exponent
        Width = $metrics[0]; Height = $metrics[1]; MeanBefore = $metrics[2]; MeanAfter = $metrics[3]
        OriginalHash = (Get-FileHash -LiteralPath $entry.Original).Hash
        ResultHash = (Get-FileHash -LiteralPath $entry.Corrected).Hash
        AppliedHash = (Get-FileHash -LiteralPath $entry.Source).Hash
        AlphaUnchanged = $true
    }
    Write-Output ('{0}: {1}x{2}, mean brightness {3:F1} -> {4:F1}' -f $entry.RelativePath, $metrics[0], $metrics[1], $metrics[2], $metrics[3])
}
$comparisonPath = Join-Path $BackupRoot 'Comparison.png'
$sheet = [Drawing.Bitmap]::new(1536, 896)
$graphics = [Drawing.Graphics]::FromImage($sheet)
$font = [Drawing.Font]::new('Arial', 9)
try {
    $graphics.Clear([Drawing.Color]::FromArgb(32, 32, 32))
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    for ($i = 0; $i -lt $entries.Count; $i++) {
        $x = ($i % 4) * 384
        $y = [int][Math]::Floor($i / 4) * 224
        $graphics.DrawString($entries[$i].RelativePath, $font, [Drawing.Brushes]::White, $x + 4, $y + 2)
        $side = 0
        foreach ($path in @($entries[$i].Original, $entries[$i].Corrected)) {
            $bitmap = [Drawing.Bitmap]::new($path)
            try { $graphics.DrawImage($bitmap, [Drawing.Rectangle]::new($x + $side * 192, $y + 20, 192, 192)) }
            finally { $bitmap.Dispose() }
            $label = if ($side -eq 0) { 'Original' } else { 'Brightened' }
            $graphics.DrawString($label, $font, [Drawing.Brushes]::White, $x + $side * 192 + 4, $y + 210)
            $side++
        }
    }
    $sheet.Save($comparisonPath, [Drawing.Imaging.ImageFormat]::Png)
}
finally { $font.Dispose(); $graphics.Dispose(); $sheet.Dispose() }
if ($Apply) {
    foreach ($entry in $entries) {
        Copy-Item -LiteralPath $entry.Corrected -Destination $entry.Source
        if ((Get-FileHash -LiteralPath $entry.Source).Hash -ne (Get-FileHash -LiteralPath $entry.Corrected).Hash) {
            throw "Applied texture differs from the validated correction: $($entry.Source)"
        }
    }
    foreach ($item in $report) { $item.AppliedHash = $item.ResultHash }
}
foreach ($path in $protectedHashes.Keys) {
    if ((Get-FileHash -LiteralPath $path).Hash -ne $protectedHashes[$path]) { throw "Protected material/import metadata changed: $path" }
}
$report | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output "Validated 16 textures; materials and import metadata unchanged. Applied: $([bool]$Apply). Backup: $BackupRoot"
Write-Output "Comparison: $comparisonPath"
