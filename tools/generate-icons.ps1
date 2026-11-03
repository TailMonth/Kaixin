param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\assets\ui')
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Palette follows Themes/Watercolor.xaml:
# paper #F7F2E8, ink #3F403B, warm fur #A9A194...#857D70, sage eye #8F9F82,
# ear pink #D8B4A6, nose rose #B9786B, focus sand #A97752, break sage #667A5E.
function Col {
    param([string]$Hex, [int]$Alpha = 255)
    $c = [System.Drawing.ColorTranslator]::FromHtml($Hex)
    return [System.Drawing.Color]::FromArgb($Alpha, $c.R, $c.G, $c.B)
}

function New-RoundedPath {
    param([System.Drawing.RectangleF]$Rect, [float]$Radius)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $Radius * 2
    $path.AddArc($Rect.X, $Rect.Y, $d, $d, 180, 90)
    $path.AddArc($Rect.Right - $d, $Rect.Y, $d, $d, 270, 90)
    $path.AddArc($Rect.Right - $d, $Rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($Rect.X, $Rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-Backdrop {
    param(
        [System.Drawing.Graphics]$G,
        [float]$S,
        [bool]$IsApp
    )

    $frame = [System.Drawing.RectangleF]::new($S * 0.02, $S * 0.02, $S * 0.96, $S * 0.96)
    $border = New-Object System.Drawing.Pen((Col '#E2D7C4'), [Math]::Max(1.0, $S * 0.012))

    if ($IsApp) {
        $path = New-RoundedPath -Rect $frame -Radius ($S * 0.22)
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $frame, (Col '#FCF8EF'), (Col '#EFE5D4'), [float]62)
        $G.FillPath($brush, $path)
        $G.DrawPath($border, $path)
        $path.Dispose()
        $brush.Dispose()
    }
    else {
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $frame, (Col '#FCF8EF'), (Col '#F0E6D5'), [float]62)
        $G.FillEllipse($brush, $frame)
        $G.DrawEllipse($border, $frame)
        $brush.Dispose()
    }

    $border.Dispose()

    if ($S -ge 24) {
        $sageWash = New-Object System.Drawing.SolidBrush((Col '#8F9F82' 16))
        $roseWash = New-Object System.Drawing.SolidBrush((Col '#D8B4A6' 14))
        $G.FillEllipse($sageWash, ($S * 0.12), ($S * 0.16), ($S * 0.46), ($S * 0.34))
        $G.FillEllipse($roseWash, ($S * 0.44), ($S * 0.60), ($S * 0.44), ($S * 0.30))
        $sageWash.Dispose()
        $roseWash.Dispose()
    }
}

function Draw-Head {
    param(
        [System.Drawing.Graphics]$G,
        [float]$S,
        [int]$LogicalSize
    )

    $detail = $LogicalSize -ge 24
    $rich = $LogicalSize -ge 32
    $whiskers = $LogicalSize -ge 48
    $tiny = $LogicalSize -lt 20

    $pxPerUnit = $S / $LogicalSize
    $outlineWidth = [Math]::Max(0.9 * $pxPerUnit, $S * 0.036)
    $outline = New-Object System.Drawing.Pen((Col '#433D34'), $outlineWidth)
    $outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $outline.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $outline.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    # Ears
    $earBrush = New-Object System.Drawing.SolidBrush((Col '#8F8779'))
    $leftEar = New-Object System.Drawing.Drawing2D.GraphicsPath
    $leftEar.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new($S * 0.225, $S * 0.415)),
        ([System.Drawing.PointF]::new($S * 0.292, $S * 0.155)),
        ([System.Drawing.PointF]::new($S * 0.462, $S * 0.345))))
    $rightEar = New-Object System.Drawing.Drawing2D.GraphicsPath
    $rightEar.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new($S * 0.775, $S * 0.415)),
        ([System.Drawing.PointF]::new($S * 0.708, $S * 0.155)),
        ([System.Drawing.PointF]::new($S * 0.538, $S * 0.345))))

    foreach ($ear in @($leftEar, $rightEar)) {
        $G.FillPath($earBrush, $ear)
        $G.DrawPath($outline, $ear)
    }

    if ($detail) {
        $innerBrush = New-Object System.Drawing.SolidBrush((Col '#D8B4A6' 225))
        $leftInner = New-Object System.Drawing.Drawing2D.GraphicsPath
        $leftInner.AddPolygon([System.Drawing.PointF[]]@(
            ([System.Drawing.PointF]::new($S * 0.281, $S * 0.372)),
            ([System.Drawing.PointF]::new($S * 0.315, $S * 0.232)),
            ([System.Drawing.PointF]::new($S * 0.413, $S * 0.345))))
        $rightInner = New-Object System.Drawing.Drawing2D.GraphicsPath
        $rightInner.AddPolygon([System.Drawing.PointF[]]@(
            ([System.Drawing.PointF]::new($S * 0.719, $S * 0.372)),
            ([System.Drawing.PointF]::new($S * 0.685, $S * 0.232)),
            ([System.Drawing.PointF]::new($S * 0.587, $S * 0.345))))
        $G.FillPath($innerBrush, $leftInner)
        $G.FillPath($innerBrush, $rightInner)
        $innerBrush.Dispose()
        $leftInner.Dispose()
        $rightInner.Dispose()
    }

    # Head
    $headRect = [System.Drawing.RectangleF]::new($S * 0.19, $S * 0.31, $S * 0.62, $S * 0.50)
    $headBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $headRect, (Col '#B2A99B'), (Col '#8A8275'), [float]90)
    $G.FillEllipse($headBrush, $headRect)

    if ($rich) {
        $darkWash = New-Object System.Drawing.SolidBrush((Col '#6F675C' 26))
        $G.FillEllipse($darkWash, ($S * 0.36), ($S * 0.60), ($S * 0.28), ($S * 0.20))
        $darkWash.Dispose()
    }

    $G.DrawEllipse($outline, $headRect)

    # Forehead tabby stripes
    if ($detail) {
        $stripe = New-Object System.Drawing.Pen((Col '#6B6459' 210), [Math]::Max(1.0, $S * 0.022))
        $stripe.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $stripe.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $G.DrawLine($stripe, ($S * 0.452), ($S * 0.342), ($S * 0.446), ($S * 0.404))
        $G.DrawLine($stripe, ($S * 0.500), ($S * 0.334), ($S * 0.500), ($S * 0.412))
        $G.DrawLine($stripe, ($S * 0.548), ($S * 0.342), ($S * 0.554), ($S * 0.404))
        $stripe.Dispose()
    }

    # Eyes
    $eyeSage = New-Object System.Drawing.SolidBrush((Col '#8F9F82'))
    $pupil = New-Object System.Drawing.SolidBrush((Col '#33372F'))
    $glint = New-Object System.Drawing.SolidBrush((Col '#FFFBEF'))
    $eyeY = $S * 0.555
    foreach ($eyeX in @(($S * 0.405), ($S * 0.595))) {
        if ($tiny) {
            $G.FillEllipse($pupil, ($eyeX - $S * 0.046), ($eyeY - $S * 0.062), ($S * 0.092), ($S * 0.124))
        }
        else {
            $G.FillEllipse($eyeSage, ($eyeX - $S * 0.071), ($eyeY - $S * 0.074), ($S * 0.142), ($S * 0.148))
            $G.FillEllipse($pupil, ($eyeX - $S * 0.029), ($eyeY - $S * 0.042), ($S * 0.058), ($S * 0.084))
            if ($rich) {
                $G.FillEllipse($glint, ($eyeX - $S * 0.048), ($eyeY - $S * 0.046), ($S * 0.032), ($S * 0.032))
            }
        }
    }

    # Muzzle
    $muzzle = New-Object System.Drawing.SolidBrush((Col '#FBF6EA' 235))
    $G.FillEllipse($muzzle, ($S * 0.388), ($S * 0.648), ($S * 0.135), ($S * 0.112))
    $G.FillEllipse($muzzle, ($S * 0.477), ($S * 0.648), ($S * 0.135), ($S * 0.112))
    $muzzle.Dispose()

    # Nose
    $noseBrush = New-Object System.Drawing.SolidBrush((Col '#B9786B'))
    $nose = New-Object System.Drawing.Drawing2D.GraphicsPath
    $nose.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new($S * 0.462, $S * 0.640)),
        ([System.Drawing.PointF]::new($S * 0.538, $S * 0.640)),
        ([System.Drawing.PointF]::new($S * 0.500, $S * 0.692))))
    $G.FillPath($noseBrush, $nose)

    if ($rich) {
        $mouth = New-Object System.Drawing.Pen((Col '#5B544A' 200), [Math]::Max(1.0, $S * 0.016))
        $mouth.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $mouth.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $G.DrawLine($mouth, ($S * 0.500), ($S * 0.686), ($S * 0.500), ($S * 0.706))
        $G.DrawArc($mouth, ($S * 0.458), ($S * 0.700), ($S * 0.042), ($S * 0.032), 0, 180)
        $G.DrawArc($mouth, ($S * 0.500), ($S * 0.700), ($S * 0.042), ($S * 0.032), 0, 180)
        $mouth.Dispose()
    }

    if ($whiskers) {
        $whisker = New-Object System.Drawing.Pen((Col '#8A8277' 170), [Math]::Max(0.8, $S * 0.010))
        $whisker.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $whisker.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $G.DrawLine($whisker, ($S * 0.300), ($S * 0.640), ($S * 0.196), ($S * 0.622))
        $G.DrawLine($whisker, ($S * 0.300), ($S * 0.672), ($S * 0.200), ($S * 0.680))
        $G.DrawLine($whisker, ($S * 0.700), ($S * 0.640), ($S * 0.804), ($S * 0.622))
        $G.DrawLine($whisker, ($S * 0.700), ($S * 0.672), ($S * 0.800), ($S * 0.680))
        $whisker.Dispose()
    }

    $outline.Dispose()
    $earBrush.Dispose()
    $headBrush.Dispose()
    $eyeSage.Dispose()
    $pupil.Dispose()
    $glint.Dispose()
    $noseBrush.Dispose()
    $leftEar.Dispose()
    $rightEar.Dispose()
    $nose.Dispose()
}

