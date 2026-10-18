// SPDX-License-Identifier: GPL-2.0-or-later
// Exact callback from delivered 0.4.4, renamed only for the compatibility test.
// Source SHA256: 2cb571f982f92bbcdcfb63777310d009f32d473830ab1cdda848dc35a322fb86
static void receive_frame_v044(uint8_t n, const void *data, uint8_t out_n, void *out) {
    if (out_n != sizeof(frame_ack_t)) return;
    frame_ack_t *ack = out;
    *ack = (frame_ack_t){.revision = 0x44, .left = is_keyboard_left()};
    if (n != sizeof(frame_packet_t)) return;
    const frame_packet_t *p = data;
    if (p->active > 1 || p->complete > 1 || p->count > 8 || p->start + p->count > DL_LAMP_COUNT) return;
    if (!p->active) {
        if (p->native.enabled > 1) return;
        remote_active = false;
        receive_offset = 0;
        native_pending = p->native;
        native_dirty = true;
    } else {
        if (!p->count) return;
        if (!p->start) { receive_sequence = p->sequence; receive_offset = 0; }
        if (receive_sequence != p->sequence || p->start > receive_offset) return;
        memcpy(remote_pending + p->start, p->colors, p->count * sizeof(pixel_t));
        if (receive_offset < p->start + p->count) receive_offset = p->start + p->count;
        if (p->complete) {
            if (receive_offset != DL_LAMP_COUNT) return;
            memcpy(remote_frame, remote_pending, sizeof(remote_frame));
            remote_active = true;
        }
    }
    remote_last_packet = timer_read32();
    ack->accepted = 1;
    ack->flags = remote_active ? 1 : 0;
}
