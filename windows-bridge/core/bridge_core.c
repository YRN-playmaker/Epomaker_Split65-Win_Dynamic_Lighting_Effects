// SPDX-License-Identifier: GPL-2.0-or-later
#include "bridge_core.h"
#include <string.h>
static const dl_lamp_info_t lamps[DL_LAMP_COUNT] = {
#include "../../dynamic_lighting/lamp_info.inc"
};
static void put32(uint8_t *p, uint32_t value) {
    for (unsigned i = 0; i < 4; ++i) p[i] = (uint8_t)(value >> (8 * i));
}
void sb_init(sb_state_t *s) { dl_init(&s->lamps); s->attached = false; }
void sb_attach(sb_state_t *s, bool attached) {
    dl_init(&s->lamps);
    s->attached = attached;
    ++s->lamps.generation;
}
bool sb_get_feature(sb_state_t *s, uint8_t id, uint8_t *buffer, size_t capacity, size_t *written) {
    uint8_t report[DL_MAX_REPORT_SIZE];
    *written = 0;
    if (!s->attached || !buffer || capacity > DL_MAX_REPORT_SIZE) return false;
    size_t size = dl_get_feature(&s->lamps, id, report, lamps);
    if (!size || capacity < size) return false;
    // Advertise the tested scheduling target; actual radio throughput may be lower.
    if (id == DL_REPORT_ATTRIBUTES) put32(report + 19, SB_FRAME_INTERVAL_MS * 1000u);
    if (id == DL_REPORT_RESPONSE) put32(report + 15, SB_FRAME_INTERVAL_MS * 1000u);
    memset(buffer, 0, capacity);
    memcpy(buffer, report, size);
    dl_feature_sent(&s->lamps, id, report, size);
    *written = size;
    return true;
}
bool sb_set_feature(sb_state_t *s, uint8_t id, const uint8_t *buffer, size_t length) {
    return s->attached && buffer && dl_set_feature(&s->lamps, id, buffer, length);
}
void sb_snapshot(const sb_state_t *s, uint8_t buffer[SB_SNAPSHOT_SIZE]) {
    memset(buffer, 0, SB_SNAPSHOT_SIZE);
    memcpy(buffer, "SLB1", 4);
    buffer[4] = 1;
    buffer[6] = (s->attached && !s->lamps.autonomous ? 1 : 0) | (s->attached ? 2 : 0);
    put32(buffer + 8, s->lamps.generation);
    put32(buffer + 12, SB_FRAME_INTERVAL_MS);
    for (unsigned i = 0; i < DL_LAMP_COUNT; ++i) {
        const dl_color_t c = s->lamps.visible[i];
        buffer[16 + i * 3] = dl_channel(c.r, c.intensity);
        buffer[17 + i * 3] = dl_channel(c.g, c.intensity);
        buffer[18 + i * 3] = dl_channel(c.b, c.intensity);
    }
}
