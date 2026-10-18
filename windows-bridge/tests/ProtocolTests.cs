// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using Split65Bridge;

public static class ProtocolTests {
    sealed class Io:IReportIo {
        public long Now;public int Writes,Dropped,Timeouts,Reads,Disposals,WriteRun,MaxWriteRun;
        public bool Reverse,Duplicate,Stale,DropAll;
        public readonly List<byte[]> Sent=new List<byte[]>();
        readonly List<byte[]> replies=new List<byte[]>();
        public void Write(byte[] p) {
            ++Writes;++WriteRun;MaxWriteRun=Math.Max(MaxWriteRun,WriteRun);Sent.Add((byte[])p.Clone());Now+=1;
            if(DropAll || Dropped-->0)return;
            if(Stale){byte[] bad=(byte[])p.Clone();bad[4]^=1;replies.Add(bad);}
            replies.Add((byte[])p.Clone());if(Duplicate)replies.Add((byte[])p.Clone());
        }
        public byte[] Read(int timeout) {
            ++Reads;WriteRun=0;
            if(Timeouts>0){--Timeouts;Now+=timeout;return null;}
            if(replies.Count==0){Now+=timeout;return null;}
            int i=Reverse?replies.Count-1:0;byte[] p=replies[i];replies.RemoveAt(i);Now+=1;return p;
        }
        public void Dispose(){++Disposals;}
    }
    static void Check(bool b){if(!b)throw new Exception("Radio protocol regression");}
    public static void Run() {
        byte[] frame=new byte[204];for(int i=0;i<frame.Length;++i)frame[i]=(byte)i;
        Io clean=new Io{Reverse=true,Duplicate=true,Stale=true};
        Check(new RadioProtocol(clean,()=>clean.Now).Frame(frame,null)==8 && clean.Writes==8);
        // One lost ACK causes only the missing packet to be retried, never all
        // eight packets. An unacknowledged frame cannot advance the baseline.
        Io lost=new Io{Dropped=1};RadioProtocol recovered=new RadioProtocol(lost,()=>lost.Now);
        Check(recovered.Frame(frame,null)==8 && lost.Writes==9);
        lost.MaxWriteRun=0;recovered.Frame(frame,null);Check(lost.MaxWriteRun==1);
        lost.MaxWriteRun=0;recovered.Frame(frame,null);Check(lost.MaxWriteRun==2);
        lost.MaxWriteRun=0;recovered.Frame(frame,null);Check(lost.MaxWriteRun==2);
        Io timed=new Io{Timeouts=1};RadioProtocol protocol=new RadioProtocol(timed,()=>timed.Now);
        byte[] command=Wire.Packet(0x25);Check(protocol.Send(command)[1]==0x25 && timed.Disposals==0);
        Io failed=new Io{DropAll=true};
        try{new RadioProtocol(failed,()=>failed.Now).Frame(frame,null);throw new Exception("Missing rejection");}
        catch(RadioTimeoutException){Check(failed.Disposals==0 && failed.Writes==4);}
        AckTiming timing=new AckTiming();Check(timing.TimeoutMs==200);
        for(int i=0;i<20;++i)timing.Sample(20);Check(timing.TimeoutMs==80);
        timing.Backoff();Check(timing.TimeoutMs==160);timing.Backoff();timing.Backoff();Check(timing.TimeoutMs==400);
        timing.Sample(2000);Check(timing.TimeoutMs<=400);
        RadioRecovery recovery=new RadioRecovery();long now=0;
        foreach(int expected in new int[]{50,100,200,400,800,1000,1000}){
            Check(recovery.Ready(now));int delay=recovery.Miss(now);Check(delay==expected);
            Check(!recovery.Ready(now+delay-1));now+=delay;
        }
        // Sleeping radio links can retry indefinitely without a USB detach.
        for(int i=0;i<100;++i){Check(recovery.Miss(now)==1000);now+=1000;}
        recovery.Good();Check(recovery.Ready(now) && recovery.Miss(now)==50);
        // Mode changes remain distinguishable from transient radio failures.
        byte[] hello=new byte[33];hello[1]=0x30;hello[2]=68;hello[3]=76;hello[4]=50;hello[5]=0x50;hello[9]=68;hello[27]=1;hello[30]=2;
        try{Wire.ValidateHello(hello);throw new Exception("USB mode accepted");}catch(ModeChangedException){}
        hello[22]=6;Wire.ValidateHello(hello);hello[30]=0;
        try{Wire.ValidateHello(hello);throw new Exception("Disconnected radio accepted");}catch(RadioTimeoutException){}
        Console.WriteLine("PASS: radio ACK ordering/loss, missing-only retry, adaptive deadlines, delta frames, mode classification and sustained-loss backoff without USB detach");
    }
}
