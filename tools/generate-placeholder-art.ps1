param(
    [string]$OutRoot = (Join-Path $PSScriptRoot '..\assets')
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$characterDir = Join-Path $OutRoot 'characters\kaixin'
$propsDir = Join-Path $OutRoot 'props'
New-Item -ItemType Directory -Force -Path $characterDir, $propsDir | Out-Null

function Col {
    param([string]$Hex, [int]$Alpha = 255)
    $c = [System.Drawing.ColorTranslator]::FromHtml($Hex)
    return [System.Drawing.Color]::FromArgb($Alpha, $c.R, $c.G, $c.B)
}

function New-Canvas {
    param([int]$Width, [int]$Height)
    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    return [pscustomobject]@{ Bitmap = $bitmap; Graphics = $graphics }
}

function New-Pen {
    param($Color, [float]$Width)
    $pen = New-Object System.Drawing.Pen($Color, $Width)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    return $pen
}

function New-RoundedPath {
    param([float]$X, [float]$Y, [float]$W, [float]$H, [float]$R)
    $d = $R * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-Eye {
    param($G, [float]$Cx, [float]$Cy)
    $white = New-Object System.Drawing.SolidBrush((Col '#F8F4EA'))
    $iris = New-Object System.Drawing.SolidBrush((Col '#9DBE86'))
    $pupil = New-Object System.Drawing.SolidBrush((Col '#2E342A'))
    $glint = New-Object System.Drawing.SolidBrush((Col '#FFFFFF' 235))
    $outline = New-Pen (Col '#3E3A34' 200) 1.4

    $G.FillEllipse($white, $Cx - 15, $Cy - 15, 30, 30)
    $G.FillEllipse($iris, $Cx - 13, $Cy - 13, 26, 26)
    $G.FillEllipse($pupil, $Cx - 4, $Cy - 10, 8, 20)
    $G.FillEllipse($glint, $Cx - 8, $Cy - 11, 5, 5)
    $G.DrawEllipse($outline, $Cx - 15, $Cy - 15, 30, 30)

    $white.Dispose(); $iris.Dispose(); $pupil.Dispose(); $glint.Dispose(); $outline.Dispose()
}

function Draw-CatFrame {
    param($G, [int]$Phase)

    $t = $Phase / 12.0 * 2 * [math]::PI
    $lift = [math]::Round(2.0 * [math]::Sin($t))
    $sway = [math]::Round(6.0 * [math]::Sin($t + 0.7))

    $fur = Col '#7B7469'
    $furLight = Col '#8A8277'
    $furDark = Col '#5F5A52'
    $lineColor = Col '#4A443C' 140
    $stripeColor = Col '#4A473F' 140

    $outlinePen = New-Pen $lineColor 1.6

    # 尾巴（在身体后方）
    $tailPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tailPath.AddBezier(158, 344, 112, 356, 64 + $sway, 336, 74 + $sway, 292)
    $tailPen = New-Pen $fur 30
    $G.DrawPath($tailPen, $tailPath)
    $tailTip = New-Object System.Drawing.SolidBrush((Col '#5B564E'))
    $G.FillEllipse($tailTip, (74 + $sway - 15), 277, 30, 30)

    # 身体
    $top = 210 - $lift
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddBezier(114, 358, 104, 300, 112, ($top + 44), 146, ($top + 10))
    $body.AddBezier(146, ($top + 10), 170, ($top - 6), 214, ($top - 6), 238, ($top + 10))
    $body.AddBezier(238, ($top + 10), 272, ($top + 44), 280, 300, 270, 358)
    $body.CloseFigure()

    $bodyRect = New-Object System.Drawing.Rectangle(100, [int]$top, 200, [int](360 - $top))
    $bodyGradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $bodyRect, $furLight, $furDark, [float]90)
    $G.FillPath($bodyGradient, $body)
    $G.DrawPath($outlinePen, $body)

    # 胸口浅色（保持虎斑，不是白围兜）
    $chest = New-Object System.Drawing.SolidBrush((Col '#8A8277' 90))
    $G.FillEllipse($chest, 158, 238, 68, 90)

    # 身体条纹
    $stripePen = New-Pen $stripeColor 5
    $G.DrawArc($stripePen, 118, 248, 40, 34, 110, 115)
    $G.DrawArc($stripePen, 226, 248, 40, 34, -45, 115)
    $G.DrawArc($stripePen, 122, 290, 34, 28, 115, 105)
    $G.DrawArc($stripePen, 228, 290, 34, 28, -40, 105)

    # 白色前腿与爪子（自然毛色，不是袜子）
    $white = New-Object System.Drawing.SolidBrush((Col '#F8F4EA'))
    $legLeft = New-RoundedPath 142 300 44 62 20
    $legRight = New-RoundedPath 198 300 44 62 20
    $G.FillPath($white, $legLeft)
    $G.FillPath($white, $legRight)

    $toePen = New-Pen (Col '#B9B0A0' 170) 1.2
    $G.DrawLine($toePen, 158, 344, 158, 356)
    $G.DrawLine($toePen, 171, 344, 171, 356)
    $G.DrawLine($toePen, 213, 344, 213, 356)
    $G.DrawLine($toePen, 226, 344, 226, 356)

    # 耳朵
    $earBrush = New-Object System.Drawing.SolidBrush((Col '#6F6860'))
    $innerBrush = New-Object System.Drawing.SolidBrush((Col '#D9A6A0' 225))

    $earLeft = New-Object System.Drawing.Drawing2D.GraphicsPath
    $earLeft.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(120, 116)),
        (New-Object System.Drawing.PointF(134, 42)),
        (New-Object System.Drawing.PointF(180, 98))))
    $G.FillPath($earBrush, $earLeft)
    $G.DrawPath($outlinePen, $earLeft)

    $innerLeft = New-Object System.Drawing.Drawing2D.GraphicsPath
    $innerLeft.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(135, 103)),
        (New-Object System.Drawing.PointF(143, 64)),
        (New-Object System.Drawing.PointF(166, 97))))
    $G.FillPath($innerBrush, $innerLeft)

    $earRight = New-Object System.Drawing.Drawing2D.GraphicsPath
    $earRight.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(264, 116)),
        (New-Object System.Drawing.PointF(250, 42)),
        (New-Object System.Drawing.PointF(204, 98))))
    $G.FillPath($earBrush, $earRight)
    $G.DrawPath($outlinePen, $earRight)

    $innerRight = New-Object System.Drawing.Drawing2D.GraphicsPath
    $innerRight.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(249, 103)),
        (New-Object System.Drawing.PointF(241, 64)),
        (New-Object System.Drawing.PointF(218, 97))))
    $G.FillPath($innerBrush, $innerRight)

    # 头部
    $headRect = New-Object System.Drawing.Rectangle(112, 74, 160, 144)
    $headGradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $headRect, $furLight, (Col '#6E675D'), [float]90)
    $G.FillEllipse($headGradient, $headRect)
    $G.DrawEllipse($outlinePen, $headRect)

    # 额头 M 纹
    $foreheadPen = New-Pen (Col '#4A473F' 175) 4
    $G.DrawLine($foreheadPen, 177, 92, 177, 117)
    $G.DrawLine($foreheadPen, 192, 87, 192, 112)
    $G.DrawLine($foreheadPen, 207, 92, 207, 117)

    # 脸颊侧纹
    $cheekPen = New-Pen (Col '#4A473F' 120) 3.5
    $G.DrawArc($cheekPen, 116, 132, 34, 44, 95, 100)
    $G.DrawArc($cheekPen, 234, 132, 34, 44, -15, 100)

    # 眼睛
    Draw-Eye $G 163 148
    Draw-Eye $G 221 148

    # 口鼻
    $muzzle = New-Object System.Drawing.SolidBrush((Col '#F8F4EA'))
    $G.FillEllipse($muzzle, 167, 181, 24, 17)
    $G.FillEllipse($muzzle, 193, 181, 24, 17)

    $nosePath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $nosePath.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(187, 179)),
        (New-Object System.Drawing.PointF(197, 179)),
        (New-Object System.Drawing.PointF(192, 186))))
    $noseBrush = New-Object System.Drawing.SolidBrush((Col '#E9A0A6'))
    $G.FillPath($noseBrush, $nosePath)

    $mouthPen = New-Pen (Col '#4A443C' 160) 1.4
    $G.DrawLine($mouthPen, 192, 186, 192, 192)
    $G.DrawArc($mouthPen, 181, 186, 22, 10, 10, 160)

    # 胡须
    $whiskerPen = New-Pen (Col '#4A443C' 95) 1
    $G.DrawLine($whiskerPen, 167, 195, 110, 187)
    $G.DrawLine($whiskerPen, 167, 200, 110, 204)
    $G.DrawLine($whiskerPen, 217, 195, 274, 187)
    $G.DrawLine($whiskerPen, 217, 200, 274, 204)

    @($tailPath, $tailPen, $tailTip, $body, $bodyGradient, $chest, $stripePen,
      $white, $legLeft, $legRight, $toePen, $earBrush, $innerBrush, $earLeft,
      $innerLeft, $earRight, $innerRight, $headGradient, $foreheadPen,
      $cheekPen, $muzzle, $nosePath, $noseBrush, $mouthPen, $whiskerPen,
      $outlinePen) | ForEach-Object { $_.Dispose() }
}

