#!/usr/bin/env python3
"""Run on Windows: py -m pip install hidapi; py tools/windows_probe.py.
Default: read metadata only. --test-rgb temporarily shows red/green/blue.
"""
import argparse
import struct
import time
import hid

parser = argparse.ArgumentParser()
parser.add_argument('--test-rgb', action='store_true')
parser.add_argument('--via', action='store_true', help='Read standard VIA and DL2 split diagnostics instead of LampArray')
args = parser.parse_args()
if args.via:
    devices = [d for d in hid.enumerate(0x342D, 0)
               if d.get("product_id") in (0xE4C6, 0xE4C7)
               if d.get('usage_page') == 0xFF60 and d.get('usage') == 0x61]
    if not devices:
        raise SystemExit('No Split65 VIA interface (FF60:0061). Close Hub/VIA and check USB mode.')
    for device in devices:
        handle = hid.device()
        handle.open_path(device['path'])
        try:
            print(f"USB release_number=0x{device.get('release_number', 0):04X}; product={device.get('product_string')}")
            for command in ([1], [11], [4, 0, 1, 0], [8, 3, 1], [8, 3, 2], [8, 3, 3], [8, 3, 4], [0x30]):
                handle.write(bytes([0] + command + [0] * (32 - len(command))))
                data = bytes(handle.read(32, 2000))
                print(f'{command}: {data.hex(" ") if data else "TIMEOUT"}')
                if len(data) >= 25 and data[0:4] == b'\x30DL2':
                    success, failed = struct.unpack_from('<II', data, 10)
                    print(f'  local=0x{data[4]:04X}, peer=0x{data[7]:04X}, lamps={data[8]}, RPC={success}/{failed}, autonomous={data[19]}, raw={data[20]}')
        finally:
            handle.close()
    raise SystemExit(0)
devices = [d for d in hid.enumerate(0x342D, 0)
               if d.get("product_id") in (0xE4C6, 0xE4C7)
           if d.get('usage_page') == 0x59 and d.get('usage') == 1]
if not devices:
    raise SystemExit('No Split65 LampArray collection. Check USB mode, both images, and USB re-enumeration.')
for device in devices:
    base = 1 if 0x42 <= device.get('release_number', 0) <= 0x45 else 0x20
    handle = hid.device()
    handle.open_path(device['path'])
    try:
        attributes = bytes(handle.get_feature_report(base, 51))
        assert len(attributes) >= 23 and attributes[0] == base
        count, width, height, depth, kind, interval = struct.unpack_from('<HIIIII', attributes, 1)
        print(f'LampCount={count}, bounds={width}/{height}/{depth} um, kind={kind}, interval={interval} us')
        assert count == 68 and kind == 1
        handle.send_feature_report(bytes([base + 1, 0, 0]))
        for lamp in range(count):
            data = bytes(handle.get_feature_report(base + 2, 51))
            assert len(data) >= 29 and data[0] == base + 2
            lamp_id, x, y, z, latency, purpose = struct.unpack_from('<HIIIII', data, 1)
            assert lamp_id == lamp
            print(f'  {lamp:02}: ({x}, {y}, {z}) um, key=0x{data[28]:02X}, latency={latency} us')
        if args.test_rgb:
            try:
                handle.send_feature_report(bytes([base + 5, 0]))
                for color in ((255, 0, 0), (0, 255, 0), (0, 0, 255)):
                    handle.send_feature_report(struct.pack('<BBHHBBBB', base + 4, 1, 0, count - 1, *color, 255))
                    time.sleep(1)
            finally:
                handle.send_feature_report(bytes([base + 5, 1]))
        print('PASS: USB feature enumeration' + (' and RGB commands sent; inspect both halves visually.' if args.test_rgb else '.'))
    finally:
        handle.close()
