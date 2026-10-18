#!/usr/bin/env python3
"""Parse actual HID items, checking report sizes and six LampArray usages."""
import re
import sys
from pathlib import Path

source = (Path(__file__).resolve().parents[1] / 'dynamic_lighting/descriptor.inc').read_text()
source = re.sub(r'//[^\n]*', '', source)
data = Path(sys.argv[1]).read_bytes() if len(sys.argv) > 1 else bytes(int(x, 16) for x in re.findall(r'0x([0-9A-Fa-f]{2})', source))
expected = {0x20: 22, 0x21: 2, 0x22: 28, 0x23: 50, 0x24: 9, 0x25: 1}
bits, usages, widths, local_usages = {}, set(), {}, []
rid = size = count = depth = i = 0
while i < len(data):
    prefix = data[i]; i += 1
    n = (0, 1, 2, 4)[prefix & 3]
    value = int.from_bytes(data[i:i+n], 'little'); i += n
    tag = prefix & 0xFC
    if tag == 0x84: rid = value
    elif tag == 0x74: size = value
    elif tag == 0x94: count = value
    elif tag == 0xB0:
        bits[rid] = bits.get(rid, 0) + size * count
        for usage in local_usages: widths[rid, usage] = size
        local_usages = []
    elif tag == 0xA0: depth += 1; local_usages = []
    elif tag == 0xC0: depth -= 1; local_usages = []
    elif tag == 0x08: usages.add(value); local_usages.append(value)
    assert depth >= 0
assert depth == 0
assert bits == {rid: size * 8 for rid, size in expected.items()}, bits
assert {2, 0x20, 0x22, 0x50, 0x60, 0x70} <= usages
assert bytes.fromhex('05 59 09 01 A1 01') in data
# Preserve the field widths implicated in the silent Windows incompatibility
# reported in microsoft/ArduinoHidForWindows#9. MultiUpdate's slot count is 8
# bits in Microsoft's descriptor; the array's total lamp count is 16 bits.
assert widths[0x20, 3] == 16
assert all(widths[rid, usage] == 16 for rid, usage in [(0x21, 0x21), (0x22, 0x21), (0x23, 0x21), (0x24, 0x61), (0x24, 0x62)])
assert widths[0x20, 7] == widths[0x22, 0x26] == 32
print('PASS: HID LampArray collection, six feature reports, sizes 23/3/29/51/10/2 including IDs')