function Draw-BlinkFrame {
    param($G, [int]$Stage)

    if ($Stage -eq 0) {
        return
    }

    $lid = New-Object System.Drawing.SolidBrush((Col '#7B7469'))
    $linePen = New-Pen (Col '#3E3A34' 210) 2.2

    if ($Stage -eq 1) {
        foreach ($cx in @(163, 221)) {
            $clip = New-Object System.Drawing.Drawing2D.GraphicsPath
            $clip.AddEllipse($cx - 16, 131, 32, 32)
            $G.SetClip($clip)
            $G.FillRectangle($lid, $cx - 17, 131, 34, 17)
            $G.ResetClip()
            $G.DrawLine($linePen, $cx - 13, 148, $cx + 13, 148)
            $clip.Dispose()
        }
    }
    else {
        foreach ($cx in @(163, 221)) {
            $clip = New-Object System.Drawing.Drawing2D.GraphicsPath
            $clip.AddEllipse($cx - 16, 131, 32, 32)
            $G.SetClip($clip)
            $G.FillEllipse($lid, $cx - 17, 131, 34, 32)
            $G.ResetClip()

            $curve = New-Object System.Drawing.Drawing2D.GraphicsPath
            $curve.AddBezier($cx - 13, 148, $cx - 6, 155, $cx + 6, 155, $cx + 13, 148)
            $G.DrawPath($linePen, $curve)
            $clip.Dispose()
            $curve.Dispose()
        }
    }

    $lid.Dispose()
    $linePen.Dispose()
}

