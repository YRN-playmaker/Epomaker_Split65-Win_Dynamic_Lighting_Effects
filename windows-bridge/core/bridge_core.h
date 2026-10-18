// SPDX-License-Identifier: GPL-2.0-or-later
#pragma once
#include "../../dynamic_lighting/protocol.h"

#define SB_SNAPSHOT_SIZE 220
#define SB_FRAME_INTERVAL_MS 100
typedef struct { dl_state_t lamps; bool attached; } sb_state_t;
void sb_init(sb_state_t *state);
void sb_attach(sb_state_t *state, bool attached);
bool sb_get_feature(sb_state_t *state, uint8_t id, uint8_t *buffer, size_t capacity, size_t *written);
bool sb_set_feature(sb_state_t *state, uint8_t id, const uint8_t *buffer, size_t length);
void sb_snapshot(const sb_state_t *state, uint8_t buffer[SB_SNAPSHOT_SIZE]);
