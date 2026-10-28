param(
    [string]$OutRoot = (Join-Path $PSScriptRoot '..\assets')
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$charDir = Join-Path $OutRoot 'characters\kaixin'
$propsDir = Join-Path $OutRoot 'props'
New-Item -ItemType Directory -Force -Path $charDir, $propsDir | Out-Null

function Col {
    param([string]$Hex, [int]$Alpha = 255)
    $c = [System.Drawing.ColorTranslator]::FromHtml($Hex)
    return [System.Drawing.Color]::FromArgb($Alpha, $c.R, $c.G, $c.B)
}

function New-Canvas {
    param([int]$Size = 384)
    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
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
    param($G, [float]$Cx, [float]$Cy, [string]$Style)
    $white = New-Object System.Drawing.SolidBrush((Col '#F8F4EA'))
    $iris = New-Object System.Drawing.SolidBrush((Col '#9DBE86'))
    $pupil = New-Object System.Drawing.SolidBrush((Col '#2E342A'))
    $glint = New-Object System.Drawing.SolidBrush((Col '#FFFFFF' 235))
    $line = New-Pen (Col '#3E3A34' 210) 2.2
    $fur = New-Object System.Drawing.SolidBrush((Col '#7B7469'))

    if ($Style -eq 'happy') {
        $G.DrawArc($line, $Cx - 13, $Cy - 8, 26, 16, 200, 140)
    }
    elseif ($Style -eq 'closed') {
        $G.FillEllipse($fur, $Cx - 15, $Cy - 15, 30, 30)
        $G.DrawArc($line, $Cx - 12, $Cy - 4, 24, 14, 200, 140)
    }
    elseif ($Style -eq 'half') {
        $G.FillEllipse($white, $Cx - 15, $Cy - 15, 30, 30)
        $G.FillEllipse($iris, $Cx - 13, $Cy - 13, 26, 26)
        $G.FillEllipse($pupil, $Cx - 4, $Cy - 10, 8, 20)
        $clip = New-Object System.Drawing.Drawing2D.GraphicsPath
        $clip.AddEllipse($Cx - 16, $Cy - 16, 32, 32)
        $G.SetClip($clip)
        $G.FillRectangle($fur, $Cx - 17, $Cy - 17, 34, 17)
        $G.ResetClip()
        $G.DrawLine($line, $Cx - 12, $Cy, $Cx + 12, $Cy)
        $clip.Dispose()
    }
    else {
        $G.FillEllipse($white, $Cx - 15, $Cy - 15, 30, 30)
        $G.FillEllipse($iris, $Cx - 13, $Cy - 13, 26, 26)
        if ($Style -eq 'notice') {
            $G.FillEllipse($pupil, $Cx - 5, $Cy - 11, 10, 22)
        }
        else {
            $G.FillEllipse($pupil, $Cx - 4, $Cy - 10, 8, 20)
        }
        $G.FillEllipse($glint, $Cx - 8, $Cy - 11, 5, 5)
        $G.DrawEllipse($line, $Cx - 15, $Cy - 15, 30, 30)
    }

    $white.Dispose(); $iris.Dispose(); $pupil.Dispose(); $glint.Dispose(); $line.Dispose(); $fur.Dispose()
}

function Draw-Heart {
    param($G, [float]$X, [float]$Y, [float]$Size, [int]$Alpha)
    $brush = New-Object System.Drawing.SolidBrush((Col '#C9604F' $Alpha))
    $r = $Size / 4
    $G.FillEllipse($brush, $X - $Size / 2, $Y - $r, $Size / 2, $Size / 2)
    $G.FillEllipse($brush, $X, $Y - $r, $Size / 2, $Size / 2)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new($X - $Size / 2, $Y)),
        ([System.Drawing.PointF]::new($X + $Size / 2, $Y)),
        ([System.Drawing.PointF]::new($X, $Y + $Size * 0.75))))
    $G.FillPath($brush, $path)
    $path.Dispose(); $brush.Dispose()
}

function Draw-Z {
    param($G, [float]$X, [float]$Y, [float]$Size, [int]$Alpha)
    $pen = New-Pen (Col '#6F6A61' $Alpha) ([Math]::Max(2.0, $Size * 0.16))
    $G.DrawLine($pen, $X, $Y, $X + $Size, $Y)
    $G.DrawLine($pen, $X + $Size, $Y, $X, $Y + $Size)
    $G.DrawLine($pen, $X, $Y + $Size, $X + $Size, $Y + $Size)
    $pen.Dispose()
}

