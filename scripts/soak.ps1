param(
    [double]$Hours = 8,
    [string]$Version = '',
    [string]$SourceDir = '',
    [switch]$KeepCopy
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $Version) {
    [xml]$projectXml = Get-Content -LiteralPath (Join-Path $root 'src\KaixinPet.App\KaixinPet.App.csproj') -Raw -Encoding UTF8
    $versionNode = $projectXml.Project.PropertyGroup |
        Where-Object { $_.Version } |
        Select-Object -First 1
    $Version = if ($versionNode) { $versionNode.Version } else { '0.0.0' }
}

if (-not $SourceDir) {
    $SourceDir = Join-Path $root "artifacts\publish\Kaixin-$Version-win-x64"
}

if (-not (Test-Path (Join-Path $SourceDir 'KaixinPet.exe'))) {
    throw "published build not found: $SourceDir (run scripts\build-release.ps1 first)"
}

$soakRoot = Join-Path $root 'artifacts\soak'
$soakDir = Join-Path $soakRoot 'KaixinPet-soak'
if (Test-Path $soakDir) {
    Remove-Item -LiteralPath $soakDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $soakRoot | Out-Null
New-Item -ItemType Directory -Force -Path $soakDir | Out-Null
Write-Host 'copying portable soak build...'
Copy-Item -Path (Join-Path $SourceDir '*') -Destination $soakDir -Recurse -Force
New-Item -ItemType File -Path (Join-Path $soakDir 'portable.flag') | Out-Null

$seconds = [int][Math]::Round($Hours * 3600)
if ($seconds -lt 30) {
    $seconds = 30
}

$exe = Join-Path $soakDir 'KaixinPet.exe'
$clock = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host ("soak start: {0:N0}s ({1:N2}h) with {2}" -f $seconds, ($seconds / 3600.0), $exe)
$process = Start-Process -FilePath $exe -ArgumentList @('--benchmark', $seconds) -PassThru
$process.WaitForExit()
$clock.Stop()

$logDir = Join-Path $soakDir 'data\logs'
$log = Get-ChildItem -LiteralPath $logDir -Filter 'app-*.log' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
$result = if ($log) {
    Select-String -LiteralPath $log.FullName -Pattern 'BENCHMARK RESULT' | Select-Object -Last 1
}

if (-not $result) {
    throw "soak finished without BENCHMARK RESULT (exit=$($process.ExitCode), elapsed=$($clock.Elapsed))"
}

Write-Host ("soak elapsed: {0:hh\:mm\:ss}" -f $clock.Elapsed)
Write-Host $result.Line

if ($KeepCopy) {
    Write-Host "soak copy kept for log inspection: $soakDir"
}
else {
    Remove-Item -LiteralPath $soakDir -Recurse -Force
    Write-Host 'soak copy removed'
}
