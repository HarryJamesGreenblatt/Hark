# Generates the HARK icon set (the Oracle's eye) — an .ico for the exe/tray and the MSIX tile PNGs.
# Pure System.Drawing so it runs anywhere with .NET on Windows; no external assets.
# Re-run whenever the brand mark changes: pwsh -NoProfile -File Hark.App/Scripts/Generate-Icon.ps1

Add-Type -AssemblyName System.Drawing

$assetsDir = Join-Path $PSScriptRoot '..\Assets'
$tilesDir  = Join-Path $assetsDir 'MsixTiles'
New-Item -ItemType Directory -Force -Path $assetsDir, $tilesDir | Out-Null

# ── Draw the Oracle's eye centered in a (width x height) transparent canvas ──
function New-EyeBitmap {
    param([int]$Width, [int]$Height, [switch]$Plate)

    $bmp = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $side = [Math]::Min($Width, $Height)
    $cx = $Width / 2.0
    $cy = $Height / 2.0
    $R  = $side * 0.46            # outer metallic ring radius

    function EllipseRect([double]$r) {
        return New-Object System.Drawing.RectangleF(($cx - $r), ($cy - $r), (2 * $r), (2 * $r))
    }

    # Optional dark rounded plate behind the eye (used for Store/Square tiles that want a fill).
    if ($Plate) {
        $plateRect = New-Object System.Drawing.RectangleF(0, 0, $Width, $Height)
        $plateBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $plateRect,
            [System.Drawing.Color]::FromArgb(255, 18, 20, 24),
            [System.Drawing.Color]::FromArgb(255, 8, 9, 11),
            [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
        $g.FillRectangle($plateBrush, $plateRect)
        $plateBrush.Dispose()
    }

    # Outer metallic ring — vertical steel gradient.
    $ringRect = EllipseRect $R
    $ring = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $ringRect,
        [System.Drawing.Color]::FromArgb(255, 120, 128, 138),
        [System.Drawing.Color]::FromArgb(255, 40, 44, 50),
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillEllipse($ring, $ringRect)
    $ring.Dispose()

    # Dark bezel inside the ring.
    $bezelR = $R * 0.82
    $bezelRect = EllipseRect $bezelR
    $bezel = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $bezelRect,
        [System.Drawing.Color]::FromArgb(255, 28, 30, 34),
        [System.Drawing.Color]::FromArgb(255, 12, 13, 15),
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillEllipse($bezel, $bezelRect)
    $bezel.Dispose()

    # Near-black socket.
    $socketR = $R * 0.70
    $g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 6, 6, 7))), (EllipseRect $socketR))

    # Red glowing core — radial gradient (hot white-red center -> deep red edge).
    $coreR = $R * 0.60
    $coreRect = EllipseRect $coreR
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($coreRect)
    $core = New-Object System.Drawing.Drawing2D.PathGradientBrush($path)
    $core.CenterPoint = New-Object System.Drawing.PointF($cx, ($cy + $coreR * 0.08))
    $core.CenterColor = [System.Drawing.Color]::FromArgb(255, 255, 236, 232)
    $core.SurroundColors = @([System.Drawing.Color]::FromArgb(255, 150, 10, 4))
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend(3)
    $blend.Colors    = @(
        [System.Drawing.Color]::FromArgb(255, 120, 6, 3),
        [System.Drawing.Color]::FromArgb(255, 255, 40, 26),
        [System.Drawing.Color]::FromArgb(255, 255, 236, 232))
    $blend.Positions = @(0.0, 0.55, 1.0)
    $core.InterpolationColors = $blend
    $g.FillEllipse($core, $coreRect)
    $core.Dispose()
    $path.Dispose()

    # Top-only specular gloss confined to the upper core.
    $glossR = $coreR * 0.9
    $glossRect = New-Object System.Drawing.RectangleF(($cx - $glossR), ($cy - $coreR * 0.95), (2 * $glossR), ($coreR * 0.85))
    $glossPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glossPath.AddEllipse($glossRect)
    $gloss = New-Object System.Drawing.Drawing2D.PathGradientBrush($glossPath)
    $gloss.CenterPoint = New-Object System.Drawing.PointF($cx, ($cy - $coreR * 0.45))
    $gloss.CenterColor = [System.Drawing.Color]::FromArgb(120, 255, 255, 255)
    $gloss.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.FillEllipse($gloss, $glossRect)
    $gloss.Dispose()
    $glossPath.Dispose()

    $g.Dispose()
    return $bmp
}

function Save-Png {
    param([System.Drawing.Bitmap]$Bitmap, [string]$Path)
    $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "  wrote $([System.IO.Path]::GetFileName($Path)) ($($Bitmap.Width)x$($Bitmap.Height))"
}

# ── MSIX tiles (transparent eye; the manifest supplies a BackgroundColor plate) ──
Write-Host 'MSIX tiles:'
$square = @{ 'Square44x44Logo.png' = 44; 'Square71x71Logo.png' = 71; 'Square150x150Logo.png' = 150; 'Square310x310Logo.png' = 310; 'StoreLogo.png' = 50 }
foreach ($name in $square.Keys) {
    $s = $square[$name]
    $bmp = New-EyeBitmap -Width $s -Height $s
    Save-Png $bmp (Join-Path $tilesDir $name)
    $bmp.Dispose()
}
$wide = New-EyeBitmap -Width 310 -Height 150
Save-Png $wide (Join-Path $tilesDir 'Wide310x150Logo.png')
$wide.Dispose()

# ── App PNG (256, transparent badge) for the installer chrome ──
Write-Host 'App image:'
$appPng = New-EyeBitmap -Width 256 -Height 256
Save-Png $appPng (Join-Path $assetsDir 'Icon.png')
$appPng.Dispose()

# ── Multi-resolution .ico for the exe + tray (transparent badge, like Spotify — no square plate) ──
Write-Host 'Icon (.ico):'
$icoSizes = @(16, 24, 32, 48, 64, 128, 256)
$pngBuffers = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($s in $icoSizes) {
    $bmp = New-EyeBitmap -Width $s -Height $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBuffers.Add($ms.ToArray())
    $ms.Dispose(); $bmp.Dispose()
}

$icoPath = Join-Path $assetsDir 'Icon.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
# ICONDIR
$bw.Write([uint16]0)              # reserved
$bw.Write([uint16]1)              # type = icon
$bw.Write([uint16]$icoSizes.Count)
$offset = 6 + (16 * $icoSizes.Count)
for ($i = 0; $i -lt $icoSizes.Count; $i++) {
    $s = $icoSizes[$i]
    $data = $pngBuffers[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))   # width  (0 == 256)
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))   # height (0 == 256)
    $bw.Write([byte]0)            # color count
    $bw.Write([byte]0)            # reserved
    $bw.Write([uint16]1)         # color planes
    $bw.Write([uint16]32)        # bits per pixel
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngBuffers) { $bw.Write($data) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()
Write-Host "  wrote Icon.ico ($($icoSizes.Count) sizes)"

Write-Host 'Done.' -ForegroundColor Green
