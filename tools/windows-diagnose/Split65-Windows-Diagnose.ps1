# Read-only Split65 PnP and native Windows LampArray diagnostics.
# Uses Windows PowerShell 5.1 and built-in Windows Runtime projections.
# Does not install drivers, modify registry, change colors, or flash firmware.
$ErrorActionPreference = 'Stop'
$report = New-Object System.Collections.Generic.List[string]
$outFile = Join-Path $PSScriptRoot 'Split65-Windows-Diagnosis.txt'
# Replace the previous diagnostic log once; append progress as it happens.
try { Set-Content -LiteralPath $outFile -Value '' -Encoding UTF8 } catch { }

function Log([string]$Message) {
    Write-Host $Message
    $report.Add($Message)
    try { Add-Content -LiteralPath $outFile -Value $Message -Encoding UTF8 } catch { }
}

function Log-ErrorDetail([string]$Stage, $ErrorRecord) {
    $cause = $ErrorRecord.Exception.GetBaseException()
    Log ($Stage + ': ' + $cause.Message)
    Log ('  Exception=' + $cause.GetType().FullName + '; HRESULT=0x' + $cause.HResult.ToString('X8'))
}

function Await-WinRT($Operation, [Type]$ResultType, [int]$TimeoutMs = 30000) {
    $method = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and
        $_.GetGenericArguments().Count -eq 1 -and $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
    } | Select-Object -First 1
    if ($null -eq $method) { throw 'Could not find the Windows Runtime AsTask bridge.' }
    $closed = $method.MakeGenericMethod(@($ResultType))
    $task = $closed.Invoke($null, @($Operation))
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    # Poll in an MTA process rather than blocking an STA with Task.Wait().
    while (-not $task.IsCompleted -and $clock.ElapsedMilliseconds -lt $TimeoutMs) { Start-Sleep -Milliseconds 100 }
    if (-not $task.IsCompleted) { throw ('Diagnostic deadline expired after ' + $TimeoutMs + ' ms; native API has not returned a device error.') }
    if ($task.IsFaulted) { throw $task.Exception.GetBaseException() }
    if ($task.IsCanceled) { throw 'Windows Runtime operation was canceled.' }
    # Preserve the DeviceInformationCollection instead of unrolling the pipeline.
    return ,($task.Result)
}

function Wait-HidProbe($Probe) {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $line = ''
    while (-not $Probe.Work.IsCompleted -and $clock.ElapsedMilliseconds -lt 30000) {
        while ($Probe.Lines.TryDequeue([ref]$line)) { Log $line }
        Start-Sleep -Milliseconds 100
    }
    while ($Probe.Lines.TryDequeue([ref]$line)) { Log $line }
    if (-not $Probe.Work.IsCompleted) { throw 'HID diagnostic did not finish in 30 seconds.' }
}

