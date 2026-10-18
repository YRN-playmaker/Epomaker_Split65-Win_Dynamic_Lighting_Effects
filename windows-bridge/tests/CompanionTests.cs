// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;
using System.IO.Compression;
using Split65Companion;
public class CompanionTests {
    static void Check(bool x){if(!x)throw new Exception("assertion failed");}
    static void Reject(Action action){try{action();}catch(IOException){return;}throw new Exception("expected rejection");}
    static byte[] Zip(string name,string body){using(MemoryStream s=new MemoryStream()){using(ZipArchive z=new ZipArchive(s,ZipArchiveMode.Create,true)){using(StreamWriter w=new StreamWriter(z.CreateEntry(name).Open()))w.Write(body);}return s.ToArray();}}
    public static int Main(string[] args){
        Check(new ProcessResult{Code=0,Text="Root present\nBRIDGE_PROBE=READY\r\n"}.Ready);
        Check(!new ProcessResult{Code=0,Text="Root present; problem=0"}.Ready);
        Check(!new ProcessResult{Code=20,Text="BRIDGE_PROBE=READY"}.Ready);
        Check(Core.Quote("C:\\two words\\file.exe")=="\"C:\\two words\\file.exe\"");
        Check(Core.Quote("C:\\end\\")=="\"C:\\end\\\\\"");
        string dir=Path.Combine(Path.GetTempPath(),"split65-companion-test-"+Guid.NewGuid());
        try{
            Core.Extract(Zip("prebuilt/hello.txt","payload"),dir);Check(File.ReadAllText(Path.Combine(dir,"prebuilt","hello.txt"))=="payload");
            File.WriteAllText(Path.Combine(dir,"prebuilt","hello.txt"),"tampered");Core.Extract(Zip("prebuilt/hello.txt","payload"),dir);Check(File.ReadAllText(Path.Combine(dir,"prebuilt","hello.txt"))=="payload");
            foreach(string name in new string[]{"../outside.txt","prebuilt/../../outside.txt","C:/Windows/file","/absolute","..\\outside.txt"})Reject(()=>Core.Extract(Zip(name,"bad"),dir));
            Reject(()=>Core.SafeEntry(dir,"folder/./file"));
            ChromaLayoutTests.Run(args);
            Console.WriteLine("Companion: actual probe readiness, Windows argument quoting, embedded extraction/repair and path traversal rejection passed.");return 0;
        }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
