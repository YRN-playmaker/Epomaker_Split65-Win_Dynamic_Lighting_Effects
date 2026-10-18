// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
namespace Split65Companion {
public sealed class ProcessResult {
    public int Code; public string Text;
    public bool Ready { get { return Code==0 && HasLine("BRIDGE_PROBE=READY"); } }
    public bool HasLine(string line) {
        foreach(string s in Text.Split(new char[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) if(s==line)return true;
        return false;
    }
}
public static class Core {
    public const string Version="0.5.0";
    public const string RepositoryUrl="https://github.com/YRN-playmaker/Epomaker_Split65-Win_Dynamic_Lighting_Effects";
    public static string Quote(string text) {
        StringBuilder value=new StringBuilder("\""); int slashes=0;
        foreach(char c in text) {
            if(c=='\\') {++slashes;continue;}
            if(c=='\"') {value.Append('\\',slashes*2+1);value.Append(c);slashes=0;continue;}
            value.Append('\\',slashes);slashes=0;value.Append(c);
        }
        value.Append('\\',slashes*2);value.Append('"');return value.ToString();
    }
    public static string Hash(byte[] bytes) {using(SHA256 sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    public static void PlainAncestors(string path) {
        for(DirectoryInfo d=new DirectoryInfo(Path.GetFullPath(path));d!=null;d=d.Parent)
            if(d.Exists && (d.Attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("Linked cache/install paths are not supported: "+d.FullName);
    }
    public static string SafeEntry(string folder,string name) {
        if(String.IsNullOrEmpty(name) || name.IndexOf(':')>=0 || name.StartsWith("/") || name.StartsWith("\\"))throw new IOException("Unsafe embedded entry.");
        string normalized=name.Replace('\\','/');
        foreach(string part in normalized.Split(new char[]{'/'})) if(part==".." || part==".")throw new IOException("Unsafe embedded entry.");
        string prefix=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string target=Path.GetFullPath(Path.Combine(folder,normalized.Replace('/',Path.DirectorySeparatorChar)));
        if(!target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Embedded entry escaped its cache.");
        return target;
    }
    public static void Extract(byte[] archive,string folder) {
        PlainAncestors(folder);Directory.CreateDirectory(folder);
        HashSet<string> names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using(MemoryStream stream=new MemoryStream(archive))using(ZipArchive zip=new ZipArchive(stream,ZipArchiveMode.Read)) {
            long total=0;
            foreach(ZipArchiveEntry entry in zip.Entries) {
                if(entry.FullName.EndsWith("/"))continue;
                if(!names.Add(entry.FullName.Replace('\\','/')))throw new IOException("Duplicate embedded entry.");
                total+=entry.Length;if(total>32*1024*1024 || entry.Length>8*1024*1024)throw new IOException("Embedded payload too large.");
                string target=SafeEntry(folder,entry.FullName);PlainAncestors(Path.GetDirectoryName(target));
                if(File.Exists(target) && (File.GetAttributes(target)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked payload file.");
                byte[] bytes;using(Stream source=entry.Open())using(MemoryStream copy=new MemoryStream()){source.CopyTo(copy);bytes=copy.ToArray();}
                if(File.Exists(target) && Hash(File.ReadAllBytes(target))==Hash(bytes))continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllBytes(target,bytes);
            }
        }
    }
    public static ProcessResult Run(string exe,string arguments,int timeoutMs) {
        using(Process process=new Process()) {
            process.StartInfo=new ProcessStartInfo(exe,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            if(!process.Start())throw new IOException("Could not start project helper.");
            Task<string> stdout=process.StandardOutput.ReadToEndAsync(),stderr=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(timeoutMs)) {
                try{process.Kill();process.WaitForExit(2000);}catch{}
                throw new TimeoutException("Project helper timed out; its own process was stopped.");
            }
            return new ProcessResult{Code=process.ExitCode,Text=stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult()};
        }
    }
}
}
