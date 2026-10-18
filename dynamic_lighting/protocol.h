// SPDX-License-Identifier: GPL-2.0-or-later
#pragma once
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define DL_LAMP_COUNT 68
#define DL_REPORT_ATTRIBUTES 0x20
#define DL_REPORT_REQUEST 0x21
#define DL_REPORT_RESPONSE 0x22
#define DL_REPORT_MULTI 0x23
#define DL_REPORT_RANGE 0x24
#define DL_REPORT_CONTROL 0x25
#define DL_MAX_REPORT_SIZE 51
#define DL_INTERVAL_US 100000
#define DL_FIRMWARE_REVISION 0x50
// USB compatibility changes do not change the inter-half packet layout.
// Keep the existing inter-half wire format; wireless ownership is host-side.
#define DL_SPLIT_PROTOCOL_REVISION 0x44

typedef struct { uint8_t r, g, b, intensity; } dl_color_t;
// Advertised as 255 independent intensity levels. Fold this gain into RGB
// for hardware/raw transports that have only RGB channels.
static inline uint8_t dl_channel(uint8_t color, uint8_t intensity) {
    return ((uint16_t)color * intensity + 127u) / 255u;
}
typedef struct {
    uint32_t x, y, z;
    uint8_t key; // USB HID Keyboard usage, not a Windows virtual-key code.
} dl_lamp_info_t;
typedef struct {
    dl_color_t pending[DL_LAMP_COUNT], visible[DL_LAMP_COUNT];
    uint16_t requested_lamp;
    uint32_t generation;
    bool autonomous;
} dl_state_t;

void dl_init(dl_state_t *state);
size_t dl_report_size(uint8_t id);
bool dl_set_feature(dl_state_t *state, uint8_t id, const uint8_t *data, size_t length);
size_t dl_get_feature(const dl_state_t *state, uint8_t id, uint8_t *data, const dl_lamp_info_t *info);

// Commit only after a complete IN data report, never while preparing a reply.
void dl_feature_sent(dl_state_t *state, uint8_t id, const uint8_t *data, size_t length);