function Draw-PhaseDot {
    param(
        [System.Drawing.Graphics]$G,
        [float]$S,
        [string]$DotHex
    )

    $cx = $S * 0.790
    $cy = $S * 0.790
    $ring = New-Object System.Drawing.SolidBrush((Col '#FBF6EA'))
    $dot = New-Object System.Drawing.SolidBrush((Col $DotHex))
    $G.FillEllipse($ring, ($cx - $S * 0.152), ($cy - $S * 0.152), ($S * 0.304), ($S * 0.304))
    $G.FillEllipse($dot, ($cx - $S * 0.110), ($cy - $S * 0.110), ($S * 0.220), ($S * 0.220))
    $ring.Dispose()
    $dot.Dispose()
}

function New-IconBitmap {
    param(
        [int]$LogicalSize,
        [string]$DotHex,
        [bool]$WithDot,
        [bool]$IsApp
    )

    $scale = 8
    if ($LogicalSize -ge 48) { $scale = 4 }
    $s = [float]($LogicalSize * $scale)

    $bmp = New-Object System.Drawing.Bitmap([int]$s, [int]$s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    Draw-Backdrop -G $g -S $s -IsApp $IsApp

    if ($IsApp) {
        $g.TranslateTransform($s * 0.5, $s * 0.52)
        $g.ScaleTransform(1.10, 1.10)
        $g.TranslateTransform(-$s * 0.5, -$s * 0.52)
    }

    Draw-Head -G $g -S $s -LogicalSize $LogicalSize

    if (!$IsApp) {
        $g.ResetTransform()
        if ($WithDot) {
            Draw-PhaseDot -G $g -S $s -DotHex $DotHex
        }
    }

    $g.Dispose()

    $final = New-Object System.Drawing.Bitmap($LogicalSize, $LogicalSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $fg = [System.Drawing.Graphics]::FromImage($final)
    $fg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $fg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $fg.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $fg.Clear([System.Drawing.Color]::Transparent)
    $fg.DrawImage($bmp, (New-Object System.Drawing.Rectangle(0, 0, $LogicalSize, $LogicalSize)), 0, 0, [int]$s, [int]$s, [System.Drawing.GraphicsUnit]::Pixel)
    $fg.Dispose()
    $bmp.Dispose()
    return $final
}

function Write-Ico {
    param([string]$Path, [int[]]$Sizes, [string]$DotHex, [bool]$WithDot, [bool]$IsApp)

    $images = @()
    foreach ($size in $Sizes) {
        $bmp = New-IconBitmap -LogicalSize $size -DotHex $DotHex -WithDot $WithDot -IsApp $IsApp
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

$states = @(
    [pscustomobject]@{ File = 'tray-idle.ico'; Dot = '#8F9F82' },
    [pscustomobject]@{ File = 'tray-focus.ico'; Dot = '#A97752' },
    [pscustomobject]@{ File = 'tray-break.ico'; Dot = '#667A5E' },
    [pscustomobject]@{ File = 'tray-paused.ico'; Dot = '#A39C90' }
)

foreach ($state in $states) {
    Write-Ico -Path (Join-Path $OutDir $state.File) -Sizes $traySizes -DotHex $state.Dot -WithDot $true -IsApp $false
}

Write-Ico -Path (Join-Path $OutDir 'app.ico') -Sizes $appSizes -DotHex '#8F9F82' -WithDot $false -IsApp $true

# Preview sheet: dark and light taskbar strips, actual sizes plus enlarged 16 px.
$cell = 56
$previewWidth = 560
$previewHeight = 460
$preview = New-Object System.Drawing.Bitmap($previewWidth, $previewHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$pg = [System.Drawing.Graphics]::FromImage($preview)
$pg.Clear((Col '#202020'))
$pg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$pg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half

$dark = New-Object System.Drawing.SolidBrush((Col '#202020'))
$light = New-Object System.Drawing.SolidBrush((Col '#F3F3F3'))
$pg.FillRectangle($dark, 0, 0, $previewWidth, 150)
$pg.FillRectangle($light, 0, 150, $previewWidth, 150)

for ($row = 0; $row -lt 2; $row++) {
    $y = 20 + ($row * 150)
    for ($stateIndex = 0; $stateIndex -lt $states.Count; $stateIndex++) {
        $icon = New-IconBitmap -LogicalSize 48 -DotHex $states[$stateIndex].Dot -WithDot $true -IsApp $false
        $x = 20 + ($stateIndex * $cell)
        $pg.DrawImage($icon, $x, $y, 48, 48)
        $icon.Dispose()
    }
    $app = New-IconBitmap -LogicalSize 48 -DotHex '#8F9F82' -WithDot $false -IsApp $true
    $pg.DrawImage($app, (20 + ($states.Count * $cell)), $y, 48, 48)
    $app.Dispose()
}

# Enlarged 16/20/24 as pixel blocks
$zoom = 8
$zx = 20
for ($stateIndex = 0; $stateIndex -lt $states.Count; $stateIndex++) {
    $icon = New-IconBitmap -LogicalSize 16 -DotHex $states[$stateIndex].Dot -WithDot $true -IsApp $false
    $pg.DrawImage($icon, $zx, 300, 16 * $zoom, 16 * $zoom)
    $icon.Dispose()
    $zx += 128
}

$pg.Dispose()
$preview.Save((Join-Path $PSScriptRoot 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()
$dark.Dispose()
$light.Dispose()

Write-Host "icons generated in $OutDir"
