param(
    [ValidateSet('Native','Inventory','Metadata','FirmwareTrace')][string]$Mode,
    [string]$DevicePath,
    [string]$DataFile
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
function Write-Stage([string]$Text) { [Console]::WriteLine([DateTime]::UtcNow.ToString('o') + "`t" + $Text) }
function Wait-Native($Operation, [type]$Type, [int]$Milliseconds, [string]$Label) {
    $bridge = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and $_.GetGenericArguments().Count -eq 1 -and
        $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
    } | Select-Object -First 1
    if ($null -eq $bridge) { throw 'No WinRT task bridge' }
    $task = $bridge.MakeGenericMethod(@($Type)).Invoke($null,@($Operation))
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (-not $task.IsCompleted -and $clock.ElapsedMilliseconds -lt $Milliseconds) { Start-Sleep -Milliseconds 100 }
    $nativeSnapshot='unavailable'
    try {$nativeSnapshot=[WinRtAsyncInfo]::Snapshot($Operation)}catch{$nativeSnapshot='unavailable: '+$_.Exception.GetBaseException().Message}
    Write-Stage ("ASYNC`t$Label`tms=" + $clock.ElapsedMilliseconds + "`ttask=" + $task.Status + "`tnative="+$nativeSnapshot)
    if (-not $task.IsCompleted) {
        try { Write-Stage ('CANCEL HRESULT=0x'+[WinRtAsyncInfo]::Cancel($Operation).ToString('X8')) } catch { Write-Stage ('Cancel unavailable: '+$_.Exception.Message) }
        throw ('OBSERVER_DEADLINE: '+$Label+' did not complete within '+$Milliseconds+' ms. This is not a device HRESULT.')
    }
    if ($task.IsFaulted) { throw $task.Exception.GetBaseException() }
    if ($task.IsCanceled) { throw ('Native operation canceled: '+$Label) }
    return ,($task.Result)
}
try {
    Write-Stage ("WORKER`tmode=$Mode`tPS="+$PSVersionTable.PSVersion+"`t64bit="+[Environment]::Is64BitProcess+"`tapartment="+[Threading.Thread]::CurrentThread.GetApartmentState())
    if ($Mode -eq 'Native') {
        # Compilation does not access hardware; native initialization still precedes direct HID queries.
        Add-Type -LiteralPath (Join-Path $PSScriptRoot 'DeepHid.cs')
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $null=[Windows.Devices.Enumeration.DeviceInformation,Windows.Devices.Enumeration,ContentType=WindowsRuntime]
        $null=[Windows.Devices.Lights.LampArray,Windows.Devices.Lights,ContentType=WindowsRuntime]
        $selector=[Windows.Devices.Lights.LampArray]::GetDeviceSelector()
        Write-Stage ('SELECTOR '+$selector)
        $items=Wait-Native ([Windows.Devices.Enumeration.DeviceInformation]::FindAllAsync($selector)) ([Windows.Devices.Enumeration.DeviceInformationCollection]) 15000 'FindAll'
        $candidates=@($items | Where-Object {$_.Id -match '(?i)VID_342D&PID_E4C[67]'})
        Write-Stage ('SPLIT65_CANDIDATES='+$candidates.Count)
        $results=New-Object System.Collections.Generic.List[object]
        # Open a reference device first, then Split65. No direct diagnostic Feature queries precede this stage.
        $selected=@($items | Where-Object {$_.Id -match '(?i)VID_1532'} | Select-Object -First 2)+$candidates
        foreach ($item in $selected) {
            Write-Stage ('OPEN_BEGIN '+$item.Name+' '+$item.Id)
            $isSplit=$item.Id -match '(?i)VID_342D&PID_E4C[67]'
            try {
                $limit=5000; if($isSplit){$limit=30000}
                $lamp=Wait-Native ([Windows.Devices.Lights.LampArray]::FromIdAsync($item.Id)) ([Windows.Devices.Lights.LampArray]) $limit ('FromId '+$item.Name)
                if ($null -eq $lamp) { throw 'FromId completed with null (not a timeout)' }
                $row=[ordered]@{Name=$item.Name;Id=$item.Id;Split65=$isSplit;Opened=$true}
                foreach ($key in @('LampCount','LampArrayKind','IsEnabled','IsConnected','IsAvailable','BoundingBox','MinUpdateInterval')) {
                    try {$row[$key]=[string]$lamp.$key;Write-Stage ('PROPERTY '+$key+'='+$row[$key])} catch {$row[$key]='ERROR: '+$_.Exception.Message}
                }
                $results.Add([pscustomobject]$row)
                Write-Stage ('OPEN_OK '+$item.Name)
            } catch {
                $cause=$_.Exception.GetBaseException()
                Write-Stage ('OPEN_FAILED '+$item.Name+' type='+$cause.GetType().FullName+' HRESULT=0x'+$cause.HResult.ToString('X8')+' '+$cause.Message)
                $results.Add([pscustomobject]@{Name=$item.Name;Id=$item.Id;Split65=$isSplit;Opened=$false;Error=$cause.Message;ExceptionHResult=('0x'+$cause.HResult.ToString('X8'))})
            }
        }
        [pscustomobject]@{CandidateCount=$candidates.Count;Devices=@($results.ToArray())} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $DataFile -Encoding UTF8
        if ($candidates.Count -eq 0 -or @($results | Where-Object {$_.Split65 -and $_.Opened}).Count -ne $candidates.Count) { exit 2 }
    } elseif ($Mode -eq 'FirmwareTrace') {
        Add-Type -LiteralPath (Join-Path $PSScriptRoot 'FirmwareTrace.cs')
        $probe=New-Object Split65HidProbe
        $probe.StartRaw($DevicePath)
        $line='';$rawFailed=$false
        while(-not $probe.Work.IsCompleted){
            while($probe.Lines.TryDequeue([ref]$line)){Write-Stage $line;if($line -match 'Raw diagnostics:'){$rawFailed=$true}}
            Start-Sleep -Milliseconds 100
        }
        while($probe.Lines.TryDequeue([ref]$line)){Write-Stage $line;if($line -match 'Raw diagnostics:'){$rawFailed=$true}}
        if($probe.Work.IsFaulted){throw $probe.Work.Exception.GetBaseException()}
        if($rawFailed){throw 'Firmware RAM trace unavailable; inspect preceding request error.'}
    } else {
        Add-Type -LiteralPath (Join-Path $PSScriptRoot 'DeepHid.cs')
        if ($Mode -eq 'Inventory') {
            $paths=[DeepHid]::Paths()
            $entries=New-Object System.Collections.Generic.List[object]
            foreach ($path in $paths) { try {
                $kind=[DeepHid]::Kind($path)
                $entries.Add([pscustomobject]@{Path=$path;Kind=$kind})
                [DeepHid]::Inventory($path)
            } catch {Write-Stage ('INVENTORY_ERROR '+$path+' '+$_.Exception.GetBaseException().Message)} }
            ConvertTo-Json -InputObject @($entries.ToArray()) | Set-Content -LiteralPath $DataFile -Encoding UTF8
            if ($paths.Count -eq 0) { throw 'No Split65 HID interfaces found' }
        } else {
            [DeepHid]::Metadata($DevicePath)
        }
    }
    Write-Stage 'WORKER_COMPLETE'
} catch {
    $cause=$_.Exception.GetBaseException()
    Write-Stage ('WORKER_FAILED type='+$cause.GetType().FullName+' HRESULT=0x'+$cause.HResult.ToString('X8')+' '+$cause.Message)
    exit 1
}
