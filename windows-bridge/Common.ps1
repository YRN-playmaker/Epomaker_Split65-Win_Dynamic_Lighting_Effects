# SPDX-License-Identifier: GPL-2.0-or-later
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$script:BridgeIdentity = '36b26c21-62c2-4eb9-b86b-b6ed44af6ba1'
$script:BridgeRebootRequired = $false
function Assert-BridgeWindows {
    if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess -or
        [Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'Use Windows x64 and 64-bit Windows PowerShell 5.1.' }
}
function Assert-BridgePlatform {
    Assert-BridgeWindows
    $build = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
    if ($build -lt 26100) { throw 'This candidate requires Windows 11 24H2 or later (build 26100+).' }
    foreach ($path in @('System32\VhfUm.dll', 'INF\hidvhf.inf', 'INF\WUDFRd.inf')) {
        if (-not (Test-Path -LiteralPath (Join-Path $env:WINDIR $path))) { throw "Missing Windows component: $path" }
    }
}
function Invoke-BridgeElevation([string]$ScriptPath, [bool]$Elevated) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { return }
    if ($Elevated) { throw 'Administrator elevation failed.' }
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $ScriptPath + '" -Elevated'
    $child = Start-Process -FilePath "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    exit $child.ExitCode
}
function Get-BridgeAppRoot { return (Join-Path $env:ProgramData 'Split65LampBridge') }
function Save-BridgeState($State) {
    $State | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path (Get-BridgeAppRoot) 'state.json') -Encoding UTF8
}
function Assert-BridgePlainDirectory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $items = @((Get-Item -LiteralPath $Path)) + @(Get-ChildItem -LiteralPath $Path -Force -Recurse)
    foreach ($item in $items) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Refusing a linked installation path: $($item.FullName)" }
    }
}
function Assert-BridgePackage([string]$Base) {
    $manifest = Get-Content -LiteralPath (Join-Path $Base 'PACKAGE-SHA256.json') -Raw | ConvertFrom-Json
    $paths = @('prebuilt/Split65Bridge.exe', 'prebuilt/driver/Split65LampBridge.dll', 'prebuilt/driver/Split65LampBridge.inf')
    if (@($manifest.files).Count -ne 3) { throw 'Unexpected prebuilt package manifest.' }
    foreach ($path in $paths) {
        $entry = @($manifest.files | Where-Object { $_.path -ceq $path })
        if ($entry.Count -ne 1) { throw "Missing hash entry: $path" }
        $file = Join-Path $Base $path
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry[0].sha256) { throw "Prebuilt file hash mismatch: $path" }
    }
}
function Assert-BridgeRootPreflight([string[]]$Lines, [int]$ExitCode) {
    if ($ExitCode -ne 0) { throw "Bridge client preflight failed (exit code $ExitCode). See the .NET/SetupAPI error above. Root enumeration did not complete." }
    $counts = @($Lines | Where-Object { $_ -match '^Matching root devices=[0-9]+$' })
    if ($counts.Count -ne 1) { throw 'Bridge client returned an unexpected root-status response. Installation stopped before making changes.' }
    if ($counts[0] -ne 'Matching root devices=0') { throw 'A project root bridge device already exists. Installation stopped before making changes.' }
}
function Get-BridgeDriverPackages {
    return @(Get-WindowsDriver -Online -All | Where-Object {
        $_.ProviderName -eq 'Split65 Community Experimental' -and
        [IO.Path]::GetFileName($_.OriginalFileName) -ieq 'Split65LampBridge.inf' -and
        $_.Driver -match '^oem[0-9]+\.inf$'
    })
}
function Invoke-BridgePnP([string[]]$Arguments) {
    & "$env:WINDIR\System32\pnputil.exe" @Arguments | Out-Host
    $code = $LASTEXITCODE
    if ($code -notin @(0, 3010)) { throw "PnPUtil failed with exit code $code. See this log and Windows INF\setupapi.dev.log." }
    if ($code -eq 3010) { $script:BridgeRebootRequired = $true; Write-Host 'Windows requested a restart.' }
}
function Stop-BridgeClient([string]$Client) {
    & $Client --stop | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Could not ask the bridge to stop.' }
    for ($i = 0; $i -lt 30; ++$i) {
        if (@(Get-Process -Name 'Split65Bridge' -ErrorAction SilentlyContinue).Count -eq 0) { return }
        Start-Sleep -Milliseconds 500
    }
    throw 'A Split65Bridge process is still running. Close its console in all Windows sessions, then run uninstall again. No process was killed.'
}
function Remove-BridgeInstallation($State, [string]$Client) {
    if ($State.identity -ne $script:BridgeIdentity) { throw 'Installation ownership marker does not match.' }
    $owned = @($State.oemInfs)
    foreach ($name in $owned) { if ($name -notmatch '^oem[0-9]+\.inf$') { throw 'Invalid recorded OEM INF name.' } }
    $thumb = [string]$State.certificateThumbprint
    if ($thumb -and $thumb -notmatch '^[0-9A-Fa-f]{40}$') { throw 'Invalid recorded certificate thumbprint.' }
    $app = Get-BridgeAppRoot
    Assert-BridgePlainDirectory $app
    Stop-BridgeClient $Client
    if ($State.rootCreated) {
        & $Client --remove-root | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Root bridge removal failed; preserving state for retry.' }
    }
    $packages = @(Get-BridgeDriverPackages)
    foreach ($name in $owned) {
        if (@($packages | Where-Object { $_.Driver -ieq $name }).Count -eq 0) { continue }
        Invoke-BridgePnP @('/delete-driver', $name, '/uninstall')
    }
    $remaining = @(Get-BridgeDriverPackages | Where-Object { $_.Driver -in $owned })
    if ($remaining.Count -ne 0) { throw 'Bridge package is still in use. Restart Windows and run uninstall again; certificate retained until removal succeeds.' }
    if ($thumb) {
        foreach ($store in @('Root', 'TrustedPublisher', 'My')) {
            $certPath = "Cert:\LocalMachine\$store\$thumb"
            if (Test-Path -LiteralPath $certPath) {
                if ($store -eq 'My') { Remove-Item -LiteralPath $certPath -DeleteKey -Force }
                else { Remove-Item -LiteralPath $certPath -Force }
            }
        }
    }
    Remove-Item -LiteralPath $app -Recurse -Force
}
function Test-BridgeProbeResult([string[]]$Lines, [int]$ExitCode) {
    return ($ExitCode -eq 0 -and $Lines -contains 'BRIDGE_PROBE=READY')
}
function Invoke-BridgeProbe([string]$Client) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName=$Client; $info.Arguments='--probe-driver'; $info.UseShellExecute=$false
    $info.CreateNoWindow=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $process=New-Object System.Diagnostics.Process; $process.StartInfo=$info
    try {
        [void]$process.Start(); $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(6000)) { $process.Kill(); [void]$process.WaitForExit(1000); throw 'Driver interface probe timed out; only its own read-only helper was stopped.' }
        $text=$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult()
        $lines=@($text -split '\r?\n'); $code=$process.ExitCode
        return [pscustomobject]@{Lines=$lines;ExitCode=$code;Ready=(Test-BridgeProbeResult $lines $code)}
    } finally { $process.Dispose() }
}
