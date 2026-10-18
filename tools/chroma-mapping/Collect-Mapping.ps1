$ErrorActionPreference='Stop'
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$base='Split65-Chroma-Mapping-'+$stamp+'-'+$PID
$run=Join-Path $PSScriptRoot $base
$null=New-Item -ItemType Directory -Path $run -Force
$process=$null;$stdout=$null;$stderr=$null
$status='NOT_STARTED';$exitCode=$null
Write-Host 'Read-only mapping check. No administrator or firmware update required.'
Write-Host 'Keep the keyboard and mouse connected; leave the current lighting settings as they are.'
Write-Host ('Output folder: '+$run)
try {
    $controllers=@(Get-Process -ErrorAction SilentlyContinue | Where-Object {$_.ProcessName -match '(?i)openrgb|synapse|chroma|razer|signalrgb|lightingservice'} | Select-Object ProcessName,Id)
    ConvertTo-Json -InputObject $controllers -Depth 3 | Set-Content -LiteralPath (Join-Path $run 'Controller-processes.json') -Encoding UTF8
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=Join-Path $PSHOME 'powershell.exe'
    $script=Join-Path $PSScriptRoot 'Read-Mapping.ps1'
    if($script.Contains('"') -or $run.Contains('"')){throw 'Unsupported quote character in path'}
    $info.Arguments='-NoProfile -MTA -ExecutionPolicy Bypass -File "'+$script+'" -OutputDirectory "'+$run+'"'
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $process=New-Object Diagnostics.Process;$process.StartInfo=$info
    $stdout=[IO.File]::Create((Join-Path $run 'Worker-output.txt'))
    $stderr=[IO.File]::Create((Join-Path $run 'Worker-errors.txt'))
    $null=$process.Start()
    $outTask=$process.StandardOutput.BaseStream.CopyToAsync($stdout)
    $errTask=$process.StandardError.BaseStream.CopyToAsync($stderr)
    $clock=[Diagnostics.Stopwatch]::StartNew();$next=10
    while(-not $process.HasExited -and $clock.Elapsed.TotalSeconds -lt 100) {
        Start-Sleep -Milliseconds 200
        if($clock.Elapsed.TotalSeconds -ge $next){Write-Host ('Reading device metadata: '+[int]$clock.Elapsed.TotalSeconds+' s');$next+=10}
    }
    if(-not $process.HasExited){$process.Kill();$status='OBSERVER_DEADLINE (diagnostic child only; not a device HRESULT)'}
    else {$exitCode=$process.ExitCode;$status='COMPLETED';if($exitCode -ne 0){$status='PARTIAL_OR_FAILED'}}
    $null=$process.WaitForExit(5000)
    $null=$outTask.Wait(5000);$null=$errTask.Wait(5000)
} catch {$status='COLLECTOR_ERROR: '+$_.Exception.GetBaseException().Message}
finally {
    if($null -ne $process){try{if(-not $process.HasExited){$process.Kill()}}catch{};$process.Dispose()}
    if($null -ne $stdout){$stdout.Dispose()};if($null -ne $stderr){$stderr.Dispose()}
}
$summary=@('Split65 / Chroma mapping check 1.0',('Status: '+$status),('Child exit: '+$exitCode),('UTC: '+[DateTime]::UtcNow.ToString('o')),('PowerShell: '+$PSVersionTable.PSVersion),('Windows version: '+[Environment]::OSVersion.Version),('Output folder: '+$run),'Metadata only. No color, brightness, enable state, services, drivers, registry or firmware changed.','Contains target device IDs and RGB controller process names/PIDs; no typed input is captured.')
$summary | Set-Content -LiteralPath (Join-Path $run '00-Run-summary.txt') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Expected-Split65.json') -Destination $run
$report=Join-Path $run 'Mapping-report.txt'
if(-not (Test-Path -LiteralPath $report)){$summary+@('No detailed report completed. Inspect Worker-output.txt and Worker-errors.txt.') | Set-Content -LiteralPath $report -Encoding UTF8}
# Always place the latest readable TXT and dated ZIP beside the CMD.
$latest=Join-Path $PSScriptRoot 'LATEST-Mapping-report.txt'
Copy-Item -LiteralPath $report -Destination $latest -Force
$zip=Join-Path $PSScriptRoot ($base+'.zip')
try {Compress-Archive -LiteralPath $run -DestinationPath $zip -Force;Write-Host ('RESULT ZIP: '+$zip)}
catch {Write-Host ('ZIP creation failed: '+$_.Exception.Message);Write-Host ('Result files remain in: '+$run)}
Write-Host ('RESULT TXT: '+$latest)
Write-Host ('Status: '+$status)
