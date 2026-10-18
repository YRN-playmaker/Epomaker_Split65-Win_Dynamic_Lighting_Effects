// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.ComponentModel;
using System.IO;
using Microsoft.Win32.SafeHandles;
namespace Split65Bridge {
public static class DriverAccess {
    public const uint ProbeIoctl = 0x0022E008;
    public static bool ValidProbe(byte[] value) {
        byte[] expected = {83,76,66,80,0,5,0,1,220,0,0,0,1,0,0,0};
        if (value == null || value.Length != expected.Length) return false;
        for (int i=0;i<value.Length;i++) if (value[i]!=expected[i]) return false;
        return true;
    }
    public static int Probe() {
        RootDevice.Status();
        int count=0;
        foreach (string path in Native.Paths(Native.BridgeGuid)) {
            ++count;
            Console.WriteLine("Bridge interface="+path);
            using (SafeFileHandle handle=Native.CreateFile(path,0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
                if (handle.IsInvalid) throw new Win32Exception();
                if (!ValidProbe(Native.Ioctl(handle,ProbeIoctl,new byte[0],16))) throw new IOException("Unexpected driver response/version. Repair this project driver.");
            }
        }
        if(count==0) { Console.WriteLine("BRIDGE_PROBE=MISSING_INTERFACE"); return 20; }
        Console.WriteLine("BRIDGE_PROBE=READY"); return 0;
    }
}
}
