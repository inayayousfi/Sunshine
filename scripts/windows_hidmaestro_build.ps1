<#
.SYNOPSIS
Build and deploy the HIDMaestro-backed Sunshine fork on Windows x64.

.DESCRIPTION
Installs missing build prerequisites, builds the vendored HIDMaestro source, the
pinned libvirtualhid Windows broker and driver, and Sunshine. It runs Sunshine
tests, backs up the live installations and HIDMaestro driver, deploys the matched
components without touching configuration, and writes a restore script under
ProgramData.
#>
[CmdletBinding()]
param(
    [string]$BuildRoot = 'C:\hmb\sunshine-hidmaestro',
    [string]$InstallDir = "$env:ProgramFiles\Sunshine"
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$sunshineRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hidRoot = Join-Path $sunshineRoot 'third-party\hidmaestro'
$libvirtualhidRoot = Join-Path $sunshineRoot 'third-party\libvirtualhid'
$libvirtualhidInstallDir = Join-Path $env:ProgramFiles 'libvirtualhid'

if (-not (Test-Path (Join-Path $hidRoot 'sdk\HIDMaestro.NativeMouse\HIDMaestro.NativeMouse.csproj'))) {
    throw "The vendored HIDMaestro source is missing from $hidRoot."
}
if (-not (Test-Path (Join-Path $libvirtualhidRoot 'scripts\windows\install-driver.ps1'))) {
    throw "The pinned libvirtualhid source is missing from $libvirtualhidRoot."
}

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
        [switch]$Mirror,
        [string[]]$ExcludeDirectories = @()
    )
    $mode = if ($Mirror) { '/MIR' } else { '/E' }
    $arguments = @($Source, $Destination, $mode, '/R:2', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
    foreach ($directory in $ExcludeDirectories) {
        $arguments += @('/XD', $directory)
    }
    & robocopy.exe @arguments
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

$driverBuild = Join-Path $hidRoot 'scripts\build.cmd'
Remove-Item (Join-Path $hidRoot 'build') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $hidRoot 'sdk\HIDMaestro.Core\Resources') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $hidRoot 'sdk\HIDMaestro.Core\bin') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $hidRoot 'sdk\HIDMaestro.Core\obj') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $hidRoot 'sdk\HIDMaestro.NativeMouse\bin') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $hidRoot 'sdk\HIDMaestro.NativeMouse\obj') -Recurse -Force -ErrorAction SilentlyContinue
Invoke-Checked cmd.exe /c $driverBuild
$coreProject = Join-Path $hidRoot 'sdk\HIDMaestro.Core\HIDMaestro.Core.csproj'
Invoke-Checked dotnet.exe build $coreProject -c Release
Invoke-Checked dotnet.exe build $coreProject -c Release --no-restore
$nativeProject = Join-Path $hidRoot 'sdk\HIDMaestro.NativeMouse\HIDMaestro.NativeMouse.csproj'
Invoke-Checked dotnet.exe publish $nativeProject -c Release -r win-x64
$nativeDll = Join-Path $hidRoot 'sdk\HIDMaestro.NativeMouse\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\HIDMaestro.NativeMouse.dll'

