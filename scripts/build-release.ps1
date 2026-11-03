param(
    [string]$Version = '',
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$NsisPath = '',
    [switch]$SkipPublish,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\KaixinPet.App\KaixinPet.App.csproj'

if (-not $Version) {
    [xml]$projectXml = Get-Content -LiteralPath $project -Raw -Encoding UTF8
    $versionNode = $projectXml.Project.PropertyGroup |
        Where-Object { $_.Version } |
        Select-Object -First 1
    $Version = if ($versionNode) { $versionNode.Version } else { '0.0.0' }
}

$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $dotnet = 'dotnet'
}

$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts "publish\Kaixin-$Version-$Runtime"
$zipPath = Join-Path $artifacts "Kaixin-$Version-$Runtime-portable.zip"
$setupPath = Join-Path $artifacts "Kaixin-$Version-Setup.exe"
$nsisScript = Join-Path $root 'installer\KaixinPet.nsi'
$iconPath = Join-Path $root 'assets\ui\app.ico'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

if (-not $SkipPublish) {
    if (Test-Path $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    Write-Host "publishing $Version ($Runtime, self-contained)..."
    & $dotnet publish $project -c $Configuration -r $Runtime --self-contained true `
        -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $publishDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed ($LASTEXITCODE)"
    }
}

$exe = Join-Path $publishDir 'KaixinPet.exe'
if (-not (Test-Path $exe)) {
    throw "published exe not found: $exe"
}

# Portable package: data stays next to the exe via portable.flag.
$portableFlag = Join-Path $publishDir 'portable.flag'
$createdFlag = $false
if (-not (Test-Path $portableFlag)) {
    New-Item -ItemType File -Path $portableFlag | Out-Null
    $createdFlag = $true
}

try {
    if (Test-Path $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Write-Host 'compressing portable package...'
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
}
finally {
    if ($createdFlag) {
        Remove-Item -LiteralPath $portableFlag -Force
    }
}

if (-not $SkipInstaller) {
    if (-not (Test-Path $nsisScript)) {
        throw "NSIS script not found: $nsisScript"
    }

    $candidates = @(
        $NsisPath,
        (Join-Path $root 'work\tools\nsis\nsis-3.10\makensis.exe')
    )
    if ($env:ProgramFiles) {
        $candidates += Join-Path $env:ProgramFiles 'NSIS\makensis.exe'
    }
    if (${env:ProgramFiles(x86)}) {
        $candidates += Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe'
    }

    $nsis = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $nsis) {
        $command = Get-Command makensis.exe -ErrorAction SilentlyContinue
        if ($command) {
            $nsis = $command.Source
        }
    }

    if (-not $nsis) {
        throw 'makensis.exe not found. Install NSIS 3 or pass -NsisPath.'
    }

    if (Test-Path $setupPath) {
        Remove-Item -LiteralPath $setupPath -Force
    }

    Write-Host "building installer with $nsis ..."
    & $nsis '/INPUTCHARSET' 'UTF8' "/DAPP_VERSION=$Version" `
        "/DPUBLISH_DIR=$publishDir" "/DOUTPUT_DIR=$artifacts" "/DAPP_ICON=$iconPath" `
        $nsisScript
    if ($LASTEXITCODE -ne 0) {
        throw "makensis failed ($LASTEXITCODE)"
    }
}

$hashes = @()
foreach ($file in @($setupPath, $zipPath)) {
    if (Test-Path $file) {
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        $hashes += '{0}  {1}' -f $hash, (Split-Path -Leaf $file)
    }
}

$sumPath = Join-Path $artifacts 'SHA256SUMS.txt'
$hashes | Set-Content -LiteralPath $sumPath -Encoding ascii
$releaseDir = Join-Path $root "docs\release-notes\$Version"
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
Copy-Item -LiteralPath $sumPath -Destination (Join-Path $releaseDir 'SHA256SUMS.txt') -Force

Write-Host ''
Write-Host "release $Version" -ForegroundColor Green
foreach ($file in @($setupPath, $zipPath)) {
    if (Test-Path $file) {
        $size = [Math]::Round((Get-Item $file).Length / 1MB, 1)
        Write-Host ("  {0}  ({1} MB)" -f $file, $size)
    }
}
Write-Host ("  {0}" -f $sumPath)
Write-Host ("  {0}" -f (Join-Path $releaseDir 'SHA256SUMS.txt'))
