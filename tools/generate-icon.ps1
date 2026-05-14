#!/usr/bin/env pwsh
# Generates src/DiscordHass/Ui/Icons/tray.ico — a multi-resolution Windows icon.
# Run from anywhere; the output path is resolved relative to this script.

[CmdletBinding()]
param()

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoRoot 'src/DiscordHass/Ui/Icons/tray.ico'

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
    param([int]$size)

    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $blurple = [System.Drawing.Color]::FromArgb(255,  88, 101, 242)  # Discord brand
    $haBlue  = [System.Drawing.Color]::FromArgb(255,  24, 188, 242)  # Home Assistant brand
    $white   = [System.Drawing.Color]::White

    # Rounded blurple background
    $bgPath  = New-RoundedPath 0 0 $size $size ($size * 0.22)
    $bgBrush = New-Object System.Drawing.SolidBrush $blurple
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

    # Small HA house silhouette in the bottom-right (only at sizes where it reads)
    if ($size -ge 32) {
        $houseW = $size * 0.20
        $houseH = $size * 0.13
        $houseX = $size * 0.72
        $houseY = $size * 0.80

        $haBrush = New-Object System.Drawing.SolidBrush $haBlue
        # roof — equilateral triangle slightly wider than the body
        $roofPts = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new([float]($houseX - $houseW * 0.18), [float]$houseY),
            [System.Drawing.PointF]::new([float]($houseX + $houseW * 0.50), [float]($houseY - $houseH * 0.85)),
            [System.Drawing.PointF]::new([float]($houseX + $houseW * 1.18), [float]$houseY)
        )
        $g.FillPolygon($haBrush, $roofPts)
        # body
        $body = [System.Drawing.RectangleF]::new([float]$houseX, [float]$houseY, [float]$houseW, [float]$houseH)
        $g.FillRectangle($haBrush, $body)
        $haBrush.Dispose()
    }

    $g.Dispose()
    return $bmp
}

# Sizes baked into the .ico. 16/24/32 cover system tray + taskbar; 48/64 explorer; 256 modern UI.
$sizes    = @(16, 24, 32, 48, 64, 128, 256)
$pngs     = @()

foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $ms  = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,($ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
}

$null  = New-Item -ItemType Directory -Force -Path (Split-Path $outputPath -Parent)
$file  = [System.IO.File]::Open($outputPath, [System.IO.FileMode]::Create)
$bw    = New-Object System.IO.BinaryWriter $file
try {
    # ICONDIR header
    $bw.Write([uint16]0)             # reserved
    $bw.Write([uint16]1)             # type ICO (1) vs CUR (2)
    $bw.Write([uint16]$sizes.Count)  # number of images

    # ICONDIRENTRY block
    $imageOffset = 6 + $sizes.Count * 16
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $sz = $sizes[$i]
        $dim = if ($sz -ge 256) { 0 } else { $sz }
        $bw.Write([byte]$dim)             # width  (0 means 256)
        $bw.Write([byte]$dim)             # height (0 means 256)
        $bw.Write([byte]0)                # palette colors
        $bw.Write([byte]0)                # reserved
        $bw.Write([uint16]1)              # color planes
        $bw.Write([uint16]32)             # bits per pixel
        $bw.Write([uint32]$pngs[$i].Length)  # image data size
        $bw.Write([uint32]$imageOffset)      # offset from start of file
        $imageOffset += $pngs[$i].Length
    }

    # Image data
    foreach ($png in $pngs) {
        $bw.Write($png)
    }
}
finally {
    $bw.Dispose()
    $file.Dispose()
}

Write-Host "Wrote $outputPath ($([Math]::Round((Get-Item $outputPath).Length / 1KB, 1)) KB, $($sizes.Count) sizes)"