$nativeCmake = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if (-not (Test-Path $nativeCmake)) {
    throw "Visual Studio CMake was not found at $nativeCmake."
}
$libvirtualhidBuild = Join-Path $libvirtualhidRoot 'cmake-build-windows-driver'
$libvirtualhidCertificate = Join-Path $libvirtualhidBuild 'certificates\libvirtualhid-local-test.cer'
$libvirtualhidPackage = Join-Path $libvirtualhidBuild 'src\platform\windows\driver\package\Release'
$libvirtualhidBroker = Join-Path $libvirtualhidBuild 'src\platform\windows\broker\Release\libvirtualhid_broker.exe'
Remove-Item $libvirtualhidBuild -Recurse -Force -ErrorAction SilentlyContinue
Invoke-Checked -FilePath $nativeCmake -Arguments @(
    '-S', $libvirtualhidRoot, '-B', $libvirtualhidBuild, '-G', 'Visual Studio 17 2022', '-A', 'x64',
    '-DBUILD_DOCS=OFF', '-DBUILD_EXAMPLES=OFF', '-DBUILD_TESTS=OFF', '-DLIBVIRTUALHID_BUILD_TOOLS=OFF',
    '-DLIBVIRTUALHID_BUILD_WINDOWS_DRIVER=ON', '-DLIBVIRTUALHID_BUILD_WINDOWS_BROKER=ON',
    '-DLIBVIRTUALHID_ENABLE_PACKAGING=OFF', '-DLIBVIRTUALHID_WARNINGS_AS_ERRORS=ON'
)
Invoke-Checked -FilePath $nativeCmake -Arguments @(
    '--build', $libvirtualhidBuild, '--config', 'Release',
    '--target', 'libvirtualhid_windows_catalog', 'libvirtualhid_broker', '--parallel', '2'
)
if (-not (Test-Path (Join-Path $libvirtualhidPackage 'libvirtualhid.inf')) -or -not (Test-Path $libvirtualhidBroker)) {
    throw 'The matched libvirtualhid build did not produce the required driver and broker artifacts.'
}
$driverSigningCertificate = Get-ChildItem Cert:\LocalMachine\My -CodeSigningCert |
    Where-Object { $_.Subject -eq 'CN=HIDMaestroTestCert' -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $driverSigningCertificate) {
    throw 'The trusted HIDMaestroTestCert machine code-signing certificate is unavailable.'
}
$signTool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signTool) {
    throw 'The x64 Windows SDK signtool.exe was not found.'
}
$libvirtualhidCatalog = Join-Path $libvirtualhidPackage 'libvirtualhid.cat'
New-Item -ItemType Directory -Path (Split-Path $libvirtualhidCertificate) -Force | Out-Null
Export-Certificate -Cert $driverSigningCertificate -FilePath $libvirtualhidCertificate -Force | Out-Null
Invoke-Checked $signTool.FullName sign /fd SHA256 /sha1 $driverSigningCertificate.Thumbprint /s My /sm $libvirtualhidCatalog
Invoke-Checked $signTool.FullName verify /pa $libvirtualhidCatalog

