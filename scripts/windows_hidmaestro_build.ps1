<#
.SYNOPSIS
Build and deploy the HIDMaestro-backed Sunshine fork on Windows x64.

.DESCRIPTION
Installs missing build prerequisites, clones fresh copies of both fork branches,
builds HIDMaestro and Sunshine, runs Sunshine tests, backs up the installed
Sunshine directory, deploys the fork without touching configuration, and writes
a restore script under ProgramData.
#>
[CmdletBinding()]
param(
    [string]$BuildRoot = 'C:\hmb',
    [string]$InstallDir = "$env:ProgramFiles\Sunshine"
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(ValueFromRemainingArguments)][string[]]$Arguments
    )
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath exited with code $LASTEXITCODE"
    }
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-PendingReboot {
    return (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
        (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') -or
        ((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue) -ne $null)
}

function Ensure-WingetPackage {
    param([Parameter(Mandatory)][string]$Id)
    & winget.exe list --id $Id --exact --accept-source-agreements | Out-Null
    if ($LASTEXITCODE -eq 0) {
        return
    }
    Invoke-Checked winget.exe install --id $Id --exact --silent --accept-package-agreements --accept-source-agreements
}

function Invoke-Robocopy {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [switch]$Mirror
    )
    $mode = if ($Mirror) { '/MIR' } else { '/E' }
    & robocopy.exe $Source $Destination $mode /R:2 /W:1 /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -gt 7) {
        throw "robocopy exited with code $LASTEXITCODE"
    }
}

if (-not (Test-Administrator)) {
    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
        '-BuildRoot', "`"$BuildRoot`"", '-InstallDir', "`"$InstallDir`""
    )
    $process = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $process.ExitCode
}

Ensure-WingetPackage 'Git.Git'
Ensure-WingetPackage 'Microsoft.DotNet.SDK.10'
Ensure-WingetPackage 'MSYS2.MSYS2'

if (-not (Test-Path $InstallDir) -or -not (Get-Service SunshineService -ErrorAction SilentlyContinue)) {
    throw "This deployment script requires an existing Sunshine installation and SunshineService at $InstallDir."
}

$vcvars = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat'
$cl = Get-ChildItem 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\*\bin\Hostx64\x64\cl.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not (Test-Path $vcvars) -or -not $cl) {
    $vsBootstrapper = Join-Path $env:TEMP 'vs_buildtools.exe'
    Invoke-WebRequest 'https://aka.ms/vs/17/release/vs_BuildTools.exe' -OutFile $vsBootstrapper
    Invoke-Checked $vsBootstrapper --wait --quiet --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended
    $cl = Get-ChildItem 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\*\bin\Hostx64\x64\cl.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not (Test-Path $vcvars) -or -not $cl) {
        throw 'Visual Studio Build Tools C++ workload installation did not produce cl.exe.'
    }
}