try {
    Log 'Split65 Windows LampArray diagnosis v6 (unbuffered HID + nested errors + native-first USB trace)'
    Log ('Time=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
    Log 'Close OpenRGB, Hub, VIA and other RGB controllers before running.'
    Log 'This tool reads device information. It does not set colors or flash firmware.'
    Log ''
    try {
        $os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
        Log ('Windows DisplayVersion=' + $os.DisplayVersion + '; build=' + $os.CurrentBuildNumber + '.' + $os.UBR)
        Log ('PowerShell=' + $PSVersionTable.PSVersion.ToString() + '; 64-bit process=' + [Environment]::Is64BitProcess)
        Log ('Apartment=' + [System.Threading.Thread]::CurrentThread.GetApartmentState())
    } catch { Log-ErrorDetail 'OS query failed' $_ }

    Log ''
    Log '--- Present Split65 USB/HID/keyboard/mouse nodes ---'
    try {
        $nodes = @(Get-PnpDevice -PresentOnly | Where-Object {
            $_.InstanceId -match '(?i)VID_342D&PID_E4C[67]'
        })
        Log ('Split65 PnP node count=' + $nodes.Count)
        foreach ($node in $nodes) {
            Log ('Name=' + $node.FriendlyName + '; class=' + $node.Class + '; status=' + $node.Status)
            Log ('  Id=' + $node.InstanceId)
            foreach ($key in @('DEVPKEY_Device_HardwareIds', 'DEVPKEY_Device_CompatibleIds', 'DEVPKEY_Device_Service', 'DEVPKEY_Device_ProblemCode', 'DEVPKEY_Device_Parent', 'DEVPKEY_Device_BusReportedDeviceDesc')) {
                $property = Get-PnpDeviceProperty -InstanceId $node.InstanceId -KeyName $key -ErrorAction SilentlyContinue
                if ($null -ne $property) { Log ('  ' + $key + '=' + ($property.Data -join '; ')) }
            }
        }
    } catch { Log-ErrorDetail 'PnP query failed' $_ }

    # Inventory does not depend on Windows already recognizing a LampArray.
    # v3 skipped bcdDevice entirely when the LampArray selector found no Split65.
    Log ''
    Log '--- All Split65 HID collection versions/capabilities and existing USB trace ---'
    $inventoryTimedOut = $false
    $rawPaths = @()
    try {
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $null = [Windows.Devices.Enumeration.DeviceInformation, Windows.Devices.Enumeration, ContentType=WindowsRuntime]
        $collectionType = [Windows.Devices.Enumeration.DeviceInformationCollection, Windows.Devices.Enumeration, ContentType=WindowsRuntime]
        if (-not ('Split65HidProbe' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'Split65HidProbe.cs') }
        $hidSelector = 'System.Devices.InterfaceClassGuid:="{4D1E55B2-F16F-11CF-88CB-001111000030}" AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True'
        $hidDevices = Await-WinRT ([Windows.Devices.Enumeration.DeviceInformation]::FindAllAsync($hidSelector)) $collectionType
        $hidEntries = @($hidDevices | Where-Object { $_.Id -match '(?i)vid_342d&pid_e4c[67]' })
        Log ('Split65 enabled HID collection count=' + $hidEntries.Count)
        if ($hidEntries.Count -gt 0) {
            $inventoryProbe = New-Object Split65HidProbe
            $inventoryProbe.StartInventory([string[]]@($hidEntries | ForEach-Object { $_.Id }))
            try { Wait-HidProbe $inventoryProbe; $rawPaths = @($inventoryProbe.RawPaths.ToArray()) }
            catch { $inventoryTimedOut = -not $inventoryProbe.Work.IsCompleted; throw }
        }
    } catch { Log-ErrorDetail 'HID inventory' $_ }

    Log ''
    Log '--- Native Windows.Devices.Lights API ---'
    $stage = 'Load Windows Runtime types'
    try {
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $null = [Windows.Devices.Lights.LampArray, Windows.Devices.Lights, ContentType=WindowsRuntime]
        $null = [Windows.Devices.Enumeration.DeviceInformation, Windows.Devices.Enumeration, ContentType=WindowsRuntime]
        $collectionType = [Windows.Devices.Enumeration.DeviceInformationCollection, Windows.Devices.Enumeration, ContentType=WindowsRuntime]
        $lampType = [Windows.Devices.Lights.LampArray, Windows.Devices.Lights, ContentType=WindowsRuntime]
        $stage = 'Get LampArray device selector'
        $selector = [Windows.Devices.Lights.LampArray]::GetDeviceSelector()
        Log ('Selector=' + $selector)
        $stage = 'Enumerate native LampArray devices'
        $operation = [Windows.Devices.Enumeration.DeviceInformation]::FindAllAsync($selector)
        $devices = Await-WinRT $operation $collectionType
        $allDevices = @($devices)
        Log ('Native LampArray device count=' + $allDevices.Count)
        $splitCount = 0
        $openedSplit = 0
        # No direct Feature metadata reads before/during native initialization:
        # those queries change the firmware's shared lamp cursor.
        $splitEntries = @($allDevices | Where-Object { $_.Id -match '(?i)vid_342d&pid_e4c[67]' })
        Log ''
        Log '--- Open native LampArrays: up to two reference devices, then Split65 ---'
        $referenceEntries = @($allDevices | Where-Object { $_.Id -notmatch '(?i)vid_342d&pid_e4c[67]' } | Select-Object -First 2)
        $openEntries = @($referenceEntries) + @($splitEntries)
        $openedReference = 0
        foreach ($entry in $openEntries) {
            $isSplit = $entry.Id -match '(?i)vid_342d&pid_e4c[67]'
            Log ('Name=' + $entry.Name + '; enabled=' + $entry.IsEnabled + '; Split65=' + $isSplit)
            Log ('  Id=' + $entry.Id)
            if ($isSplit) { $splitCount++ }
            if ($isSplit -and $inventoryTimedOut) { Log '  Skipped: raw HID probe is still outstanding.'; continue }
            $lamp = $null
            try {
                Log '  Starting FromIdAsync; diagnostic deadline=30 seconds...'
                $clock = [System.Diagnostics.Stopwatch]::StartNew()
                $operation = [Windows.Devices.Lights.LampArray]::FromIdAsync($entry.Id)
                $lamp = Await-WinRT $operation $lampType
                Log ('  Native operation completed in ' + $clock.ElapsedMilliseconds + ' ms')
                if ($null -eq $lamp) {
                    Log '  FromIdAsync returned null. Native initialization/access did not succeed.'
                    continue
                }
                if ($isSplit) { $openedSplit++ } else { $openedReference++ }
                Log ('  LampCount=' + $lamp.LampCount + '; kind=' + $lamp.LampArrayKind)
                Log ('  Connected=' + $lamp.IsConnected + '; Available=' + $lamp.IsAvailable + '; Enabled=' + $lamp.IsEnabled)
                Log ('  Hardware=' + $lamp.HardwareVendorId + ':' + $lamp.HardwareProductId + '; version=' + $lamp.HardwareVersion)
                Log ('  MinUpdateInterval=' + $lamp.MinUpdateInterval + '; SupportsVirtualKeys=' + $lamp.SupportsVirtualKeys)
                Log ('  BoundingBox=' + $lamp.BoundingBox)
            } catch { Log-ErrorDetail '  FromIdAsync/property read failed' $_ }
            finally {
                if ($null -ne $lamp -and $lamp -is [System.IDisposable]) {
                    try { ([System.IDisposable]$lamp).Dispose() } catch { }
                }
            }
        }
        Log ('Native Split65 count=' + $splitCount + '; opened successfully=' + $openedSplit)
        Log ('Reference devices opened successfully=' + $openedReference + '/' + $referenceEntries.Count)
        if ($openedSplit -gt 0) {
            Log 'Result: the native Windows API can open Split65. Investigate the Settings UI/policy if its card is absent.'
        } elseif ($splitCount -gt 0) {
            Log 'Result: a native candidate exists, but opening failed. Preserve the exact error; firmware metadata/access may need investigation.'
        } else {
            Log 'Result: no Split65 matched the native selector. Compare with the PnP compatible IDs above.'
        }
    } catch {
        Log-ErrorDetail $stage $_
        Log 'A script/API failure is not proof that the keyboard firmware is invalid.'
    }

    Log ''
    Log '--- USB trace after native initialization (no direct metadata reads) ---'
    foreach ($rawPath in $rawPaths) {
        try {
            $afterProbe = New-Object Split65HidProbe
            $afterProbe.StartRaw([string]$rawPath)
            Wait-HidProbe $afterProbe
        } catch { Log-ErrorDetail 'Post-native USB trace' $_ }
    }
    if ($rawPaths.Count -eq 0) { Log 'No supported Raw HID collection was identified; no trace requests sent.' }

    Log ''
    Log '--- Dynamic Lighting preferences (read only) ---'
    $lighting = Get-ItemProperty 'HKCU:\Software\Microsoft\Lighting' -ErrorAction SilentlyContinue
    if ($null -eq $lighting) { Log 'No per-user Lighting preference key was found.' }
    else {
        foreach ($name in @('AmbientLightingEnabled', 'ControlledByForegroundApp')) {
            $property = $lighting.PSObject.Properties[$name]
            if ($null -ne $property) { Log ($name + '=' + $property.Value) }
        }
    }
} catch { Log-ErrorDetail 'Unexpected diagnostic error' $_ }
finally {
    Log ''
    try {
        $report | Set-Content -LiteralPath $outFile -Encoding UTF8
        Write-Host ('Report saved: ' + $outFile)
    } catch {
        Write-Host 'Could not write the report beside this script. Copy or screenshot this window.'
    }
}
