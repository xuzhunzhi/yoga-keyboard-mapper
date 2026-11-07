<#
.SYNOPSIS
    Generates src\YogaKeyboardMapper\Assets\YogaKeyboardMapper.ico (the application icon).

.DESCRIPTION
    Blue rounded square with the white keyboard glyph (U+E765) from
    "Segoe Fluent Icons" (falls back to "Segoe MDL2 Assets").
    Every size is rendered natively so the small ones stay crisp
    (16, 20 and 32 px use a hand-hinted pixel copy of the glyph).
    Entries below 256 px are stored as 32-bit DIBs, the 256 px entry as PNG.

    Runs on Windows PowerShell 5.1 and PowerShell 7 (Windows):
        powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
        powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 -PreviewDir $env:TEMP

.PARAMETER OutFile
    Target .ico path. Defaults to src\YogaKeyboardMapper\Assets\YogaKeyboardMapper.ico in the repo.

.PARAMETER PreviewDir
    Optional folder for PNG previews (each size, plus an 8x enlarged contact sheet).

.PARAMETER PackageAssetsDir
    Optional folder for the MSIX logos referenced by Package.appxmanifest
    (normally src\YogaKeyboardMapper\Assets).
#>
param(
    [string]$OutFile = (Join-Path $PSScriptRoot '..\src\YogaKeyboardMapper\Assets\YogaKeyboardMapper.ico'),
    [string]$PreviewDir = '',
    [string]$PackageAssetsDir = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Pixel scanning is far too slow in script, so the ink bounds come from a tiny C# helper.
if (-not ('YogaKeyboardMapperIcon.Ink' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace YogaKeyboardMapperIcon
{
    public static class Ink
    {
        public static Rectangle Bounds(Bitmap bmp, byte threshold)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var bytes = new byte[data.Stride * bmp.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            bmp.UnlockBits(data);

            int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (bytes[y * data.Stride + x * 4 + 3] > threshold)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            return maxX < 0 ? Rectangle.Empty : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        // 0 = every pixel fully on or off; higher = more half-covered (blurry) pixels.
        public static double Blur(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var bytes = new byte[data.Stride * bmp.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            bmp.UnlockBits(data);

            double partial = 0, total = 1;
            for (int i = 3; i < bytes.Length; i += 4)
            {
                int a = bytes[i];
                partial += a < 255 - a ? a : 255 - a;
                total += a;
            }

            return partial / total;
        }
    }
}
'@
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$glyph = [string][char]0xE765
$topColor = [System.Drawing.Color]::FromArgb(255, 0x2B, 0x88, 0xD8)
$bottomColor = [System.Drawing.Color]::FromArgb(255, 0x00, 0x5A, 0x9E)

$installed = (New-Object System.Drawing.Text.InstalledFontCollection).Families | ForEach-Object { $_.Name }
$fontName = if ($installed -contains 'Segoe Fluent Icons') { 'Segoe Fluent Icons' } else { 'Segoe MDL2 Assets' }
if ($installed -notcontains $fontName) { throw "Neither Segoe Fluent Icons nor Segoe MDL2 Assets is installed." }
Write-Host "glyph font: $fontName"

# The font only renders cleanly at 16/32 px em. At 16 and 20 px no em size keeps the
# keys apart, and at 32 px the clean em (16) is too small, so these sizes use a
# hand-hinted copy of the same glyph ('#' = white, '+' = soft corner).
$pixelGlyphs = @{
    16 = @(
        '+#########+',
        '#.........#',
        '#.#.#.#.#.#',
        '#.........#',
        '#..#.#.#..#',
        '#.........#',
        '#..#####..#',
        '#.........#',
        '+#########+'
    )
    20 = @(
        '+###########+',
        '#...........#',
        '#..#.#.#.#..#',
        '#...........#',
        '#...#.#.#...#',
        '#...........#',
        '#..#######..#',
        '#...........#',
        '+###########+'
    )
    32 = @(
        '+##################+',
        '#..................#',
        '#..................#',
        '#..##..##..##..##..#',
        '#..##..##..##..##..#',
        '#..................#',
        '#..................#',
        '#....##..##..##....#',
        '#....##..##..##....#',
        '#..................#',
        '#..................#',
        '#....##########....#',
        '#..................#',
        '#..................#',
        '+##################+'
    )
}

function Add-PixelGlyph([System.Drawing.Bitmap]$bmp, [string[]]$rows, [int]$left, [int]$top) {
    for ($y = 0; $y -lt $rows.Count; $y++) {
        for ($x = 0; $x -lt $rows[$y].Length; $x++) {
            $alpha = switch ($rows[$y][$x]) { '#' { 1.0 } '+' { 0.45 } default { 0.0 } }
            if ($alpha -eq 0) { continue }
            $bg = $bmp.GetPixel($left + $x, $top + $y)
            $blend = { param($c) [int][Math]::Round($c + (255 - $c) * $alpha) }
            $bmp.SetPixel($left + $x, $top + $y, [System.Drawing.Color]::FromArgb($bg.A, (& $blend $bg.R), (& $blend $bg.G), (& $blend $bg.B)))
        }
    }
}

function New-RoundedRectPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $d = 2 * $r
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# Renders the glyph white on transparent and returns the bitmap plus its ink bounds.
function Get-GlyphBitmap([int]$canvas, [single]$emPixels) {
    $bmp = New-Object System.Drawing.Bitmap $canvas, $canvas, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $font = New-Object System.Drawing.Font $fontName, $emPixels, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $origin = [single][Math]::Floor($canvas / 4)
    $g.DrawString($glyph, $font, [System.Drawing.Brushes]::White, $origin, $origin, [System.Drawing.StringFormat]::GenericTypographic)
    $font.Dispose()
    $g.Dispose()

    $ink = [YogaKeyboardMapperIcon.Ink]::Bounds($bmp, 24)
    return [pscustomobject]@{
        Bitmap = $bmp
        X = $ink.X; Y = $ink.Y
        Width = $ink.Width; Height = $ink.Height
        Blur = [YogaKeyboardMapperIcon.Ink]::Blur($bmp)
    }
}

function New-IconBitmap([int]$n) {
    $bmp = New-Object System.Drawing.Bitmap $n, $n, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    # Background: rounded square with a small transparent margin.
    $margin = [Math]::Max(1, [Math]::Round($n * 0.06))
    $side = $n - 2 * $margin
    $radius = [single]($side * 0.22)
    $path = New-RoundedRectPath $margin $margin $side $side $radius
    $gradientRect = New-Object System.Drawing.RectangleF ($margin - 1), ($margin - 1), ($side + 2), ($side + 2)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $gradientRect, $topColor, $bottomColor, ([single]70)
    $g.FillPath($brush, $path)
    $brush.Dispose()
    $path.Dispose()

    if ($pixelGlyphs.ContainsKey($n)) {
        Add-PixelGlyph $bmp $pixelGlyphs[$n] ($margin + [Math]::Floor(($side - $pixelGlyphs[$n][0].Length) / 2)) ($margin + [Math]::Floor(($side - $pixelGlyphs[$n].Count) / 2))
        $g.Dispose()
        return $bmp
    }

    # Glyph: the font is only hinted cleanly at a few em sizes (16, 32 px), so small
    # icons take the crispest em whose ink is 50-70 % of the icon; larger icons
    # simply take the em closest to 60 %.
    $best = $null
    $lo = [Math]::Ceiling($n * 0.5)
    $hi = [Math]::Floor($n * 0.7)
    for ($em = $lo; $em -le $hi; $em++) {
        $candidate = Get-GlyphBitmap ($n * 2) ([single]$em)
        $better = $null -eq $best -or $(
            if ($n -le 48) { $candidate.Blur -lt $best.Blur - 0.01 }
            else { [Math]::Abs($candidate.Width - $n * 0.6) -lt [Math]::Abs($best.Width - $n * 0.6) })
        if ($better) {
            if ($null -ne $best) { $best.Bitmap.Dispose() }
            $best = $candidate
        }
        else {
            $candidate.Bitmap.Dispose()
        }
    }

    # Integer offsets keep the grid-fitted strokes on whole pixels.
    $dx = [int][Math]::Floor(($n - $best.Width) / 2) - $best.X
    $dy = [int][Math]::Floor(($n - $best.Height) / 2) - $best.Y
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.DrawImage($best.Bitmap, $dx, $dy, $best.Bitmap.Width, $best.Bitmap.Height)
    $best.Bitmap.Dispose()
    $g.Dispose()
    return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return , $bytes
}

# 32-bit BITMAPINFOHEADER + bottom-up BGRA pixels + an all-zero AND mask.
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $n = $bmp.Width
    $rect = New-Object System.Drawing.Rectangle 0, 0, $n, $n
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($data.Stride * $n)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)

    $maskStride = [int]([Math]::Floor(($n + 31) / 32) * 4)
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([int]40)
    $w.Write([int]$n)
    $w.Write([int]($n * 2))
    $w.Write([int16]1)
    $w.Write([int16]32)
    $w.Write([int]0)
    $w.Write([int]($n * $n * 4 + $maskStride * $n))
    $w.Write([int]0)
    $w.Write([int]0)
    $w.Write([int]0)
    $w.Write([int]0)
    for ($y = $n - 1; $y -ge 0; $y--) {
        $w.Write($pixels, $y * $data.Stride, $n * 4)
    }
    $w.Write((New-Object byte[] ($maskStride * $n)))
    $w.Flush()
    $bytes = $ms.ToArray()
    $w.Dispose()
    return , $bytes
}

$images = @()
foreach ($n in $sizes) {
    $bmp = New-IconBitmap $n
    $payload = if ($n -ge 256) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
    $images += [pscustomobject]@{ Size = $n; Bitmap = $bmp; Bytes = $payload }
}

$OutFile = [System.IO.Path]::GetFullPath($OutFile)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($OutFile)) | Out-Null
$fs = [System.IO.File]::Create($OutFile)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([int16]0)
$w.Write([int16]1)
$w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim)
    $w.Write([byte]$dim)
    $w.Write([byte]0)
    $w.Write([byte]0)
    $w.Write([int16]1)
    $w.Write([int16]32)
    $w.Write([int]$img.Bytes.Length)
    $w.Write([int]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) {
    $w.Write($img.Bytes)
}
$w.Dispose()
Write-Host "wrote $OutFile ($((Get-Item $OutFile).Length) bytes, sizes: $($sizes -join ', '))"

if ($PreviewDir) {
    [System.IO.Directory]::CreateDirectory($PreviewDir) | Out-Null
    foreach ($img in $images) {
        $img.Bitmap.Save((Join-Path $PreviewDir "yoga-keyboard-mapper_$($img.Size).png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }

    # Contact sheet: every size enlarged 8x (nearest neighbour), on light and dark strips.
    $small = $images | Where-Object { $_.Size -le 64 }
    $scale = 8
    $gap = 16
    $sheetW = $gap
    foreach ($img in $small) { $sheetW += $img.Size * $scale + $gap }
    $rowH = 64 * $scale + 2 * $gap
    $sheet = New-Object System.Drawing.Bitmap $sheetW, ($rowH * 2)
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 243, 243, 243))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 32, 32, 32))), 0, $rowH, $sheetW, $rowH)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    foreach ($row in 0, 1) {
        $x = $gap
        foreach ($img in $small) {
            $g.DrawImage($img.Bitmap, $x, $row * $rowH + $gap, $img.Size * $scale, $img.Size * $scale)
            $x += $img.Size * $scale + $gap
        }
    }
    $g.Dispose()
    $sheetPath = Join-Path $PreviewDir 'yoga-keyboard-mapper_sheet.png'
    $sheet.Save($sheetPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
    Write-Host "previews in $PreviewDir"
}

