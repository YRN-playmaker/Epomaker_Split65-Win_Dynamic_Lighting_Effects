$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$probe = Join-Path $project 'tools/windows-diagnose/Split65HidProbe.cs'
Add-Type -Path $probe -CompilerOptions '/langversion:5'
Add-Type -CompilerOptions '/langversion:5' -TypeDefinition @'
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
public static class ProbeHarness {
    public static void CheckError(object probe, Stream stream) {
        try {Query(probe,stream,0x30,0,0);}
        catch(System.Reflection.TargetInvocationException e) {
            var method=probe.GetType().GetMethod("ErrorDetail",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            var detail=(string)method.Invoke(null,new object[]{e.InnerException});
            if(!detail.Contains("Raw HID write failed")||!detail.Contains("underlying simulated HID write error")||!detail.Contains("HRESULT=0x"))throw new Exception(detail);
            return;
        }
        throw new Exception("Simulated HID failure was not reported");
    }
    public static byte[] Query(object probe, Stream stream, byte command, byte operation, byte index) {
        var method=probe.GetType().GetMethod("RawQuery",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        return (byte[])method.Invoke(probe,new object[]{stream,command,operation,index});
    }
}
public sealed class UnbufferedHidTestStream : Stream {
    byte[] pending;
    public bool Written;
    public bool FailWrite;
    public int Writes, Flushes, Reads;
    public int ReplyLength=33;
    public byte ReplyId;
    public override bool CanRead {get{return true;}}
    public override bool CanWrite {get{return true;}}
    public override bool CanSeek {get{return false;}}
    public override long Length {get{throw new NotSupportedException();}}
    public override long Position {get{throw new NotSupportedException();}set{throw new NotSupportedException();}}
    public override void Write(byte[] b,int o,int n) {
        if(n!=33||b[o]!=0) throw new Exception("Invalid Windows HID framing");
        pending=new byte[n];Array.Copy(b,o,pending,0,n);Written=true;Writes++;
    }
    public override void Flush(){Flushes++;throw new IOException("HID device does not support FlushFileBuffers");}
    public override int Read(byte[] b,int o,int n){
        if(!Written)throw new Exception("Read before unbuffered HID request was written");
        if(n!=33)throw new Exception("Incorrect read length");
        Array.Copy(pending,0,b,o,33);b[o]=ReplyId;Reads++;return ReplyLength;
    }
    public override Task WriteAsync(byte[] b,int o,int n,CancellationToken c){
        if(FailWrite){var task=new TaskCompletionSource<int>();task.SetException(new IOException("underlying simulated HID write error"));return task.Task;}
        Write(b,o,n);return Task.FromResult(0);
    }
    public override Task FlushAsync(CancellationToken c){Flush();return Task.FromResult(0);}
    public override Task<int> ReadAsync(byte[] b,int o,int n,CancellationToken c){return Task.FromResult(Read(b,o,n));}
    public override long Seek(long o,SeekOrigin s){throw new NotSupportedException();}
    public override void SetLength(long n){throw new NotSupportedException();}
}
'@
$instance = New-Object Split65HidProbe
$method = [Split65HidProbe].GetMethod('RawQuery', [System.Reflection.BindingFlags]'NonPublic,Instance')
$stream = New-Object UnbufferedHidTestStream
foreach ($command in @(0x30,0x31,0x30)) {
    [byte[]]$reply = [ProbeHarness]::Query($instance,$stream,[byte]$command,[byte]1,[byte]7)
    if ($reply.Length -ne 32 -or $reply[0] -ne $command -or $reply[1] -ne 1 -or $reply[2] -ne 7) {throw 'Raw query payload mismatch'}
}
if ($stream.Writes -ne 3 -or $stream.Flushes -ne 0 -or $stream.Reads -ne 3) {throw 'Unbuffered HID must write/read without flushing'}
foreach ($kind in @('short','id')) {
    $invalid = New-Object UnbufferedHidTestStream
    if ($kind -eq 'short') {$invalid.ReplyLength=32} else {$invalid.ReplyId=1}
    $rejected=$false
    try {$null=[ProbeHarness]::Query($instance,$invalid,[byte]0x30,[byte]0,[byte]0)}
    catch {if ($_.Exception.GetBaseException() -is [System.InvalidOperationException]) {$rejected=$true} else {throw}}
    if (-not $rejected) {throw 'Invalid HID report was accepted'}
}
$failed = New-Object UnbufferedHidTestStream
$failed.FailWrite=$true
[ProbeHarness]::CheckError($instance,$failed)
$caps = [Split65HidProbe].GetNestedType('Caps',[System.Reflection.BindingFlags]'NonPublic')
$attributes = [Split65HidProbe].GetNestedType('Attributes',[System.Reflection.BindingFlags]'NonPublic')
if ([System.Runtime.InteropServices.Marshal]::SizeOf([type]$caps) -ne 64 -or [System.Runtime.InteropServices.Marshal]::SizeOf([type]$attributes) -ne 12) {throw 'HID interop layout mismatch'}
$tokens=$null; $errors=$null
$script=Join-Path $project 'tools/windows-diagnose/Split65-Windows-Diagnose.ps1'
$null=[System.Management.Automation.Language.Parser]::ParseFile($script,[ref]$tokens,[ref]$errors)
if($errors.Count){throw ($errors | Out-String)}
$content=Get-Content -Raw $script
if($content.Contains('$probe.Start(')){throw 'Direct lamp metadata query must not precede native initialization'}
if(-not $content.Contains('$afterProbe.StartRaw')){throw 'Post-native USB trace missing'}
'PASS: C# 5 compilation, unbuffered raw query framing and nested HRESULT errors, malformed reply rejection, HID ABI and native-first PowerShell syntax'

if((Get-Content -Raw $probe).Contains('AwaitRaw(stream.FlushAsync')){throw 'HID queries must not call FlushAsync'}
if(-not (Get-Content -Raw $probe).Contains('FileAccess.ReadWrite, 1, true')){throw 'Production HID writes must bypass FileStream buffering'}
