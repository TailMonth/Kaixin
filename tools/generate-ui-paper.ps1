$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'assets\ui'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outPath = Join-Path $outDir 'paper-tile.png'

$size = 128
$bmp = New-Object System.Drawing.Bitmap $size, $size
$seed = 20261028
for ($y = 0; $y -lt $size; $y++) {
    for ($x = 0; $x -lt $size; $x++) {
        $seed = (1103515245 * $seed + 12345) -band 0x7fffffff
        $noise = (($seed / 1073741824.0) - 1.0) * 3.2
        $mottle = [Math]::Sin(($x + $y * 0.6) * 0.055) * 1.4 + [Math]::Sin(($x * 0.21 - $y) * 0.035) * 1.1
        $fiber = 0.0
        if (($x + $y * 3) % 97 -eq 0) { $fiber = -4.0 }
        if (($y + $x * 2) % 151 -eq 0) { $fiber = -3.0 }
        $r = [Math]::Max(0, [Math]::Min(255, [int](247 + $noise + $mottle + $fiber)))
        $g = [Math]::Max(0, [Math]::Min(255, [int](242 + $noise * 0.9 + $mottle + $fiber)))
        $b = [Math]::Max(0, [Math]::Min(255, [int](232 + $noise * 0.75 + $mottle * 0.8 + $fiber)))
        $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $r, $g, $b))
    }
}

$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("wrote {0} ({1:N0} bytes)" -f $outPath, (Get-Item $outPath).Length)
