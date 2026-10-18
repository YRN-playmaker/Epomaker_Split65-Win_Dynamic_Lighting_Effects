// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.IO;

namespace Split65Bridge {
public sealed class RadioTimeoutException:IOException {public RadioTimeoutException(string message):base(message){}}
public sealed class ModeChangedException:IOException {public ModeChangedException(string message):base(message){}}
public interface IReportIo:IDisposable {
    void Write(byte[] packet);
    // null means the deadline elapsed. A pending native read remains owned by
    // the transport and is reused on the next call, never abandoned.
    byte[] Read(int timeoutMs);
}
// Bound retries to the observed radio round trip. Do not use a fixed multi-
// second blocking read, or learn latency from retransmitted/ambiguous ACKs.
public sealed class AckTiming {
    double smoothed,variation;bool sampled;
    int timeout=200;
    public int TimeoutMs {get{return timeout;}}
    public void Sample(long elapsed) {
        double rtt=Math.Max(1,Math.Min(2000,elapsed));
        if(!sampled){smoothed=rtt;variation=rtt/2;sampled=true;}
        else{variation=.75*variation+.25*Math.Abs(smoothed-rtt);smoothed=.875*smoothed+.125*rtt;}
        timeout=(int)Math.Max(80,Math.Min(400,Math.Ceiling(smoothed+Math.Max(20,4*variation))));
    }
    public void Backoff(){timeout=Math.Min(400,timeout*2);}
}
public sealed class RadioProtocol {
    readonly IReportIo io;
    readonly Func<long> clock;
    int window=2;
    readonly AckTiming timing=new AckTiming();
    public RadioProtocol(IReportIo transport,Func<long> now){io=transport;clock=now;}
    public byte[] Send(byte[] request) {
        for(int attempt=0;attempt<2;++attempt) {
            long sentAt=clock();
            io.Write(request);
            long deadline=clock()+timing.TimeoutMs;
            while(clock()<deadline) {
                byte[] reply=io.Read((int)Math.Max(1,deadline-clock()));
                if(reply!=null && Wire.IsResponseTo(request,reply)){
                    if(attempt==0)timing.Sample(clock()-sentAt);
                    return reply;
                }
            }
            timing.Backoff();
        }
        throw new RadioTimeoutException("Radio ACK timeout; the pending read and virtual device are retained for retry.");
    }
    public int Frame(byte[] rgb,byte[] previous) {
        List<byte[]> packets=new List<byte[]>(Wire.ChangedFrame(rgb,previous));
        bool sawLoss=false;
        for(int start=0;start<packets.Count;) {
            int count=Math.Min(window,packets.Count-start);
            bool[] acknowledged=new bool[count];int remaining=count;
            long[] sentAt=new long[count];
            for(int attempt=0;attempt<2 && remaining>0;++attempt) {
                for(int i=0;i<count;++i)if(!acknowledged[i]){sentAt[i]=clock();io.Write(packets[start+i]);}
                long deadline=clock()+timing.TimeoutMs;
                while(remaining>0 && clock()<deadline) {
                    byte[] reply=io.Read((int)Math.Max(1,deadline-clock()));
                    if(reply==null)continue;
                    for(int i=0;i<count;++i) {
                        if(!acknowledged[i] && Wire.IsResponseTo(packets[start+i],reply)) {
                            if(attempt==0)timing.Sample(clock()-sentAt[i]);
                            acknowledged[i]=true;--remaining;break;
                        }
                    }
                }
                if(remaining>0){sawLoss=true;window=1;timing.Backoff();} // Reduce pressure after loss; never count duplicate ACKs.
            }
            if(remaining>0)throw new RadioTimeoutException("RGB batch not fully acknowledged; keeping the last confirmed frame and retrying.");
            start+=count;
        }
        // Recover throughput after clean frames; one lost ACK must not leave
        // every future animation permanently serialized.
        if(packets.Count>0 && !sawLoss && window<2)window=Math.Min(2,window*2);
        return packets.Count;
    }
    public void Release(){io.Write(Wire.Packet(0x26));}
}
// Radio silence is not USB removal. Keep the virtual device alive while the
// physical HID handle exists, with bounded retry traffic during sleep/loss.
public sealed class RadioRecovery {
    int failures;long nextTry;
    public bool Ready(long now){return now>=nextTry;}
    public int Miss(long now){int delay=Math.Min(1000,50<<Math.Min(failures,5));if(failures<6)++failures;nextTry=now+delay;return delay;}
    public void Good(){failures=0;nextTry=0;}
}
}
