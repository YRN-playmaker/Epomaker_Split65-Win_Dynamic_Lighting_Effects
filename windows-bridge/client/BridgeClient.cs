// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Split65Bridge {
public static class Wire {
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    public static long Now { get { return Clock.ElapsedMilliseconds; } }
    public const uint AttachIoctl = 0x0022E000, SnapshotIoctl = 0x0022E004;
    public static byte[] Packet(byte command) { byte[] p = new byte[33]; p[1] = command; return p; }
    public static void ValidateHello(byte[] p) {
        if(p.Length==33 && p[1]==0x30 && p[2]==68 && p[3]==76 && p[4]==50 && (p[5]==0x48 || p[5]==0x49))
            throw new IOException("Firmware update required: flash the left half with 0.5.0 before streaming RGB. Older firmware can delay key releases under lighting traffic.");
        if (p.Length != 33 || p[0] != 0 || p[1] != 0x30 || p[2] != 68 || p[3] != 76 || p[4] != 50 ||
            p[5] != 0x50 || p[9] != 68 || p[27] != 1)
            throw new IOException("Receiver is not an active Split65 v0.5.0 2.4G connection.");
        if (p[22] != 6) throw new ModeChangedException("Keyboard left 2.4G mode; removing the virtual lighting device.");
        if (p[30] != 2) throw new RadioTimeoutException("Keyboard radio is temporarily not connected.");
    }
    public static byte[] SnapshotRgb(byte[] snapshot, out bool active, out uint generation) {
        if (snapshot.Length != 220 || snapshot[0] != 83 || snapshot[1] != 76 || snapshot[2] != 66 || snapshot[3] != 49 || snapshot[4] != 1)
            throw new IOException("Invalid virtual LampArray snapshot.");
        active = (snapshot[6] & 3) == 3;
        generation = BitConverter.ToUInt32(snapshot, 8);
        byte[] rgb = new byte[204]; Buffer.BlockCopy(snapshot, 16, rgb, 0, rgb.Length); return rgb;
    }
    public static IEnumerable<byte[]> Frame(byte[] rgb) {
        return ChangedFrame(rgb, null);
    }
    public static IEnumerable<byte[]> ChangedFrame(byte[] rgb, byte[] previous) {
        if (rgb.Length != 204) throw new ArgumentException("Expected 68 RGB lamps.");
        if (previous != null && previous.Length != 204) throw new ArgumentException("Expected a 68-lamp previous frame.");
        for (int first = 0; first < 68; first += 9) {
            int count = Math.Min(9, 68 - first);
            int start=first, end=first+count-1;
            if(previous!=null) {
                while(start<=end && SameLamp(rgb,previous,start))++start;
                while(end>=start && SameLamp(rgb,previous,end))--end;
                if(start>end)continue;
            }
            byte[] p = Packet(0x24); p[2] = (byte)start; p[3] = (byte)(end-start+1);
            Buffer.BlockCopy(rgb, start * 3, p, 4, p[3] * 3); yield return p;
        }
    }
    static bool SameLamp(byte[] a, byte[] b, int lamp) {int i=lamp*3;return a[i]==b[i] && a[i+1]==b[i+1] && a[i+2]==b[i+2];}
    public static bool IsResponseTo(byte[] request, byte[] response) {
        if (request.Length != 33 || response.Length != 33 || response[0] != 0 || response[1] != request[1]) return false;
        if ((request[1] == 0x24 || request[1] == 0x25) && response[2] == 253)
            throw new RadioTimeoutException("Keyboard temporarily rejected RGB; rechecking mode before detaching.");
        if (request[1] == 0x30) return true;
        for (int i = 0; i < request.Length; ++i) if (response[i] != request[i]) return false;
        return true;
    }
}
public static class Native {
    public static readonly Guid HidGuid = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");
    public static readonly Guid BridgeGuid = new Guid("36b26c21-62c2-4eb9-b86b-b6ed44af6ba1");
    [StructLayout(LayoutKind.Sequential)] public struct InterfaceData { public uint Size; public Guid Class; public uint Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] public struct Attributes { public uint Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)] public struct Caps {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort Links, InputButtons, InputValues, InputIndices, OutputButtons, OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint needed, IntPtr device);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, uint inputSize, byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
    [DllImport("hid.dll", SetLastError=true)] static extern byte HidD_GetAttributes(SafeFileHandle file, ref Attributes a);
    [DllImport("hid.dll", SetLastError=true)] static extern byte HidD_GetPreparsedData(SafeFileHandle file, out IntPtr data);
    [DllImport("hid.dll")] static extern byte HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    [DllImport("hid.dll", CharSet=CharSet.Unicode)] static extern byte HidD_GetProductString(SafeFileHandle file, byte[] text, uint size);
    public static IEnumerable<string> Paths(Guid guid) {
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        try {
            for (uint i=0; ; ++i) {
                InterfaceData d = new InterfaceData(); d.Size = (uint)Marshal.SizeOf(typeof(InterfaceData));
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref d)) {
                    int error = Marshal.GetLastWin32Error(); if (error == 259) break; throw new Win32Exception(error);
                }
                uint size; SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out size, IntPtr.Zero);
                if (size < 8 || size > 65536) continue;
                IntPtr buffer = Marshal.AllocHGlobal((int)size);
                try {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref d, buffer, size, out size, IntPtr.Zero)) throw new Win32Exception();
                    yield return Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
                } finally { Marshal.FreeHGlobal(buffer); }
            }
        } finally { SetupDiDestroyDeviceInfoList(set); }
    }
    public static bool IsReceiverCandidate(string path) {
        using (SafeFileHandle handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
            if (handle.IsInvalid) return false;
            Attributes a = new Attributes(); a.Size = (uint)Marshal.SizeOf(typeof(Attributes));
            if (HidD_GetAttributes(handle, ref a) == 0 || a.Vendor != 0x342D || (a.Product != 0xE4C6)) return false;
            byte[] product = new byte[512];
            string name = HidD_GetProductString(handle, product, (uint)product.Length) != 0 ? System.Text.Encoding.Unicode.GetString(product).TrimEnd('\0') : "";
            // Do not accidentally use a direct keyboard USB link as a radio test.
            if (a.Version >= 0x40 && a.Version <= 0x50 && name.IndexOf("dongle", StringComparison.OrdinalIgnoreCase) < 0) return false;
            IntPtr preparsed;
            if (HidD_GetPreparsedData(handle, out preparsed) == 0) return false;
            try { Caps c; return HidP_GetCaps(preparsed, out c) >= 0 && c.UsagePage == 0xFF60 && c.Usage == 0x61 && c.InputLength == 33 && c.OutputLength == 33; }
            finally { HidD_FreePreparsedData(preparsed); }
        }
    }
    public static bool WiredLampArrayPresent() {
        foreach(string path in Paths(HidGuid)) {
            using(SafeFileHandle handle=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
                if(handle.IsInvalid)continue;
                Attributes a=new Attributes();a.Size=(uint)Marshal.SizeOf(typeof(Attributes));
                if(HidD_GetAttributes(handle,ref a)==0 || a.Vendor!=0x342D || (a.Product!=0xE4C6 && a.Product!=0xE4C7))continue;
                IntPtr data;if(HidD_GetPreparsedData(handle,out data)==0)continue;
                try{Caps c;if(HidP_GetCaps(data,out c)>=0 && c.UsagePage==0x59 && c.Usage==1)return true;}
                finally{HidD_FreePreparsedData(data);}
            }
        }
        return false;
    }
    public static byte[] Ioctl(SafeFileHandle handle, uint code, byte[] input, int outputSize) {
        byte[] output = new byte[outputSize]; uint returned;
        if (!DeviceIoControl(handle, code, input, (uint)input.Length, output, (uint)output.Length, out returned, IntPtr.Zero)) throw new Win32Exception();
        if (returned != output.Length) throw new IOException("Unexpected bridge response size.");
        return output;
    }
}
public sealed class HidReportIo:IReportIo {
    readonly SafeFileHandle handle;readonly FileStream stream;
    Task<int> pendingRead;byte[] pendingBuffer;
    public HidReportIo(string path) {
        handle=Native.CreateFile(path,0xC0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
        if(handle.IsInvalid){int error=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(error);}
        stream=new FileStream(handle,FileAccess.ReadWrite,1,true);
    }
    public void Write(byte[] packet) {
        if(packet.Length!=33 || packet[0]!=0)throw new ArgumentException("Expected a 33-byte Windows HID report.");
        try {
            Task task=stream.WriteAsync(packet,0,packet.Length);
            if(!task.Wait(1000)) {Native.CancelIoEx(handle,IntPtr.Zero);throw new IOException("Raw HID write timed out.");}
            task.GetAwaiter().GetResult();
        }catch(AggregateException error){throw new IOException("Raw HID write failed.",error.GetBaseException());}
    }
    public byte[] Read(int timeoutMs) {
        if(pendingRead==null){pendingBuffer=new byte[33];pendingRead=stream.ReadAsync(pendingBuffer,0,33);}
        try {
            if(!pendingRead.Wait(timeoutMs))return null;
            int length=pendingRead.GetAwaiter().GetResult();byte[] result=pendingBuffer;
            pendingRead=null;pendingBuffer=null;
            if(length!=33)throw new IOException("Unexpected Raw HID input report length.");
            return result;
        }catch(AggregateException error){throw new IOException("Raw HID read failed.",error.GetBaseException());}
    }
    public void Dispose() {
        Native.CancelIoEx(handle,IntPtr.Zero);
        // Drain cancellation before releasing the buffer/handle. A soft read
        // deadline never cancels or abandons the still-pending ReadAsync.
        if(pendingRead!=null)try{pendingRead.Wait(500);}catch(AggregateException){}
        stream.Dispose();handle.Dispose();
    }
}
public sealed class Radio:IDisposable {
    readonly IReportIo io;readonly RadioProtocol protocol;
    public Radio(string path) {
        io=new HidReportIo(path);protocol=new RadioProtocol(io,()=>Wire.Now);
        try{CheckMode();}catch{Dispose();throw;}
    }
    public void CheckMode(){Wire.ValidateHello(protocol.Send(Wire.Packet(0x30)));}
    public int Frame(byte[] rgb,byte[] previous){return protocol.Frame(rgb,previous);}
    public void Renew(){protocol.Send(Wire.Packet(0x25));}
    public void Release(){protocol.Release();}
    public void Dispose(){io.Dispose();}
}
public static class Program {
    static volatile bool stop;
    static EventWaitHandle stopEvent;
    static bool Stopping { get { return stop || (stopEvent != null && stopEvent.WaitOne(0)); } }
    static void Log(string text) { Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "  " + text); }
    public static int Main(string[] args) {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) { Console.Error.WriteLine("Windows only."); return 2; }
        try {
            if (args.Length == 1 && args[0] == "--install-root") return RootDevice.Install();
            if (args.Length == 1 && args[0] == "--remove-root") return RootDevice.Remove();
            if (args.Length == 1 && args[0] == "--root-status") return RootDevice.Status();
            if (args.Length == 1 && args[0] == "--probe-driver") return DriverAccess.Probe();
            if (args.Length == 2 && args[0] == "--bind-root") return RootDevice.Bind(args[1]);
            if (args.Length == 1 && args[0] == "--stop") {
                try { using (EventWaitHandle signal = EventWaitHandle.OpenExisting("Local\\Split65LampArrayBridgeStop")) signal.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                return 0;
            }
            if (args.Length != 0) throw new ArgumentException("Unknown bridge option.");
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\Split65LampArrayBridge", out created)) {
                if (!created) throw new IOException("Another bridge instance is already running.");
                stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\Split65LampArrayBridgeStop");
                stopEvent.Reset();
                Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e) { e.Cancel = true; stop = true; };
                Console.WriteLine("Split65 wireless LampArray bridge 0.5.0 candidate. Ctrl+C stops and releases RGB.");
                Console.WriteLine("Keep OpenRGB/Hub and the browser RGB test closed. No keyboard reflashing required.");
                while (!Stopping) {
                    try { ConnectAndRun(); }
                    catch (Exception error) { Log(error.Message); }
                    if (!Stopping) Thread.Sleep(2000);
                }
                stopEvent.Dispose(); stopEvent = null;
                mutex.ReleaseMutex();
            }
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error.GetBaseException().Message); Win32Exception native=error.GetBaseException() as Win32Exception; if(native!=null)Console.Error.WriteLine("Win32 error="+native.NativeErrorCode+"; HRESULT=0x"+native.HResult.ToString("X8")); return 1; }
    }
    static void ConnectAndRun() {
        if(Native.WiredLampArrayPresent())throw new ModeChangedException("Wired LampArray active. Wireless virtual lighting stays detached.");
        string bridgePath = null;
        foreach (string path in Native.Paths(Native.BridgeGuid)) { bridgePath = path; break; }
        if (bridgePath == null) throw new IOException("Driver control interface missing. Use the Companion repair action; root registration alone does not prove the UMDF driver loaded.");
        Radio radio = null;
        foreach (string path in Native.Paths(Native.HidGuid)) {
            if (!Native.IsReceiverCandidate(path)) continue;
            try { radio = new Radio(path); break; } catch (Exception error) { Log("Receiver candidate: " + error.GetBaseException().Message); }
        }
        if (radio == null) throw new IOException("No acknowledged Split65 v0.5.0 receiver in 2.4G mode.");
        using (radio)
        using (SafeFileHandle bridge = Native.CreateFile(bridgePath, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
            if (bridge.IsInvalid) throw new Win32Exception();
            bool active=false,attached=false;uint lastGeneration=UInt32.MaxValue;
            long lastFrame=-10000,lastRenew=-10000,lastStatus=-10000,lastPresence=-10000;
            byte[] confirmed=null;RadioRecovery recovery=new RadioRecovery();
            string detachReason="worker stopped";
            try {
                Native.Ioctl(bridge,Wire.AttachIoctl,BitConverter.GetBytes(1u),0);attached=true;
                Log("Radio handshake OK. Virtual LampArray created; open Windows Dynamic Lighting.");
                while(!Stopping) {
                    long now=Wire.Now;
                    // Presence and mode checks are independent of Windows RGB
                    // ownership and of ACK/keepalive timestamps.
                    if(now-lastPresence>=500) {
                        lastPresence=now;
                        if(Native.WiredLampArrayPresent())throw new ModeChangedException("Wired LampArray active; detaching the wireless virtual device.");
                    }
                    if(!recovery.Ready(now)){Thread.Sleep(25);continue;}
                    try {
                        if(now-lastStatus>=500){radio.CheckMode();lastStatus=Wire.Now;}
                        bool owned;uint generation;
                        byte[] rgb=Wire.SnapshotRgb(Native.Ioctl(bridge,Wire.SnapshotIoctl,new byte[0],220),out owned,out generation);
                        if(owned && (!active || (generation!=lastGeneration && now-lastFrame>=100))) {
                            long sentAt=Wire.Now;int packets=radio.Frame(rgb,active?confirmed:null);
                            confirmed=rgb;lastGeneration=generation;lastFrame=sentAt;
                            if(packets>0)lastRenew=Wire.Now;
                            if(!active)Log("Windows lighting acquired; sending RGB over 2.4G.");active=true;
                            if(Wire.Now-sentAt>500)Log("Slow radio frame: "+(Wire.Now-sentAt)+" ms, "+packets+" packets.");
                        } else if(!owned && active) {
                            radio.Release();active=false;confirmed=null;lastRenew=Wire.Now;
                            Log("Windows released lighting; native effects restored.");
                        } else if(owned && active && now-lastRenew>=1000) {
                            radio.Renew();lastRenew=Wire.Now;
                        }
                        recovery.Good();
                    }catch(RadioTimeoutException error) {
                        // A few lost ACKs must not become Windows USB removals.
                        // Do not mark an unacknowledged frame as delivered.
                        active=false;confirmed=null;lastStatus=-10000;
                        int delay=recovery.Miss(Wire.Now);
                        Log("Transient radio timeout; virtual device retained; retry in "+delay+" ms. "+error.Message);
                    }
                    Thread.Sleep(10);
                }
            } catch(Exception error) {
                detachReason=error.Message;throw;
            } finally {
                // Remove the virtual child before touching a failed physical link.
                if (attached) {
                    Log("Detaching virtual LampArray: "+detachReason);
                    try { Native.Ioctl(bridge, Wire.AttachIoctl, BitConverter.GetBytes(0u), 0); } catch { }
                    try { radio.Release(); } catch { }
                }
            }
        }
    }
}
}