function Draw-HourglassBase {
    param($G)

    $wood = Col '#B89A74'
    $woodDark = Col '#8E7355'
    $glassEdge = Col '#8A7C66' 170
    $glassFill = Col '#FFFFFF' 18

    # 玻璃
    $upper = New-Object System.Drawing.Drawing2D.GraphicsPath
    $upper.AddBezier(32, 30, 28, 50, 44, 70, 53, 78)
    $upper.AddLine(53, 78, 59, 78)
    $upper.AddBezier(59, 78, 68, 70, 84, 50, 80, 30)
    $upper.AddLine(80, 30, 32, 30)
    $upper.CloseFigure()

    $lower = New-Object System.Drawing.Drawing2D.GraphicsPath
    $lower.AddBezier(53, 82, 44, 92, 28, 112, 32, 132)
    $lower.AddLine(32, 132, 80, 132)
    $lower.AddBezier(80, 132, 84, 112, 68, 92, 59, 82)
    $lower.CloseFigure()

    $glassBrush = New-Object System.Drawing.SolidBrush($glassFill)
    $glassPen = New-Pen $glassEdge 2.4
    $G.FillPath($glassBrush, $upper)
    $G.FillPath($glassBrush, $lower)
    $G.DrawPath($glassPen, $upper)
    $G.DrawPath($glassPen, $lower)

    # 颈部金环
    $ringBrush = New-Object System.Drawing.SolidBrush((Col '#C8AE7C'))
    $G.FillEllipse($ringBrush, 47, 76, 18, 8)

    # 立柱
    $postBrush = New-Object System.Drawing.SolidBrush($wood)
    $postPen = New-Pen $woodDark 1.2
    $G.FillRectangle($postBrush, 13, 22, 7, 116)
    $G.DrawRectangle($postPen, 13, 22, 7, 116)
    $G.FillRectangle($postBrush, 92, 22, 7, 116)
    $G.DrawRectangle($postPen, 92, 22, 7, 116)

    # 顶底木盖
    $capPen = New-Pen $woodDark 1.2
    $topCap = New-RoundedPath 6 4 100 18 8
    $bottomCap = New-RoundedPath 6 138 100 18 8
    $G.FillPath($postBrush, $topCap)
    $G.FillPath($postBrush, $bottomCap)
    $G.DrawPath($capPen, $topCap)
    $G.DrawPath($capPen, $bottomCap)

    $highlightPen = New-Pen (Col '#D8C29A' 180) 2
    $G.DrawLine($highlightPen, 14, 40, 14, 120)
    $G.DrawLine($highlightPen, 20, 10, 92, 10)

    @($glassBrush, $glassPen, $ringBrush, $postBrush, $postPen, $capPen,
      $topCap, $bottomCap, $highlightPen) | ForEach-Object { $_.Dispose() }
}

