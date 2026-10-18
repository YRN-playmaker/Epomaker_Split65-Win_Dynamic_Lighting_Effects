// SPDX-License-Identifier: GPL-2.0-or-later
#include "protocol.h"
#include <string.h>

static uint16_t get16(const uint8_t *p) { return p[0] | ((uint16_t)p[1] << 8); }
static void put16(uint8_t *p, uint16_t x) { p[0] = x; p[1] = x >> 8; }
static void put32(uint8_t *p, uint32_t x) {
    p[0] = x; p[1] = x >> 8; p[2] = x >> 16; p[3] = x >> 24;
}
void dl_init(dl_state_t *s) { memset(s, 0, sizeof(*s)); s->autonomous = true; }
size_t dl_report_size(uint8_t id) {
    static const uint8_t sizes[] = {23, 3, 29, 51, 10, 2};
    return id >= DL_REPORT_ATTRIBUTES && id <= DL_REPORT_CONTROL ? sizes[id - DL_REPORT_ATTRIBUTES] : 0;
}
static void commit(dl_state_t *s, uint8_t flags) {
    if (flags & 1) {
        memcpy(s->visible, s->pending, sizeof(s->visible));
        ++s->generation;
    }
}
bool dl_set_feature(dl_state_t *s, uint8_t id, const uint8_t *p, size_t n) {
    const size_t expected = dl_report_size(id);
    // Windows HID clients may pad smaller features to FeatureReportByteLength.
    if (!expected || n < expected || n > DL_MAX_REPORT_SIZE || p[0] != id) return false;
    switch (id) {
        case DL_REPORT_REQUEST:
            s->requested_lamp = get16(p + 1);
            if (s->requested_lamp >= DL_LAMP_COUNT) s->requested_lamp = 0;
            return true;
        case DL_REPORT_CONTROL:
            if (p[1] > 1) return false;
            if (s->autonomous != !!p[1]) {
                s->autonomous = !!p[1];
                ++s->generation;
            }
            return true;
        case DL_REPORT_MULTI:
            if (p[1] > 8 || (p[2] & ~1u)) return false;
            if (s->autonomous) return true;
            for (unsigned i = 0; i < p[1]; ++i) {
                unsigned lamp = get16(p + 3 + 2 * i);
                if (lamp < DL_LAMP_COUNT) memcpy(&s->pending[lamp], p + 19 + 4 * i, 4);
            }
            commit(s, p[2]);
            return true;
        case DL_REPORT_RANGE: {
            unsigned first = get16(p + 2), last = get16(p + 4);
            if ((p[1] & ~1u) || first > last || last >= DL_LAMP_COUNT) return false;
            if (s->autonomous) return true;
            for (unsigned i = first; i <= last; ++i) memcpy(&s->pending[i], p + 6, 4);
            commit(s, p[1]);
            return true;
        }
        default: return false;
    }
}
size_t dl_get_feature(const dl_state_t *s, uint8_t id, uint8_t *p, const dl_lamp_info_t *info) {
    memset(p, 0, DL_MAX_REPORT_SIZE);
    p[0] = id;
    switch (id) {
        case DL_REPORT_ATTRIBUTES:
            put16(p + 1, DL_LAMP_COUNT);
            // Approximate joined layout, in micrometers; detached spacing is unknown.
            put32(p + 3, 330000); put32(p + 7, 105000); put32(p + 11, 30000);
            put32(p + 15, 1); // Keyboard
            put32(p + 19, DL_INTERVAL_US);
            return 23;
        case DL_REPORT_RESPONSE: {
            const unsigned lamp = s->requested_lamp;
            put16(p + 1, lamp);
            put32(p + 3, info[lamp].x); put32(p + 7, info[lamp].y); put32(p + 11, info[lamp].z);
            put32(p + 15, DL_INTERVAL_US); put32(p + 19, 1); // Control purpose
            p[23] = p[24] = p[25] = 255;
            p[26] = 1; // RGB LEDs have no independent intensity channel (Microsoft sample).
            p[27] = 1; p[28] = info[lamp].key;
            return 29;
        }
        case DL_REPORT_CONTROL: p[1] = s->autonomous; return 2;
        default: return 0;
    }
}

void dl_feature_sent(dl_state_t *s, uint8_t id, const uint8_t *p, size_t n) {
    if (id != DL_REPORT_RESPONSE || n != dl_report_size(id) || p[0] != id) return;
    const unsigned lamp = get16(p + 1);
    if (lamp < DL_LAMP_COUNT && s->requested_lamp == lamp)
        s->requested_lamp = (lamp + 1) % DL_LAMP_COUNT;
}