if ($PackageAssetsDir) {
    $PackageAssetsDir = [System.IO.Path]::GetFullPath($PackageAssetsDir)
    [System.IO.Directory]::CreateDirectory($PackageAssetsDir) | Out-Null

    # The icon rendered at $iconSize, centred on a transparent $w x $h canvas.
    function Save-PackageAsset([string]$name, [int]$w, [int]$h, [int]$iconSize) {
        $icon = New-IconBitmap $iconSize
        $canvas = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($canvas)
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImageUnscaled($icon, [int][Math]::Floor(($w - $iconSize) / 2), [int][Math]::Floor(($h - $iconSize) / 2))
        $g.Dispose()
        $icon.Dispose()
        $canvas.Save((Join-Path $PackageAssetsDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
        $canvas.Dispose()
    }

    foreach ($scale in 100, 200) {
        $f = $scale / 100
        Save-PackageAsset "Square44x44Logo.scale-$scale.png" (44 * $f) (44 * $f) (44 * $f)
        Save-PackageAsset "Square150x150Logo.scale-$scale.png" (150 * $f) (150 * $f) (100 * $f)
        Save-PackageAsset "StoreLogo.scale-$scale.png" (50 * $f) (50 * $f) (50 * $f)
        Save-PackageAsset "SplashScreen.scale-$scale.png" (620 * $f) (300 * $f) (150 * $f)
    }

    # Taskbar / Start / Explorer use the unplated target sizes; without them Windows
    # draws the 44 px logo on an accent-coloured plate.
    foreach ($t in 16, 24, 32, 48, 256) {
        $plated = Join-Path $PackageAssetsDir "Square44x44Logo.targetsize-$t.png"
        Save-PackageAsset (Split-Path $plated -Leaf) $t $t $t
        foreach ($suffix in 'altform-unplated', 'altform-lightunplated') {
            Copy-Item $plated (Join-Path $PackageAssetsDir "Square44x44Logo.targetsize-${t}_$suffix.png") -Force
        }
    }

    Write-Host "package assets in $PackageAssetsDir"
}

foreach ($img in $images) { $img.Bitmap.Dispose() }
