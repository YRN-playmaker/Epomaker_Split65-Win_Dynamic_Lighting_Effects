// SPDX-License-Identifier: GPL-2.0-or-later
// Keep standard VIA remapping/lighting intact. Route the community's legacy
// streaming commands through the same full-frame path as LampArray, instead of
// writing only the local LED buffer in quantum/via.c.
#include QMK_KEYBOARD_H
#include "lamparray.h"
#include "raw_hid.h"

bool via_command_kb(uint8_t *data, uint8_t length) {
    if (length != 32) return false;
    switch (data[0]) {
        case 0x24: // SignalRGB-compatible LED stream (up to nine RGB triples)
            if (!split65_lamp_stream(data[1], data[2], data + 3)) data[1] = 253;
            break;
        case 0x25: split65_lamp_raw_enable(true); break;
        case 0x26: split65_lamp_raw_enable(false); break;
        case 0x30: split65_lamp_diagnostics(data); break;
        case 0x31: split65_lamp_usb_diagnostics(data); break;
        default:
            // Manual VIA/Hub lighting edits release the legacy raw stream.
            // LampArray ownership remains with Windows until it releases it.
            if (data[0] == 0x07 && data[1] == 3) split65_lamp_raw_enable(false);
            return false;
    }
    raw_hid_send(data, length);
    return true;
}
