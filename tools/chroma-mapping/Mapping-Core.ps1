# Metadata only. Device identity remains part of every lamp address.
function Convert-NumericKey([type]$KeyType,[int]$Code) {
    # Windows.System.VirtualKey omits OEM constants such as 186/220.
    # PowerShell's enum cast rejects unnamed values; ToObject preserves them.
    return [Enum]::ToObject($KeyType,$Code)
}
function Get-KeyLabel([int]$Code) {
    switch ($Code) { 13 {'Enter';break} 32 {'Space';break} 80 {'P';break}
        186 {'OEM_1 (; on US layout)';break} 220 {'OEM_5 (backslash on US layout)';break}
        default { 'VK_0x{0:X2}' -f $Code }
    }
}
function Read-LampMetadata($Lamp,[string]$Name,[string]$DeviceId,[scriptblock]$KeyValue) {
    $errors=New-Object 'System.Collections.Generic.List[string]'
    $properties=[ordered]@{}
    foreach($key in @('DeviceId','HardwareVendorId','HardwareProductId','HardwareVersion','LampArrayKind','LampCount','SupportsVirtualKeys','IsAvailable','IsConnected','IsEnabled','MinUpdateInterval')) {
        try {$properties[$key]=[string]$Lamp.$key} catch {$properties[$key]=$null;$errors.Add($key+': '+$_.Exception.GetBaseException().Message)}
    }
    try {$box=$Lamp.BoundingBox;$properties.BoundingBoxMeters=[ordered]@{X=[double]$box.X;Y=[double]$box.Y;Z=[double]$box.Z}}
    catch {$errors.Add('BoundingBox: '+$_.Exception.GetBaseException().Message)}
    $count=[int]$Lamp.LampCount
    if($count -lt 1 -or $count -gt 4096){throw ('Unsupported lamp count: '+$count)}
    $bindings=New-Object 'System.Collections.Generic.List[object]'
    $keysByLamp=@{}
    if($Lamp.SupportsVirtualKeys) {
        for($vk=1;$vk -le 255;$vk++) {
            try {
                $key=& $KeyValue $vk
                $indices=@($Lamp.GetIndicesForKey($key))
                foreach($index in $indices) {
                    $number=[int]$index
                    if($number -lt 0 -or $number -ge $count){$errors.Add(('Out of range: VK {0} -> lamp {1}' -f $vk,$number));continue}
                    if(-not $keysByLamp.ContainsKey($number)){$keysByLamp[$number]=New-Object 'System.Collections.Generic.List[int]'}
                    $keysByLamp[$number].Add($vk)
                }
                if($indices.Count -gt 0){$bindings.Add([pscustomobject]@{VirtualKey=$vk;KeyLabel=(Get-KeyLabel $vk);Indices=@($indices | ForEach-Object {[int]$_})})}
            } catch {$errors.Add(('GetIndicesForKey {0}: {1}' -f $vk,$_.Exception.GetBaseException().Message))}
        }
    }
    $lamps=New-Object 'System.Collections.Generic.List[object]'
    for($i=0;$i -lt $count;$i++) {
        try {
            $info=$Lamp.GetLampInfo($i);$position=$info.Position
            $keys=@();if($keysByLamp.ContainsKey($i)){$keys=@($keysByLamp[$i].ToArray())}
            $lamps.Add([pscustomobject]@{Index=$i;ReturnedIndex=[int]$info.Index;X=[double]$position.X;Y=[double]$position.Y;Z=[double]$position.Z;Purposes=[string]$info.Purposes;RedLevelCount=[int]$info.RedLevelCount;GreenLevelCount=[int]$info.GreenLevelCount;BlueLevelCount=[int]$info.BlueLevelCount;GainLevelCount=[int]$info.GainLevelCount;UpdateLatency=[string]$info.UpdateLatency;VirtualKeys=$keys})
            if([int]$info.Index -ne $i){$errors.Add(('Index mismatch: requested {0}, returned {1}' -f $i,$info.Index))}
        } catch {$errors.Add(('GetLampInfo {0}: {1}' -f $i,$_.Exception.GetBaseException().Message))}
    }
    return [pscustomobject]@{Name=$Name;DeviceId=$DeviceId;Opened=$true;Properties=[pscustomobject]$properties;Bindings=@($bindings.ToArray());Lamps=@($lamps.ToArray());Errors=@($errors.ToArray())}
}
function Write-MappingReport($Devices,[string]$Directory,$Expected) {
    $lines=New-Object 'System.Collections.Generic.List[string]'
    $lines.Add('Split65 / Chroma mapping check 1.0 - metadata only')
    $lines.Add('UTC: '+[DateTime]::UtcNow.ToString('o'))
    $lines.Add('No color, enable, brightness, firmware or registry settings are written.')
    $lines.Add('Lamp indices and key bindings are device-local. Equal values across devices are allowed; this is not proof of a conflict.')
    $lines.Add('Key labels for OEM codes assume a US physical layout. Screenshot-to-key identification is approximate.')
    $lines.Add('IsEnabled/IsAvailable are API instance observations, not proof of Chroma ownership or the Windows Settings toggle state.')
    $rows=New-Object 'System.Collections.Generic.List[object]'
    $focus=New-Object 'System.Collections.Generic.List[object]'
    foreach($device in @($Devices)) {
        $lines.Add('');$lines.Add('DEVICE: '+$device.Name);$lines.Add('ID: '+$device.DeviceId)
        if(-not $device.Opened){$lines.Add('OPEN FAILED: '+$device.Error);continue}
        $lines.Add(('Kind={0}; Lamps={1}; SupportsVirtualKeys={2}; Enabled={3}; Available={4}' -f $device.Properties.LampArrayKind,$device.Properties.LampCount,$device.Properties.SupportsVirtualKeys,$device.Properties.IsEnabled,$device.Properties.IsAvailable))
        foreach($lamp in $device.Lamps) {
            $rows.Add([pscustomobject]@{Device=$device.Name;DeviceId=$device.DeviceId;Kind=$device.Properties.LampArrayKind;LampIndex=$lamp.Index;Xmeters=$lamp.X;Ymeters=$lamp.Y;Zmeters=$lamp.Z;VirtualKeys=($lamp.VirtualKeys -join ',');Purposes=$lamp.Purposes})
        }
        foreach($vk in @(13,80,186,220)) {
            $indices=@($device.Bindings | Where-Object {$_.VirtualKey -eq $vk} | ForEach-Object {$_.Indices})
            $indexText=$indices -join ',';if($indices.Count -eq 0){$indexText='none'}
            $lines.Add(('  {0} [VK {1}] -> lamp(s): {2}' -f (Get-KeyLabel $vk),$vk,$indexText))
            $focus.Add([pscustomobject]@{Device=$device.Name;DeviceId=$device.DeviceId;VirtualKey=$vk;KeyLabel=(Get-KeyLabel $vk);LampIndices=$indexText;SupportsVirtualKeys=$device.Properties.SupportsVirtualKeys})
        }
        if($device.DeviceId -match '(?i)VID_342D&PID_E4C[67]' -and $null -ne $Expected) {
            $lines.Add('  Compare with v0.4.7 source (USB HID usages are distinct from Windows virtual keys):')
            foreach($expectedLamp in $Expected.FocusLamps) {
                $actual=@($device.Lamps | Where-Object {$_.Index -eq $expectedLamp.Index})
                $binding=@($device.Bindings | Where-Object {$_.VirtualKey -eq $expectedLamp.VirtualKey} | ForEach-Object {$_.Indices})
                if($actual.Count -eq 1) {
                    $a=$actual[0];$distance=[Math]::Abs($a.X-$expectedLamp.Xmeters)+[Math]::Abs($a.Y-$expectedLamp.Ymeters)+[Math]::Abs($a.Z-$expectedLamp.Zmeters)
                    $lines.Add(('    {0}: lamp {1}; position matches={2}; expected VK points here={3}' -f $expectedLamp.Label,$expectedLamp.Index,($distance -lt 0.00001),($binding -contains [int]$expectedLamp.Index)))
                } else {$lines.Add('    Missing metadata for expected lamp '+$expectedLamp.Index)}
            }
        }
        $lines.Add('Read errors: '+@($device.Errors).Count)
        foreach($errorText in @($device.Errors | Select-Object -First 12)){$lines.Add('  '+$errorText)}
    }
    $lines.Add('');$lines.Add('Interpretation: this report can reveal metadata mismatches. It cannot observe internal Chroma effect-layer routing or prove which software sent a mouse color.')
    $lines.Add('Return this run ZIP, keeping the full device IDs. No new firmware is included or required.')
    $lines | Set-Content -LiteralPath (Join-Path $Directory 'Mapping-report.txt') -Encoding UTF8
    $rows.ToArray() | Export-Csv -LiteralPath (Join-Path $Directory 'All-lamps.csv') -NoTypeInformation -Encoding UTF8
    $focus.ToArray() | Export-Csv -LiteralPath (Join-Path $Directory 'Conflict-keys.csv') -NoTypeInformation -Encoding UTF8
}