$wdkInclude = 'C:\Program Files (x86)\Windows Kits\10\Include\10.0.26100.0'
if (-not (Test-Path $wdkInclude)) {
    Invoke-Checked winget.exe install --id Microsoft.WindowsWDK.10.0.26100 --exact --silent `
        --accept-package-agreements --accept-source-agreements
}

if (Test-PendingReboot) {
    Write-Warning 'A prerequisite requires a reboot. Sunshine was not changed.'
    Write-Host "After reboot, rerun: powershell.exe -ExecutionPolicy Bypass -File `"$PSCommandPath`" -BuildRoot `"$BuildRoot`" -InstallDir `"$InstallDir`""
    exit 3010
}

$msys = 'C:\msys64\msys2_shell.cmd'
$packages = @(
    'git',
    'mingw-w64-ucrt-x86_64-boost',
    'mingw-w64-ucrt-x86_64-cmake',
    'mingw-w64-ucrt-x86_64-cppwinrt',
    'mingw-w64-ucrt-x86_64-curl-winssl',
    'mingw-w64-ucrt-x86_64-doxygen',
    'mingw-w64-ucrt-x86_64-gcc',
    'mingw-w64-ucrt-x86_64-graphviz',
    'mingw-w64-ucrt-x86_64-miniupnpc',
    'mingw-w64-ucrt-x86_64-MinHook',
    'mingw-w64-ucrt-x86_64-ninja',
    'mingw-w64-ucrt-x86_64-nodejs',
    'mingw-w64-ucrt-x86_64-nlohmann-json',
    'mingw-w64-ucrt-x86_64-nsis',
    'mingw-w64-ucrt-x86_64-onevpl',
    'mingw-w64-ucrt-x86_64-openssl',
    'mingw-w64-ucrt-x86_64-opus',
    'mingw-w64-ucrt-x86_64-qt6-static',
    'mingw-w64-ucrt-x86_64-toolchain'
)
Invoke-Checked $msys -defterm -here -no-start -ucrt64 -c "pacman -Syu --needed --noconfirm $($packages -join ' ')"

if (Test-Path $BuildRoot) {
    Remove-Item $BuildRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $BuildRoot | Out-Null

$nodeVersion = '22.23.2'
$nodeArchive = Join-Path $BuildRoot "node-v$nodeVersion-win-x64.zip"
$nodeRoot = Join-Path $BuildRoot "node-v$nodeVersion-win-x64"
Invoke-WebRequest "https://nodejs.org/dist/v$nodeVersion/node-v$nodeVersion-win-x64.zip" -OutFile $nodeArchive
$nodeHash = (Get-FileHash $nodeArchive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($nodeHash -ne '1177b4137ba5adaa56354ae40f1080c7450e8ae09cecb47da459d1c52ac99f97') {
    throw "Unexpected Node.js archive hash: $nodeHash"
}
Expand-Archive $nodeArchive -DestinationPath $BuildRoot
$npmWrapper = Join-Path $nodeRoot 'npm-sunshine.cmd'
Set-Content -Path $npmWrapper -Encoding ASCII -Value @(
    '@echo off',
    'set "PATH=%~dp0;%PATH%"',
    '"%~dp0npm.cmd" %*'
)

$hidRoot = Join-Path $BuildRoot 'HIDMaestro'
$sunshineRoot = Join-Path $BuildRoot 'Sunshine'
Invoke-Checked git.exe -c core.longpaths=true clone --branch feature/sunshine-mouse-native --single-branch https://github.com/inayayousfi/HIDMaestro.git $hidRoot
Invoke-Checked git.exe -c core.longpaths=true clone --branch feature/hidmaestro-mouse --single-branch --recurse-submodules https://github.com/inayayousfi/Sunshine.git $sunshineRoot

Invoke-Checked cmd.exe /c (Join-Path $hidRoot 'scripts\build_all.cmd')
$nativeProject = Join-Path $hidRoot 'sdk\HIDMaestro.NativeMouse\HIDMaestro.NativeMouse.csproj'
Invoke-Checked dotnet.exe publish $nativeProject -c Release -r win-x64
$nativeDll = Join-Path $hidRoot 'sdk\HIDMaestro.NativeMouse\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\HIDMaestro.NativeMouse.dll'

$buildDir = Join-Path $sunshineRoot 'cmake-build-hidmaestro'
$stagingDir = Join-Path $BuildRoot 'staging'
Push-Location $sunshineRoot
try {
    $cmakeDll = $nativeDll.Replace('\', '/')
    $cmakeNpm = $npmWrapper.Replace('\', '/')
    Invoke-Checked $msys -defterm -here -no-start -ucrt64 -c "cmake -B cmake-build-hidmaestro -G Ninja -S . -DBUILD_DOCS=OFF -DBUILD_TESTS=ON -DCMAKE_BUILD_TYPE=Release -DDOTNET_EXECUTABLE=OFF -DHIDMAESTRO_MOUSE_DLL='$cmakeDll' -DNPM='$cmakeNpm'"
    Invoke-Checked $msys -defterm -here -no-start -ucrt64 -c 'cmake --build cmake-build-hidmaestro'
    Invoke-Checked $msys -defterm -here -no-start -ucrt64 -c './cmake-build-hidmaestro/tests/test_sunshine.exe'
    $cmakeStaging = $stagingDir.Replace('\', '/')
    Invoke-Checked $msys -defterm -here -no-start -ucrt64 -c "cmake --install cmake-build-hidmaestro --prefix '$cmakeStaging'"
} finally {
    Pop-Location
}

$backupBase = Join-Path $env:ProgramData 'Sunshine-HIDMaestro\backups'
$backupDir = Join-Path $backupBase (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
if (Test-Path $InstallDir) {
    Invoke-Robocopy $InstallDir $backupDir
}

$service = Get-Service SunshineService -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') {
    Stop-Service SunshineService -Force
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Invoke-Robocopy $stagingDir $InstallDir

$restorePath = Join-Path $env:ProgramData 'Sunshine-HIDMaestro\restore.ps1'
$restore = @"
`$ErrorActionPreference = 'Stop'
Stop-Service SunshineService -Force -ErrorAction SilentlyContinue
robocopy.exe '$backupDir' '$InstallDir' /MIR /R:2 /W:1
if (`$LASTEXITCODE -gt 7) { throw "robocopy exited with code `$LASTEXITCODE" }
Start-Service SunshineService
"@
New-Item -ItemType Directory -Path (Split-Path $restorePath) -Force | Out-Null
Set-Content -Path $restorePath -Value $restore -Encoding UTF8

if ($service) {
    try {
        Start-Service SunshineService
        (Get-Service SunshineService).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        Start-Sleep -Seconds 15
        if (-not (Get-Process sunshine -ErrorAction SilentlyContinue)) {
            throw 'Sunshine exited during startup.'
        }
    } catch {
        Stop-Service SunshineService -Force -ErrorAction SilentlyContinue
        Invoke-Robocopy $backupDir $InstallDir -Mirror
        Start-Service SunshineService
        throw "HIDMaestro deployment failed and the previous Sunshine installation was restored: $_"
    }
}

Write-Host "Installed HIDMaestro Sunshine to $InstallDir"
Write-Host "Backup: $backupDir"
Write-Host "Restore: powershell.exe -ExecutionPolicy Bypass -File `"$restorePath`""
