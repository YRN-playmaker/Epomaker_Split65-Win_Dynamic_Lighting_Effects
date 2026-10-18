$ErrorActionPreference='Stop'
$file=Join-Path $PSScriptRoot 'Active-Session.txt'
if(-not (Test-Path -LiteralPath $file)){Write-Host 'No active session record.';exit}
$name=(Get-Content -Raw -LiteralPath $file).Trim()
if($name -notmatch '^Split65Deep_[0-9]+_[0-9]{8}-[0-9]{6}$'){throw 'Invalid collector session name; no session changed.'}
& "$env:SystemRoot\System32\logman.exe" stop -n $name
& "$env:SystemRoot\System32\logman.exe" delete -n $name
if($LASTEXITCODE -eq 0){Remove-Item -LiteralPath $file}
