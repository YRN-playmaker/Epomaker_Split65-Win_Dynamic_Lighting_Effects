#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-2.0-or-later
"""Read-only 2.4G status probe for the Split65 wireless RGB channel.

Sends command 0x30 over the receiver's VIA Raw HID collection (usage page
0xFF60) and decodes the 32-byte reply produced by split65_lamp_diagnostics().
It never writes colours, key mappings, or EEPROM.

Usage: python probe_status.py [--timeout 3] [--verbose]
"""

import argparse
import ctypes
import ctypes.wintypes as w
import sys
import time

setupapi = ctypes.WinDLL("setupapi", use_last_error=True)
hid = ctypes.WinDLL("hid", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

DIGCF_PRESENT = 0x02
DIGCF_DEVICEINTERFACE = 0x10
GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
FILE_SHARE_READ = 0x01
FILE_SHARE_WRITE = 0x02
OPEN_EXISTING = 3
FILE_FLAG_OVERLAPPED = 0x40000000
ERROR_IO_PENDING = 997
WAIT_OBJECT_0 = 0

DEVICE_CODES = {0: "USB", 1: "BLUETOOTH", 6: "2.4G"}


class GUID(ctypes.c_ubyte * 16):
    pass


class SP_DEVICE_INTERFACE_DATA(ctypes.Structure):
    _fields_ = [("cbSize", w.DWORD), ("InterfaceClassGuid", GUID),
                ("Flags", w.DWORD), ("Reserved", ctypes.c_size_t)]


class SP_DEVICE_INTERFACE_DETAIL_DATA_W(ctypes.Structure):
    _fields_ = [("cbSize", w.DWORD), ("DevicePath", ctypes.c_wchar * 1)]


class HIDD_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("Size", w.ULONG), ("VendorID", w.USHORT),
                ("ProductID", w.USHORT), ("VersionNumber", w.USHORT)]


class HIDP_CAPS(ctypes.Structure):
    _fields_ = [("Usage", w.USHORT), ("UsagePage", w.USHORT),
                ("InputReportByteLength", w.USHORT), ("OutputReportByteLength", w.USHORT),
                ("FeatureReportByteLength", w.USHORT), ("Reserved", w.USHORT * 17),
                ("NumberLinkCollectionNodes", w.USHORT),
                ("NumberInputButtonCaps", w.USHORT), ("NumberInputValueCaps", w.USHORT),
                ("NumberInputDataIndices", w.USHORT),
                ("NumberOutputButtonCaps", w.USHORT), ("NumberOutputValueCaps", w.USHORT),
                ("NumberOutputDataIndices", w.USHORT),
                ("NumberFeatureButtonCaps", w.USHORT), ("NumberFeatureValueCaps", w.USHORT),
                ("NumberFeatureDataIndices", w.USHORT)]


class OVERLAPPED(ctypes.Structure):
    _fields_ = [("Internal", ctypes.c_size_t), ("InternalHigh", ctypes.c_size_t),
                ("Offset", w.DWORD), ("OffsetHigh", w.DWORD), ("hEvent", w.HANDLE)]


hid.HidD_GetHidGuid.argtypes = [ctypes.POINTER(GUID)]
hid.HidD_GetAttributes.argtypes = [w.HANDLE, ctypes.POINTER(HIDD_ATTRIBUTES)]
hid.HidD_GetPreparsedData.argtypes = [w.HANDLE, ctypes.POINTER(ctypes.c_void_p)]
hid.HidD_FreePreparsedData.argtypes = [ctypes.c_void_p]
hid.HidP_GetCaps.argtypes = [ctypes.c_void_p, ctypes.POINTER(HIDP_CAPS)]
hid.HidD_SetOutputReport.argtypes = [w.HANDLE, ctypes.c_void_p, w.ULONG]
setupapi.SetupDiGetClassDevsW.restype = w.HANDLE
setupapi.SetupDiGetClassDevsW.argtypes = [ctypes.POINTER(GUID), w.LPCWSTR, w.HWND, w.DWORD]
setupapi.SetupDiEnumDeviceInterfaces.argtypes = [
    w.HANDLE, ctypes.c_void_p, ctypes.POINTER(GUID), w.DWORD, ctypes.POINTER(SP_DEVICE_INTERFACE_DATA)]
setupapi.SetupDiGetDeviceInterfaceDetailW.argtypes = [
    w.HANDLE, ctypes.POINTER(SP_DEVICE_INTERFACE_DATA),
    ctypes.POINTER(SP_DEVICE_INTERFACE_DETAIL_DATA_W), w.DWORD,
    ctypes.POINTER(w.DWORD), ctypes.c_void_p]
