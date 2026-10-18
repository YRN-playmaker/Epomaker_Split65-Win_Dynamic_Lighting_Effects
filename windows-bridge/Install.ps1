#requires -Version 5.1
# SPDX-License-Identifier: GPL-2.0-or-later
param([switch]$Elevated, [switch]$Managed, [switch]$Repair, [string]$ResultsDirectory, [string]$ResultFile)
. (Join-Path $PSScriptRoot 'Common.ps1')
$transcript = $false; $state = $null; $exitCode = 1; $failure = ''; $log = ''; $reboot = $false
try {
    Assert-BridgePlatform
    Assert-BridgePackage $PSScriptRoot
    Write-Host 'Installs an independent experimental UMDF2 LampArray device.'
    Write-Host 'Administrator access is needed to stage its driver and trust a locally generated code-signing certificate.'
    Invoke-BridgeElevation $PSCommandPath $Elevated.IsPresent
    $results = if ($ResultsDirectory) { $ResultsDirectory } else { Join-Path $PSScriptRoot 'Results' }
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    $log = Join-Path $results ('Install-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt')
    Start-Transcript -LiteralPath $log | Out-Null; $transcript = $true
    $app = Get-BridgeAppRoot
    if (Test-Path -LiteralPath $app) {
        if (-not $Repair) { throw "An existing installation is present at $app. Use the Companion repair action." }
        Assert-BridgePlainDirectory $app
        $oldStatePath = Join-Path $app 'state.json'
        if (-not (Test-Path -LiteralPath $oldStatePath)) { throw 'The existing folder has no ownership receipt; automatic repair stopped.' }
        $oldState = Get-Content -LiteralPath $oldStatePath -Raw | ConvertFrom-Json
        Write-Host 'Removing the recorded old project component before reinstalling.'
        Remove-BridgeInstallation $oldState (Join-Path $PSScriptRoot 'prebuilt\Split65Bridge.exe')
        if ($script:BridgeRebootRequired) { $reboot = $true; throw 'Restart Windows to finish removing the old component, then enable the Companion again.' }
    }
    if (@(Get-BridgeDriverPackages).Count -ne 0) { throw 'An untracked project driver is already staged. Installation stopped without modifying it.' }
    $sourceClient = Join-Path $PSScriptRoot 'prebuilt\Split65Bridge.exe'
    $rootStatus = @(& $sourceClient --root-status)
    $rootExit = $LASTEXITCODE
    $rootStatus | Out-Host
    Assert-BridgeRootPreflight $rootStatus $rootExit
    New-Item -ItemType Directory -Path $app | Out-Null
    & "$env:WINDIR\System32\icacls.exe" $app /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Host
    if ($LASTEXITCODE -ne 0) { Remove-Item -LiteralPath $app -Force; throw 'Could not secure installation directory.' }
    $state = [pscustomobject]@{ identity=$script:BridgeIdentity; version='0.5.0'; phase='created'; rootCreated=$false; certificateThumbprint=''; oemInfs=@(); installed=$false }
    Save-BridgeState $state
    $driver = Join-Path $app 'driver'; New-Item -ItemType Directory -Path $driver | Out-Null
    Copy-Item -LiteralPath $sourceClient -Destination (Join-Path $app 'Split65Bridge.exe')
    foreach ($name in @('Split65LampBridge.inf', 'Split65LampBridge.dll')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "prebuilt\driver\$name") -Destination $driver
    }
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject ('CN=Split65 Local Bridge ' + [guid]::NewGuid().ToString()) -FriendlyName 'Split65 bridge local package signing' -CertStoreLocation 'Cert:\LocalMachine\My' -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(1)
    $state.certificateThumbprint = $cert.Thumbprint; $state.phase = 'certificate-created'; Save-BridgeState $state
    $cer = Join-Path $app 'LocalPackageSigner.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\TrustedPublisher' | Out-Null
    $signed = Set-AuthenticodeSignature -FilePath (Join-Path $driver 'Split65LampBridge.dll') -Certificate $cert -HashAlgorithm SHA256
    if ($signed.Status -ne 'Valid') { throw "DLL signing failed: $($signed.Status) $($signed.StatusMessage)" }
    $cat = Join-Path $app 'Split65LampBridge.cat'
    New-FileCatalog -Path $driver -CatalogFilePath $cat -CatalogVersion 2 | Out-Null
    $signed = Set-AuthenticodeSignature -FilePath $cat -Certificate $cert -HashAlgorithm SHA256
    if ($signed.Status -ne 'Valid') { throw "Catalog signing failed: $($signed.Status) $($signed.StatusMessage)" }
    if ((Test-FileCatalog -Path $driver -CatalogFilePath $cat).ToString() -ne 'Valid') { throw 'Signed catalog content verification failed.' }
    Move-Item -LiteralPath $cat -Destination $driver
    Remove-Item -LiteralPath ('Cert:\LocalMachine\My\' + $cert.Thumbprint) -DeleteKey -Force
    $state.phase = 'signed-private-key-deleted'; Save-BridgeState $state
    $inf = Join-Path $driver 'Split65LampBridge.inf'
    try { Invoke-BridgePnP @('/add-driver', $inf) }
    finally { $state.oemInfs = @(Get-BridgeDriverPackages | ForEach-Object { $_.Driver }); Save-BridgeState $state }
    $state.rootCreated = $true; $state.phase = 'root-registration'; Save-BridgeState $state
    & (Join-Path $app 'Split65Bridge.exe') --install-root | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'SetupAPI root device registration failed.' }
    & (Join-Path $app 'Split65Bridge.exe') --bind-root $inf | Out-Host
    if ($LASTEXITCODE -eq 10) { $script:BridgeRebootRequired = $true }
    elseif ($LASTEXITCODE -ne 0) { throw 'Exact root driver binding failed. See the native error above.' }
    for ($attempt = 0; $attempt -lt 16; ++$attempt) {
        Start-Sleep -Milliseconds 500
        $rootStatus = @(& (Join-Path $app 'Split65Bridge.exe') --root-status)
        if ($LASTEXITCODE -ne 0) { break }
        if (@($rootStatus | Where-Object { $_ -match '; CM result=0; problem=(0|14)$' }).Count -eq 1) { break }
    }
    $rootStatus | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not ($rootStatus -contains 'Matching root devices=1') -or
        @($rootStatus | Where-Object { $_ -match '; CM result=0; problem=(0|14)$' }).Count -ne 1) {
        throw 'Root driver has not started successfully. The log preserves its exact problem code; no security settings were changed.'
    }
    $reboot = $script:BridgeRebootRequired -or @($rootStatus | Where-Object { $_ -match '; problem=14$' }).Count -ne 0
    if (-not $reboot) {
        $probeReady = $false
        for ($attempt = 0; $attempt -lt 20; ++$attempt) {
            $probe = Invoke-BridgeProbe (Join-Path $app 'Split65Bridge.exe')
            $probeOutput = $probe.Lines
            if ($probe.Ready) { $probeReady = $true; break }
            Start-Sleep -Milliseconds 500
        }
        $probeOutput | Out-Host
        if (-not $probeReady) { throw 'The root node exists but the UMDF control interface failed its open/IOCTL check. Installation is not considered successful.' }
    }
    $state.installed = -not $reboot; $state.phase = if ($reboot) { 'restart-required' } else { 'installed-interface-verified' }; Save-BridgeState $state
    if ($script:BridgeRebootRequired -or @($rootStatus | Where-Object { $_ -match '; problem=14$' }).Count -ne 0) {
        Write-Host 'Restart Windows before starting the bridge.' -ForegroundColor Yellow
    }
    Write-Host 'Installed. Firmware 0.4.8/0.5.0 is supported. Continue in the Companion window.' -ForegroundColor Green
    Write-Host 'The virtual LampArray appears only after the bridge validates the live 2.4G receiver.'
    $exitCode = 0
} catch {
    $failure = $_.Exception.Message
    Write-Host ('INSTALL FAILED: ' + $failure) -ForegroundColor Red
    try { & (Join-Path $PSScriptRoot 'Collect-Driver.ps1') -OutputDirectory $results | Out-Host } catch { Write-Host ('Evidence collection: '+$_.Exception.Message) }
    if ($null -ne $state) {
        try {
            Write-Host 'Rolling back only this installation.'
            Remove-BridgeInstallation $state (Join-Path $PSScriptRoot 'prebuilt\Split65Bridge.exe')
        } catch { Write-Host ('Rollback incomplete: ' + $_.Exception.Message + ' Run UninstallBridge.cmd to retry.') -ForegroundColor Yellow }
    }
} finally {
    if ($transcript) { Stop-Transcript | Out-Null; Write-Host "Log: $log" }
    if ($ResultFile) {
        [pscustomobject]@{success=($exitCode -eq 0);rebootRequired=($reboot -or $script:BridgeRebootRequired);error=$failure;log=$log;version='0.5.0'} | ConvertTo-Json | Set-Content -LiteralPath $ResultFile -Encoding UTF8
    }
    if ($Elevated -and -not $Managed) { Read-Host 'Press Enter to close' | Out-Null }
}
exit $exitCode