function Draw-SandTop {
    param($G)
    $sand = New-Object System.Drawing.SolidBrush((Col '#D9B36C'))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddLine(34, 33, 78, 33)
    $path.AddBezier(74, 48, 64, 68, 59, 77, 56, 77)
    $path.AddLine(56, 77, 53, 77)
    $path.AddBezier(50, 68, 38, 48, 34, 33, 34, 33)
    $path.CloseFigure()
    $G.FillPath($sand, $path)

    $grain = New-Object System.Drawing.SolidBrush((Col '#C79F58' 90))
    $random = New-Object System.Random(7)
    for ($i = 0; $i -lt 60; $i++) {
        $x = 36 + $random.NextDouble() * 40
        $y = 36 + $random.NextDouble() * 38
        $G.FillEllipse($grain, [float]$x, [float]$y, 1.6, 1.6)
    }

    $sand.Dispose(); $path.Dispose(); $grain.Dispose()
}

function Draw-SandBottom {
    param($G)
    $sand = New-Object System.Drawing.SolidBrush((Col '#D9B36C'))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddLine(53, 83, 59, 83)
    $path.AddBezier(68, 94, 82, 114, 79, 130, 79, 130)
    $path.AddLine(79, 130, 33, 130)
    $path.AddBezier(33, 130, 30, 114, 44, 94, 53, 83)
    $path.CloseFigure()
    $G.FillPath($sand, $path)

    $mound = New-Object System.Drawing.SolidBrush((Col '#E4C483'))
    $G.FillEllipse($mound, 42, 118, 28, 10)

    $grain = New-Object System.Drawing.SolidBrush((Col '#C79F58' 90))
    $random = New-Object System.Random(11)
    for ($i = 0; $i -lt 80; $i++) {
        $x = 35 + $random.NextDouble() * 42
        $y = 90 + $random.NextDouble() * 38
        $G.FillEllipse($grain, [float]$x, [float]$y, 1.6, 1.6)
    }

    $sand.Dispose(); $path.Dispose(); $mound.Dispose(); $grain.Dispose()
}