setupapi.SetupDiDestroyDeviceInfoList.argtypes = [w.HANDLE]
kernel32.CreateFileW.restype = w.HANDLE
kernel32.CreateFileW.argtypes = [w.LPCWSTR, w.DWORD, w.DWORD, ctypes.c_void_p, w.DWORD, w.DWORD, w.HANDLE]
kernel32.CreateEventW.restype = w.HANDLE
kernel32.CreateEventW.argtypes = [ctypes.c_void_p, w.BOOL, w.BOOL, w.LPCWSTR]
kernel32.WriteFile.argtypes = [w.HANDLE, ctypes.c_void_p, w.DWORD,
                              ctypes.POINTER(w.DWORD), ctypes.POINTER(OVERLAPPED)]
kernel32.ReadFile.argtypes = [w.HANDLE, ctypes.c_void_p, w.DWORD,
                             ctypes.POINTER(w.DWORD), ctypes.POINTER(OVERLAPPED)]
kernel32.GetOverlappedResult.argtypes = [w.HANDLE, ctypes.POINTER(OVERLAPPED),
                                         ctypes.POINTER(w.DWORD), w.BOOL]
kernel32.CancelIoEx.argtypes = [w.HANDLE, ctypes.POINTER(OVERLAPPED)]
kernel32.WaitForSingleObject.argtypes = [w.HANDLE, w.DWORD]
kernel32.CloseHandle.argtypes = [w.HANDLE]


def find_receiver():
    guid = GUID()
    hid.HidD_GetHidGuid(ctypes.byref(guid))
    hdev = setupapi.SetupDiGetClassDevsW(ctypes.byref(guid), None, None,
                                         DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)
    found = []
    index = 0
    while True:
        did = SP_DEVICE_INTERFACE_DATA()
        did.cbSize = ctypes.sizeof(did)
        if not setupapi.SetupDiEnumDeviceInterfaces(hdev, None, ctypes.byref(guid), index,
                                                    ctypes.byref(did)):
            break
        index += 1
        need = w.DWORD(0)
        setupapi.SetupDiGetDeviceInterfaceDetailW(hdev, ctypes.byref(did), None, 0,
                                                  ctypes.byref(need), None)
        buf = ctypes.create_string_buffer(need.value)
        detail = ctypes.cast(buf, ctypes.POINTER(SP_DEVICE_INTERFACE_DETAIL_DATA_W))
        detail.contents.cbSize = 8 if ctypes.sizeof(ctypes.c_void_p) == 8 else 6
        if not setupapi.SetupDiGetDeviceInterfaceDetailW(hdev, ctypes.byref(did), detail,
                                                         need.value, ctypes.byref(need), None):
            continue
        path = ctypes.wstring_at(ctypes.addressof(detail.contents) + 4)
        handle = kernel32.CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                      None, OPEN_EXISTING, 0, None)
        if handle in (0, w.HANDLE(-1).value):
            continue
        attrs = HIDD_ATTRIBUTES()
        attrs.Size = ctypes.sizeof(attrs)
        hid.HidD_GetAttributes(handle, ctypes.byref(attrs))
        caps = HIDP_CAPS()
        preparsed = ctypes.c_void_p()
        if hid.HidD_GetPreparsedData(handle, ctypes.byref(preparsed)):
            hid.HidP_GetCaps(preparsed, ctypes.byref(caps))
            hid.HidD_FreePreparsedData(preparsed)
        kernel32.CloseHandle(handle)
        if attrs.VendorID == 0x342D and caps.UsagePage == 0xFF60:
            found.append((path, attrs, caps))
    setupapi.SetupDiDestroyDeviceInfoList(hdev)
    return found


def open_device(path):
    return kernel32.CreateFileW(path, GENERIC_READ | GENERIC_WRITE,
                                FILE_SHARE_READ | FILE_SHARE_WRITE, None,
                                OPEN_EXISTING, FILE_FLAG_OVERLAPPED, None)


def write_report(handle, payload, timeout_ms=1000):
    event = kernel32.CreateEventW(None, True, False, None)
    ov = OVERLAPPED()
    ov.hEvent = event
    written = w.DWORD(0)
    buf = ctypes.create_string_buffer(bytes(payload), len(payload))
    ok = kernel32.WriteFile(handle, buf, len(payload), ctypes.byref(written), ctypes.byref(ov))
    if not ok:
        err = ctypes.get_last_error()
        if err != ERROR_IO_PENDING:
            kernel32.CloseHandle(event)
            return False, err
    kernel32.WaitForSingleObject(event, timeout_ms)
    got = w.DWORD(0)
    kernel32.GetOverlappedResult(handle, ctypes.byref(ov), ctypes.byref(got), True)
    kernel32.CloseHandle(event)
    return True, got.value