$buildDir = Join-Path $sunshineRoot 'cmake-build-hidmaestro'
$stagingDir = Join-Path $BuildRoot 'staging'
Remove-Item $buildDir -Recurse -Force -ErrorAction SilentlyContinue
Push-Location $sunshineRoot
try {
    $cmakeDll = $nativeDll.Replace('\', '/')
    $cmakeNpm = $npmWrapper.Replace('\', '/')
    Invoke-Checked $msys -defterm -here -no-start -ucrt64 -c "cmake -B cmake-build-hidmaestro -G Ninja -S . -DBUILD_DOCS=OFF -DBUILD_TESTS=ON -DCMAKE_BUILD_TYPE=Release -DDOTNET_EXECUTABLE=OFF -DHIDMAESTRO_MOUSE_DLL='$cmakeDll' -DNPM='$cmakeNpm'"
    Invoke-Checked $msys -defterm -here -no-start -ucrt64 -c 'cmake --build cmake-build-hidmaestro'
    Invoke-Checked $msys -defterm -here -no-start -ucrt64 -c "./cmake-build-hidmaestro/tests/test_sunshine.exe --gtest_filter='-EncoderVariants/EncoderTest.ValidateEncoder/*'"
    $cmakeStaging = $stagingDir.Replace('\', '/')
    Invoke-Checked $msys -defterm -here -no-start -ucrt64 -c "cmake --install cmake-build-hidmaestro --prefix '$cmakeStaging'"
} finally {
    Pop-Location
}

$backupBase = Join-Path $env:ProgramData 'Sunshine-HIDMaestro\backups'
$backupDir = Join-Path $backupBase (Get-Date -Format 'yyyyMMdd-HHmmss-ffff')
$sunshineBackupDir = Join-Path $backupDir 'Sunshine'
$libvirtualhidBackupDir = Join-Path $backupDir 'libvirtualhid'
$libvirtualhidRestoreScripts = Join-Path $backupDir 'libvirtualhid-scripts'
$hidMaestroBackupDir = Join-Path $backupDir 'HIDMaestro-driver'
$restorePath = Join-Path $env:ProgramData 'Sunshine-HIDMaestro\restore.ps1'
$service = Get-Service SunshineService
$brokerService = Get-Service libvirtualhid_broker -ErrorAction SilentlyContinue
$sunshineWasRunning = $service.Status -ne 'Stopped'
$brokerWasRunning = $brokerService -and $brokerService.Status -ne 'Stopped'
$libvirtualhidWasPresent = Test-Path $libvirtualhidInstallDir
$hidMaestroDeviceDriver = Get-CimInstance Win32_PnPSignedDriver |
    Where-Object { $_.DeviceID -like 'ROOT\HIDCLASS*' -and $_.DeviceName -like 'HIDMaestro*' } |
    Select-Object -First 1
$hidMaestroDriver = if ($hidMaestroDeviceDriver) {
    Get-WindowsDriver -Online -Driver $hidMaestroDeviceDriver.InfName
}
$hidMaestroWasPresent = $null -ne $hidMaestroDriver
$hidMaestroPackageDir = if ($hidMaestroWasPresent) {
    Split-Path $hidMaestroDriver.OriginalFileName
}
$hidMaestroManifest = Get-ItemProperty 'HKLM:\SOFTWARE\HIDMaestro' -Name InstalledManifestSha256 -ErrorAction SilentlyContinue
$hidMaestroManifestHash = if ($hidMaestroManifest) { [string]$hidMaestroManifest.InstalledManifestSha256 } else { '' }
$hidMaestroManifestWasPresent = -not [string]::IsNullOrEmpty($hidMaestroManifestHash)
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
Copy-Item (Join-Path $libvirtualhidRoot 'scripts\windows') $libvirtualhidRestoreScripts -Recurse

try {
    if ($sunshineWasRunning) {
        Stop-Service SunshineService -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    if ($brokerWasRunning) {
        Stop-Service libvirtualhid_broker -Force
        $brokerService.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }

    Invoke-Robocopy $InstallDir $sunshineBackupDir
    if ($libvirtualhidWasPresent) {
        Invoke-Robocopy $libvirtualhidInstallDir $libvirtualhidBackupDir
    }
    if ($hidMaestroWasPresent) {
        Invoke-Robocopy $hidMaestroPackageDir $hidMaestroBackupDir
        $hidMaestroDriver | Select-Object Driver, OriginalFileName, ProviderName, Version, Date |
            ConvertTo-Json | Set-Content (Join-Path $backupDir 'hidmaestro-driver.json') -Encoding UTF8
    }
    Get-CimInstance Win32_PnPSignedDriver |
        Where-Object { $_.DeviceID -like 'ROOT\LIBVIRTUALHID*' } |
        Select-Object DeviceID, InfName, DriverVersion, DriverDate |
        ConvertTo-Json | Set-Content (Join-Path $backupDir 'libvirtualhid-driver.json') -Encoding UTF8

    $restore = @"
`$ErrorActionPreference = 'Stop'
Stop-Service SunshineService -Force -ErrorAction SilentlyContinue
Stop-Service libvirtualhid_broker -Force -ErrorAction SilentlyContinue
`$hidMaestroDrivers = @(Get-WindowsDriver -Online | Where-Object { `$_.ProviderName -eq 'HIDMaestro' })
foreach (`$driver in `$hidMaestroDrivers) {
    pnputil.exe /delete-driver `$driver.Driver /uninstall /force | Out-Null
    if (`$LASTEXITCODE -ne 0) { throw "pnputil failed to remove `$(`$driver.Driver) with code `$LASTEXITCODE" }
}
if ([bool]::Parse('$hidMaestroWasPresent')) {
    pnputil.exe /add-driver '$hidMaestroBackupDir\hidmaestro.inf' /install | Out-Null
    if (`$LASTEXITCODE -ne 0) { throw "pnputil failed to restore HIDMaestro with code `$LASTEXITCODE" }
}
if ([bool]::Parse('$hidMaestroManifestWasPresent')) {
    New-Item 'HKLM:\SOFTWARE\HIDMaestro' -Force | Out-Null
    Set-ItemProperty 'HKLM:\SOFTWARE\HIDMaestro' -Name InstalledManifestSha256 -Value '$hidMaestroManifestHash'
} else {
    Remove-ItemProperty 'HKLM:\SOFTWARE\HIDMaestro' -Name InstalledManifestSha256 -ErrorAction SilentlyContinue
}
robocopy.exe '$sunshineBackupDir' '$InstallDir' /MIR /XD config /R:2 /W:1 /NFL /NDL /NJH /NJS /NP
if (`$LASTEXITCODE -gt 7) { throw "robocopy exited with code `$LASTEXITCODE" }
if ([bool]::Parse('$libvirtualhidWasPresent')) {
    robocopy.exe '$libvirtualhidBackupDir' '$libvirtualhidInstallDir' /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP
    if (`$LASTEXITCODE -gt 7) { throw "robocopy exited with code `$LASTEXITCODE" }
    & '$libvirtualhidRestoreScripts\install-driver.ps1' -InfPath '$libvirtualhidInstallDir\drivers\windows\libvirtualhid.inf' -BrokerPath '$libvirtualhidInstallDir\services\windows\libvirtualhid_broker.exe'
} else {
    Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object { `$_.InstanceId -like 'ROOT\LIBVIRTUALHID*' } |
        ForEach-Object { pnputil.exe /remove-device `$_.InstanceId | Out-Null }
    sc.exe delete libvirtualhid_broker | Out-Null
    Remove-Item '$libvirtualhidInstallDir' -Recurse -Force -ErrorAction SilentlyContinue
}
if (Get-Service libvirtualhid_broker -ErrorAction SilentlyContinue) {
    if ([bool]::Parse('$brokerWasRunning')) {
        Start-Service libvirtualhid_broker
    } else {
        Stop-Service libvirtualhid_broker -Force -ErrorAction SilentlyContinue
    }
}
if ([bool]::Parse('$sunshineWasRunning')) {
    Start-Service SunshineService
}
"@
    New-Item -ItemType Directory -Path (Split-Path $restorePath) -Force | Out-Null
    Set-Content -Path $restorePath -Value $restore -Encoding UTF8

    $installedDriverDir = Join-Path $libvirtualhidInstallDir 'drivers\windows'
    $installedBrokerDir = Join-Path $libvirtualhidInstallDir 'services\windows'
    New-Item -ItemType Directory -Path $installedDriverDir,$installedBrokerDir -Force | Out-Null
    Copy-Item (Join-Path $libvirtualhidPackage 'libvirtualhid.inf') $installedDriverDir -Force
    Copy-Item (Join-Path $libvirtualhidPackage 'libvirtualhid.cat') $installedDriverDir -Force
    Copy-Item (Join-Path $libvirtualhidPackage 'libvirtualhid_umdf.dll') $installedDriverDir -Force
    Copy-Item $libvirtualhidBroker $installedBrokerDir -Force
    $installedInf = Join-Path $installedDriverDir 'libvirtualhid.inf'
    $installedBroker = Join-Path $installedBrokerDir 'libvirtualhid_broker.exe'
    & (Join-Path $libvirtualhidRoot 'scripts\windows\install-driver.ps1') `
        -InfPath $installedInf -CertificatePath $libvirtualhidCertificate -BrokerPath $installedBroker `
        -LogPath (Join-Path $backupDir 'libvirtualhid-install.log')

    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Invoke-Robocopy $stagingDir $InstallDir

    if ($sunshineWasRunning) {
        Start-Service SunshineService
        (Get-Service SunshineService).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        Start-Sleep -Seconds 15
        if (-not (Get-Process sunshine -ErrorAction SilentlyContinue)) {
            throw 'Sunshine exited during startup.'
        }
    }
} catch {
    $deploymentError = $_
    try {
        if (Test-Path $restorePath) {
            & $restorePath
        } else {
            if ($brokerWasRunning -and (Get-Service libvirtualhid_broker -ErrorAction SilentlyContinue)) {
                Start-Service libvirtualhid_broker
            }
            if ($sunshineWasRunning) {
                Start-Service SunshineService
            }
        }
    } catch {
        throw "Deployment failed: $deploymentError Rollback also failed: $_"
    }
    throw "Deployment failed and the previous installations were restored: $deploymentError"
}

Write-Host "Installed HIDMaestro Sunshine to $InstallDir"
Write-Host "Installed matched libvirtualhid broker and driver to $libvirtualhidInstallDir"
Write-Host "Backup: $backupDir"
Write-Host "Restore: powershell.exe -ExecutionPolicy Bypass -File `"$restorePath`""
