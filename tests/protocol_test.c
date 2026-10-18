// SPDX-License-Identifier: GPL-2.0-or-later
#include "protocol.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>

static dl_lamp_info_t info[DL_LAMP_COUNT];
static void control(dl_state_t *s, unsigned mode) {
    uint8_t p[] = {DL_REPORT_CONTROL, mode};
    assert(dl_set_feature(s, p[0], p, sizeof(p)));
}
int main(void) {
    dl_state_t s;
    uint8_t out[DL_MAX_REPORT_SIZE];
    dl_init(&s);
    assert(s.autonomous);
    assert(dl_get_feature(&s, DL_REPORT_ATTRIBUTES, out, info) == 23);
    assert(out[1] == 68 && out[2] == 0 && out[15] == 1);
    uint8_t range[] = {DL_REPORT_RANGE, 1, 0, 0, 67, 0, 255, 32, 16, 255};
    assert(dl_set_feature(&s, range[0], range, sizeof(range)));
    assert(s.visible[0].r == 0); // Autonomous mode ignores host colors.
    control(&s, 0);
    range[1] = 0;
    assert(dl_set_feature(&s, range[0], range, sizeof(range)));
    assert(s.pending[0].r == 255 && s.visible[0].r == 0);
    range[1] = 1;
    assert(dl_set_feature(&s, range[0], range, sizeof(range)));
    assert(s.visible[0].r == 255 && s.visible[67].g == 32);
    uint8_t multi[51] = {DL_REPORT_MULTI, 2, 1, 0, 0, 67, 0};
    multi[19] = 8; multi[22] = 255;
    multi[23] = 9; multi[26] = 127;
    assert(dl_set_feature(&s, multi[0], multi, sizeof(multi)));
    assert(s.visible[0].r == 8 && s.visible[67].r == 9 && s.visible[67].intensity == 127);
    dl_state_t before = s;
    multi[1] = 9;
    assert(!dl_set_feature(&s, multi[0], multi, sizeof(multi)));
    assert(memcmp(&s, &before, sizeof(s)) == 0);
    multi[1] = 1;
    assert(!dl_set_feature(&s, multi[0], multi, 50));
    range[2] = 67; range[4] = 66;
    assert(!dl_set_feature(&s, range[0], range, sizeof(range)));
    range[2] = 0; range[4] = 68;
    assert(!dl_set_feature(&s, range[0], range, sizeof(range)));
    uint8_t request[51] = {DL_REPORT_REQUEST, 67, 0};
    info[67].key = 79; info[67].x = 123456;
    assert(dl_set_feature(&s, request[0], request, 51)); // HID padding
    assert(dl_get_feature(&s, DL_REPORT_RESPONSE, out, info) == 29);
    assert(out[26] == 1 && out[1] == 67 && out[28] == 79 && out[3] == 0x40 && out[4] == 0xE2);
    assert(s.requested_lamp == 67); // Preparing a reply must not consume its ID.
    dl_feature_sent(&s, DL_REPORT_RESPONSE, out, 28);
    assert(s.requested_lamp == 67); // Truncated transfers do not advance.
    dl_feature_sent(&s, DL_REPORT_RESPONSE, out, 29);
    assert(s.requested_lamp == 0); // A full response advances and wraps.
    dl_feature_sent(&s, DL_REPORT_RESPONSE, out, 29);
    assert(s.requested_lamp == 0); // Duplicate completion cannot consume another ID.
    request[1] = 255; request[2] = 255;
    assert(dl_set_feature(&s, request[0], request, 3) && s.requested_lamp == 0);
    for (unsigned i = 0; i < DL_LAMP_COUNT; ++i) {
        assert(dl_get_feature(&s, DL_REPORT_RESPONSE, out, info) == 29 && out[1] == i);
        assert(s.requested_lamp == i);
        dl_feature_sent(&s, DL_REPORT_RESPONSE, out, 29);
    }
    control(&s, 1);
    assert(s.autonomous);
    uint8_t invalid_control[] = {DL_REPORT_CONTROL, 2};
    assert(!dl_set_feature(&s, invalid_control[0], invalid_control, 2));
    // Deterministic malformed input exercise under ASan/UBSan.
    uint32_t rng = 0x901abc;
    uint8_t fuzz[DL_MAX_REPORT_SIZE + 8];
    for (unsigned i = 0; i < 200000; ++i) {
        for (unsigned j = 0; j < sizeof(fuzz); ++j) {
            rng = rng * 1664525 + 1013904223; fuzz[j] = rng >> 24;
        }
        uint8_t id = DL_REPORT_ATTRIBUTES + i % 8;
        fuzz[0] = id;
        dl_set_feature(&s, id, fuzz, i % sizeof(fuzz));
        assert(s.requested_lamp < DL_LAMP_COUNT);
    }
    puts("PASS: enumeration, batching, all 68 lamps, bounds, control, HID padding, 200000 malformed reports");
}
