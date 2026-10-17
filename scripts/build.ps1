<#
.SYNOPSIS
    Local release build for VR Monitor Switch.

.DESCRIPTION
    1. dotnet publish (Release, net48)            -> dist\app\
    2. Download the Parsec VDD redist if missing  -> installer\redist\parsec-vdd-0.45.0.0.exe (SHA-256 verified)
    3. Inno Setup (ISCC)                          -> dist\VRMonitorSwitch-Setup-<version>.exe
    4. Portable zip                               -> dist\VRMonitorSwitch-<version>-portable.zip

    Requirements: .NET SDK (any 6+; found in PATH or %LOCALAPPDATA%\Microsoft\dotnet) and Inno Setup 6
    (https://jrsoftware.org/isdl.php). Windows PowerShell 5.1 or PowerShell 7.

.PARAMETER Version
    Semantic version baked into the assembly, the setup file name and the zip (default 0.1.0).

.PARAMETER SkipInstaller
    Only publish and zip; do not require Inno Setup.

.EXAMPLE
    .\scripts\build.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0',
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$RepoRoot    = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Project     = Join-Path $RepoRoot 'src\VRMonitorSwitch\VRMonitorSwitch.csproj'
$IssFile     = Join-Path $RepoRoot 'installer\VRMonitorSwitch.iss'
$RedistDir   = Join-Path $RepoRoot 'installer\redist'
$DistDir     = Join-Path $RepoRoot 'dist'
$AppDir      = Join-Path $DistDir 'app'
$SetupExe    = Join-Path $DistDir "VRMonitorSwitch-Setup-$Version.exe"
$PortableZip = Join-Path $DistDir "VRMonitorSwitch-$Version-portable.zip"

$DriverFile   = 'parsec-vdd-0.45.0.0.exe'
$DriverUrl    = 'https://builds.parsec.app/vdd/parsec-vdd-0.45.0.0.exe'
$DriverSha256 = 'E23332448FDAF5AA017CB308DB5EF6855FAC526A7DED05D80C039404126D5362'
$DriverPath   = Join-Path $RedistDir $DriverFile

function Write-Step([string]$Text) { Write-Host "==> $Text" -ForegroundColor Cyan }

function Find-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $local = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (Test-Path $local) { return $local }
    throw "dotnet SDK not found. Install it from https://dotnet.microsoft.com/download or run dotnet-install.ps1 into %LOCALAPPDATA%\Microsoft\dotnet."
}

function Find-Iscc {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @()
    if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
    $candidates += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'
    $candidates += Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw "Inno Setup 6 (ISCC.exe) not found. Install it from https://jrsoftware.org/isdl.php or use -SkipInstaller."
}

function Test-FileHash256([string]$Path, [string]$Expected) {
    $actual = (Get-FileHash -Path $Path -Algorithm SHA256).Hash
    return ($actual -ieq $Expected)
}

function Get-Redist {
    if (Test-Path $DriverPath) {
        if (Test-FileHash256 $DriverPath $DriverSha256) {
            Write-Host "Redist already present and verified: $DriverPath"
            return
        }
        Write-Warning "Existing $DriverFile has an unexpected hash; re-downloading."
        Remove-Item $DriverPath -Force
    }
    New-Item -ItemType Directory -Force -Path $RedistDir | Out-Null
    $tmp = "$DriverPath.download"
    Write-Host "Downloading $DriverUrl"
    try {
        Invoke-WebRequest -Uri $DriverUrl -OutFile $tmp -UseBasicParsing
    } catch {
        throw "Could not download the Parsec VDD installer from $DriverUrl : $($_.Exception.Message)"
    }
    if (-not (Test-FileHash256 $tmp $DriverSha256)) {
        Remove-Item $tmp -Force
        throw "SHA-256 mismatch for the downloaded $DriverFile (expected $DriverSha256). Aborting."
    }
    Move-Item $tmp $DriverPath -Force
    Write-Host "Redist downloaded and verified: $DriverPath"
}

function Invoke-Checked([string]$Exe, [string[]]$Arguments) {
    Write-Host "> $Exe $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($Exe)) exited with code $LASTEXITCODE" }
}

# ---------------------------------------------------------------------------------------------
Write-Step "VR Monitor Switch $Version - build started"
$dotnet = Find-Dotnet
Write-Host "dotnet: $dotnet ($(& $dotnet --version))"

Write-Step 'Cleaning dist\app'
if (Test-Path $AppDir) { Remove-Item $AppDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $AppDir | Out-Null
foreach ($old in @($SetupExe, $PortableZip)) { if (Test-Path $old) { Remove-Item $old -Force } }

Write-Step 'dotnet publish (Release, net48)'
$assemblyVersion = ($Version -split '-')[0] + '.0'
Invoke-Checked $dotnet @(
    'publish', $Project,
    '-c', 'Release',
    '-o', $AppDir,
    "-p:Version=$Version",
    "-p:AssemblyVersion=$assemblyVersion",
    "-p:FileVersion=$assemblyVersion",
    '-nologo'
)
# Keep the app folder lean: pdb is embedded, these files are not needed at runtime.
Get-ChildItem $AppDir -Include '*.pdb', '*.xml' -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
if (-not (Test-Path (Join-Path $AppDir 'VRMonitorSwitch.exe'))) { throw 'Publish did not produce VRMonitorSwitch.exe' }

Write-Step 'Parsec Virtual Display Driver redist'
Get-Redist

if (-not $SkipInstaller) {
    Write-Step 'Inno Setup'
    $iscc = Find-Iscc
    Invoke-Checked $iscc @("/DAppVersion=$Version", '/Q', $IssFile)
    if (-not (Test-Path $SetupExe)) { throw "ISCC finished but $SetupExe was not produced" }
}

Write-Step 'Portable zip'
Compress-Archive -Path (Join-Path $AppDir '*') -DestinationPath $PortableZip -CompressionLevel Optimal -Force

Write-Step 'Done. Artifacts:'
$artifacts = @($PortableZip)
if (-not $SkipInstaller) { $artifacts = @($SetupExe) + $artifacts }
foreach ($a in $artifacts) {
    $item = Get-Item $a
    $hash = (Get-FileHash $a -Algorithm SHA256).Hash
    Write-Host ("  {0}  ({1:N0} bytes)" -f $item.FullName, $item.Length)
    Write-Host ("      SHA-256 {0}" -f $hash)
}
