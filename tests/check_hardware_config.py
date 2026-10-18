#!/usr/bin/env python3
"""Guard the known regression: preserve upstream's full LED address space."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
keyboard = json.loads((root / 'keyboard.json').read_text())
assert 'split_count' not in keyboard['rgb_matrix'], 'Do not clip/remap the upstream LED driver'
assert len(keyboard['rgb_matrix']['layout']) == 68
assert keyboard['processor'] == 'WB32FQ95'
assert keyboard['usb']['pid'] == '0xE4C6'
assert keyboard['usb']['device_version'] == '0.5.0'
assert keyboard['usb']['shared_endpoint']['keyboard'] is True
assert '#define RGB_MATRIX_SPLIT' not in (root / 'config.h').read_text()
print('PASS: upstream 68-entry LED address space, WB32FQ95, keyboard endpoint sharing, USB revision 0.5.0')