function New-SoftShadow {
    param([int]$Width, [int]$Height, [int]$Alpha, [string]$Path)
    $canvas = New-Canvas $Width $Height
    $g = $canvas.Graphics
    $pathShape = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pathShape.AddEllipse(3, 3, $Width - 6, $Height - 6)
    $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush($pathShape)
    $brush.CenterColor = [System.Drawing.Color]::FromArgb($Alpha, 0, 0, 0)
    $brush.SurroundColors = [System.Drawing.Color[]]@([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
    $g.FillPath($brush, $pathShape)
    $canvas.Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $brush.Dispose(); $pathShape.Dispose(); $g.Dispose(); $canvas.Bitmap.Dispose()
}

# ---- 待机呼吸：12 帧条带 ----
$frameSize = 384
$frameCount = 12
$strip = New-Object System.Drawing.Bitmap(($frameSize * $frameCount), $frameSize,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stripGraphics = [System.Drawing.Graphics]::FromImage($strip)
$stripGraphics.Clear([System.Drawing.Color]::Transparent)

for ($phase = 0; $phase -lt $frameCount; $phase++) {
    $frame = New-Canvas $frameSize $frameSize
    Draw-CatFrame $frame.Graphics $phase
    $stripGraphics.DrawImageUnscaled($frame.Bitmap, ($phase * $frameSize), 0)
    $frame.Graphics.Dispose()
    $frame.Bitmap.Dispose()
}

$idlePath = Join-Path $characterDir 'idle.png'
$strip.Save($idlePath, [System.Drawing.Imaging.ImageFormat]::Png)
$stripGraphics.Dispose()
$strip.Dispose()

# ---- 眨眼叠加：3 帧条带 ----
$blinkStrip = New-Object System.Drawing.Bitmap(($frameSize * 3), $frameSize,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$blinkGraphics = [System.Drawing.Graphics]::FromImage($blinkStrip)
$blinkGraphics.Clear([System.Drawing.Color]::Transparent)

for ($stage = 0; $stage -lt 3; $stage++) {
    $frame = New-Canvas $frameSize $frameSize
    Draw-BlinkFrame $frame.Graphics $stage
    $blinkGraphics.DrawImageUnscaled($frame.Bitmap, ($stage * $frameSize), 0)
    $frame.Graphics.Dispose()
    $frame.Bitmap.Dispose()
}

$eyesPath = Join-Path $characterDir 'eyes.png'
$blinkStrip.Save($eyesPath, [System.Drawing.Imaging.ImageFormat]::Png)
$blinkGraphics.Dispose()
$blinkStrip.Dispose()

# ---- 沙漏分层 ----
$hgBase = New-Canvas 112 160
Draw-HourglassBase $hgBase.Graphics
$hgBase.Bitmap.Save((Join-Path $propsDir 'hourglass.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$hgBase.Graphics.Dispose(); $hgBase.Bitmap.Dispose()

$hgTop = New-Canvas 112 160
Draw-SandTop $hgTop.Graphics
$hgTop.Bitmap.Save((Join-Path $propsDir 'hourglass_sand_top.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$hgTop.Graphics.Dispose(); $hgTop.Bitmap.Dispose()

$hgBottom = New-Canvas 112 160
Draw-SandBottom $hgBottom.Graphics
$hgBottom.Bitmap.Save((Join-Path $propsDir 'hourglass_sand_bottom.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$hgBottom.Graphics.Dispose(); $hgBottom.Bitmap.Dispose()

# ---- 接触阴影 ----
New-SoftShadow 320 80 70 (Join-Path $propsDir 'shadow_cat.png')
New-SoftShadow 176 52 46 (Join-Path $propsDir 'shadow_hourglass.png')

Write-Host "placeholder art generated:"
Write-Host "  $idlePath"
Write-Host "  $eyesPath"
Write-Host "  $propsDir\hourglass*.png"
Write-Host "  $propsDir\shadow_*.png"
