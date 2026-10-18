# SPDX-License-Identifier: GPL-2.0-or-later
# Pure file/mocked installer checks. Never installs devices or certificates.
$ErrorActionPreference = 'Stop'
$base = Split-Path $PSScriptRoot -Parent
foreach ($file in (Get-ChildItem -LiteralPath $base -Filter *.ps1 -Recurse)) {
    $tokens=$null; $errors=$null
    [System.Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors) | Out-Null
    if ($errors.Count) { throw ($errors | Out-String) }
}
. (Join-Path $base 'Common.ps1')
function Check([bool]$value, [string]$message) { if (-not $value) { throw $message } }
function Expect-Failure([scriptblock]$action, [string]$pattern) {
    try { & $action } catch { if ($_.Exception.Message -match $pattern) { return }; throw }
    throw 'Expected rejection did not happen.'
}
$temp = Join-Path ([IO.Path]::GetTempPath()) ('split65-installer-test-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    Assert-BridgeRootPreflight @('Matching root devices=0') 0
    Check (-not (Test-BridgeProbeResult @('Matching root devices=1','Root problem=0') 0)) 'Root registration alone was treated as driver readiness'
    Check (Test-BridgeProbeResult @('BRIDGE_PROBE=READY') 0) 'Valid live probe rejected'
    Check (-not (Test-BridgeProbeResult @('BRIDGE_PROBE=READY') 20)) 'Failed probe treated as ready'
    Expect-Failure { Assert-BridgeRootPreflight @() 1 } 'client preflight failed'
    Expect-Failure { Assert-BridgeRootPreflight @('Matching root devices=1') 1 } 'client preflight failed'
    Expect-Failure { Assert-BridgeRootPreflight @('Matching root devices=1') 0 } 'already exists'
    Expect-Failure { Assert-BridgeRootPreflight @('unexpected text') 0 } 'unexpected root-status'
    Assert-BridgePackage $base
    $copy = Join-Path $temp 'package'; New-Item -ItemType Directory -Path $copy | Out-Null
    Copy-Item -LiteralPath (Join-Path $base 'prebuilt') -Destination $copy -Recurse
    Copy-Item -LiteralPath (Join-Path $base 'PACKAGE-SHA256.json') -Destination $copy
    Assert-BridgePackage $copy
    Add-Content -LiteralPath (Join-Path $copy 'prebuilt\Split65Bridge.exe') -Value 'tampered'
    Expect-Failure { Assert-BridgePackage $copy } 'hash mismatch'
    $script:testApp = Join-Path $temp 'owned-stage'
    function Get-BridgeAppRoot { return $script:testApp }
    $script:stopCalls=0; $script:pnpCalls=@()
    function Stop-BridgeClient([string]$Client) { ++$script:stopCalls }
    $script:fakePackages=@(
        [pscustomobject]@{ProviderName='Split65 Community Experimental'; OriginalFileName='Split65LampBridge.inf'; Driver='oem12.inf'},
        [pscustomobject]@{ProviderName='Other Vendor'; OriginalFileName='Split65LampBridge.inf'; Driver='oem99.inf'},
        [pscustomobject]@{ProviderName='Split65 Community Experimental'; OriginalFileName='OtherDevice.inf'; Driver='oem88.inf'}
    )
    function Get-WindowsDriver { return $script:fakePackages }
    function Invoke-BridgePnP([string[]]$Arguments) {
        $script:pnpCalls += ,$Arguments
        Check ($Arguments[0] -eq '/delete-driver' -and $Arguments[2] -eq '/uninstall') 'Unexpected mutation type'
        $script:fakePackages = @($script:fakePackages | Where-Object { $_.Driver -ne $Arguments[1] })
    }
    $state=[pscustomobject]@{identity='wrong';rootCreated=$false;oemInfs=@();certificateThumbprint=''}
    Expect-Failure { Remove-BridgeInstallation $state 'unused.exe' } 'ownership marker'
    Check ($script:stopCalls -eq 0) 'Invalid state reached mutation stage'
    $state.identity=$script:BridgeIdentity; $state.oemInfs=@('keyboard.inf')
    Expect-Failure { Remove-BridgeInstallation $state 'unused.exe' } 'OEM INF'
    Check ($script:stopCalls -eq 0) 'Invalid OEM reached mutation stage'
    $state.oemInfs=@('oem12.inf'); $state.certificateThumbprint='not-a-certificate'
    Expect-Failure { Remove-BridgeInstallation $state 'unused.exe' } 'thumbprint'
    Check ($script:stopCalls -eq 0) 'Invalid certificate reached mutation stage'
    $state.certificateThumbprint=''
    New-Item -ItemType Directory -Path $script:testApp | Out-Null
    Set-Content -LiteralPath (Join-Path $script:testApp 'owned.txt') -Value 'fixture'
    Remove-BridgeInstallation $state 'unused.exe'
    Check ($script:stopCalls -eq 1 -and $script:pnpCalls.Count -eq 1) 'Unexpected cleanup actions'
    Check ($script:pnpCalls[0][1] -eq 'oem12.inf') 'Unrelated driver was targeted'
    Check (@($script:fakePackages | Where-Object { $_.Driver -in @('oem99.inf','oem88.inf') }).Count -eq 2) 'Unrelated drivers were changed'
    Check (-not (Test-Path -LiteralPath $script:testApp)) 'Owned files left behind'
    Write-Output 'Installer: PowerShell parse, real package hashes, tamper rejection, ownership/state guards and scoped rollback mocks passed.'
} finally { Remove-Item -LiteralPath $temp -Recurse -Force }
