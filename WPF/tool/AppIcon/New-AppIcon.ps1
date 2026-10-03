# Builds MiniApps/MiniApps.ico: the "app grid" logo (three light tiles, one accent tile with a
# download arrow) at every size Windows asks for, each stored as PNG inside the .ico.
# Tiles snap to whole pixels at every size; icons below 32 px leave the arrow out so the mark stays sharp.
# Usage: ./tool/AppIcon/New-AppIcon.ps1   (writes MiniApps/MiniApps.ico and preview PNGs in artifacts/icon)
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\..\MiniApps\MiniApps.ico'),
    [string]$PreviewDir = (Join-Path $PSScriptRoot '..\..\artifacts\icon')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$tile = [Drawing.ColorTranslator]::FromHtml('#B9C9DD')
$accent = [Drawing.ColorTranslator]::FromHtml('#3B6EA8')
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256

function New-RoundedRect([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $d = [float]($r * 2)
    if ($d -le 0) { $path.AddRectangle((New-Object Drawing.RectangleF ([float]$x), ([float]$y), ([float]$w), ([float]$h))); return $path }
    $path.AddArc([float]$x, [float]$y, $d, $d, 180, 90)
    $path.AddArc([float]($x + $w - $d), [float]$y, $d, $d, 270, 90)
    $path.AddArc([float]($x + $w - $d), [float]($y + $h - $d), $d, $d, 0, 90)
    $path.AddArc([float]$x, [float]($y + $h - $d), $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$size) {
    $bitmap = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([Drawing.Color]::Transparent)
    # Whole-pixel layout at every size, so tile edges stay sharp: margin and gap of 6/64,
    # tiles of 23/64 rounded down, then centred.
    $margin = [Math]::Max(1, [Math]::Round($size * 6 / 64))
    $gap = [Math]::Max(2, [Math]::Round($size * 6 / 64))
    $side = [Math]::Floor(($size - 2 * $margin - $gap) / 2)
    $margin = [Math]::Floor(($size - 2 * $side - $gap) / 2)
    $radius = [Math]::Max(1, $side * 5 / 23)
    $origins = @($margin, ($margin + $side + $gap))
    foreach ($row in 0, 1) {
        foreach ($column in 0, 1) {
            $isAccent = $row -eq 1 -and $column -eq 1
            $brush = New-Object Drawing.SolidBrush ($(if ($isAccent) { $accent } else { $tile }))
            $path = New-RoundedRect $origins[$column] $origins[$row] $side $side $radius
            $g.FillPath($brush, $path)
            $path.Dispose(); $brush.Dispose()
        }
    }
    # The arrow needs a tile of 11 px (the 32 px icon) to stay readable; smaller icons show the tiles only.
    if ($side -ge 11) {
        $stroke = [Math]::Max(2, [Math]::Round($side * 3 / 23))
        $x0 = $origins[1]; $y0 = $origins[1]
        # An odd stroke is centred on a pixel, an even one on a pixel edge.
        $cx = [Math]::Floor($x0 + $side / 2) + $(if ($stroke % 2) { 0.5 } else { 0 })
        $half = $side * 5 / 23
        $pen = New-Object Drawing.Pen ([Drawing.Color]::White), ([float]$stroke)
        $pen.StartCap = [Drawing.Drawing2D.LineCap]::Round; $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
        $g.DrawLine($pen, [float]$cx, [float]($y0 + $side * 5.5 / 23), [float]$cx, [float]($y0 + $side * 16.5 / 23))
        $g.DrawLines($pen, [Drawing.PointF[]]@(
            (New-Object Drawing.PointF ([float]($cx - $half)), ([float]($y0 + $side * 12 / 23))),
            (New-Object Drawing.PointF ([float]$cx), ([float]($y0 + $side * 17 / 23))),
            (New-Object Drawing.PointF ([float]($cx + $half)), ([float]($y0 + $side * 12 / 23)))))
        $pen.Dispose()
    }
    $g.Dispose()
    return $bitmap
}

New-Item -ItemType Directory -Force -Path $PreviewDir | Out-Null
$images = foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Save((Join-Path $PreviewDir "icon-$size.png"), [Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
    $stream.Dispose()
}

# ICO: 6-byte header, one 16-byte entry per image, then the PNG data.
$file = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter $file
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $edge = if ($image.Size -ge 256) { 0 } else { $image.Size }
    $writer.Write([byte]$edge); $writer.Write([byte]$edge); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$image.Bytes.Length); $writer.Write([UInt32]$offset)
    $offset += $image.Bytes.Length
}
foreach ($image in $images) { $writer.Write($image.Bytes) }
$writer.Flush()
[IO.File]::WriteAllBytes([IO.Path]::GetFullPath($Output), $file.ToArray())
$writer.Dispose()
Write-Output "Wrote $([IO.Path]::GetFullPath($Output)) ($($images.Count) sizes: $($sizes -join ', '))"
