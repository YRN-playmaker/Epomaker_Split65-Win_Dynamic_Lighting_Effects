// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.IO;
namespace Split65Bridge {
public static class RootDevice {
    const string HardwareId = "ROOT\\Split65LampBridge";
    static Guid SystemClass = new Guid("4d36e97d-e325-11ce-bfc1-08002be10318");
    [StructLayout(LayoutKind.Sequential)] struct Info { public uint Size; public Guid Class; public uint DevInst; public IntPtr Reserved; }
    [DllImport("setupapi.dll", SetLastError=true)] static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid guid, IntPtr parent);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref Info info);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref Info info, uint property, out uint type, byte[] data, uint size, out uint required);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiCreateDeviceInfo(IntPtr set, string name, ref Guid guid, string description, IntPtr parent, uint flags, ref Info info);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiSetDeviceRegistryProperty(IntPtr set, ref Info info, uint property, byte[] data, uint size);
    [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiCallClassInstaller(uint function, IntPtr set, ref Info info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref Info info, StringBuilder value, uint size, out uint needed);
    [DllImport("cfgmgr32.dll")] static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint instance, uint flags);
    [DllImport("newdev.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool UpdateDriverForPlugAndPlayDevices(IntPtr parent, string hardwareId, string fullInfPath, uint flags, out bool reboot);
    static string Property(IntPtr set, ref Info info, uint property) {
        byte[] data = new byte[8192]; uint type, needed;
        if (!SetupDiGetDeviceRegistryProperty(set, ref info, property, out type, data, (uint)data.Length, out needed)) return "(not set)";
        return Encoding.Unicode.GetString(data,0,(int)Math.Min(needed,data.Length)).TrimEnd('\0').Replace('\0',';');
    }
    public static bool MatchesHardwareId(byte[] data, int length) {
        if (data == null || length < 0 || length > data.Length || (length & 1) != 0) return false;
        // Bind explicitly to the Windows .NET Framework Char[] overload.
        string[] ids = Encoding.Unicode.GetString(data, 0, length).Split(new char[] {'\0'}, StringSplitOptions.RemoveEmptyEntries);
        foreach (string id in ids) if (String.Equals(id, HardwareId, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    static bool Match(IntPtr set, ref Info info) {
        byte[] data = new byte[4096]; uint type, needed;
        if (!SetupDiGetDeviceRegistryProperty(set, ref info, 1, out type, data, (uint)data.Length, out needed)) return false;
        return type == 7 && needed <= data.Length && MatchesHardwareId(data, (int)needed);
    }
    static int Existing(bool remove, bool showStatus = false) {
        IntPtr set = SetupDiGetClassDevs(ref SystemClass, IntPtr.Zero, IntPtr.Zero, 0);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        List<Info> matches = new List<Info>();
        try {
            for (uint i = 0; ; ++i) {
                Info d = new Info(); d.Size = (uint)Marshal.SizeOf(typeof(Info));
                if (!SetupDiEnumDeviceInfo(set, i, ref d)) {
                    int error = Marshal.GetLastWin32Error(); if (error == 259) break; throw new Win32Exception(error);
                }
                if (!Match(set, ref d)) continue;
                matches.Add(d);
                if (showStatus) {
                    uint status, problem; uint result = CM_Get_DevNode_Status(out status, out problem, d.DevInst, 0);
                    Console.WriteLine("Root hardware ID=" + HardwareId + "; CM result=" + result + "; problem=" + problem);
                    StringBuilder instance = new StringBuilder(512); uint needed;
                    if (SetupDiGetDeviceInstanceId(set,ref d,instance,512,out needed)) Console.WriteLine("Root instance="+instance);
                    Console.WriteLine("Root service="+Property(set,ref d,4));
                    Console.WriteLine("Root upper filters="+Property(set,ref d,17));
                    Console.WriteLine("Root driver key="+Property(set,ref d,9));
                }
            }
            if (remove) for (int i = matches.Count - 1; i >= 0; --i) {
                Info d = matches[i];
                if (!SetupDiCallClassInstaller(5, set, ref d)) throw new Win32Exception(); // DIF_REMOVE, exact hardware ID only
            }
            return matches.Count;
        } finally { SetupDiDestroyDeviceInfoList(set); }
    }
    public static int Install() {
        if (Existing(false) != 0) { Console.WriteLine("Root bridge device already exists."); return 0; }
        IntPtr set = SetupDiCreateDeviceInfoList(ref SystemClass, IntPtr.Zero);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        try {
            Info d = new Info(); d.Size = (uint)Marshal.SizeOf(typeof(Info));
            if (!SetupDiCreateDeviceInfo(set, "Split65LampBridge", ref SystemClass, "Split65 Wireless LampArray Bridge", IntPtr.Zero, 1, ref d)) throw new Win32Exception();
            byte[] ids = Encoding.Unicode.GetBytes(HardwareId + "\0\0");
            if (!SetupDiSetDeviceRegistryProperty(set, ref d, 1, ids, (uint)ids.Length)) throw new Win32Exception();
            byte[] name = Encoding.Unicode.GetBytes("Split65 Wireless LampArray Bridge\0");
            if (!SetupDiSetDeviceRegistryProperty(set, ref d, 12, name, (uint)name.Length)) throw new Win32Exception();
            if (!SetupDiCallClassInstaller(0x19, set, ref d)) throw new Win32Exception(); // DIF_REGISTERDEVICE
            Console.WriteLine("Created the project's independent root device."); return 0;
        } finally { SetupDiDestroyDeviceInfoList(set); }
    }
    public static int Remove() { Console.WriteLine("Removed matching project root devices: " + Existing(true)); return 0; }
    public static int Status() { Console.WriteLine("Matching root devices=" + Existing(false, true)); return 0; }
    public static int Bind(string inf) {
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Split65LampBridge", "driver", "Split65LampBridge.inf");
        if (!String.Equals(Path.GetFullPath(inf), expected, StringComparison.OrdinalIgnoreCase) || !File.Exists(inf))
            throw new IOException("Driver binding is restricted to this project's staged INF.");
        if (Existing(false) != 1) throw new IOException("Exactly one project root device is required for binding.");
        bool reboot;
        // Exact project hardware ID only; avoids PnPUtil's driver-ranking no-op.
        if (!UpdateDriverForPlugAndPlayDevices(IntPtr.Zero, HardwareId, inf, 1, out reboot)) throw new Win32Exception();
        Console.WriteLine("Bound project INF; reboot required="+(reboot?"1":"0")); return reboot?10:0;
    }
}
}