def read_report(handle, size, timeout_ms):
    """One read, cancelled on timeout so no pending request keeps swallowing replies."""
    event = kernel32.CreateEventW(None, True, False, None)
    ov = OVERLAPPED()
    ov.hEvent = event
    buf = ctypes.create_string_buffer(size)
    got = w.DWORD(0)
    ok = kernel32.ReadFile(handle, buf, size, ctypes.byref(got), ctypes.byref(ov))
    if not ok:
        err = ctypes.get_last_error()
        if err != ERROR_IO_PENDING:
            kernel32.CloseHandle(event)
            return None
    if kernel32.WaitForSingleObject(event, timeout_ms) != WAIT_OBJECT_0:
        kernel32.CancelIoEx(handle, ctypes.byref(ov))
        kernel32.GetOverlappedResult(handle, ctypes.byref(ov), ctypes.byref(got), True)
        kernel32.CloseHandle(event)
        return None
    kernel32.GetOverlappedResult(handle, ctypes.byref(ov), ctypes.byref(got), False)
    kernel32.CloseHandle(event)
    return buf.raw[:got.value] if got.value else None


def transact(handle, payload, timeout, verbose):
    if verbose:
        print("  -> %s" % bytes(payload).hex())
    ok, info = write_report(handle, payload)
    if not ok:
        if verbose:
            print("  write error %d" % info)
        return None
    deadline = time.time() + timeout
    while time.time() < deadline:
        chunk = read_report(handle, 64, 350)
        if chunk:
            if verbose:
                print("  <- %s" % chunk.hex())
            return chunk
    return None


def decode(payload):
    def at(i):
        return payload[i] if i < len(payload) else None
    return {
        "signature": bytes(payload[1:4]).decode("ascii", "replace"),
        "revision": at(4),
        "lamp_count": at(8),
        "autonomous": at(19),
        "raw_active": at(20),
        "device": at(21),
        "raw_protocol": at(26),
        "raw_device": at(27),
        "lease_seconds": at(28),
        "module_state": at(29),
    }


def main():
    parser = argparse.ArgumentParser(description="Read-only Split65 wireless status probe")
    parser.add_argument("--timeout", type=float, default=3.0)
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args()

    receivers = find_receiver()
    if not receivers:
        print("no Split65 rawhid collection (usage 0xFF60) found; is the receiver plugged in?")
        return 2
    path, attrs, caps = receivers[0]
    print("device  : VID %04X PID %04X bcd %04X" % (attrs.VendorID, attrs.ProductID, attrs.VersionNumber))
    print("raw hid : in=%d out=%d usage 0x%04X/0x%04X"
          % (caps.InputReportByteLength, caps.OutputReportByteLength, caps.UsagePage, caps.Usage))

    handle = open_device(path)
    if handle in (0, w.HANDLE(-1).value):
        print("could not open the raw HID collection")
        return 3

    # framing A: report id 0 prefix + 32 byte payload (caps say out=33)
    reply = transact(handle, bytes([0x00, 0x30] + [0] * 31), args.timeout, args.verbose)
    framing = "report-id-0 + 32B"
    if reply is None:
        # framing B: 32-byte payload through HidD_SetOutputReport
        buf = ctypes.create_string_buffer(bytes([0x30] + [0] * 31), 32)
        if hid.HidD_SetOutputReport(handle, buf, 32):
            deadline = time.time() + args.timeout
            while time.time() < deadline and reply is None:
                reply = read_report(handle, 64, 350)
            framing = "HidD_SetOutputReport 32B"
        elif args.verbose:
            print("  HidD_SetOutputReport failed, err %d" % ctypes.get_last_error())

    kernel32.CloseHandle(handle)

    if not reply:
        print("no reply to 0x30 (%.1f s, both framings)" % args.timeout)
        print("check: keyboard awake on 2.4G, paired to this receiver, v0.4.8 on the left half")
        return 5

    payload = reply[1:] if len(reply) == 33 else reply
    info = decode(payload)
    print("reply   : %s  (%s)" % (reply.hex(), framing))
    print("signature: %r  revision: 0x%02X" % (info["signature"], info["revision"] or 0))
    print("lamps   : %s" % info["lamp_count"])
    print("device  : %s (%s)" % (info["device"], DEVICE_CODES.get(info["device"], "?")))
    print("raw     : active=%s protocol=%s device=%s lease=%ss"
          % (info["raw_active"], info["raw_protocol"], info["raw_device"], info["lease_seconds"]))
    print("module  : state=%s autonomous=%s" % (info["module_state"], info["autonomous"]))
    ok = (info["signature"] == "DL2" and info["revision"] == 0x48
          and info["lamp_count"] == 68 and info["raw_protocol"] == 1)
    print("verdict : %s" % ("v0.4.8 wireless channel ready" if ok else "unexpected answer"))
    return 0 if ok else 6


if __name__ == "__main__":
    sys.exit(main())
