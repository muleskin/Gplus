# Renders the G+ logo (same family as Q+ / Z+ / N+): dark rounded tile, bold stroked "G", "+" top-right.
# Every size is drawn from vectors (not downscaled), so small icons stay crisp.
#   powershell -ExecutionPolicy Bypass -File tools\make-logo.ps1
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\src\Gplus\Assets'),
    [string]$Glyph  = '#EA4335',   # Gmail red
    [string]$Tile   = '#1F2024',
    [string]$Edge   = '#34363C'
)
Add-Type -AssemblyName System.Drawing
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Render([int]$size) {
    $s = $size / 256.0                        # design grid is 256x256
    $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)

    # Tile
    $m = 4 * $s; $w = $size - 2 * $m; $r = 44 * $s
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, $r, $r, 180, 90)
    $path.AddArc($m + $w - $r, $m, $r, $r, 270, 90)
    $path.AddArc($m + $w - $r, $m + $w - $r, $r, $r, 0, 90)
    $path.AddArc($m, $m + $w - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml($Tile))), $path)
    if ($size -ge 48) { $g.DrawPath((New-Object Drawing.Pen ([Drawing.ColorTranslator]::FromHtml($Edge)), (2 * $s)), $path) }

    $color = [Drawing.ColorTranslator]::FromHtml($Glyph)

    # "G": ring open at the upper right, plus the crossbar into the middle
    $pen = New-Object Drawing.Pen $color, (24 * $s)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $cx = 112 * $s; $cy = 140 * $s; $rad = 52 * $s
    $g.DrawArc($pen, $cx - $rad, $cy - $rad, 2 * $rad, 2 * $rad, 0, 318)
    $g.DrawLine($pen, $cx + $rad, $cy, $cx + 8 * $s, $cy)

    # "+"
    $plus = New-Object Drawing.Pen $color, (17 * $s)
    $plus.StartCap = 'Flat'; $plus.EndCap = 'Flat'
    $px = 196 * $s; $py = 94 * $s; $arm = 25 * $s
    $g.DrawLine($plus, $px - $arm, $py, $px + $arm, $py)
    $g.DrawLine($plus, $px, $py - $arm, $px, $py + $arm)

    $g.Dispose()
    return $bmp
}

function PngBytes($bmp) {
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

# Stand-alone PNGs
foreach ($sz in 256, 512) {
    $b = Render $sz
    $name = if ($sz -eq 256) { 'gplus-256.png' } else { 'gplus-512.png' }
    $b.Save((Join-Path $OutDir $name), [Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
}

# Multi-resolution .ico (PNG-compressed entries, supported since Vista)
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($sz in $sizes) { $b = Render $sz; , (PngBytes $b); $b.Dispose() }
$fs = [IO.File]::Create((Join-Path $OutDir 'gplus.ico'))
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$images[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()

Write-Host "Wrote gplus.ico, gplus-256.png, gplus-512.png to $OutDir"
