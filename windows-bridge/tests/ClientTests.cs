// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;
using System.Runtime.InteropServices;
using Split65Bridge;
public class ClientTests {
    static void Check(bool condition) { if (!condition) throw new Exception("assertion failed"); }
    static void Reject(Action action) { try { action(); } catch (IOException) { return; } throw new Exception("expected rejection"); }
    public static int Main() {
        foreach (string ids in new string[]{"ROOT\\Split65LampBridge\0\0", "OTHER\\Device\0root\\split65lampbridge\0\0"}) {
            byte[] data=System.Text.Encoding.Unicode.GetBytes(ids);
            Check(RootDevice.MatchesHardwareId(data,data.Length));
        }
        foreach (string ids in new string[]{"", "OTHER\\Device\0\0", "ROOT\\Split65LampBridgeOther\0\0", "prefixROOT\\Split65LampBridge\0\0"}) {
            byte[] data=System.Text.Encoding.Unicode.GetBytes(ids);
            Check(!RootDevice.MatchesHardwareId(data,data.Length));
        }
        Check(!RootDevice.MatchesHardwareId(null,0));
        Check(!RootDevice.MatchesHardwareId(new byte[4],3));
        Check(!RootDevice.MatchesHardwareId(new byte[4],6));
        Console.WriteLine("Hardware-ID MULTI_SZ parsing and exact, case-insensitive root matching passed.");
        byte[] probe={83,76,66,80,0,5,0,1,220,0,0,0,1,0,0,0};
        Check(DriverAccess.ValidProbe(probe));
        for(int i=0;i<probe.Length;i++){byte[] bad=(byte[])probe.Clone();bad[i]^=1;Check(!DriverAccess.ValidProbe(bad));}
        Check(!DriverAccess.ValidProbe(null) && !DriverAccess.ValidProbe(new byte[8]));
        byte[] hello=new byte[33]; hello[1]=0x30; hello[2]=68; hello[3]=76; hello[4]=50; hello[5]=0x50; hello[9]=68; hello[22]=6; hello[27]=1; hello[30]=2;
        Wire.ValidateHello(hello);
        hello[5]=0x50; Wire.ValidateHello(hello);
        foreach(byte revision in new byte[]{0x47,0x48,0x49,0x4a,0}) {byte[] bad=(byte[])hello.Clone();bad[5]=revision;Reject(delegate {Wire.ValidateHello(bad);});}
        foreach (int index in new int[]{0,1,2,3,4,9,22,27,30}) {
            byte[] bad=(byte[])hello.Clone(); bad[index]^=1; Reject(delegate { Wire.ValidateHello(bad); });
        }
        Reject(delegate { Wire.ValidateHello(new byte[32]); });
        byte[] rgb=new byte[204]; for (int i=0;i<rgb.Length;i++) rgb[i]=(byte)(i^0xa5);
        int next=0, packets=0;
        foreach (byte[] p in Wire.Frame(rgb)) {
            Check(p.Length==33 && p[0]==0 && p[1]==0x24 && p[2]==next);
            Check(p[3]==Math.Min(9,68-next));
            for (int i=0;i<p[3]*3;i++) Check(p[4+i]==rgb[next*3+i]);
            for (int i=4+p[3]*3;i<33;i++) Check(p[i]==0);
            Check(Wire.IsResponseTo(p,(byte[])p.Clone()));
            byte[] stale=(byte[])p.Clone(); stale[2]=(byte)((next+9)%68);
            Check(!Wire.IsResponseTo(p,stale));
            stale=(byte[])p.Clone(); stale[4]^=1; Check(!Wire.IsResponseTo(p,stale));
            stale=(byte[])p.Clone(); stale[2]=253; Reject(delegate { Wire.IsResponseTo(p,stale); });
            next+=p[3]; ++packets;
        }
        Check(next==68 && packets==8);
        int deltaCount=0;byte[] changed=(byte[])rgb.Clone();changed[38*3]^=1;
        foreach(byte[] p in Wire.ChangedFrame(changed,rgb)){Check(p[2]==38 && p[3]==1 && p[4]==changed[114]);++deltaCount;}
        Check(deltaCount==1);
        Check(new System.Collections.Generic.List<byte[]>(Wire.ChangedFrame(rgb,rgb)).Count==0);
        ProtocolTests.Run();
        byte[] snapshot=new byte[220]; Buffer.BlockCopy(new byte[]{83,76,66,49,1},0,snapshot,0,5);
        snapshot[6]=3; snapshot[8]=0xf1; snapshot[9]=0x12;
        Buffer.BlockCopy(rgb,0,snapshot,16,204);
        bool active; uint generation;
        byte[] result=Wire.SnapshotRgb(snapshot,out active,out generation);
        Check(active && generation==0x12f1);
        for (int i=0;i<204;i++) Check(result[i]==rgb[i]);
        foreach(byte flags in new byte[]{0,1,2}) { snapshot[6]=flags; Wire.SnapshotRgb(snapshot,out active,out generation); Check(!active); }
        snapshot[4]=2; Reject(delegate { Wire.SnapshotRgb(snapshot,out active,out generation); });
        Check(Marshal.SizeOf(typeof(Native.InterfaceData))==(IntPtr.Size==8?32:28));
        Check(Marshal.SizeOf(typeof(Native.Attributes))==12);
        Check(Marshal.SizeOf(typeof(Native.Caps))==64);
        Console.WriteLine("Client: handshake guards, exact 68-lamp frames, stale/error ACK rejection, ownership snapshots and x64 P/Invoke sizes passed.");
        return 0;
    }
}
