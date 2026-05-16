#!/usr/bin/env pwsh
# Generates the tray icons in src/DiscordHass/Ui/Icons/:
#   - tray.ico       — default (blurple), kept for backward compatibility
#   - tray-idle.ico  — gray, both sides not yet connected
#   - tray-ok.ico    — blurple, both sides connected
#   - tray-warn.ico  — amber, one side connecting/reconnecting
#   - tray-fault.ico — red, any side faulted
# Run from anywhere; output paths are resolved relative to this script.

[CmdletBinding()]
param()

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$iconsDir = Join-Path $repoRoot 'src/DiscordHass/Ui/Icons'

function New-RoundedPath {
    param([float]$x, [float]$y, [float]$w, [float]$h, [float]$r)
    $r = [Math]::Min($r, [Math]::Min($w, $h) / 2)
    $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x,           $y,           $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y,           $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d,   0, 90)
    $path.AddArc($x,           $y + $h - $d, $d, $d,  90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param(
        [int]$size,
        [System.Drawing.Color]$bgColor,
        [System.Drawing.Color]$accentColor
    )

    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $white = [System.Drawing.Color]::White

    # Rounded background
    $bgPath  = New-RoundedPath 0 0 $size $size ($size * 0.22)
    $bgBrush = New-Object System.Drawing.SolidBrush $bgColor
    $g.FillPath($bgBrush, $bgPath)
    $bgBrush.Dispose()
    $bgPath.Dispose()

    # Headphone band (white arc)
    $bandStroke = [Math]::Max(2.0, $size / 12.0)
    $pen = New-Object System.Drawing.Pen $white, $bandStroke
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $bandX = $size * 0.22
    $bandY = $size * 0.24
    $bandW = $size * 0.56
    $bandH = $size * 0.40
    $g.DrawArc($pen, $bandX, $bandY, $bandW, $bandH, 180, 180)
    $pen.Dispose()

    # Headphone ear cups (filled white rounded rectangles)
    $cupW = $size * 0.18
    $cupH = $size * 0.26
    $cupY = $size * 0.44
    $leftX  = $size * 0.18
    $rightX = $size - $leftX - $cupW
    $cupRad = $cupW * 0.45
    $leftCup  = New-RoundedPath $leftX  $cupY $cupW $cupH $cupRad
    $rightCup = New-RoundedPath $rightX $cupY $cupW $cupH $cupRad
    $whiteBrush = New-Object System.Drawing.SolidBrush $white
    $g.FillPath($whiteBrush, $leftCup)
    $g.FillPath($whiteBrush, $rightCup)
    $whiteBrush.Dispose()
    $leftCup.Dispose()
    $rightCup.Dispose()

    # Small accent-colored HA house silhouette in the bottom-right
    if ($size -ge 32) {
        $houseW = $size * 0.20
        $houseH = $size * 0.13
        $houseX = $size * 0.72
        $houseY = $size * 0.80

        $accentBrush = New-Object System.Drawing.SolidBrush $accentColor
        $roofPts = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new([float]($houseX - $houseW * 0.18), [float]$houseY),
            [System.Drawing.PointF]::new([float]($houseX + $houseW * 0.50), [float]($houseY - $houseH * 0.85)),
            [System.Drawing.PointF]::new([float]($houseX + $houseW * 1.18), [float]$houseY)
        )
        $g.FillPolygon($accentBrush, $roofPts)
        $body = [System.Drawing.RectangleF]::new([float]$houseX, [float]$houseY, [float]$houseW, [float]$houseH)
        $g.FillRectangle($accentBrush, $body)
        $accentBrush.Dispose()
    }

    $g.Dispose()
    return $bmp
}

function Write-IcoFile {
    param(
        [string]$outputPath,
        [System.Drawing.Color]$bgColor,
        [System.Drawing.Color]$accentColor
    )

    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $pngs  = @()

    foreach ($size in $sizes) {
        $bmp = New-IconBitmap -size $size -bgColor $bgColor -accentColor $accentColor
        $ms  = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs += ,($ms.ToArray())
        $ms.Dispose()
        $bmp.Dispose()
    }

    $null = New-Item -ItemType Directory -Force -Path (Split-Path $outputPath -Parent)
    $file = [System.IO.File]::Open($outputPath, [System.IO.FileMode]::Create)
    $bw   = New-Object System.IO.BinaryWriter $file
    try {
        # ICONDIR header
        $bw.Write([uint16]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]$sizes.Count)

        $imageOffset = 6 + $sizes.Count * 16
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $sz = $sizes[$i]
            $dim = if ($sz -ge 256) { 0 } else { $sz }
            $bw.Write([byte]$dim)
            $bw.Write([byte]$dim)
            $bw.Write([byte]0)
            $bw.Write([byte]0)
            $bw.Write([uint16]1)
            $bw.Write([uint16]32)
            $bw.Write([uint32]$pngs[$i].Length)
            $bw.Write([uint32]$imageOffset)
            $imageOffset += $pngs[$i].Length
        }

        foreach ($png in $pngs) { $bw.Write($png) }
    }
    finally {
        $bw.Dispose()
        $file.Dispose()
    }

    Write-Host "Wrote $outputPath ($([Math]::Round((Get-Item $outputPath).Length / 1KB, 1)) KB)"
}

# Variants: (filename, background color, accent color for the HA house)
$blurple = [System.Drawing.Color]::FromArgb(255,  88, 101, 242)   # Discord brand
$haBlue  = [System.Drawing.Color]::FromArgb(255,  24, 188, 242)   # Home Assistant brand
$gray    = [System.Drawing.Color]::FromArgb(255, 110, 113, 119)   # idle
$amber   = [System.Drawing.Color]::FromArgb(255, 224, 136,  26)   # warn / reconnecting
$red     = [System.Drawing.Color]::FromArgb(255, 178,  34,  34)   # fault

Write-IcoFile -outputPath (Join-Path $iconsDir 'tray.ico')       -bgColor $blurple -accentColor $haBlue
Write-IcoFile -outputPath (Join-Path $iconsDir 'tray-idle.ico')  -bgColor $gray    -accentColor $haBlue
Write-IcoFile -outputPath (Join-Path $iconsDir 'tray-ok.ico')    -bgColor $blurple -accentColor $haBlue
Write-IcoFile -outputPath (Join-Path $iconsDir 'tray-warn.ico')  -bgColor $amber   -accentColor $haBlue
Write-IcoFile -outputPath (Join-Path $iconsDir 'tray-fault.ico') -bgColor $red     -accentColor $haBlue
