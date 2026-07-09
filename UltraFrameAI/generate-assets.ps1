$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $projectDir
$assetsDir = Join-Path $projectDir 'Assets'
if (-not (Test-Path $assetsDir)) {
    New-Item -ItemType Directory -Path $assetsDir | Out-Null
}

$imagesDir = Join-Path $projectDir 'images'
if (-not (Test-Path $imagesDir)) {
    New-Item -ItemType Directory -Path $imagesDir | Out-Null
}

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeMethods {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
"@

function New-GradientBitmap {
    param(
        [int]$Width,
        [int]$Height,
        [string]$Path
    )

    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.Clear([System.Drawing.Color]::FromArgb(14, 22, 40))

        $rect = New-Object System.Drawing.Rectangle 0, 0, $Width, $Height
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $rect,
            [System.Drawing.Color]::FromArgb(24, 35, 61),
            [System.Drawing.Color]::FromArgb(15, 23, 42),
            35
        )
        $g.FillRectangle($brush, $rect)

        $glowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(44, 77, 163, 255))
        $g.FillEllipse($glowBrush, [int]($Width * 0.62), [int]($Height * 0.08), [int]($Width * 0.25), [int]($Height * 0.32))

        $ringPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(220, 77, 163, 255), [Math]::Max(6, [int]($Width * 0.03)))
        $ringPen.Alignment = [System.Drawing.Drawing2D.PenAlignment]::Inset
        $ringRect = New-Object System.Drawing.Rectangle ([int]($Width * 0.18)), ([int]($Height * 0.18)), ([int]($Width * 0.46)), ([int]($Height * 0.46))
        $g.DrawEllipse($ringPen, $ringRect)

        $arrowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(246, 248, 250))
        $points = @(
            New-Object System.Drawing.Point ([int]($Width * 0.38)), ([int]($Height * 0.30))
            New-Object System.Drawing.Point ([int]($Width * 0.56)), ([int]($Height * 0.48))
            New-Object System.Drawing.Point ([int]($Width * 0.45)), ([int]($Height * 0.48))
            New-Object System.Drawing.Point ([int]($Width * 0.45)), ([int]($Height * 0.62))
            New-Object System.Drawing.Point ([int]($Width * 0.31)), ([int]($Height * 0.62))
            New-Object System.Drawing.Point ([int]($Width * 0.31)), ([int]($Height * 0.48))
            New-Object System.Drawing.Point ([int]($Width * 0.20)), ([int]($Height * 0.48))
        )
        $g.FillPolygon($arrowBrush, $points)

        $accentPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 77, 163, 255), [Math]::Max(3, [int]($Width * 0.015)))
        $accentPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $accentPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLine($accentPen, [int]($Width * 0.18), [int]($Height * 0.74), [int]($Width * 0.58), [int]($Height * 0.74))
    }
    finally {
        $g.Dispose()
    }

    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Copy-ResizedBitmap {
    param(
        [string]$SourcePath,
        [string]$DestinationPath,
        [int]$MaxSize = 800
    )

    $src = [System.Drawing.Image]::FromFile($SourcePath)
    try {
        $scale = [Math]::Min($MaxSize / [double]$src.Width, $MaxSize / [double]$src.Height)
        $width = [Math]::Max(1, [int][Math]::Round($src.Width * $scale))
        $height = [Math]::Max(1, [int][Math]::Round($src.Height * $scale))

        $bmp = New-Object System.Drawing.Bitmap $width, $height
        $bmp.SetResolution($src.HorizontalResolution, $src.VerticalResolution)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.DrawImage($src, 0, 0, $width, $height)
        }
        finally {
            $g.Dispose()
        }

        $bmp.Save($DestinationPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }
    finally {
        $src.Dispose()
    }
}

function New-IconFile {
    param(
        [string]$Path,
        [string]$PngPath
    )

    $pythonScript = @"
from pathlib import Path
from PIL import Image

source = Path(r"$PngPath")
target = Path(r"$Path")

image = Image.open(source).convert("RGBA")
image.save(target, format="ICO", sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
"@

    $pythonScript | python -
}

function New-FlagBitmap {
    param(
        [string]$Path,
        [string]$Country
    )

    $bmp = New-Object System.Drawing.Bitmap 96, 64
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        switch ($Country) {
            'en' {
                $g.Clear([System.Drawing.Color]::White)
                $stripeH = 5
                for ($i = 0; $i -lt 13; $i++) {
                    if ($i % 2 -eq 0) {
                        $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(178, 34, 52))), 0, ($i * $stripeH), 96, $stripeH)
                    }
                }
                $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(10, 49, 97))), 0, 0, 38, 34)
                for ($y = 0; $y -lt 5; $y++) {
                    for ($x = 0; $x -lt 6; $x++) {
                        $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)), 4 + ($x * 6), 4 + ($y * 6), 2, 2)
                    }
                }
            }
            'ru' {
                $g.Clear([System.Drawing.Color]::White)
                $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)), 0, 0, 96, 21)
                $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 57, 166))), 0, 21, 96, 21)
                $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(213, 43, 30))), 0, 42, 96, 22)
            }
            'de' {
                $g.Clear([System.Drawing.Color]::Black)
                $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Black)), 0, 0, 96, 21)
                $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(221, 0, 0))), 0, 21, 96, 21)
                $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 206, 0))), 0, 42, 96, 22)
            }
            'ja' {
                $g.Clear([System.Drawing.Color]::White)
                $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(188, 0, 45))), 33, 17, 30, 30)
            }
            'zh' {
                $g.Clear([System.Drawing.Color]::FromArgb(222, 41, 16))
                $starBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 222, 0))
                $g.FillEllipse($starBrush, 13, 10, 12, 12)
                $g.FillEllipse($starBrush, 31, 8, 5, 5)
                $g.FillEllipse($starBrush, 39, 16, 5, 5)
                $g.FillEllipse($starBrush, 39, 27, 5, 5)
                $g.FillEllipse($starBrush, 31, 36, 5, 5)
            }
            default {
                throw "Unknown flag country: $Country"
            }
        }
        $border = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(120, 255, 255, 255), 1)
        $g.DrawRectangle($border, 0, 0, 95, 63)
    }
    finally {
        $g.Dispose()
    }

    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Copy-Item -LiteralPath (Join-Path $repoRoot 'images\icon.png') -Destination (Join-Path $imagesDir 'icon.png') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'images\logo.png') -Destination (Join-Path $imagesDir 'logo.png') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'images\splashscreen.png') -Destination (Join-Path $imagesDir 'splashscreen.png') -Force
New-IconFile -Path (Join-Path $assetsDir 'UltraFrameAI.ico') -PngPath (Join-Path $imagesDir 'icon.png')
New-FlagBitmap -Path (Join-Path $imagesDir 'flag-en.png') -Country 'en'
New-FlagBitmap -Path (Join-Path $imagesDir 'flag-ru.png') -Country 'ru'
New-FlagBitmap -Path (Join-Path $imagesDir 'flag-de.png') -Country 'de'
New-FlagBitmap -Path (Join-Path $imagesDir 'flag-ja.png') -Country 'ja'
New-FlagBitmap -Path (Join-Path $imagesDir 'flag-zh.png') -Country 'zh'

Write-Host "Generated assets in $assetsDir"