function Draw-Sit {
    param(
        $G,
        [double]$Phase = 0,
        [double]$HeadDx = 0,
        [double]$HeadDy = 0,
        [int]$EarTwitch = 0,
        [string]$EyeStyle = 'open',
        [double]$PawLift = 0
    )

    $t = $Phase / 8.0 * 2 * [math]::PI
    $lift = [math]::Round(2.0 * [math]::Sin($t))
    $sway = [math]::Round(6.0 * [math]::Sin($t + 0.7))

    $fur = Col '#7B7469'
    $furLight = Col '#8A8277'
    $furDark = Col '#5F5A52'
    $lineColor = Col '#4A443C' 140
    $stripeColor = Col '#4A473F' 140
    $outline = New-Pen $lineColor 1.6

    $tailPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tailPath.AddBezier(158, 344, 112, 356, 64 + $sway, 336, 74 + $sway, 292)
    $tailPen = New-Pen $fur 30
    $G.DrawPath($tailPen, $tailPath)
    $tailTip = New-Object System.Drawing.SolidBrush((Col '#5B564E'))
    $G.FillEllipse($tailTip, (74 + $sway - 15), 277, 30, 30)

    $top = 210 - $lift
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddBezier(114, 358, 104, 300, 112, ($top + 44), 146, ($top + 10))
    $body.AddBezier(146, ($top + 10), 170, ($top - 6), 214, ($top - 6), 238, ($top + 10))
    $body.AddBezier(238, ($top + 10), 272, ($top + 44), 280, 300, 270, 358)
    $body.CloseFigure()

    $bodyRect = [System.Drawing.Rectangle]::new(100, [int]$top, 200, [int](360 - $top))
    $bodyGradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush($bodyRect, $furLight, $furDark, [float]90)
    $G.FillPath($bodyGradient, $body)
    $G.DrawPath($outline, $body)

    $chest = New-Object System.Drawing.SolidBrush((Col '#8A8277' 90))
    $G.FillEllipse($chest, 158, 238, 68, 90)

    $stripePen = New-Pen $stripeColor 5
    $G.DrawArc($stripePen, 118, 248, 40, 34, 110, 115)
    $G.DrawArc($stripePen, 226, 248, 40, 34, -45, 115)
    $G.DrawArc($stripePen, 122, 290, 34, 28, 115, 105)
    $G.DrawArc($stripePen, 228, 290, 34, 28, -40, 105)

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

    if ($PawLift -gt 0.01) {
        $pawX = 192 + $HeadDx - 52
        $pawY = 198 + $HeadDy - 30 * $PawLift
        $G.FillEllipse($white, $pawX, $pawY, 42, 34)
        $G.DrawEllipse($outline, $pawX, $pawY, 42, 34)
        $pawToe = New-Pen (Col '#B9B0A0' 170) 1.4
        $G.DrawLine($pawToe, $pawX + 14, $pawY + 26, $pawX + 14, $pawY + 31)
        $G.DrawLine($pawToe, $pawX + 27, $pawY + 26, $pawX + 27, $pawY + 31)
        $pawToe.Dispose()
    }

    $neck = New-Object System.Drawing.SolidBrush($fur)
    $G.FillEllipse($neck, 156 + $HeadDx, 196 + $HeadDy, 72, 55)

    $earBrush = New-Object System.Drawing.SolidBrush((Col '#6F6860'))
    $innerBrush = New-Object System.Drawing.SolidBrush((Col '#D9A6A0' 225))
    $twitch = $EarTwitch * 8

    $earLeft = New-Object System.Drawing.Drawing2D.GraphicsPath
    $earLeft.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new(120 + $HeadDx, 116 + $HeadDy)),
        ([System.Drawing.PointF]::new(134 + $HeadDx, 42 + $HeadDy)),
        ([System.Drawing.PointF]::new(180 + $HeadDx, 98 + $HeadDy))))
    $G.FillPath($earBrush, $earLeft)
    $G.DrawPath($outline, $earLeft)

    $innerLeft = New-Object System.Drawing.Drawing2D.GraphicsPath
    $innerLeft.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new(135 + $HeadDx, 103 + $HeadDy)),
        ([System.Drawing.PointF]::new(143 + $HeadDx, 64 + $HeadDy)),
        ([System.Drawing.PointF]::new(166 + $HeadDx, 97 + $HeadDy))))
    $G.FillPath($innerBrush, $innerLeft)

    $earRight = New-Object System.Drawing.Drawing2D.GraphicsPath
    $earRight.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new(264 + $HeadDx + $twitch, 116 + $HeadDy)),
        ([System.Drawing.PointF]::new(250 + $HeadDx + $twitch, 42 + $HeadDy)),
        ([System.Drawing.PointF]::new(204 + $HeadDx, 98 + $HeadDy))))
    $G.FillPath($earBrush, $earRight)
    $G.DrawPath($outline, $earRight)

    $innerRight = New-Object System.Drawing.Drawing2D.GraphicsPath
    $innerRight.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new(249 + $HeadDx + $twitch, 103 + $HeadDy)),
        ([System.Drawing.PointF]::new(241 + $HeadDx, 64 + $HeadDy)),
        ([System.Drawing.PointF]::new(218 + $HeadDx, 97 + $HeadDy))))
    $G.FillPath($innerBrush, $innerRight)

    $headRect = [System.Drawing.Rectangle]::new(112 + $HeadDx, 74 + $HeadDy, 160, 144)
    $headGradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush($headRect, $furLight, (Col '#6E675D'), [float]90)
    $G.FillEllipse($headGradient, $headRect)
    $G.DrawEllipse($outline, $headRect)

    $foreheadPen = New-Pen (Col '#4A473F' 175) 4
    $G.DrawLine($foreheadPen, (177 + $HeadDx), (92 + $HeadDy), (177 + $HeadDx), (117 + $HeadDy))
    $G.DrawLine($foreheadPen, (192 + $HeadDx), (87 + $HeadDy), (192 + $HeadDx), (112 + $HeadDy))
    $G.DrawLine($foreheadPen, (207 + $HeadDx), (92 + $HeadDy), (207 + $HeadDx), (117 + $HeadDy))

    $cheekPen = New-Pen (Col '#4A473F' 120) 3.5
    $G.DrawArc($cheekPen, (116 + $HeadDx), (132 + $HeadDy), 34, 44, 95, 100)
    $G.DrawArc($cheekPen, (234 + $HeadDx), (132 + $HeadDy), 34, 44, -15, 100)

    Draw-Eye $G (163 + $HeadDx) (148 + $HeadDy) $EyeStyle
    Draw-Eye $G (221 + $HeadDx) (148 + $HeadDy) $EyeStyle

    $muzzle = New-Object System.Drawing.SolidBrush((Col '#F8F4EA'))
    $G.FillEllipse($muzzle, (167 + $HeadDx), (181 + $HeadDy), 24, 17)
    $G.FillEllipse($muzzle, (193 + $HeadDx), (181 + $HeadDy), 24, 17)

    $nosePath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $nosePath.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new(187 + $HeadDx, 179 + $HeadDy)),
        ([System.Drawing.PointF]::new(197 + $HeadDx, 179 + $HeadDy)),
        ([System.Drawing.PointF]::new(192 + $HeadDx, 186 + $HeadDy))))
    $noseBrush = New-Object System.Drawing.SolidBrush((Col '#E9A0A6'))
    $G.FillPath($noseBrush, $nosePath)

    $mouthPen = New-Pen (Col '#4A443C' 160) 1.4
    $G.DrawLine($mouthPen, (192 + $HeadDx), (186 + $HeadDy), (192 + $HeadDx), (192 + $HeadDy))
    $G.DrawArc($mouthPen, (181 + $HeadDx), (186 + $HeadDy), 22, 10, 10, 160)

    $whiskerPen = New-Pen (Col '#4A443C' 95) 1
    $G.DrawLine($whiskerPen, (167 + $HeadDx), (195 + $HeadDy), (110 + $HeadDx), (187 + $HeadDy))
    $G.DrawLine($whiskerPen, (167 + $HeadDx), (200 + $HeadDy), (110 + $HeadDx), (204 + $HeadDy))
    $G.DrawLine($whiskerPen, (217 + $HeadDx), (195 + $HeadDy), (274 + $HeadDx), (187 + $HeadDy))
    $G.DrawLine($whiskerPen, (217 + $HeadDx), (200 + $HeadDy), (274 + $HeadDx), (204 + $HeadDy))

    foreach ($item in @($tailPath, $tailPen, $tailTip, $body, $bodyGradient, $chest, $stripePen,
                        $white, $legLeft, $legRight, $toePen, $neck, $earBrush, $innerBrush,
                        $earLeft, $innerLeft, $earRight, $innerRight, $headGradient, $foreheadPen,
                        $cheekPen, $muzzle, $nosePath, $noseBrush, $mouthPen, $whiskerPen, $outline)) {
        if ($null -ne $item) { $item.Dispose() }
    }
}

