#requires -Version 5.1
# SPDX-License-Identifier: GPL-2.0-or-later
# Run in VS 2022 x64 Developer PowerShell with SDK/WDK 10.0.26100 installed.
param([string]$KitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10", [string]$KitVersion = '10.0.26100.0')
$ErrorActionPreference = 'Stop'
if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) { throw 'Open the VS 2022 x64 Developer PowerShell first.' }
$repo = Split-Path $PSScriptRoot -Parent
$out = Join-Path $PSScriptRoot 'build'
$bin = Join-Path $PSScriptRoot 'prebuilt'
New-Item -ItemType Directory -Path $out, (Join-Path $bin 'driver') -Force | Out-Null
$include = Join-Path $KitsRoot "Include\wdf\umdf\2.15"
if (-not (Test-Path -LiteralPath $include)) { throw 'WDK UMDF 2.15 headers missing.' }
Push-Location $out
try {
    $sources = @((Join-Path $PSScriptRoot 'driver\Split65LampBridge.c'), (Join-Path $PSScriptRoot 'core\bridge_core.c'), (Join-Path $repo 'dynamic_lighting\protocol.c'))
    & cl.exe /nologo /c /std:c11 /O2 /MT /W4 /DUNICODE /D_UNICODE /D_WIN32_WINNT=0x0A00 /DNTDDI_VERSION=0x0A000000 /DUMDF_VERSION_MAJOR=2 /DUMDF_VERSION_MINOR=15 "/I$include" "/I$KitsRoot\Include\$KitVersion\um" @sources
    if ($LASTEXITCODE -ne 0) { throw 'C compilation failed.' }
    & link.exe /nologo /dll /machine:x64 /subsystem:windows /dynamicbase /nxcompat "/out:$bin\driver\Split65LampBridge.dll" Split65LampBridge.obj bridge_core.obj protocol.obj "/libpath:$KitsRoot\Lib\wdf\umdf\x64\2.15" "/libpath:$KitsRoot\Lib\$KitVersion\um\x64" WdfDriverStubUm.lib VhfUm.lib mincore.lib ntdll.lib advapi32.lib
    if ($LASTEXITCODE -ne 0) { throw 'DLL link failed.' }
    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    & $csc /nologo /langversion:5 /platform:x64 /optimize+ "/out:$bin\Split65Bridge.exe" (Join-Path $PSScriptRoot 'client\BridgeClient.cs') (Join-Path $PSScriptRoot 'client\RootDevice.cs') (Join-Path $PSScriptRoot 'client\DriverAccess.cs') (Join-Path $PSScriptRoot 'client\RadioProtocol.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Client compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'driver\Split65LampBridge.inf') -Destination (Join-Path $bin 'driver') -Force
    $files = foreach ($relative in @('prebuilt/Split65Bridge.exe', 'prebuilt/driver/Split65LampBridge.dll', 'prebuilt/driver/Split65LampBridge.inf')) {
        [pscustomobject]@{ path=$relative; sha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $relative) -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    [pscustomobject]@{ version='0.5.0'; files=@($files) } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'PACKAGE-SHA256.json') -Encoding UTF8
    Write-Host 'Built unsigned candidate binaries. Install.ps1 performs local package signing.'
} finally { Pop-Location }
