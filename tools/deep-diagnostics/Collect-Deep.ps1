param([switch]$HeadersOnly,[switch]$IsolateOpenRGB)
$ErrorActionPreference='Stop'
$version='Split65 Deep Diagnostics 1.1'
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$run=Join-Path $PSScriptRoot ('Results\'+$stamp+'-'+$PID)
$null=New-Item -ItemType Directory -Path $run -Force
$journal=Join-Path $run '00-Summary.txt'
$stages=New-Object System.Collections.Generic.List[object]
$session='Split65Deep_'+$PID+'_'+$stamp
$created=$false; $started=$false
$restoreServices=New-Object System.Collections.Generic.List[string]
$engine=Join-Path $PSHOME 'powershell.exe'
function Log([string]$Message) {
    $line=[DateTime]::UtcNow.ToString('o')+"`t"+$Message
    Write-Host $Message
    Add-Content -LiteralPath $journal -Value $line -Encoding UTF8
}
function Save-Json($Object,[string]$Name) {
    ConvertTo-Json -InputObject $Object -Depth 10 | Set-Content -LiteralPath (Join-Path $run $Name) -Encoding UTF8
}
function Invoke-Logman([string[]]$Arguments) {
    $text=(& "$env:SystemRoot\System32\logman.exe" @Arguments 2>&1 | Out-String)
    $code=$LASTEXITCODE
    Add-Content -LiteralPath (Join-Path $run 'ETW-commands.txt') -Value (($Arguments -join ' ') + "`r`nexit="+$code+"`r`n"+$text) -Encoding UTF8
    if($code -ne 0){Log ('ETW command failed: '+($Arguments -join ' ')+'; exit='+$code)}
    return $code
}
function Quote-Arg([string]$Text) {
    if($Text.Contains('"') -or $Text.EndsWith('\')){throw 'Unsupported child argument quoting'}
    return '"'+$Text+'"'
}
function Run-Child([string]$Name,[string]$File,[string[]]$Arguments,[int]$Seconds) {
    Log ('BEGIN '+$Name)
    $row=[ordered]@{Name=$Name;StartedUtc=[DateTime]::UtcNow.ToString('o');Result='NOT_STARTED';ExitCode=$null;LimitSeconds=$Seconds}
    $process=$null;$stdout=$null;$stderr=$null
    try {
        $info=New-Object Diagnostics.ProcessStartInfo
        $info.FileName=$File
        $info.Arguments=($Arguments | ForEach-Object {Quote-Arg $_}) -join ' '
        $info.UseShellExecute=$false;$info.CreateNoWindow=$true
        $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
        $process=New-Object Diagnostics.Process;$process.StartInfo=$info
        $stdout=[IO.File]::Create((Join-Path $run ($Name+'.txt')))
        $stderr=[IO.File]::Create((Join-Path $run ($Name+'-stderr.txt')))
        $null=$process.Start()
        $outTask=$process.StandardOutput.BaseStream.CopyToAsync($stdout)
        $errTask=$process.StandardError.BaseStream.CopyToAsync($stderr)
        $clock=[Diagnostics.Stopwatch]::StartNew();$next=15
        while(-not $process.HasExited -and $clock.Elapsed.TotalSeconds -lt $Seconds) {
            Start-Sleep -Milliseconds 200
            if($clock.Elapsed.TotalSeconds -ge $next){Log ($Name+' running: '+[int]$clock.Elapsed.TotalSeconds+' s');$next+=15}
        }
        if(-not $process.HasExited){$process.Kill();$row.Result='OBSERVER_DEADLINE';Log ($Name+': terminated this diagnostic child after deadline; not a device HRESULT')}
        else {$row.ExitCode=$process.ExitCode;$row.Result='COMPLETED';if($process.ExitCode -ne 0){$row.Result='FAILED_OR_UNAVAILABLE'}}
        $null=$process.WaitForExit(5000)
        $null=$outTask.Wait(5000);$null=$errTask.Wait(5000)
    } catch {$row.Result='COLLECTOR_ERROR';$row.Error=$_.Exception.GetBaseException().Message;Log ($Name+': '+$row.Error)}
    finally {
        if($null -ne $process){try{if(-not $process.HasExited){$process.Kill()}}catch{};$process.Dispose()}
        if($null -ne $stdout){$stdout.Dispose()};if($null -ne $stderr){$stderr.Dispose()}
        $row.EndedUtc=[DateTime]::UtcNow.ToString('o');$stages.Add([pscustomobject]$row)
        Save-Json @($stages.ToArray()) 'Stages.json'
        Log ('END '+$Name+' '+$row.Result+' exit='+$row.ExitCode)
    }
}
function Worker([string]$Name,[string]$Mode,[int]$Seconds,[string]$Path='') {
    $arguments=@('-NoProfile','-MTA','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'Worker.ps1'),'-Mode',$Mode,'-DataFile',(Join-Path $run ($Name+'.json')))
    if($Path){$arguments+=@('-DevicePath',$Path)}
    Run-Child $Name $engine $arguments $Seconds
}
function Snapshot-Pnp {
    $list=New-Object System.Collections.Generic.List[object]
    $seen=@{}
    $queue=New-Object System.Collections.Generic.Queue[string]
    foreach($device in @(Get-PnpDevice -PresentOnly | Where-Object {$_.InstanceId -match '(?i)VID_342D&PID_E4C[67]'})){$queue.Enqueue($device.InstanceId)}
    while($queue.Count -gt 0 -and $seen.Count -lt 100) {
        $id=$queue.Dequeue();if($seen.ContainsKey($id)){continue};$seen[$id]=$true
        $device=Get-PnpDevice -InstanceId $id -ErrorAction SilentlyContinue
        $properties=[ordered]@{}
        $keys=@('HardwareIds','CompatibleIds','Service','ProblemCode','Parent','ContainerId','LocationPaths','BusReportedDeviceDesc','DriverInfPath','DriverProvider','DriverVersion','DriverDate','ClassGuid','UpperFilters','LowerFilters') | ForEach-Object {'DEVPKEY_Device_'+$_}
        foreach($value in @(Get-PnpDeviceProperty -InstanceId $id -KeyName $keys -ErrorAction SilentlyContinue)) {
            $properties[$value.KeyName.Replace('DEVPKEY_Device_','')]=$value.Data
        }
        $list.Add([pscustomobject]@{InstanceId=$id;Name=$device.FriendlyName;Status=$device.Status;Class=$device.Class;Properties=$properties})
        if($properties.Parent){$queue.Enqueue([string]$properties.Parent)}
    }
    Save-Json @($list.ToArray()) 'PnP-and-parent-chain.json'
}
function Assert-OpenRGBClosed {
    $processes=@(Get-Process -Name OpenRGB -ErrorAction SilentlyContinue)
    $services=@(Get-Service -ErrorAction SilentlyContinue | Where-Object {$_.Name -match '(?i)openrgb' -and $_.Status -eq 'Running'})
    if($processes.Count -or $services.Count){throw 'OpenRGB is still running. Use Run-Clean-OpenRGB-Test.cmd as administrator to run the isolated comparison.'}
}
function Isolate-OpenRGB {
    # Explicitly selected by Run-Clean-OpenRGB-Test.cmd. No startup type changes.
    $services=@(Get-Service | Where-Object {$_.Name -match '(?i)openrgb' -and $_.Status -ne 'Stopped'})
    foreach($service in $services) {
        $restoreServices.Add($service.Name)
        Log ('TEMPORARY_STOP_SERVICE '+$service.Name)
        Stop-Service -Name $service.Name -ErrorAction Stop
        $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(10))
    }
    foreach($process in @(Get-Process -Name OpenRGB -ErrorAction SilentlyContinue)) {
        Log ('TEMPORARY_STOP_PROCESS OpenRGB PID='+$process.Id)
        Stop-Process -Id $process.Id -ErrorAction Stop
        $null=$process.WaitForExit(10000)
    }
    Assert-OpenRGBClosed
}
function Restore-OpenRGB {
    foreach($name in $restoreServices) {
        try {Start-Service -Name $name -ErrorAction Stop;Log ('RESTORED_SERVICE '+$name)}
        catch {Log ('RESTORE_SERVICE_FAILED '+$name+' '+$_.Exception.Message)}
    }
}
Log $version
Log ('Results directory: '+$run)
try {
    if($env:OS -ne 'Windows_NT'){throw 'Run this collector on the Windows PC connected to Split65.'}
    if(-not [Environment]::Is64BitProcess){throw 'Use 64-bit Windows PowerShell. A 32-bit process was detected.'}
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    $admin=(New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if(-not $admin){throw 'Right-click Run-Deep-Diagnosis.cmd and choose Run as administrator. USB ETW requires elevation.'}
    if(-not (Test-Path -LiteralPath $engine)){throw 'Windows PowerShell 5.1 executable missing.'}
    Log 'Close EPOMAKER Hub, VIA and other lighting apps before continuing.'
    if($IsolateOpenRGB){Log 'ISOLATION MODE: OpenRGB services and processes will be stopped temporarily. Previously active services will be started again at cleanup; a standalone GUI will not be reopened.'}
    else {Log 'No apps will be terminated. OpenRGB must already be fully stopped, including its service.'}
    Log 'USB capture includes transfer payloads from other USB devices. During capture, do not type private text or copy files to USB storage.'
    Log 'No firmware, driver, lighting colors, power policy or registry values will be changed.'
    $null=Read-Host 'Press ENTER when ready; capture starts next'
    $begin=Get-Date
    if($IsolateOpenRGB){Isolate-OpenRGB}else{Assert-OpenRGBClosed}
    $os=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    Save-Json ([ordered]@{Collector=$version;Utc=[DateTime]::UtcNow.ToString('o');DisplayVersion=$os.DisplayVersion;Build=([string]$os.CurrentBuildNumber+'.'+$os.UBR);Edition=$os.EditionID;PS=$PSVersionTable.PSVersion.ToString();Is64Bit=[Environment]::Is64BitProcess;HeadersOnly=[bool]$HeadersOnly;IsolateOpenRGB=[bool]$IsolateOpenRGB;RestoreServices=@($restoreServices.ToArray())}) 'Environment.json'
    Get-ChildItem -LiteralPath $PSScriptRoot -File | Get-FileHash -Algorithm SHA256 | Select-Object Hash,Path | ConvertTo-Json | Set-Content (Join-Path $run 'Collector-hashes.json') -Encoding UTF8
    $matching='(?i)razer|openrgb|signalrgb|epomaker|(^|[ _-])via($|[ _-])|lighting|lamparray|lghub|icue|rgbfusion|armoury'
    Save-Json @(Get-Process | Where-Object {$_.ProcessName -match $matching} | Select-Object ProcessName,Id) 'Related-processes.json'
    Save-Json @(Get-Service | Where-Object {$_.Name -match $matching -or $_.DisplayName -match $matching} | Select-Object Name,DisplayName,Status,StartType) 'Related-services.json'
    Save-Json @(Get-Process | Select-Object ProcessName,Id) 'Process-index.json'
    try {Save-Json @(Get-CimInstance Win32_Service | Where-Object {$_.State -eq 'Running'} | Select-Object Name,ProcessId) 'Running-service-PIDs.json'}catch{Log ('Service PID index unavailable: '+$_.Exception.Message)}
    $preferences=New-Object System.Collections.Generic.List[object]
    foreach($key in @('HKCU:\Software\Microsoft\Lighting','HKLM:\Software\Microsoft\Lighting','HKCU:\Software\Policies\Microsoft\Windows\Lighting','HKLM:\Software\Policies\Microsoft\Windows\Lighting')) {
        if(Test-Path $key){$preferences.Add([pscustomobject]@{Path=$key;Values=(Get-ItemProperty $key | Select-Object * -ExcludeProperty PS*)})}
    }
    Save-Json @($preferences.ToArray()) 'Lighting-preferences-readonly.json'
    $filters=New-Object System.Collections.Generic.List[object]
    foreach($key in @('HKLM:\SYSTEM\CurrentControlSet\Control\Class\{745a17a0-74d3-11d0-b6fe-00a0c90f57da}','HKLM:\SYSTEM\CurrentControlSet\Services\spacedeskDriverAndroidControl')) {
        if(Test-Path $key){$filters.Add([pscustomobject]@{Path=$key;Values=(Get-ItemProperty $key | Select-Object UpperFilters,LowerFilters,ImagePath,Start,Type)})}
    }
    Save-Json @($filters.ToArray()) 'HID-class-filters-readonly.json'
    $providers=(& "$env:SystemRoot\System32\logman.exe" query providers 2>&1 | Out-String)
    $providers | Set-Content (Join-Path $run 'Registered-ETW-providers.txt') -Encoding UTF8
    $providerList=New-Object System.Collections.Generic.List[object]
    foreach($line in ($providers -split "`r?`n")) {
        if($line -match '^\s*(.+?)\s+(\{[0-9A-Fa-f-]{36}\})\s*$'){$providerList.Add([pscustomobject]@{Name=$Matches[1].Trim();Guid=$Matches[2]})}
    }
    $code=Invoke-Logman @('create','trace','-n',$session,'-o',(Join-Path $run 'USB.etl'),'-f','bincirc','-max','256','-nb','32','128','-bs','128')
    if($code -ne 0){throw 'Could not create ETW collector; preserve ETW-commands.txt.'}
    $created=$true
    Set-Content (Join-Path $PSScriptRoot 'Active-Session.txt') $session -Encoding ASCII
    $keywords='(Default,FullDataBusTrace,StateMachine)'
    if($HeadersOnly){$keywords='(Default,HeadersBusTrace,StateMachine)'}
    $enabled=0;$usbEnabled=0
    foreach($name in @('Microsoft-Windows-USB-USBXHCI','Microsoft-Windows-USB-UCX','Microsoft-Windows-USB-USBHUB3')) {
        if(@($providerList | Where-Object {$_.Name -eq $name}).Count -gt 0) {
            $code=Invoke-Logman @('update','trace','-n',$session,'-p',$name,$keywords)
            if($code -eq 0){$enabled++;$usbEnabled++}
        } else {Log ('Provider unavailable: '+$name)}
    }
    # These providers vary by Windows build. Enable only names actually registered, using their GUIDs.
    foreach($provider in @($providerList | Where-Object {$_.Name -match '(?i)lamp|ambientlight|lighting|hidclass|hidusb'})) {
        $code=Invoke-Logman @('update','trace','-n',$session,'-p',$provider.Guid,'0xFFFFFFFFFFFFFFFF','5')
        if($code -eq 0){$enabled++}
    }
    if($usbEnabled -eq 0){throw 'No USB xHCI-stack provider enabled. This run lacks required USB transfer coverage.'}
    $code=Invoke-Logman @('start','-n',$session)
    if($code -ne 0){throw 'ETW did not start.'};$started=$true
    Log ('ETW STARTED: '+$session+'; providers='+$enabled+'; keywords='+$keywords)
    Log 'Unplug ONLY the left-half USB cable from the PC, wait 3 seconds, then reconnect it in wired mode. Leave the right-half interconnect as usual.'
    $null=Read-Host 'After reconnecting, press ENTER. Do not open other RGB tools'
    Log 'REPLUG_CONFIRMED_BY_USER'
    Assert-OpenRGBClosed
    # Native baseline comes before every direct Feature query. Inventory below reads only local parsed HID capabilities.
    Worker '01-Native-before-direct-probes' 'Native' 100
    Assert-OpenRGBClosed
    try{Snapshot-Pnp}catch{Log ('PnP snapshot failed: '+$_.Exception.Message)}
    Worker '02-HID-inventory' 'Inventory' 45
    $pathsFile=Join-Path $run '02-HID-inventory.json'
    $paths=@();if(Test-Path $pathsFile){$paths=@(Get-Content -Raw $pathsFile | ConvertFrom-Json)}
    $index=0
    foreach($entry in @($paths | Where-Object {$_.Kind -eq 'Raw'})) {
        $index++;Worker ('02b-Firmware-trace-before-metadata-'+$index) 'FirmwareTrace' 40 ([string]$entry.Path)
    }
    $index=0
    foreach($entry in @($paths | Where-Object {$_.Kind -eq 'LampArray'})) {
        $index++;Worker ('03-Metadata-'+$index) 'Metadata' 120 ([string]$entry.Path)
    }
    if($index -eq 0){Log 'COVERAGE_GAP: no LampArray collection available for direct metadata tests.'}
    Worker '04-Native-after-direct-probes' 'Native' 100
    Log 'Opening Windows Dynamic Lighting settings while ETW is still recording.'
    try{Start-Process 'ms-settings:personalization-lighting'}catch{Log ('Settings launch failed: '+$_.Exception.Message)}
    $answer=Read-Host 'Wait about 10 seconds. If Split65 is shown type YES, otherwise type NO, then ENTER'
    # Do not store arbitrary text entered at the prompt.
    $observation='UNSPECIFIED';if($answer -match '^(?i)YES$'){$observation='VISIBLE'}elseif($answer -match '^(?i)NO$'){$observation='ABSENT'}
    Log ('SETTINGS_OBSERVATION='+$observation+' (reported by user; no screenshot captured)')
    # End transfer capture before slower event-log queries and ZIP creation.
    if($started){$code=Invoke-Logman @('stop','-n',$session);if($code -eq 0){$started=$false;Log 'ETW STOPPED after reproduction'}}
    try {
        $events=New-Object System.Collections.Generic.List[object]
        $logs=@('System')+@(Get-WinEvent -ListLog * -ErrorAction SilentlyContinue | Where-Object {$_.IsEnabled -and $_.LogName -match '(?i)lighting|lamparray|hidclass|usb|userpnp|kernel-pnp'} | Select-Object -ExpandProperty LogName)
        foreach($channel in @($logs | Select-Object -Unique)) {
            foreach($event in @(Get-WinEvent -FilterHashtable @{LogName=$channel;StartTime=$begin} -MaxEvents 300 -ErrorAction SilentlyContinue)) {
                if($channel -eq 'System' -and $event.ProviderName -notmatch '(?i)usb|hid|pnp|driverframework|lighting'){continue}
                $events.Add([pscustomobject]@{TimeUtc=$event.TimeCreated.ToUniversalTime().ToString('o');Channel=$channel;Provider=$event.ProviderName;Id=$event.Id;Level=$event.LevelDisplayName;Xml=$event.ToXml()})
            }
        }
        Save-Json @($events.ToArray()) 'Recent-device-events.json'
    } catch {Log ('Event query failed: '+$_.Exception.Message)}
    try {
        $setup=Join-Path $env:SystemRoot 'INF\setupapi.dev.log'
        Select-String -LiteralPath $setup -Pattern 'VID_342D&PID_E4C[67]' -Context 5,15 | Select-Object -Last 30 | Out-String -Width 240 | Set-Content (Join-Path $run 'SetupAPI-target-excerpts.txt') -Encoding UTF8
    } catch {Log ('SetupAPI excerpts failed: '+$_.Exception.Message)}
} catch {Log ('COLLECTION_ERROR: '+$_.Exception.GetBaseException().Message)}
finally {
    if($started){$null=Invoke-Logman @('stop','-n',$session);$started=$false}
    $deleted=$true
    if($created){$deleted=(Invoke-Logman @('delete','-n',$session)) -eq 0}
    $active=Join-Path $PSScriptRoot 'Active-Session.txt'
    if($deleted -and (Test-Path $active) -and (Get-Content -Raw $active).Trim() -eq $session){Remove-Item -LiteralPath $active -Force}
    if(-not $deleted){Log ('CLEANUP_INCOMPLETE: run Stop-Capture.cmd as administrator; session='+$session)}
    Log 'Capture stage finished; check cleanup status above. Original ETL files are authoritative; XML decoding is a convenience.'
    Restore-OpenRGB
}
foreach($etl in @(Get-ChildItem -LiteralPath $run -Filter '*.etl' -File)) {
    Run-Child ('Decode-'+$etl.BaseName) (Join-Path $env:SystemRoot 'System32\tracerpt.exe') @($etl.FullName,'-o',(Join-Path $run ($etl.BaseName+'.xml')),'-of','XML','-y') 60
}
Log 'Root cause is NOT automatically determined by enumeration success, a deadline, or a metadata pass. Preserve all logs for request-level analysis.'
Log ('Finished. Results: '+$run)
try {
    $archive=$run+'.zip'
    Compress-Archive -Path (Join-Path $run '*') -DestinationPath $archive -CompressionLevel Optimal -Force
    Write-Host ('SEND THIS ZIP: '+$archive) -ForegroundColor Green
    Start-Process explorer.exe -ArgumentList ('/select,"'+$archive+'"')
} catch {Log ('ZIP creation failed; send the result directory instead. '+$_.Exception.Message)}
