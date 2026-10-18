param([string]$Project=(Join-Path $PSScriptRoot '..'))
$ErrorActionPreference='Stop'
$directory=Join-Path $Project 'tools/chroma-mapping'
foreach($file in Get-ChildItem -LiteralPath $directory -Filter '*.ps1') {
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
    if($errors.Count){throw ($file.Name+': '+($errors.Message -join '; '))}
    $unsafe=$ast.FindAll({param($node)
        ($node -is [Management.Automation.Language.MemberExpressionAst] -and $node.Member.Value -match '^SetColor') -or
        ($node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -match '\.(IsEnabled|BrightnessLevel)\s*$') -or
        ($node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -match '^(Stop-Process|Stop-Service|Set-ItemProperty|Set-Service)$')
    },$true)
    if($unsafe.Count){throw ('Mutating operation in metadata tool: '+$file.Name)}
}
. (Join-Path $directory 'Mapping-Core.ps1')
function Assert($condition,[string]$message){if(-not $condition){throw $message}}
Assert ([int](Convert-NumericKey ([System.DayOfWeek]) 220) -eq 220) 'unnamed OEM enum values must survive PowerShell conversion'
$lamp=[pscustomobject]@{DeviceId='test';HardwareVendorId=13357;HardwareProductId=58567;HardwareVersion=71;LampArrayKind='Keyboard';LampCount=3;SupportsVirtualKeys=$true;IsAvailable=$true;IsConnected=$true;IsEnabled=$false;MinUpdateInterval='00:00:00.001';BoundingBox=[pscustomobject]@{X=0.3;Y=0.1;Z=0}}
$lamp | Add-Member -MemberType ScriptMethod -Name GetIndicesForKey -Value {param($key)
    switch([int]$key){13 {return ,([int[]]@(2))} 80 {return ,([int[]]@(0,1))} default{return ,([int[]]@())}}
}
$lamp | Add-Member -MemberType ScriptMethod -Name GetLampInfo -Value {param($index)
    return [pscustomobject]@{Index=$index;Position=[pscustomobject]@{X=$index/10.0;Y=0.05;Z=0};Purposes='Keyboard';RedLevelCount=256;GreenLevelCount=256;BlueLevelCount=256;GainLevelCount=2;UpdateLatency='00:00:00.001'}
}
$split=Read-LampMetadata $lamp 'Split65' 'HID#VID_342D&PID_E4C7#TEST' {param($n) $n}
Assert ($split.Lamps.Count -eq 3) 'all lamps must be read'
Assert ($split.Errors.Count -eq 0) ('unexpected metadata errors: '+($split.Errors -join ';'))
Assert (($split.Bindings | Where-Object VirtualKey -eq 80).Indices.Count -eq 2) 'one key may map to multiple lamps'
Assert ($split.Lamps[2].VirtualKeys[0] -eq 13) 'reverse key mapping must be preserved'
Assert (-not $lamp.IsEnabled) 'reader changed IsEnabled'
$mouse=Read-LampMetadata $lamp 'Razer reference' 'HID#VID_1532&PID_00B0&MI_03#TEST' {param($n) $n}
$tmp=Join-Path ([IO.Path]::GetTempPath()) ('split65-mapping-'+[Guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $tmp
try {
    $expected=[pscustomobject]@{FocusLamps=@([pscustomobject]@{Index=2;Label='Enter';VirtualKey=13;Xmeters=0.2;Ymeters=0.05;Zmeters=0})}
    Write-MappingReport @($split,$mouse) $tmp $expected
    $csv=@(Import-Csv -LiteralPath (Join-Path $tmp 'All-lamps.csv'))
    Assert ($csv.Count -eq 6) 'device-local lamp IDs must not be merged across devices'
    Assert (@($csv | Select-Object -ExpandProperty DeviceId -Unique).Count -eq 2) 'both device identities must survive export'
    $text=Get-Content -LiteralPath (Join-Path $tmp 'Mapping-report.txt') -Raw
    Assert ($text -match 'position matches=True; expected VK points here=True') 'source metadata comparison failed'
    Assert ($text -match 'not proof of a conflict') 'must not diagnose normal cross-device duplicate keys as an error'
    $lamp.SupportsVirtualKeys=$false
    $noKeys=Read-LampMetadata $lamp 'No key support' 'test' {throw 'key converter must not run'}
    Assert ($noKeys.Bindings.Count -eq 0 -and $noKeys.Lamps.Count -eq 3) 'non-keyboard metadata must still be collected'
    $lamp.SupportsVirtualKeys=$true
    $lamp | Add-Member -Force -MemberType ScriptMethod -Name GetIndicesForKey -Value {param($key) if([int]$key -eq 80){return ,([int[]]@(9))};return ,([int[]]@())}
    $lamp | Add-Member -Force -MemberType ScriptMethod -Name GetLampInfo -Value {param($index) throw 'injected metadata read failure'}
    $bad=Read-LampMetadata $lamp 'Malformed metadata' 'test' {param($n) $n}
    Assert (@($bad.Errors | Where-Object {$_ -match 'Out of range'}).Count -eq 1) 'out-of-range key mapping must be retained as an error'
    Assert (@($bad.Errors | Where-Object {$_ -match '^GetLampInfo'}).Count -eq 3) 'per-lamp failures must not prevent later reads'
} finally {Remove-Item -LiteralPath $tmp -Recurse -Force}
Write-Output 'PASS: syntax, read-only operations, device-local addresses, multi-lamp key bindings, source comparison, no-key device'
