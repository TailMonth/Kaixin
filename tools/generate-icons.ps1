param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\assets\ui')
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Col {
    param([string]$Hex, [int]$Alpha = 255)
    $c = [System.Drawing.ColorTranslator]::FromHtml($Hex)
    return [System.Drawing.Color]::FromArgb($Alpha, $c.R, $c.G, $c.B)
}

function Draw-CatIcon {
    param([int]$Size, [string]$DotHex, [bool]$WithDot)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$Size
    $fur = Col '#7B7469'
    $furDark = Col '#5B564E'
    $line = Col '#4A443C' 220
    $inner = Col '#D9A6A0'
    $eye = Col '#2E342A'
    $nose = Col '#E9A0A6'

    $penWidth = [Math]::Max(1.0, $s * 0.035)
    $outline = New-Object System.Drawing.Pen($line, $penWidth)
    $outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    # Ears
    $earBrush = New-Object System.Drawing.SolidBrush($furDark)
    $earLeft = New-Object System.Drawing.Drawing2D.GraphicsPath
    $earLeft.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new($s * 0.20, $s * 0.44)),
        ([System.Drawing.PointF]::new($s * 0.27, $s * 0.12)),
        ([System.Drawing.PointF]::new($s * 0.46, $s * 0.34))))
    $g.FillPath($earBrush, $earLeft)
    $g.DrawPath($outline, $earLeft)

    $earRight = New-Object System.Drawing.Drawing2D.GraphicsPath
    $earRight.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new($s * 0.80, $s * 0.44)),
        ([System.Drawing.PointF]::new($s * 0.73, $s * 0.12)),
        ([System.Drawing.PointF]::new($s * 0.54, $s * 0.34))))
    $g.FillPath($earBrush, $earRight)
    $g.DrawPath($outline, $earRight)

    $innerBrush = New-Object System.Drawing.SolidBrush($inner)
    $innerLeft = New-Object System.Drawing.Drawing2D.GraphicsPath
    $innerLeft.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new($s * 0.27, $s * 0.40)),
        ([System.Drawing.PointF]::new($s * 0.30, $s * 0.22)),
        ([System.Drawing.PointF]::new($s * 0.41, $s * 0.35))))
    $g.FillPath($innerBrush, $innerLeft)

    $innerRight = New-Object System.Drawing.Drawing2D.GraphicsPath
    $innerRight.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new($s * 0.73, $s * 0.40)),
        ([System.Drawing.PointF]::new($s * 0.70, $s * 0.22)),
        ([System.Drawing.PointF]::new($s * 0.59, $s * 0.35))))
    $g.FillPath($innerBrush, $innerRight)

    # Head
    $headRect = [System.Drawing.RectangleF]::new(($s * 0.16), ($s * 0.34), ($s * 0.68), ($s * 0.54))
    $headBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $headRect, (Col '#8A8277'), (Col '#6E675D'), [float]90)
    $g.FillEllipse($headBrush, $headRect)
    $g.DrawEllipse($outline, $headRect)

    # Eyes
    $eyeBrush = New-Object System.Drawing.SolidBrush($eye)
    $eyeW = [Math]::Max(1.4, $s * 0.075)
    $eyeH = [Math]::Max(1.8, $s * 0.105)
    $g.FillEllipse($eyeBrush, ($s * 0.33), ($s * 0.55), $eyeW, $eyeH)
    $g.FillEllipse($eyeBrush, ($s * 0.60), ($s * 0.55), $eyeW, $eyeH)

    # Nose
    $noseBrush = New-Object System.Drawing.SolidBrush($nose)
    $noseW = [Math]::Max(1.6, $s * 0.09)
    $noseH = [Math]::Max(1.2, $s * 0.06)
    $g.FillEllipse($noseBrush, ($s * 0.455), ($s * 0.69), $noseW, $noseH)

    if ($WithDot -and $Size -ge 16) {
        $dotColor = Col $DotHex
        $dotBrush = New-Object System.Drawing.SolidBrush($dotColor)
        $ringBrush = New-Object System.Drawing.SolidBrush((Col '#FFFDF9'))
        $dotSize = [Math]::Max(4.0, $s * 0.28)
        $dotX = $s - $dotSize - ($s * 0.02)
        $dotY = $s - $dotSize - ($s * 0.02)
        $ringSize = $dotSize + [Math]::Max(2.0, $s * 0.07)
        $g.FillEllipse($ringBrush, ($dotX - ($ringSize - $dotSize) / 2), ($dotY - ($ringSize - $dotSize) / 2), $ringSize, $ringSize)
        $g.FillEllipse($dotBrush, $dotX, $dotY, $dotSize, $dotSize)
        $dotBrush.Dispose(); $ringBrush.Dispose()
    }

    foreach ($item in @($outline, $earBrush, $innerBrush, $headBrush, $eyeBrush, $noseBrush,
                        $earLeft, $earRight, $innerLeft, $innerRight)) {
        if ($null -ne $item) {
            $item.Dispose()
        }
    }

    $g.Dispose()
    return $bmp
}