function Draw-Stretch {
    param($G, [int]$Phase)
    $t = $Phase / 6.0 * [math]::PI
    $reach = [math]::Round(30 * [math]::Sin($t))
    $fur = Col '#7B7469'
    $furLight = Col '#8A8277'
    $furDark = Col '#5F5A52'
    $outline = New-Pen (Col '#4A443C' 140) 1.6

    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddBezier(120, 358, 100, 320, 110 + $reach, 300, 150 + $reach, 300)
    $body.AddBezier(150 + $reach, 300, 190, 270, 230, 300, 250, 330)
    $body.AddBezier(250, 330, 265, 345, 268, 358, 268, 358)
    $body.CloseFigure()
    $rect = [System.Drawing.Rectangle]::new(90, 270, 190, 90)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $furLight, $furDark, [float]90)
    $G.FillPath($grad, $body)
    $G.DrawPath($outline, $body)

    $headRect = [System.Drawing.Rectangle]::new(84 + $reach, 218, 130, 120)
    $headGrad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($headRect, $furLight, (Col '#6E675D'), [float]90)
    $G.FillEllipse($headGrad, $headRect)
    $G.DrawEllipse($outline, $headRect)

    $earLeft = New-Object System.Drawing.Drawing2D.GraphicsPath
    $earLeft.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new(90 + $reach, 245)),
        ([System.Drawing.PointF]::new(102 + $reach, 185)),
        ([System.Drawing.PointF]::new(138 + $reach, 222))))
    $G.FillPath((New-Object System.Drawing.SolidBrush((Col '#6F6860'))), $earLeft)
    $G.DrawPath($outline, $earLeft)

    $earRight = New-Object System.Drawing.Drawing2D.GraphicsPath
    $earRight.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new(208 + $reach, 245)),
        ([System.Drawing.PointF]::new(196 + $reach, 185)),
        ([System.Drawing.PointF]::new(160 + $reach, 222))))
    $G.FillPath((New-Object System.Drawing.SolidBrush((Col '#6F6860'))), $earRight)
    $G.DrawPath($outline, $earRight)

    Draw-Eye $G (120 + $reach) 272 'happy'
    Draw-Eye $G (168 + $reach) 272 'happy'

    $white = New-Object System.Drawing.SolidBrush((Col '#F8F4EA'))
    $G.FillEllipse($white, (56 + $reach), 330, 62, 34)
    $G.FillEllipse($white, (56 + $reach), 346, 62, 22)
    $G.FillEllipse($white, 236, 330, 46, 30)

    $tailPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tailPath.AddBezier(250, 330, 300, 300, 300, 250, 290, 220)
    $tailPen = New-Pen $fur 26
    $G.DrawPath($tailPen, $tailPath)

    foreach ($item in @($body, $grad, $headGrad, $earLeft, $earRight, $white, $tailPath, $tailPen, $outline)) {
        if ($null -ne $item) { $item.Dispose() }
    }
}

