#!/usr/bin/env python3
"""Check USB descriptors extracted from the actual linked WB32 image."""
import re
import subprocess
import sys
from pathlib import Path

qmk = Path(sys.argv[1]).resolve()
nm = sys.argv[2] if len(sys.argv) > 2 else 'arm-none-eabi-nm'
symbols = subprocess.check_output([nm, '-S', qmk / '.build/epomaker_split65_default.elf'], text=True)
binary = (qmk / 'epomaker_split65_default.bin').read_bytes()

def linked(name):
    match = re.search(r'^([0-9a-f]+) ([0-9a-f]+) \w ' + name + r'$', symbols, re.M)
    assert match, name
    address, size = (int(n, 16) for n in match.groups())
    offset = address - 0x08000000
    assert 0 <= offset < len(binary) and offset + size <= len(binary)
    return binary[offset:offset + size]

device = linked('DeviceDescriptor')
assert device[8:14] == bytes.fromhex('2d34 c7e4 4800')
assert device[16] == 0, 'Do not reuse the all-zero USB serial'
assert ' SerialNumberString' not in symbols
config = linked('ConfigurationDescriptor')
assert config[4] == 2 and int.from_bytes(config[2:4], 'little') == len(config)
interfaces, endpoints = {}, []
offset = 0
while offset < len(config):
    length, kind = config[offset:offset + 2]
    assert length >= 2
    record = config[offset:offset + length]
    if kind == 4:
        interface = record[2]
        interfaces[interface] = {'class': record[5], 'subclass': record[6], 'protocol': record[7], 'endpoints': []}
    elif kind == 0x21:
        interfaces[interface]['report_length'] = int.from_bytes(record[7:9], 'little')
    elif kind == 5:
        address = record[2]
        assert 1 <= (address & 15) <= 3, 'WB32 endpoint budget exceeded'
        endpoints.append(address)
        interfaces[interface]['endpoints'].append(address)
    offset += length
assert set(interfaces) == {0, 1}
assert interfaces[0]['subclass'] == interfaces[0]['protocol'] == 0
assert interfaces[0]['endpoints'] == [0x82]
assert interfaces[1]['endpoints'] == [0x81, 0x01], 'VIA Raw HID must remain interface 1'
assert len(endpoints) == len(set(endpoints))
shared = linked('SharedReport')
original = (Path(__file__).resolve().parent / 'fixtures/shared-input-v0.4.5.bin').read_bytes()
assert shared.startswith(original), 'QMK keyboard/mouse/media/NKRO descriptors changed'
assert shared[len(original)] == 0xA4 and shared[-1] == 0xB4
lamp = shared[len(original)+1:-1]
assert bytes.fromhex('05590901a101') in lamp
assert all(bytes((0x85, report_id)) in lamp for report_id in range(0x20, 0x26))
assert bytes.fromhex('8501') in original
assert interfaces[0]['report_length'] == len(shared)
assert ' LampArrayReport' not in symbols
subprocess.run([sys.executable, Path(__file__).resolve().parents[1] / 'tools/generate_lamp_descriptor.py', '--check'], check=True)
print(f'PASS: linked VID/PID 342D:E4C7 bcdDevice=0048; non-boot keyboard+LampArray interface 0, VIA 1; unchanged {len(original)} input bytes; LampArray {len(lamp)} bytes')
