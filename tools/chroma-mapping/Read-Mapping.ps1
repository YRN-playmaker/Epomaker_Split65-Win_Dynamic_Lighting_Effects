param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
. (Join-Path $PSScriptRoot 'Mapping-Core.ps1')
function Log([string]$Text){[Console]::WriteLine([DateTime]::UtcNow.ToString('o')+' '+$Text)}
function Await-WinRT($Operation,[type]$ResultType,[int]$Seconds) {
    $bridge=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {$_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and $_.GetGenericArguments().Count -eq 1 -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'} | Select-Object -First 1
    if($null -eq $bridge){throw 'WinRT task bridge unavailable'}
    $task=$bridge.MakeGenericMethod(@($ResultType)).Invoke($null,@($Operation))
    $clock=[Diagnostics.Stopwatch]::StartNew()
    while(-not $task.IsCompleted -and $clock.Elapsed.TotalSeconds -lt $Seconds){Start-Sleep -Milliseconds 100}
    if(-not $task.IsCompleted){throw ('OBSERVER_DEADLINE after '+$Seconds+' seconds; not a device HRESULT')}
    if($task.IsFaulted){throw $task.Exception.GetBaseException()}
    if($task.IsCanceled){throw 'WinRT operation canceled'}
    return ,($task.Result)
}
$devices=New-Object 'System.Collections.Generic.List[object]'
$fatal=$null
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $null=[Windows.Devices.Enumeration.DeviceInformation,Windows.Devices.Enumeration,ContentType=WindowsRuntime]
    $null=[Windows.Devices.Lights.LampArray,Windows.Devices.Lights,ContentType=WindowsRuntime]
    $null=[Windows.System.VirtualKey,Windows.System,ContentType=WindowsRuntime]
    Log ('Windows PowerShell '+$PSVersionTable.PSVersion+'; 64bit='+[Environment]::Is64BitProcess)
    $selector=[Windows.Devices.Lights.LampArray]::GetDeviceSelector()
    Log ('Selector: '+$selector)
    $items=Await-WinRT ([Windows.Devices.Enumeration.DeviceInformation]::FindAllAsync($selector)) ([Windows.Devices.Enumeration.DeviceInformationCollection]) 15
    $selected=@($items | Where-Object {$_.Id -match '(?i)VID_342D&PID_E4C[67]|VID_1532&PID_00B0'} | Sort-Object Id)
    Log ('Target LampArray candidates: '+$selected.Count)
    foreach($item in $selected) {
        Log ('OPEN '+$item.Name+' '+$item.Id)
        try {
            $lamp=Await-WinRT ([Windows.Devices.Lights.LampArray]::FromIdAsync($item.Id)) ([Windows.Devices.Lights.LampArray]) 15
            if($null -eq $lamp){throw 'FromIdAsync returned null'}
            $result=Read-LampMetadata $lamp $item.Name $item.Id {param($number) Convert-NumericKey ([Windows.System.VirtualKey]) $number}
            $devices.Add($result)
            Log ('READ '+$result.Name+'; lamps='+$result.Properties.LampCount+'; errors='+$result.Errors.Count)
        } catch {
            $cause=$_.Exception.GetBaseException()
            $devices.Add([pscustomobject]@{Name=$item.Name;DeviceId=$item.Id;Opened=$false;Error=$cause.Message;ExceptionHResult=('0x'+$cause.HResult.ToString('X8'))})
            Log ('FAILED '+$cause.Message)
        }
        # Persist after every device; a stalled later device cannot erase earlier evidence.
        ConvertTo-Json -InputObject @($devices.ToArray()) -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'Devices.json') -Encoding UTF8
    }
} catch {$fatal=$_.Exception.GetBaseException().Message;Log ('FATAL '+$fatal)}
try {
    $expected=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Expected-Split65.json') -Raw | ConvertFrom-Json
    Write-MappingReport @($devices.ToArray()) $OutputDirectory $expected
    if($fatal){Add-Content -LiteralPath (Join-Path $OutputDirectory 'Mapping-report.txt') -Value ('FATAL: '+$fatal) -Encoding UTF8}
    if($devices.Count -eq 0){Add-Content -LiteralPath (Join-Path $OutputDirectory 'Mapping-report.txt') -Value 'No target LampArrays opened. Inspect Worker-output.txt.' -Encoding UTF8}
} catch {Log ('REPORT FAILED '+$_.Exception.GetBaseException().Message);exit 2}
if($fatal -or @($devices | Where-Object {-not $_.Opened}).Count -gt 0 -or @($devices | Where-Object {$_.Opened -and $_.DeviceId -match '(?i)VID_342D&PID_E4C[67]'}).Count -eq 0){exit 1}
