#requires -Version 5.1
# SPDX-License-Identifier: GPL-2.0-or-later
param([switch]$Elevated, [switch]$Managed, [string]$ResultsDirectory, [string]$ResultFile)
. (Join-Path $PSScriptRoot 'Common.ps1')
$transcript = $false; $exitCode = 1; $failure = ''; $log = ''
try {
    Assert-BridgeWindows
    Assert-BridgePackage $PSScriptRoot
    Invoke-BridgeElevation $PSCommandPath $Elevated.IsPresent
    $results = if ($ResultsDirectory) { $ResultsDirectory } else { Join-Path $PSScriptRoot 'Results' }
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    $log = Join-Path $results ('Uninstall-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt')
    Start-Transcript -LiteralPath $log | Out-Null; $transcript = $true
    $app = Get-BridgeAppRoot
    $statePath = Join-Path $app 'state.json'
    if (-not (Test-Path -LiteralPath $app)) { Write-Host 'No project installation found.'; $exitCode = 0 }
    else {
        Assert-BridgePlainDirectory $app
        if (-not (Test-Path -LiteralPath $statePath)) { throw 'Ownership state is missing. No device, driver or certificate was removed.' }
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        Remove-BridgeInstallation $state (Join-Path $PSScriptRoot 'prebuilt\Split65Bridge.exe')
        Write-Host 'Removed the project root device, recorded driver package, local certificate and app files.' -ForegroundColor Green
        $exitCode = 0
    }
} catch { $failure=$_.Exception.Message; Write-Host ('UNINSTALL FAILED: ' + $failure) -ForegroundColor Red }
finally {
    if ($transcript) { Stop-Transcript | Out-Null; Write-Host "Log: $log" }
    if ($ResultFile) { [pscustomobject]@{success=($exitCode -eq 0);rebootRequired=$script:BridgeRebootRequired;error=$failure;log=$log;version='0.4.9'} | ConvertTo-Json | Set-Content -LiteralPath $ResultFile -Encoding UTF8 }
    if ($Elevated -and -not $Managed) { Read-Host 'Press Enter to close' | Out-Null }
}
exit $exitCode