function Draw-Sleeping {
    param($G, [double]$Phase = 0, [double]$HeadLift = 0, [int]$Twitch = 0, [bool]$OpenEyes = $false)
    $breath = [math]::Round(2.0 * [math]::Sin($Phase / 5.0 * 2 * [math]::PI))
    $fur = Col '#7B7469'
    $furLight = Col '#8A8277'
    $furDark = Col '#5F5A52'
    $outline = New-Pen (Col '#4A443C' 140) 1.6

    $tailPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tailPath.AddBezier(300, 330, 340, 300, 230, 360, 130, 350)
    $tailPen = New-Pen $fur 32
    $G.DrawPath($tailPen, $tailPath)

    $bodyRect = [System.Drawing.Rectangle]::new(86, 286, 230, 120)
    $bodyGrad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($bodyRect, $furLight, $furDark, [float]90)
    $G.FillEllipse($bodyGrad, 86, 286 - $breath, 230, (120 + $breath))
    $G.DrawEllipse($outline, 86, 286 - $breath, 230, (120 + $breath))

    $stripePen = New-Pen (Col '#4A473F' 130) 6
    $G.DrawArc($stripePen, 120, 300, 50, 40, 150, 100)
    $G.DrawArc($stripePen, 190, 300, 50, 40, -70, 100)

    $white = New-Object System.Drawing.SolidBrush((Col '#F8F4EA'))
    $G.FillEllipse($white, 140, 340, 54, 24)
    $G.FillEllipse($white, 220, 340, 54, 24)

    $headCy = 282 - $HeadLift
    $headRect = [System.Drawing.Rectangle]::new(78, [int]($headCy - 60), 120, 120)
    $headGrad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($headRect, $furLight, (Col '#6E675D'), [float]90)
    $G.FillEllipse($headGrad, $headRect)
    $G.DrawEllipse($outline, $headRect)

    $earBrush = New-Object System.Drawing.SolidBrush((Col '#6F6860'))
    $tw = $Twitch * 6
    $leftBaseY = $headCy - 42
    $leftTipY = $headCy - 92
    $leftInnerY = $headCy - 52
    $earLeft = New-Object System.Drawing.Drawing2D.GraphicsPath
    $earLeft.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new(88, $leftBaseY)),
        ([System.Drawing.PointF]::new(94, $leftTipY)),
        ([System.Drawing.PointF]::new(128, $leftInnerY))))
    $G.FillPath($earBrush, $earLeft)
    $G.DrawPath($outline, $earLeft)

    $earRight = New-Object System.Drawing.Drawing2D.GraphicsPath
    $earRight.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new(188 + $tw, $leftBaseY)),
        ([System.Drawing.PointF]::new(182 + $tw, $leftTipY)),
        ([System.Drawing.PointF]::new(148, $leftInnerY))))
    $G.FillPath($earBrush, $earRight)
    $G.DrawPath($outline, $earRight)

    $eyeStyle = if ($OpenEyes) { if ($HeadLift -gt 20) { 'open' } else { 'half' } } else { 'closed' }
    Draw-Eye $G 112 $headCy $eyeStyle
    Draw-Eye $G 164 $headCy $eyeStyle

    $muzzle = New-Object System.Drawing.SolidBrush((Col '#F8F4EA'))
    $G.FillEllipse($muzzle, 122, ($headCy + 26), 20, 14)
    $G.FillEllipse($muzzle, 146, ($headCy + 26), 20, 14)
    $nose = New-Object System.Drawing.SolidBrush((Col '#E9A0A6'))
    $G.FillEllipse($nose, 136, ($headCy + 28), 16, 10)

    $whisker = New-Pen (Col '#4A443C' 90) 1
    $G.DrawLine($whisker, 122, ($headCy + 36), 86, ($headCy + 30))
    $G.DrawLine($whisker, 122, ($headCy + 41), 86, ($headCy + 44))
    $G.DrawLine($whisker, 166, ($headCy + 36), 202, ($headCy + 30))
    $G.DrawLine($whisker, 166, ($headCy + 41), 202, ($headCy + 44))

    foreach ($item in @($tailPath, $tailPen, $bodyGrad, $stripePen, $white, $headGrad,
                        $earBrush, $earLeft, $earRight, $muzzle, $nose, $whisker, $outline)) {
        if ($null -ne $item) { $item.Dispose() }
    }
}

