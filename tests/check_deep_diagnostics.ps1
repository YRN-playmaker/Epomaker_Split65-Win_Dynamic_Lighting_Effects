$ErrorActionPreference='Stop'
$directory=Join-Path (Split-Path -Parent $PSScriptRoot) 'tools/deep-diagnostics'
$harness=@'
public sealed class FakeDeepLamp:DeepHid.IReports {
    public byte First=0x20;public int Cursor,Gets,Selectors;
    public string Fault="";int arrays;
    public void Select(byte id,ushort lamp){if(id!=First+1||lamp>=68)throw new Exception("Unexpected diagnostic write");Cursor=lamp;Selectors++;}
    static void U32(byte[] b,int o,uint v){Array.Copy(BitConverter.GetBytes(v),0,b,o,4);}
    public byte[] Get(byte id){
        Gets++;byte[] b=new byte[51];b[0]=id;
        if(id==First){arrays++;b[1]=68;U32(b,3,100000);U32(b,7,100000);U32(b,11,100000);U32(b,15,1);U32(b,19,1000);
            if(Fault=="short-array"&&arrays>1)return new byte[2];
            if(Fault=="array-change"&&arrays>1)b[3]++;return b;}
        if(id!=First+2)throw new Exception("Unexpected GET report");
        b[1]=(byte)Cursor;U32(b,3,(uint)(100*Cursor));U32(b,7,100);U32(b,11,10);U32(b,15,1000);U32(b,19,1);
        b[23]=b[24]=b[25]=255;b[26]=1;b[27]=1;b[28]=(byte)(4+Cursor);
        if(Fault=="cursor")b[1]=(byte)((Cursor+1)%68);
        if(Fault=="stability"&&Gets>80)b[28]++;
        if(Fault=="bounds")U32(b,3,100001);
        if(Fault=="short-lamp")return new byte[3];
        if(Fault=="report-id")b[0]++;
        Cursor=(Cursor+1)%68;return b;
    }
}
public static class AsyncInfoHarness {
    public static void Check() {
        WinRtAsyncInfo.GetInt id=delegate(IntPtr p,out int v){v=123;return 0;};
        WinRtAsyncInfo.GetInt status=delegate(IntPtr p,out int v){v=0;return 0;};
        WinRtAsyncInfo.GetInt error=delegate(IntPtr p,out int v){v=unchecked((int)0x80070005);return 0;};
        IntPtr table=System.Runtime.InteropServices.Marshal.AllocHGlobal(9*IntPtr.Size);
        IntPtr instance=System.Runtime.InteropServices.Marshal.AllocHGlobal(IntPtr.Size);
        try {
            System.Runtime.InteropServices.Marshal.WriteIntPtr(instance,table);
            System.Runtime.InteropServices.Marshal.WriteIntPtr(table,6*IntPtr.Size,System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(id));
            System.Runtime.InteropServices.Marshal.WriteIntPtr(table,7*IntPtr.Size,System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(status));
            System.Runtime.InteropServices.Marshal.WriteIntPtr(table,8*IntPtr.Size,System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(error));
            string actual=WinRtAsyncInfo.SnapshotPointer(instance);
            if(actual!="Id=123;Status=Started;ErrorCode=0x80070005;GetterHR=0x00000000/0x00000000/0x00000000")throw new Exception(actual);
        }finally{System.Runtime.InteropServices.Marshal.FreeHGlobal(instance);System.Runtime.InteropServices.Marshal.FreeHGlobal(table);GC.KeepAlive(id);GC.KeepAlive(status);GC.KeepAlive(error);}
    }
}
'@
Add-Type -CompilerOptions '/langversion:5' -TypeDefinition ((Get-Content -Raw (Join-Path $directory 'DeepHid.cs'))+"`n"+$harness)
[AsyncInfoHarness]::Check()
$writer=[Console]::Out
$sink=New-Object IO.StringWriter
try {
    [Console]::SetOut($sink)
    foreach($first in @(1,32)) {
        $fake=New-Object FakeDeepLamp;$fake.First=[byte]$first
        [DeepHid]::Validate($fake,[byte]$first)
        if($fake.Gets -ne 276 -or $fake.Selectors -ne 71){throw 'Unexpected query coverage or writes'}
    }
    foreach($fault in @('cursor','stability','bounds','short-lamp','short-array','array-change','report-id')) {
        $fake=New-Object FakeDeepLamp;$fake.Fault=$fault;$rejected=$false
        try{[DeepHid]::Validate($fake,[byte]32)}catch{$rejected=$true}
        if(-not $rejected){throw ('Accepted fault: '+$fault)}
    }
} finally {[Console]::SetOut($writer);$sink.Dispose()}
foreach($layout in @(@('Attributes',12),@('Caps',64),@('ValueCap',72),@('LinkNode',$(if([IntPtr]::Size -eq 8){24}else{20})))) {
    $type=[DeepHid].GetNestedType($layout[0])
    if([Runtime.InteropServices.Marshal]::SizeOf([type]$type) -ne $layout[1]){throw ('ABI mismatch: '+$layout[0])}
}
foreach($script in @(Get-ChildItem $directory -Filter '*.ps1' -File)) {
    $tokens=$null;$errors=$null
    $null=[Management.Automation.Language.Parser]::ParseFile($script.FullName,[ref]$tokens,[ref]$errors)
    if($errors.Count){throw ($errors | Out-String)}
    $ast=[Management.Automation.Language.Parser]::ParseFile($script.FullName,[ref]$tokens,[ref]$errors)
    foreach($command in $ast.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Add-Type'},$true)) {
        # Official Windows PowerShell 5.1 Add-Type metadata has CompilerParameters,
        # not the CompilerOptions parameter supplied by PowerShell 7.
        foreach($argument in $command.CommandElements) {
            if($argument -is [Management.Automation.Language.CommandParameterAst] -and $argument.ParameterName -notin @('AssemblyName','LiteralPath','Path','TypeDefinition','CompilerParameters','ReferencedAssemblies','Language','PassThru','ErrorAction')){throw ('Non-PS5 Add-Type parameter: '+$argument.ParameterName)}
        }
    }
    # The collector targets PS 5.1: reject PS 7-only syntax even when tested with PS 7.
    foreach($token in $tokens){if($token.Kind -in @('QuestionQuestion','QuestionQuestionEquals','QuestionMark')){throw 'Unsupported PowerShell syntax'}}
}
$source=Get-Content -Raw (Join-Path $directory 'Collect-Deep.ps1')
$nativePosition=$source.IndexOf("Worker '01-Native-before-direct-probes'");$metadataPosition=$source.IndexOf("'03-Metadata-'")
if($nativePosition -lt 0 -or $metadataPosition -lt 0 -or $nativePosition -gt $metadataPosition){throw 'Native baseline ordering violated'}
if(-not $source.Contains('FullDataBusTrace') -or -not $source.Contains('HeadersBusTrace')){throw 'USB transfer coverage missing'}
$catalog=Get-Content -Raw (Join-Path $directory 'official/Microsoft-HLK-25H2-LampArray-tests.json') | ConvertFrom-Json
if($catalog.tests.Count -ne 12){throw 'Official test catalog incomplete'}
[xml]$playlist=Get-Content -Raw (Join-Path $directory 'official/HLK-25H2-Descriptors.xml')
if($playlist.Playlist.Test.Count -ne 2){throw 'Descriptor playlist incorrect'}
# Exercise the actual child-process runner: streaming both pipes, nonzero exit,
# and a deadline that terminates only the process created by the runner.
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $directory 'Collect-Deep.ps1'),[ref]$tokens,[ref]$errors)
foreach($definition in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in @('Log','Save-Json','Quote-Arg','Run-Child')},$false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}
$run=Join-Path ([IO.Path]::GetTempPath()) ('split65-deep-test-'+[Guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory $run
$journal=Join-Path $run 'journal.txt';$stages=New-Object System.Collections.Generic.List[object]
try {
    $executable=Join-Path $PSHOME 'pwsh'
    if(-not (Test-Path $executable)){$executable=Join-Path $PSHOME 'powershell.exe'}
    Run-Child 'PipeTest' $executable @('-NoProfile','-Command',"[Console]::Write(('X'*100000)); [Console]::Error.Write('stderr'); exit 7") 10
    if($stages[0].ExitCode -ne 7 -or (Get-Item (Join-Path $run 'PipeTest.txt')).Length -ne 100000 -or (Get-Content -Raw (Join-Path $run 'PipeTest-stderr.txt')) -ne 'stderr'){throw 'Child pipe/exit handling failed'}
    Run-Child 'DeadlineTest' $executable @('-NoProfile','-Command','Start-Sleep -Seconds 20') 1
    if($stages[1].Result -ne 'OBSERVER_DEADLINE'){throw 'Child deadline handling failed'}
} finally {Remove-Item -LiteralPath $run -Recurse -Force}
'PASS: C# 5 compile; 64-bit HID ABI; 276 GETs and only 71 metadata-selector writes per successful run; old/new IDs; seven injected protocol failures; PowerShell parser; native-first ordering; 12 official test entries; two descriptor playlist tests.'
'PASS: actual subprocess stdout/stderr streaming, nonzero exit capture, and deadline cleanup.'
'PASS: IAsyncInfo getter ABI with injected vtable; production Add-Type calls checked against Windows PowerShell 5.1 parameter names.'
Add-Type -AssemblyName System.ServiceProcess.ServiceController
foreach($definition in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in @('Assert-OpenRGBClosed','Isolate-OpenRGB','Restore-OpenRGB')},$false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}
$script:serviceMock=[pscustomobject]@{Name='OpenRGB';Status='Running'}
$script:serviceMock | Add-Member ScriptMethod WaitForStatus {param($state,$time) if($this.Status -ne 'Stopped'){throw 'Mock service still active'}}
$script:processMock=[pscustomobject]@{Id=999999}
$script:processMock | Add-Member ScriptMethod WaitForExit {param($time) return $true}
$script:processPresent=$true
function Get-Service {[CmdletBinding()]param() return $script:serviceMock}
function Stop-Service {[CmdletBinding()]param($Name) if($Name -ne 'OpenRGB'){throw 'Unrelated service stop'};$script:serviceMock.Status='Stopped'}
function Start-Service {[CmdletBinding()]param($Name) if($Name -ne 'OpenRGB'){throw 'Unrelated service start'};$script:serviceMock.Status='Running'}
function Get-Process {[CmdletBinding()]param($Name) if($Name -ne 'OpenRGB'){throw 'Unexpected process query'};if($script:processPresent){return $script:processMock}}
function Stop-Process {[CmdletBinding()]param($Id) if($Id -ne 999999){throw 'Unexpected process stop'};$script:processPresent=$false}
function Log([string]$Message) { }
$restoreServices=New-Object System.Collections.Generic.List[string]
Isolate-OpenRGB
if($script:serviceMock.Status -ne 'Stopped' -or $script:processPresent -or $restoreServices.Count -ne 1){throw 'Isolation logic failed'}
Restore-OpenRGB
if($script:serviceMock.Status -ne 'Running'){throw 'Previously active service not restored'}
$rejected=$false;try{Assert-OpenRGBClosed}catch{$rejected=$true}
if(-not $rejected){throw 'Active OpenRGB service was ignored'}
'PASS: isolated service/process control with mocks, original running service restored, active-service preflight rejection.'
'LIMIT: no Windows hardware, WinRT, logman/ETW or HLK execution performed by these local tests.'
