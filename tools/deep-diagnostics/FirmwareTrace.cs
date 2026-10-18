// Read metadata only: attributes requests (0x02 or 0x21) select a lamp, never set colors.
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

public sealed class Split65HidProbe {
    public readonly ConcurrentQueue<string> Lines = new ConcurrentQueue<string>();
    public readonly ConcurrentQueue<string> RawPaths = new ConcurrentQueue<string>();
    static string ErrorDetail(Exception error) {
        string result = "";
        for (Exception e = error; e != null; e = e.InnerException) {
            if (result.Length != 0) result += " -> ";
            result += e.GetType().Name + " HRESULT=0x" + e.HResult.ToString("X8") + ": " + e.Message;
        }
        return result;
    }
    public Task Work { get; private set; }

    [StructLayout(LayoutKind.Sequential)]
    struct Caps {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort Links, InputButtons, InputValues, InputIndices;
        public ushort OutputButtons, OutputValues, OutputIndices;
        public ushort FeatureButtons, FeatureValues, FeatureIndices;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Attributes { public uint Size; public ushort Vendor, Product, Version; }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("hid.dll", SetLastError=true)]
    static extern byte HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")]
    static extern byte HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")]
    static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    [DllImport("hid.dll", SetLastError=true)]
    static extern byte HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll", SetLastError=true)]
    static extern byte HidD_GetFeature(SafeFileHandle handle, [In,Out] byte[] data, uint size);
    [DllImport("hid.dll", SetLastError=true)]
    static extern byte HidD_SetFeature(SafeFileHandle handle, [In] byte[] data, uint size);
    [DllImport("hid.dll")]
    static extern int HidP_GetUsageValue(int type, ushort page, ushort link, ushort usage, out uint value, IntPtr parsed, byte[] report, uint size);
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);

    public void Start(string path) {
        Work = Task.Factory.StartNew(delegate {
            try { Read(path, true); }
            catch (Exception e) { Lines.Enqueue("HID probe failed: " + e.GetType().Name + ": " + e.Message); }
        });
    }
    public void StartInventory(string[] paths) {
        Work = Task.Factory.StartNew(delegate {
            foreach (string path in paths) {
                Lines.Enqueue("HID path=" + path);
                try { Read(path, false); }
                catch (Exception e) { Lines.Enqueue("Inventory: " + e.GetType().Name + ": " + e.Message); }
            }
        });
    }
    public void StartRaw(string path) {
        Work = Task.Factory.StartNew(delegate {
            try { Read(path, false); }
            catch (Exception e) { Lines.Enqueue("Raw diagnostics: " + ErrorDetail(e)); }
        });
    }
    static void AwaitRaw(Task task, Stream stream, string stage) {
        try { if (task.Wait(2000)) return; }
        catch (Exception e) { throw new IOException("Raw HID " + stage + " failed", e.GetBaseException()); }
        FileStream file = stream as FileStream;
        if (file != null) CancelIoEx(file.SafeFileHandle, IntPtr.Zero);
        throw new TimeoutException("Raw HID " + stage + " timed out");
    }
    byte[] RawQuery(Stream stream, byte command, byte operation, byte index) {
        byte[] request = new byte[33], response = new byte[33];
        request[1] = command; request[2] = operation; request[3] = index;
        // Production FileStream uses bufferSize=1, so 33-byte HID writes bypass
        // buffering. Do not call FlushAsync: that invokes FlushFileBuffers on
        // Windows device handles, which can reject that disk-oriented request.
        AwaitRaw(stream.WriteAsync(request, 0, request.Length), stream, "write");
        Task<int> read = stream.ReadAsync(response, 0, response.Length);
        AwaitRaw(read, stream, "read after unbuffered output");
        if (read.Result != 33 || response[0] != 0 || response[1] != command)
            throw new InvalidOperationException("Unexpected Raw HID reply: " + BitConverter.ToString(response,0,Math.Max(0,read.Result)));
        byte[] payload = new byte[32]; Array.Copy(response, 1, payload, 0, payload.Length);
        return payload;
    }
    void ReadRawDiagnostics(string path) {
        // Only called after bcdDevice/capabilities identify our 0.4.2..0.4.7 Raw
        // interface. These commands only read RAM; never send trace-clear,
        // keymap, RGB, EEPROM or bootloader commands.
        using (SafeFileHandle handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero)) {
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Open Raw HID diagnostics");
            using (FileStream stream = new FileStream(handle, FileAccess.ReadWrite, 1, true)) {
                byte[] data = RawQuery(stream, 0x30, 0, 0);
                if (data[1] != 'D' || data[2] != 'L' || data[3] != '2')
                    throw new InvalidOperationException("Missing DL2 firmware diagnostic marker");
                Lines.Enqueue(String.Format("Firmware DL2 revision=0x{0:X2}; left={1}; master={2}; peer protocol=0x{3:X2}; lamps={4}; connection mode={5}", data[4], (data[6]&2)!=0, (data[6]&1)!=0, data[7], data[8], data[21]));
                Lines.Enqueue(String.Format("Split RPC success={0}; failure={1}; peer left={2}; retry interval={3} ms", BitConverter.ToUInt32(data,10), BitConverter.ToUInt32(data,14), data[24]!=0, data[25]*10));
                data = RawQuery(stream, 0x31, 0, 0);
                if (data[2] != 'T' || data[3] != 'R') throw new InvalidOperationException("Missing USB trace marker");
                Lines.Enqueue(String.Format("USB trace: total={0}; array GET={1}; lamp GET={2}; attributes SET parsed={3}; rejected={4}; cursor={5}", BitConverter.ToUInt32(data,8), BitConverter.ToUInt32(data,12), BitConverter.ToUInt32(data,16), BitConverter.ToUInt32(data,20), BitConverter.ToUInt32(data,24), data[28]));
                int count = Math.Min((int)data[5],32);
                bool detailedTrace = data[6] >= 2;
                Lines.Enqueue("Stages: 1=GET armed, 2=SET armed, 3=SET parsed, 4=SET failed, 5=idle, 6=GET driver IN completed (0x44+)");
                for (int i=0; i<count; ++i) {
                    byte[] item = RawQuery(stream, 0x31, 1, (byte)i);
                    if (item[3] == 0) break;
                    Lines.Enqueue(String.Format("  trace[{0}]: tick={1}; type=0x{2:X2}; request=0x{3:X2}; value=0x{4:X4}; interface={5}; length={6}; stage={7}; received={8}", i, BitConverter.ToUInt16(item,4), item[12], item[13], BitConverter.ToUInt16(item,6), BitConverter.ToUInt16(item,8), BitConverter.ToUInt16(item,10), item[14], item[15]));
                    if (detailedTrace) Lines.Enqueue(String.Format("    lamp={0}; report={1}; bytes={2}", BitConverter.ToUInt16(item,20), item[22], item[23]));
                }
            }
        }
    }
    byte[] Get(SafeFileHandle handle, int length, byte id) {
        byte[] report = new byte[length]; report[0] = id;
        if (HidD_GetFeature(handle, report, (uint)report.Length) == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFeature 0x" + id.ToString("X2"));
        if (report[0] != id) throw new InvalidOperationException("Response report ID changed");
        return report;
    }
    void Select(SafeFileHandle handle, int length, byte requestId, ushort lamp) {
        byte[] report = new byte[length]; report[0] = requestId; report[1] = (byte)lamp; report[2] = (byte)(lamp >> 8);
        if (HidD_SetFeature(handle, report, (uint)report.Length) == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Lamp attributes request");
    }
    void ParsedValue(IntPtr parsed, byte[] report, ushort usage) {
        uint value;
        int status = HidP_GetUsageValue(2, 0x59, 0, usage, out value, parsed, report, (uint)report.Length);
        Lines.Enqueue(String.Format("HidP Feature usage 0x{0:X2}: status=0x{1:X8}; value={2}", usage, status, value));
    }
    void Read(string path, bool metadata) {
        // Zero desired access is sufficient for HID feature queries. Share
        // read/write so this diagnostic never requests exclusive ownership.
        using (SafeFileHandle handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Open HID collection");
            Attributes attributes = new Attributes(); attributes.Size = (uint)Marshal.SizeOf(typeof(Attributes));
            if (HidD_GetAttributes(handle, ref attributes) == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_GetAttributes");
            Lines.Enqueue(String.Format("HID hardware={0:X4}:{1:X4}; bcdDevice=0x{2:X4}", attributes.Vendor, attributes.Product, attributes.Version));
            if (attributes.Vendor != 0x342D || (attributes.Product != 0xE4C6 && attributes.Product != 0xE4C7)) throw new InvalidOperationException("Not a Split65 collection");
            IntPtr parsed;
            if (HidD_GetPreparsedData(handle, out parsed) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                Caps caps;
                int status = HidP_GetCaps(parsed, out caps);
                if (status != 0x110000) throw new InvalidOperationException("HidP_GetCaps status=0x" + status.ToString("X8"));
                Lines.Enqueue(String.Format("Caps page/usage={0:X4}:{1:X4}; input/output/feature bytes={2}/{3}/{4}; feature value caps={5}; links={6}", caps.UsagePage, caps.Usage, caps.InputLength, caps.OutputLength, caps.FeatureLength, caps.FeatureValues, caps.Links));
                if (!metadata) {
                    if ((attributes.Version == 0x42 || attributes.Version == 0x43 || attributes.Version == 0x44 || attributes.Version == 0x45 || attributes.Version == 0x46 || attributes.Version == 0x47) && caps.UsagePage == 0xFF60 && caps.Usage == 0x61 && caps.InputLength == 33 && caps.OutputLength == 33) {
                        RawPaths.Enqueue(path);
                        try { ReadRawDiagnostics(path); }
                        catch (Exception e) { Lines.Enqueue("Raw diagnostics: " + ErrorDetail(e)); }
                    }
                    return;
                }
                if (caps.UsagePage != 0x59 || caps.Usage != 1 || caps.FeatureLength < 51 || caps.FeatureLength > 4096)
                    throw new InvalidOperationException("Unexpected LampArray collection capabilities");
                byte firstReport = attributes.Version >= 0x42 && attributes.Version <= 0x45 ? (byte)1 : (byte)0x20;
                Lines.Enqueue("Metadata report IDs=" + firstReport + ".." + (firstReport+5));
                byte[] array = Get(handle, caps.FeatureLength, firstReport);
                int count = BitConverter.ToUInt16(array, 1);
                Lines.Enqueue("Array bytes=" + BitConverter.ToString(array, 0, 23));
                Lines.Enqueue(String.Format("Array count={0}; bounds={1}/{2}/{3} um; kind={4}; interval={5} us", count, BitConverter.ToUInt32(array,3), BitConverter.ToUInt32(array,7), BitConverter.ToUInt32(array,11), BitConverter.ToUInt32(array,15), BitConverter.ToUInt32(array,19)));
                for (ushort usage = 3; usage <= 8; ++usage) ParsedValue(parsed, array, usage);
                if (count != 68) throw new InvalidOperationException("Expected 68 lamps; stop before requesting unexpected IDs");
                int matches = 0;
                for (ushort i = 0; i < count; ++i) {
                    Select(handle, caps.FeatureLength, (byte)(firstReport+1), i);
                    byte[] lamp = Get(handle, caps.FeatureLength, (byte)(firstReport+2));
                    int returned = BitConverter.ToUInt16(lamp, 1);
                    if (returned == i) ++matches;
                    Lines.Enqueue(String.Format("Lamp request={0}, returned={1}; xyz={2}/{3}/{4}; latency={5}; purposes={6}; levels={7}/{8}/{9}/{10}; programmable={11}; binding={12}", i, returned, BitConverter.ToUInt32(lamp,3), BitConverter.ToUInt32(lamp,7), BitConverter.ToUInt32(lamp,11), BitConverter.ToUInt32(lamp,15), BitConverter.ToUInt32(lamp,19), lamp[23],lamp[24],lamp[25],lamp[26],lamp[27],lamp[28]));
                    if (i == 0) { ParsedValue(parsed, lamp, 0x21); ParsedValue(parsed, lamp, 0x2C); }
                }
                Lines.Enqueue(String.Format("HID metadata read complete: matching lamp IDs={0}/{1}. Concurrent metadata readers can alter the shared lamp cursor.", matches, count));
            } finally { HidD_FreePreparsedData(parsed); }
        }
    }
}