function Draw-Pounce {
    param($G, [int]$Phase)
    $reach = [math]::Round(18 * $Phase / 4.0)
    $fur = Col '#7B7469'
    $furLight = Col '#8A8277'
    $furDark = Col '#5F5A52'
    $outline = New-Pen (Col '#4A443C' 140) 1.6

    $bodyRect = [System.Drawing.Rectangle]::new(130, 280, 150, 80)
    $bodyGrad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($bodyRect, $furLight, $furDark, [float]90)
    $G.FillEllipse($bodyGrad, 130 - $reach, 286, 150, 72)
    $G.DrawEllipse($outline, 130 - $reach, 286, 150, 72)

    $headRect = [System.Drawing.Rectangle]::new(64 - $reach, 232, 118, 112)
    $headGrad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($headRect, $furLight, (Col '#6E675D'), [float]90)
    $G.FillEllipse($headGrad, $headRect)
    $G.DrawEllipse($outline, $headRect)

    $earLeft = New-Object System.Drawing.Drawing2D.GraphicsPath
    $earLeft.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new(70 - $reach, 258)),
        ([System.Drawing.PointF]::new(82 - $reach, 206)),
        ([System.Drawing.PointF]::new(116 - $reach, 240))))
    $G.FillPath((New-Object System.Drawing.SolidBrush((Col '#6F6860'))), $earLeft)
    $G.DrawPath($outline, $earLeft)

    $earRight = New-Object System.Drawing.Drawing2D.GraphicsPath
    $earRight.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new(176 - $reach, 258)),
        ([System.Drawing.PointF]::new(164 - $reach, 206)),
        ([System.Drawing.PointF]::new(130 - $reach, 240))))
    $G.FillPath((New-Object System.Drawing.SolidBrush((Col '#6F6860'))), $earRight)
    $G.DrawPath($outline, $earRight)

    Draw-Eye $G (100 - $reach) 282 'notice'
    Draw-Eye $G (146 - $reach) 282 'notice'

    $white = New-Object System.Drawing.SolidBrush((Col '#F8F4EA'))
    $G.FillEllipse($white, 44 - $reach, 336, 70, 28)
    $G.FillEllipse($white, 96 - $reach, 344, 70, 22)
    $G.FillEllipse($white, 236, 344, 52, 22)

    $tailPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tailPath.AddBezier(268, 330, 330, 310, 330, 250, 316, 224)
    $tailPen = New-Pen $fur 24
    $G.DrawPath($tailPen, $tailPath)

    foreach ($item in @($bodyGrad, $headGrad, $earLeft, $earRight, $white, $tailPath, $tailPen, $outline)) {
        if ($null -ne $item) { $item.Dispose() }
    }
}

function Draw-Bat {
    param($G, [int]$Phase)
    Draw-Sit $G ($Phase * 0.8) 4 0 0 'notice' 0
    $white = New-Object System.Drawing.SolidBrush((Col '#F8F4EA'))
    $outline = New-Pen (Col '#4A443C' 140) 1.6
    $pawX = 240 + $Phase * 8
    $pawY = 226 - $Phase * 10
    $armPen = New-Pen (Col '#7B7469') 18
    $G.DrawLine($armPen, 245, 262, $pawX, $pawY + 14)
    $G.FillEllipse($white, $pawX, $pawY, 48, 34)
    $G.DrawEllipse($outline, $pawX, $pawY, 48, 34)
    $toe = New-Pen (Col '#B9B0A0' 170) 1.3
    $G.DrawLine($toe, $pawX + 14, $pawY + 28, $pawX + 14, $pawY + 33)
    $G.DrawLine($toe, $pawX + 28, $pawY + 28, $pawX + 28, $pawY + 33)
    foreach ($item in @($white, $outline, $armPen, $toe)) { $item.Dispose() }
}

