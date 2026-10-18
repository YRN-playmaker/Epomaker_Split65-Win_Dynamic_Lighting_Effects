#requires -Version 5.1
# SPDX-License-Identifier: GPL-2.0-or-later
param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'Results'))
$ErrorActionPreference='Continue'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$path = Join-Path $OutputDirectory ('Driver-evidence-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.txt')
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('Split65 scoped driver evidence 0.4.9; read only')
$lines.Add((Get-Date).ToString('o'))
foreach ($arguments in @(
    @('/enum-devices','/deviceid','ROOT\Split65LampBridge','/drivers'),
    @('/enum-devices','/deviceid','ROOT\Split65LampBridge','/stack'),
    @('/enum-interfaces','/class','{36b26c21-62c2-4eb9-b86b-b6ed44af6ba1}')
)) {
    $lines.Add('PnPUtil '+($arguments -join ' '))
    & "$env:WINDIR\System32\pnputil.exe" @arguments 2>&1 | ForEach-Object {$lines.Add([string]$_)}
}
$setup = Join-Path $env:WINDIR 'INF\setupapi.dev.log'
if (Test-Path -LiteralPath $setup) {
    $tail = @(Get-Content -LiteralPath $setup -Tail 12000)
    $lines.Add('--- SetupAPI sections mentioning this project ---')
    $selected = New-Object 'System.Collections.Generic.SortedSet[int]'
    for($i=0;$i -lt $tail.Count;$i++) {
        if ($tail[$i] -match '(?i)Split65LampBridge|Split65 Local Bridge|36b26c21') {
            $start=$i;while($start -gt 0 -and $i-$start -lt 120 -and $tail[$start] -notmatch '^>>>'){$start--}
            $end=$i;while($end+1 -lt $tail.Count -and $end-$i -lt 180 -and $tail[$end] -notmatch '^<<<'){$end++}
            for($n=$start;$n -le $end;$n++){[void]$selected.Add($n)}
        }
    }
    foreach($n in $selected){$lines.Add([string]$tail[$n])}
}
foreach($channel in @('System','Application','Microsoft-Windows-DriverFrameworks-UserMode/Operational')) {
    $lines.Add('--- Recent scoped events: '+$channel+' ---')
    try {
        Get-WinEvent -FilterHashtable @{LogName=$channel;StartTime=(Get-Date).AddMinutes(-30)} -MaxEvents 300 -ErrorAction Stop |
        Where-Object { ($_.ProviderName -eq 'Split65LampBridge') -or $_.ToXml() -match '(?i)Split65LampBridge|342D.*E4C8|36b26c21|WUDFHost|VhfUm' } |
        ForEach-Object { $lines.Add(($_ | Select-Object TimeCreated,Id,ProviderName,Message,@{n='RawXml';e={$_.ToXml()}} | Format-List | Out-String)) }
    } catch { $lines.Add($_.Exception.Message) }
}
$lines | Set-Content -LiteralPath $path -Encoding UTF8
Write-Output ('Driver evidence: '+$path)
