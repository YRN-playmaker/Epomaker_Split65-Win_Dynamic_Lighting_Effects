// Split65 hardware diagnostics. Metadata selectors only: no RGB/EEPROM/DFU writes.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class DeepHid {
    [StructLayout(LayoutKind.Sequential)] public struct Attributes {public uint Size;public ushort Vendor,Product,Version;}
    [StructLayout(LayoutKind.Sequential)] public struct Caps {
        public ushort Usage,UsagePage,InputLength,OutputLength,FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=17)] public ushort[] Reserved;
        public ushort Links,InputButtons,InputValues,InputIndices,OutputButtons,OutputValues,OutputIndices,FeatureButtons,FeatureValues,FeatureIndices;
    }
    [StructLayout(LayoutKind.Sequential)] public struct ValueCap {
        public ushort Page;public byte ReportId,Alias;public ushort BitField,Link,LinkUsage,LinkPage;
        public byte IsRange,IsStringRange,IsDesignatorRange,IsAbsolute,HasNull,Reserved;
        public ushort BitSize,ReportCount;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=5)] public ushort[] Reserved2;
        public uint UnitExponent,Units;public int LogicalMin,LogicalMax,PhysicalMin,PhysicalMax;
        public ushort UsageMin,UsageMax,StringMin,StringMax,DesignatorMin,DesignatorMax,DataIndexMin,DataIndexMax;
    }
    [StructLayout(LayoutKind.Sequential)] public struct LinkNode {public ushort Usage,Page,Parent,Children,Sibling,FirstChild;public uint Flags;public IntPtr UserContext;}
    [StructLayout(LayoutKind.Sequential)] struct InterfaceData {public uint Size;public Guid Class;public uint Flags;public IntPtr Reserved;}
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid,IntPtr enumerator,IntPtr parent,uint flags);
    [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set,IntPtr info,ref Guid guid,uint index,ref InterfaceData data);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set,ref InterfaceData data,IntPtr detail,uint size,out uint required,IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("hid.dll",SetLastError=true)] static extern byte HidD_GetAttributes(SafeFileHandle h,ref Attributes attributes);
    [DllImport("hid.dll",SetLastError=true)] static extern byte HidD_GetPreparsedData(SafeFileHandle h,out IntPtr data);
    [DllImport("hid.dll")] static extern byte HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,out Caps caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type,IntPtr caps,ref ushort count,IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetLinkCollectionNodes(IntPtr nodes,ref uint count,IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int type,ushort page,ushort link,ushort usage,out uint value,IntPtr data,byte[] report,uint length);
    [DllImport("hid.dll",SetLastError=true)] static extern byte HidD_GetFeature(SafeFileHandle h,[In,Out] byte[] data,uint length);
    [DllImport("hid.dll",SetLastError=true)] static extern byte HidD_SetFeature(SafeFileHandle h,byte[] data,uint length);
    public static void Log(string message) {Console.WriteLine(DateTime.UtcNow.ToString("o")+"\t"+message);Console.Out.Flush();}
    public static string[] Paths() {
        Guid guid;HidD_GetHidGuid(out guid);IntPtr set=SetupDiGetClassDevs(ref guid,IntPtr.Zero,IntPtr.Zero,18);
        if(set==new IntPtr(-1))throw new Win32Exception(Marshal.GetLastWin32Error());
        var paths=new List<string>();
        try {for(uint i=0;;i++) {
            var data=new InterfaceData();data.Size=(uint)Marshal.SizeOf(typeof(InterfaceData));
            if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,i,ref data)) {int error=Marshal.GetLastWin32Error();if(error==259)break;throw new Win32Exception(error);}
            uint needed;SetupDiGetDeviceInterfaceDetail(set,ref data,IntPtr.Zero,0,out needed,IntPtr.Zero);
            if(needed<6||needed>65536)throw new InvalidOperationException("Invalid HID path size");
            IntPtr detail=Marshal.AllocHGlobal((int)needed);
            try {Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);if(!SetupDiGetDeviceInterfaceDetail(set,ref data,detail,needed,out needed,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
                string path=Marshal.PtrToStringUni(IntPtr.Add(detail,4));
                if(path.IndexOf("vid_342d&pid_e4c6",StringComparison.OrdinalIgnoreCase)>=0||path.IndexOf("vid_342d&pid_e4c7",StringComparison.OrdinalIgnoreCase)>=0)paths.Add(path);
            } finally {Marshal.FreeHGlobal(detail);}
        }} finally {SetupDiDestroyDeviceInfoList(set);}
        return paths.ToArray();
    }
    static List<ValueCap> Values(IntPtr parsed,ushort count) {
        var result=new List<ValueCap>();if(count==0)return result;if(count>4096)throw new InvalidOperationException("Unbounded caps count");
        int size=Marshal.SizeOf(typeof(ValueCap));IntPtr buffer=Marshal.AllocHGlobal(size*count);
        try {int status=HidP_GetValueCaps(2,buffer,ref count,parsed);if(status!=0x110000)throw new InvalidOperationException("HidP_GetValueCaps=0x"+status.ToString("X8"));
            for(int i=0;i<count;i++)result.Add((ValueCap)Marshal.PtrToStructure(IntPtr.Add(buffer,i*size),typeof(ValueCap)));
        } finally {Marshal.FreeHGlobal(buffer);}return result;
    }
    public static string Kind(string path) {
        using(var h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
            if(h.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr p;if(HidD_GetPreparsedData(h,out p)==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            try {Caps c;if(HidP_GetCaps(p,out c)!=0x110000)throw new InvalidOperationException("HidP_GetCaps failed");
                if(c.UsagePage==0x59&&c.Usage==1)return "LampArray";
                if(c.UsagePage==0xFF60&&c.Usage==0x61)return "Raw";
                return "Other";
            }finally{HidD_FreePreparsedData(p);}
        }
    }
    public static void Inventory(string path) {
        using(var h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
            if(h.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error(),"Open HID");
            var a=new Attributes();a.Size=(uint)Marshal.SizeOf(typeof(Attributes));if(HidD_GetAttributes(h,ref a)==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            Log(String.Format("IDENTITY\t{0:X4}:{1:X4}\tbcd={2:X4}\t{3}",a.Vendor,a.Product,a.Version,path));
            IntPtr parsed;if(HidD_GetPreparsedData(h,out parsed)==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            try {Caps c;int status=HidP_GetCaps(parsed,out c);if(status!=0x110000)throw new InvalidOperationException("HidP_GetCaps=0x"+status.ToString("X8"));
                Log(String.Format("CAPS\tpage={0:X4}\tusage={1:X4}\tinput={2}\toutput={3}\tfeature={4}\tlinks={5}\tfeatureButtons={6}\tfeatureValues={7}",c.UsagePage,c.Usage,c.InputLength,c.OutputLength,c.FeatureLength,c.Links,c.FeatureButtons,c.FeatureValues));
                foreach(var v in Values(parsed,c.FeatureValues))Log(String.Format("VALUECAP\tID={0:X2}\tpage={1:X4}\tusage={2:X4}..{3:X4}\tbits={4}\tcount={5}\tlogical={6}..{7}\tlink={8}\tflags={9:X4}\tunits={10:X8}\texponent={11:X8}",v.ReportId,v.Page,v.UsageMin,v.IsRange!=0?v.UsageMax:v.UsageMin,v.BitSize,v.ReportCount,v.LogicalMin,v.LogicalMax,v.Link,v.BitField,v.Units,v.UnitExponent));
                if(c.Links>0&&c.Links<4096) {uint count=c.Links;int size=Marshal.SizeOf(typeof(LinkNode));IntPtr buffer=Marshal.AllocHGlobal(size*(int)count);
                    try {status=HidP_GetLinkCollectionNodes(buffer,ref count,parsed);Log("LINK_STATUS\t0x"+status.ToString("X8"));
                        if(status==0x110000)for(int i=0;i<count;i++) {var n=(LinkNode)Marshal.PtrToStructure(IntPtr.Add(buffer,i*size),typeof(LinkNode));Log(String.Format("LINK\t{0}\tpage={1:X4}\tusage={2:X4}\tparent={3}\tchildren={4}\tfirst={5}\tsibling={6}\tflags={7:X8}",i,n.Page,n.Usage,n.Parent,n.Children,n.FirstChild,n.Sibling,n.Flags));}
                    } finally {Marshal.FreeHGlobal(buffer);}}
            } finally {HidD_FreePreparsedData(parsed);}
        }
    }
    public interface IReports {byte[] Get(byte id);void Select(byte id,ushort lamp);}
    sealed class HardwareReports:IReports {
        readonly SafeFileHandle handle;readonly int length;int sequence;
        public HardwareReports(SafeFileHandle h,int n){handle=h;length=n;}
        public byte[] Get(byte id){byte[] report=new byte[length];report[0]=id;Log("BEGIN_GET\tseq="+(++sequence)+"\tid="+id.ToString("X2")+"\tlength="+length);var clock=Stopwatch.StartNew();byte ok=HidD_GetFeature(handle,report,(uint)length);int error=ok!=0?0:Marshal.GetLastWin32Error();
            Log("END_GET\tseq="+sequence+"\tok="+ok+"\twin32="+error+"\tms="+clock.Elapsed.TotalMilliseconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"\thex="+BitConverter.ToString(report));
            if(ok==0)throw new Win32Exception(error,"GetFeature");if(report[0]!=id)throw new InvalidOperationException("Unexpected report ID");return report;}
        public void Select(byte id,ushort lamp){byte[] report=new byte[length];report[0]=id;report[1]=(byte)lamp;report[2]=(byte)(lamp>>8);Log("BEGIN_SELECT\tseq="+(++sequence)+"\tlamp="+lamp+"\tid="+id.ToString("X2"));var clock=Stopwatch.StartNew();byte ok=HidD_SetFeature(handle,report,(uint)length);int error=ok!=0?0:Marshal.GetLastWin32Error();
            Log("END_SELECT\tseq="+sequence+"\tok="+ok+"\twin32="+error+"\tms="+clock.Elapsed.TotalMilliseconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));if(ok==0)throw new Win32Exception(error,"Set metadata selector");}
    }
    static ValueCap Field(List<ValueCap> values,byte id,ushort usage,int bits) {
        foreach(var v in values)if(v.ReportId==id&&v.Page==0x59&&v.UsageMin<=usage&&(v.IsRange!=0?v.UsageMax>=usage:v.UsageMin==usage)&&v.BitSize==bits)return v;
        throw new InvalidOperationException("Missing/wrong-width parsed field: ID="+id.ToString("X2")+" usage="+usage.ToString("X2")+" bits="+bits);
    }
    public static void Metadata(string path) {
        using(var h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
            if(h.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());var a=new Attributes();a.Size=(uint)Marshal.SizeOf(typeof(Attributes));if(HidD_GetAttributes(h,ref a)==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            if(a.Vendor!=0x342D||(a.Product!=0xE4C6&&a.Product!=0xE4C7)||a.Version<0x42||a.Version>0x47)throw new InvalidOperationException("Unsupported firmware: no Feature writes sent");
            IntPtr p;if(HidD_GetPreparsedData(h,out p)==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            try {Caps c;if(HidP_GetCaps(p,out c)!=0x110000||c.UsagePage!=0x59||c.Usage!=1||c.FeatureLength!=51)throw new InvalidOperationException("Not expected LampArray capabilities");
                byte first=a.Version<=0x45?(byte)1:(byte)0x20;var values=Values(p,c.FeatureValues);
                Field(values,first,3,16);for(ushort u=4;u<=8;u++)Field(values,first,u,32);
                Field(values,(byte)(first+1),0x21,16);Field(values,(byte)(first+2),0x21,16);
                for(ushort u=0x23;u<=0x27;u++)Field(values,(byte)(first+2),u,32);
                for(ushort u=0x28;u<=0x2D;u++)Field(values,(byte)(first+2),u,8);
                Log("PARSED_CONTRACT\tPASS\tbase="+first.ToString("X2"));
                Validate(new HardwareReports(h,c.FeatureLength),first);
            }finally{HidD_FreePreparsedData(p);}
        }
    }
    public static void Validate(IReports device,byte first) {
        byte[] array=device.Get(first);if(array.Length<23||array[0]!=first)throw new InvalidOperationException("Short/invalid array report");
        int count=BitConverter.ToUInt16(array,1);uint width=BitConverter.ToUInt32(array,3),height=BitConverter.ToUInt32(array,7),depth=BitConverter.ToUInt32(array,11);
        if(count!=68||width==0||height==0||depth==0||BitConverter.ToUInt32(array,15)!=1)throw new InvalidOperationException("Unexpected Split65 array metadata");
        Log(String.Format("ARRAY\tlamps={0}\tbounds={1}/{2}/{3}\tinterval={4}",count,width,height,depth,BitConverter.ToUInt32(array,19)));
        byte[][] baseline=new byte[count][];int failures=0;
        for(int pass=0;pass<3;pass++) {
            Log("PATTERN\t"+pass+"\t"+(pass==0?"one selector, sequential":pass==1?"explicit descending selectors":"two fast cycles with wrap"));
            device.Select((byte)(first+1),0);
            int total=pass==2?count*2:count;
            for(int i=0;i<total;i++) {
                int expected=pass==1?count-1-i:i%count;if(pass==1)device.Select((byte)(first+1),(ushort)expected);
                byte[] data=device.Get((byte)(first+2));
                if(data.Length<29||data[0]!=first+2)throw new InvalidOperationException("Short/wrong-ID lamp report");
                int id=BitConverter.ToUInt16(data,1);
                bool valid=id==expected&&BitConverter.ToUInt32(data,3)<=width&&BitConverter.ToUInt32(data,7)<=height&&BitConverter.ToUInt32(data,11)<=depth&&BitConverter.ToUInt32(data,19)>0&&data[27]<=1&&data[28]<=0xE7;
                if(valid&&baseline[id]!=null)for(int j=0;j<29;j++)if(baseline[id][j]!=data[j])valid=false;
                if(valid&&baseline[id]==null){baseline[id]=new byte[29];Array.Copy(data,baseline[id],29);}
                if(!valid)failures++;
                Log(String.Format("LAMP\tpass={0}\texpected={1}\tactual={2}\tvalid={3}\txyz={4}/{5}/{6}\tlatency={7}\tpurpose={8}\tlevels={9}/{10}/{11}/{12}\tprogrammable={13}\tbinding={14}",pass,expected,id,valid,BitConverter.ToUInt32(data,3),BitConverter.ToUInt32(data,7),BitConverter.ToUInt32(data,11),BitConverter.ToUInt32(data,15),BitConverter.ToUInt32(data,19),data[23],data[24],data[25],data[26],data[27],data[28]));
            }
            byte[] check=device.Get(first);if(check.Length<23)throw new InvalidOperationException("Short array report after pass");
            for(int j=0;j<23;j++)if(check[j]!=array[j])throw new InvalidOperationException("Array metadata changed between passes");
        }
        Log("METADATA_RESULT\tfailures="+failures+"\treads=272\tNo color/control/EEPROM/DFU reports sent");
        if(failures!=0)throw new InvalidOperationException("Metadata/cursor/stability failures="+failures+"; concurrent readers are also possible");
    }
}

// IAsyncInfo is IInspectable-based. QueryInterface avoids PowerShell 5.1's
// inability to cast its System.__ComObject projection to IAsyncInfo.
public static class WinRtAsyncInfo {
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetInt(IntPtr self,out int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int Method(IntPtr self);
    static IntPtr Open(object operation) {
        IntPtr unknown=Marshal.GetIUnknownForObject(operation),info=IntPtr.Zero;
        try {Guid id=new Guid("00000036-0000-0000-c000-000000000046");int hr=Marshal.QueryInterface(unknown,ref id,out info);if(hr<0)Marshal.ThrowExceptionForHR(hr);return info;}
        finally {Marshal.Release(unknown);}
    }
    static int Read(IntPtr info,int slot,out int value) {
        var call=(GetInt)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(info),slot*IntPtr.Size),typeof(GetInt));
        return call(info,out value);
    }
    public static string SnapshotPointer(IntPtr info) {
        int id,status,error;int hi=Read(info,6,out id),hs=Read(info,7,out status),he=Read(info,8,out error);
        string name=status==0?"Started":status==1?"Completed":status==2?"Canceled":status==3?"Error":"Unknown";
        return String.Format("Id={0};Status={1};ErrorCode=0x{2:X8};GetterHR=0x{3:X8}/0x{4:X8}/0x{5:X8}",id,name,error,hi,hs,he);
    }
    public static string Snapshot(object operation) {IntPtr p=Open(operation);try{return SnapshotPointer(p);}finally{Marshal.Release(p);}}
    public static int Cancel(object operation) {
        IntPtr p=Open(operation);
        try {var call=(Method)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(p),9*IntPtr.Size),typeof(Method));return call(p);}
        finally{Marshal.Release(p);}
    }
}