function Draw-Drag {
    param($G, [int]$Phase, [int]$Pose)
    $fur = Col '#7B7469'
    $furLight = Col '#8A8277'
    $furDark = Col '#5F5A52'
    $outline = New-Pen (Col '#4A443C' 140) 1.6
    $sway = [math]::Round(6.0 * [math]::Sin($Phase / 4.0 * 2 * [math]::PI))

    if ($Pose -eq 3) {
        Draw-Pounce $G 2
        return
    }

    $tailPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tailPath.AddBezier(232, 260, 292, 240, 316, 196, 300, 158)
    $tailPen = New-Pen $fur 26
    $G.DrawPath($tailPen, $tailPath)

    $bodyRect = [System.Drawing.Rectangle]::new(156, 170, 88, 170)
    $bodyGrad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($bodyRect, $furLight, $furDark, [float]90)
    $G.FillEllipse($bodyGrad, 156, 168, 88, 170)
    $G.DrawEllipse($outline, 156, 168, 88, 170)

    $headRect = [System.Drawing.Rectangle]::new(140, 72, 120, 118)
    $headGrad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($headRect, $furLight, (Col '#6E675D'), [float]90)
    $G.FillEllipse($headGrad, $headRect)
    $G.DrawEllipse($outline, $headRect)

    $earLeft = New-Object System.Drawing.Drawing2D.GraphicsPath
    $earLeft.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new(148, 100)),
        ([System.Drawing.PointF]::new(154, 42)),
        ([System.Drawing.PointF]::new(188, 86))))
    $G.FillPath((New-Object System.Drawing.SolidBrush((Col '#6F6860'))), $earLeft)
    $G.DrawPath($outline, $earLeft)

    $earRight = New-Object System.Drawing.Drawing2D.GraphicsPath
    $earRight.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new(252, 100)),
        ([System.Drawing.PointF]::new(246, 42)),
        ([System.Drawing.PointF]::new(212, 86))))
    $G.FillPath((New-Object System.Drawing.SolidBrush((Col '#6F6860'))), $earRight)
    $G.DrawPath($outline, $earRight)

    Draw-Eye $G 174 124 'notice'
    Draw-Eye $G 222 124 'notice'

    $white = New-Object System.Drawing.SolidBrush((Col '#F8F4EA'))
    $legSwing = if ($Pose -eq 1) { $sway } else { 0 }
    $G.FillEllipse($white, 158 + $legSwing, 300, 34, 62)
    $G.FillEllipse($white, 204 + $legSwing, 300, 34, 62)
    $G.FillEllipse($white, 168 + $legSwing, 330, 28, 34)
    $G.FillEllipse($white, 198 + $legSwing, 330, 28, 34)

    foreach ($item in @($tailPath, $tailPen, $bodyGrad, $headGrad, $earLeft, $earRight, $white, $outline)) {
        if ($null -ne $item) { $item.Dispose() }
    }
}

function Draw-PetFace {
    param($G, [int]$Phase)
    $bob = [math]::Round(2 * [math]::Sin($Phase / 6.0 * 2 * [math]::PI))
    Draw-Sit $G $Phase 3 (-4 + $bob) 0 'happy' 0
    $blush = New-Object System.Drawing.SolidBrush((Col '#E9A0A6' 90))
    $G.FillEllipse($blush, 140, 174 + $bob, 26, 14)
    $G.FillEllipse($blush, 218, 174 + $bob, 26, 14)
    $blush.Dispose()
}

function Draw-Hearts {
    param($G, [int]$Phase)
    $rise = $Phase * 10
    Draw-Heart $G 150 (158 - $rise) 34 (170 - $Phase * 20)
    Draw-Heart $G 192 (140 - $rise) 26 (160 - $Phase * 18)
    Draw-Heart $G 228 (166 - $rise) 30 (150 - $Phase * 20)
}

function Draw-Zzz {
    param($G, [int]$Phase)
    $rise = $Phase * 8
    Draw-Z $G 206 (216 - $rise) 30 (160 - $Phase * 18)
    Draw-Z $G 226 (184 - $rise) 24 (140 - $Phase * 16)
    Draw-Z $G 244 (156 - $rise) 18 (120 - $Phase * 14)
}

function New-YarnFrame {
    param([int]$Frame)
    $canvas = New-Canvas 64
    $g = $canvas.Graphics
    $g.TranslateTransform(32, 32)
    $g.RotateTransform($Frame * 45)
    $g.TranslateTransform(-32, -32)
    $ball = New-Object System.Drawing.SolidBrush((Col '#C9604F'))
    $g.FillEllipse($ball, 10, 10, 44, 44)
    $outline = New-Pen (Col '#7B3F35' 200) 2
    $g.DrawEllipse($outline, 10, 10, 44, 44)
    $gold = New-Pen (Col '#E0A24C' 200) 2.2
    $g.DrawArc($gold, 12, 12, 40, 40, 20, 70)
    $g.DrawArc($gold, 14, 14, 36, 36, 120, 60)
    $g.DrawArc($gold, 16, 16, 32, 32, 220, 55)
    $shine = New-Object System.Drawing.SolidBrush((Col '#F8F4EA' 150))
    $g.FillEllipse($shine, 18, 16, 10, 8)
    foreach ($item in @($ball, $outline, $gold, $shine)) { $item.Dispose() }
    $g.Dispose()
    return $canvas.Bitmap
}

