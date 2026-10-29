$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'assets\sounds\default'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$sampleRate = 44100

function Write-Wav([string]$path, [double[]]$samples) {
    $n = $samples.Length
    $dataBytes = $n * 2
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
    $writer.Write([int](36 + $dataBytes))
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('WAVE'))
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('fmt '))
    $writer.Write([int]16)
    $writer.Write([int16]1)
    $writer.Write([int16]1)
    $writer.Write([int]$sampleRate)
    $writer.Write([int]($sampleRate * 2))
    $writer.Write([int16]2)
    $writer.Write([int16]16)
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
    $writer.Write([int]$dataBytes)
    for ($i = 0; $i -lt $n; $i++) {
        $v = [Math]::Max(-1.0, [Math]::Min(1.0, $samples[$i]))
        $writer.Write([int16]($v * 32767))
    }
    $writer.Flush()
    [System.IO.File]::WriteAllBytes($path, $stream.ToArray())
    $writer.Dispose()
    $stream.Dispose()
}

function New-Meow(
    [double]$duration,
    [double]$f0,
    [double]$f1,
    [double]$f2,
    [double]$vibratoHz,
    [double]$vibratoDepth,
    [double]$amp,
    [double]$breath = 0.006) {
    $n = [int]($duration * $sampleRate)
    $samples = New-Object double[] $n
    $phase = 0.0
    $seed = 12345
    for ($i = 0; $i -lt $n; $i++) {
        $t = $i / $sampleRate
        $p = $t / $duration
        if ($p -lt 0.22) {
            $f = $f0 + ($f1 - $f0) * ($p / 0.22)
        }
        else {
            $f = $f1 + ($f2 - $f1) * (($p - 0.22) / 0.78)
        }
        $f += $vibratoDepth * [Math]::Sin(2 * [Math]::PI * $vibratoHz * $t)
        $phase += 2 * [Math]::PI * $f / $sampleRate
        $attack = [Math]::Min(1.0, $t / 0.035)
        $release = [Math]::Min(1.0, ($duration - $t) / 0.14)
        $env = $attack * $release
        $v = [Math]::Sin($phase) + 0.34 * [Math]::Sin(2 * $phase) + 0.13 * [Math]::Sin(3 * $phase)
        $seed = (1103515245 * $seed + 12345) -band 0x7fffffff
        $noise = (($seed / 1073741824.0) - 1.0) * $breath
        $samples[$i] = $amp * $env * $v + $noise * $env
    }
    return $samples
}

function New-Purr([double]$duration, [double]$amp) {
    $n = [int]($duration * $sampleRate)
    $samples = New-Object double[] $n
    $phase = 0.0
    $seed = 777
    for ($i = 0; $i -lt $n; $i++) {
        $t = $i / $sampleRate
        $phase += 2 * [Math]::PI * 68.0 / $sampleRate
        $mod = 0.55 + 0.45 * [Math]::Sin(2 * [Math]::PI * 24.0 * $t)
        $seed = (1103515245 * $seed + 12345) -band 0x7fffffff
        $noise = (($seed / 1073741824.0) - 1.0) * 0.18
        $v = ([Math]::Sin($phase) + 0.4 * [Math]::Sin(2 * $phase)) * $mod + $noise * $mod
        $samples[$i] = $amp * $v
    }
    return $samples
}

$events = @(
    @{ File = 'interaction.pet.wav';        Duration = 0.55; F0 = 520; F1 = 620; F2 = 430; Vib = 6;   Depth = 6;  Amp = 0.36; Breath = 0.004 },
    @{ File = 'interaction.pickup.wav';     Duration = 0.50; F0 = 500; F1 = 760; F2 = 420; Vib = 7;   Depth = 8;  Amp = 0.40; Breath = 0.005 },
    @{ File = 'interaction.drop.wav';       Duration = 0.30; F0 = 280; F1 = 240; F2 = 180; Vib = 5;   Depth = 4;  Amp = 0.30; Breath = 0.004 },
    @{ File = 'interaction.play_start.wav'; Duration = 0.45; F0 = 420; F1 = 760; F2 = 650; Vib = 8;   Depth = 7;  Amp = 0.34; Breath = 0.004 },
    @{ File = 'interaction.ball_bat.wav';   Duration = 0.12; F0 = 850; F1 = 700; F2 = 650; Vib = 0;   Depth = 0;  Amp = 0.26; Breath = 0.002 },
    @{ File = 'interaction.wake.wav';       Duration = 0.50; F0 = 380; F1 = 480; F2 = 320; Vib = 5;   Depth = 5;  Amp = 0.30; Breath = 0.004 },
    @{ File = 'timer.focus_start.wav';      Duration = 0.60; F0 = 480; F1 = 640; F2 = 520; Vib = 6;   Depth = 5;  Amp = 0.36; Breath = 0.004 },
    @{ File = 'timer.focus_complete.wav';   Duration = 1.20; F0 = 500; F1 = 750; F2 = 560; Vib = 6;   Depth = 7;  Amp = 0.38; Breath = 0.005 },
    @{ File = 'timer.break_start.wav';      Duration = 0.45; F0 = 600; F1 = 780; F2 = 600; Vib = 7;   Depth = 6;  Amp = 0.34; Breath = 0.004 },
    @{ File = 'timer.break_complete.wav';   Duration = 0.70; F0 = 520; F1 = 680; F2 = 500; Vib = 6;   Depth = 6;  Amp = 0.36; Breath = 0.004 },
    @{ File = 'timer.pause.wav';            Duration = 0.35; F0 = 550; F1 = 640; F2 = 700; Vib = 6;   Depth = 5;  Amp = 0.30; Breath = 0.004 },
    @{ File = 'timer.skip.wav';             Duration = 0.25; F0 = 420; F1 = 380; F2 = 340; Vib = 5;   Depth = 4;  Amp = 0.26; Breath = 0.003 }
)

foreach ($event in $events) {
    $path = Join-Path $outDir $event.File
    $samples = New-Meow -duration $event.Duration -f0 $event.F0 -f1 $event.F1 -f2 $event.F2 `
        -vibratoHz $event.Vib -vibratoDepth $event.Depth -amp $event.Amp -breath $event.Breath
    Write-Wav $path $samples
    Write-Output ("wrote {0} ({1:N0} bytes)" -f $event.File, (Get-Item $path).Length)
}

$purrPath = Join-Path $outDir 'ambient.purr.wav'
Write-Wav $purrPath (New-Purr -duration 10.0 -amp 0.30)
Write-Output ("wrote ambient.purr.wav ({0:N0} bytes)" -f (Get-Item $purrPath).Length)

$total = (Get-ChildItem $outDir -File | Measure-Object Length -Sum).Sum
Write-Output ("total default audio: {0:N0} bytes ({1:N1} KB)" -f $total, ($total / 1KB))