function Write-Ico {
    param([string]$Path, [int[]]$Sizes, [string]$DotHex, [bool]$WithDot)

    $images = @()
    foreach ($size in $Sizes) {
        $bmp = Draw-CatIcon -Size $size -DotHex $DotHex -WithDot $WithDot
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $images += [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
        $ms.Dispose()
        $bmp.Dispose()
    }

    $stream = [System.IO.File]::Create($Path)
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$images.Count)

    $offset = 6 + (16 * $images.Count)
    foreach ($image in $images) {
        $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$image.Bytes.Length)
        $writer.Write([UInt32]$offset)
        $offset += $image.Bytes.Length
    }

    foreach ($image in $images) {
        $writer.Write($image.Bytes)
    }

    $writer.Close()
    $stream.Dispose()
}

$traySizes = @(16, 20, 24, 32, 48)
$appSizes = @(16, 20, 24, 32, 48, 64, 128, 256)

Write-Ico -Path (Join-Path $OutDir 'tray-idle.ico') -Sizes $traySizes -DotHex '#A8BFA3' -WithDot $true
Write-Ico -Path (Join-Path $OutDir 'tray-focus.ico') -Sizes $traySizes -DotHex '#E4572E' -WithDot $true
Write-Ico -Path (Join-Path $OutDir 'tray-break.ico') -Sizes $traySizes -DotHex '#6D9E6A' -WithDot $true
Write-Ico -Path (Join-Path $OutDir 'tray-paused.ico') -Sizes $traySizes -DotHex '#A39C90' -WithDot $true
Write-Ico -Path (Join-Path $OutDir 'app.ico') -Sizes $appSizes -DotHex '#A8BFA3' -WithDot $false

# Preview sheet for manual inspection
$previewWidth = 5 * 72
$preview = New-Object System.Drawing.Bitmap($previewWidth, 72, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$pg = [System.Drawing.Graphics]::FromImage($preview)
$pg.Clear([System.Drawing.Color]::FromArgb(255, 40, 40, 40))
$states = @('#A8BFA3', '#E4572E', '#6D9E6A', '#A39C90')
for ($i = 0; $i -lt $states.Count; $i++) {
    $icon = Draw-CatIcon -Size 64 -DotHex $states[$i] -WithDot $true
    $pg.DrawImage($icon, 4 + ($i * 72), 4, 64, 64)
    $icon.Dispose()
}
$appIcon = Draw-CatIcon -Size 64 -DotHex '#A8BFA3' -WithDot $false
$pg.DrawImage($appIcon, 4 + (4 * 72), 4, 64, 64)
$appIcon.Dispose()
$pg.Dispose()
$preview.Save((Join-Path $PSScriptRoot 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()

Write-Host "icons generated in $OutDir"