function New-SoftShadow {
    param([int]$Width, [int]$Height, [int]$Alpha, [string]$Path)
    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shape.AddEllipse(3, 3, $Width - 6, $Height - 6)
    $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush($shape)
    $brush.CenterColor = [System.Drawing.Color]::FromArgb($Alpha, 0, 0, 0)
    $brush.SurroundColors = [System.Drawing.Color[]]@([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
    $g.FillPath($brush, $shape)
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $brush.Dispose(); $shape.Dispose(); $g.Dispose(); $bitmap.Dispose()
}

function New-Strip {
    param([int]$Count)
    $bitmap = [System.Drawing.Bitmap]::new($Count * 192, 192, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    return [pscustomobject]@{ Bitmap = $bitmap; Graphics = $graphics }
}

function Add-Frame {
    param($Strip, [int]$Index, $Frame)
    $dest = [System.Drawing.Rectangle]::new(($Index * 192), 0, 192, 192)
    $src = [System.Drawing.Rectangle]::new(0, 0, 384, 384)
    $Strip.Graphics.DrawImage($Frame.Bitmap, $dest, $src, [System.Drawing.GraphicsUnit]::Pixel)
}

function Save-Strip {
    param($Strip, [string]$Name)
    $path = Join-Path $charDir $Name
    $Strip.Bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $Strip.Graphics.Dispose()
    $Strip.Bitmap.Dispose()
    Write-Host "  $path"
}

# idle.png: 8 breath frames
$strip = New-Strip 8
for ($i = 0; $i -lt 8; $i++) {
    $frame = New-Canvas
    Draw-Sit $frame.Graphics $i
    Add-Frame $strip $i $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
Save-Strip $strip 'idle.png'

# eyes.png: blink overlay
$strip = New-Strip 3
$frame = New-Canvas
Add-Frame $strip 0 $frame
$frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
for ($i = 1; $i -le 2; $i++) {
    $frame = New-Canvas
    if ($i -eq 1) {
        foreach ($cx in @(163, 221)) {
            $clip = New-Object System.Drawing.Drawing2D.GraphicsPath
            $clip.AddEllipse($cx - 16, 131, 32, 32)
            $frame.Graphics.SetClip($clip)
            $frame.Graphics.FillRectangle((New-Object System.Drawing.SolidBrush((Col '#7B7469'))), $cx - 17, 131, 34, 17)
            $frame.Graphics.ResetClip()
            $frame.Graphics.DrawLine((New-Pen (Col '#3E3A34' 210) 2.2), $cx - 13, 148, $cx + 13, 148)
            $clip.Dispose()
        }
    }
    else {
        foreach ($cx in @(163, 221)) {
            $clip = New-Object System.Drawing.Drawing2D.GraphicsPath
            $clip.AddEllipse($cx - 16, 131, 32, 32)
            $frame.Graphics.SetClip($clip)
            $frame.Graphics.FillEllipse((New-Object System.Drawing.SolidBrush((Col '#7B7469'))), $cx - 17, 131, 34, 32)
            $frame.Graphics.ResetClip()
            $path = New-Object System.Drawing.Drawing2D.GraphicsPath
            $path.AddBezier($cx - 13, 148, $cx - 6, 155, $cx + 6, 155, $cx + 13, 148)
            $frame.Graphics.DrawPath((New-Pen (Col '#3E3A34' 210) 2.2), $path)
            $clip.Dispose(); $path.Dispose()
        }
    }
    Add-Frame $strip $i $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
Save-Strip $strip 'eyes.png'

# actions.png: tail 0-5, look 6-11, groom 12-17, stretch 18-23, ear 24-26
$strip = New-Strip 27
for ($i = 0; $i -lt 6; $i++) {
    $frame = New-Canvas
    Draw-Sit $frame.Graphics $i
    Add-Frame $strip $i $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
$look = @(0, -8, -14, -10, -4, 4)
for ($i = 0; $i -lt 6; $i++) {
    $frame = New-Canvas
    Draw-Sit $frame.Graphics ($i * 0.5) $look[$i] 0 0 'open' 0
    Add-Frame $strip (6 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
$groomDy = @(6, 10, 14, 12, 8, 2)
$groomPaw = @(0, 0.4, 0.8, 1, 0.6, 0)
for ($i = 0; $i -lt 6; $i++) {
    $frame = New-Canvas
    Draw-Sit $frame.Graphics ($i * 0.4) 6 $groomDy[$i] 0 'closed' $groomPaw[$i]
    Add-Frame $strip (12 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
for ($i = 0; $i -lt 6; $i++) {
    $frame = New-Canvas
    Draw-Stretch $frame.Graphics $i
    Add-Frame $strip (18 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
$ear = @(0, 1, 0)
for ($i = 0; $i -lt 3; $i++) {
    $frame = New-Canvas
    Draw-Sit $frame.Graphics 0 0 0 $ear[$i] 'open' 0
    Add-Frame $strip (24 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
Save-Strip $strip 'actions.png'

# sleep.png: curl 0-3, breath 4-8, twitch 9-11, wake 12-15
$strip = New-Strip 16
for ($i = 0; $i -lt 4; $i++) {
    $frame = New-Canvas
    Draw-Sleeping $frame.Graphics $i 0 0 $false
    Add-Frame $strip $i $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
$breath = @(0, 1, 2, 1, 0)
for ($i = 0; $i -lt 5; $i++) {
    $frame = New-Canvas
    Draw-Sleeping $frame.Graphics $breath[$i] 0 0 $false
    Add-Frame $strip (4 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
$twitch = @(0, 1, 0)
for ($i = 0; $i -lt 3; $i++) {
    $frame = New-Canvas
    Draw-Sleeping $frame.Graphics 0 0 $twitch[$i] $false
    Add-Frame $strip (9 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
$lift = @(0, 10, 22, 34)
for ($i = 0; $i -lt 4; $i++) {
    $frame = New-Canvas
    Draw-Sleeping $frame.Graphics 0 $lift[$i] 0 ($i -ge 2)
    Add-Frame $strip (12 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
Save-Strip $strip 'sleep.png'

# play.png: notice 0-2, pounce 3-7, bat 8-12, chase 13-17, flop 18-21
$strip = New-Strip 22
for ($i = 0; $i -lt 3; $i++) {
    $frame = New-Canvas
    Draw-Sit $frame.Graphics ($i * 0.6) 4 (-2) 0 'notice' 0
    Add-Frame $strip $i $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
for ($i = 0; $i -lt 5; $i++) {
    $frame = New-Canvas
    Draw-Pounce $frame.Graphics $i
    Add-Frame $strip (3 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
for ($i = 0; $i -lt 5; $i++) {
    $frame = New-Canvas
    Draw-Bat $frame.Graphics $i
    Add-Frame $strip (8 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
for ($i = 0; $i -lt 5; $i++) {
    $frame = New-Canvas
    Draw-Sit $frame.Graphics ($i * 0.8) (4 + ($i % 2) * 2) 0 0 'notice' 0
    Add-Frame $strip (13 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
for ($i = 0; $i -lt 4; $i++) {
    $frame = New-Canvas
    Draw-Sleeping $frame.Graphics $i 18 0 $true
    Add-Frame $strip (18 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
Save-Strip $strip 'play.png'

# drag.png: grab 0-3, hold 4-7, fall 8-11, land 12-15
$strip = New-Strip 16
for ($i = 0; $i -lt 4; $i++) {
    $frame = New-Canvas
    Draw-Drag $frame.Graphics $i 0
    Add-Frame $strip $i $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
for ($i = 0; $i -lt 4; $i++) {
    $frame = New-Canvas
    Draw-Drag $frame.Graphics $i 1
    Add-Frame $strip (4 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
for ($i = 0; $i -lt 4; $i++) {
    $frame = New-Canvas
    Draw-Drag $frame.Graphics $i 2
    Add-Frame $strip (8 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
for ($i = 0; $i -lt 4; $i++) {
    $frame = New-Canvas
    Draw-Pounce $frame.Graphics $i
    Add-Frame $strip (12 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
Save-Strip $strip 'drag.png'

# reactions.png: pet 0-5, hearts 6-11, zzz 12-17
$strip = New-Strip 18
for ($i = 0; $i -lt 6; $i++) {
    $frame = New-Canvas
    Draw-PetFace $frame.Graphics $i
    Add-Frame $strip $i $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
for ($i = 0; $i -lt 6; $i++) {
    $frame = New-Canvas
    Draw-Hearts $frame.Graphics $i
    Add-Frame $strip (6 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
for ($i = 0; $i -lt 6; $i++) {
    $frame = New-Canvas
    Draw-Zzz $frame.Graphics $i
    Add-Frame $strip (12 + $i) $frame
    $frame.Graphics.Dispose(); $frame.Bitmap.Dispose()
}
Save-Strip $strip 'reactions.png'

# yarn.png: 8 rotation frames at 64 px
$yarn = New-Object System.Drawing.Bitmap(512, 64, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$yg = [System.Drawing.Graphics]::FromImage($yarn)
$yg.Clear([System.Drawing.Color]::Transparent)
for ($i = 0; $i -lt 8; $i++) {
    $ball = New-YarnFrame $i
    $yg.DrawImageUnscaled($ball, ($i * 64), 0)
    $ball.Dispose()
}
$yarn.Save((Join-Path $propsDir 'yarn.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$yg.Dispose(); $yarn.Dispose()

# Shadows
New-SoftShadow 320 80 70 (Join-Path $propsDir 'shadow_cat.png')
New-SoftShadow 360 90 60 (Join-Path $propsDir 'shadow_sleep.png')
New-SoftShadow 176 52 46 (Join-Path $propsDir 'shadow_hourglass.png')

Write-Host 'M2 placeholder art generated.'
