// SPDX-License-Identifier: GPL-2.0-or-later
#include "lamparray.h"
#include "protocol.h"
#include QMK_KEYBOARD_H
#include "usb_main.h"
#include "usb_types.h"
#include "usb_descriptor.h"
#include "transactions.h"
#include "wireless.h"
#include <string.h>

_Static_assert(RGB_MATRIX_LED_COUNT == DL_LAMP_COUNT, "Check Split65 LED revision");
// Shared non-boot HID interface, following Microsoft's RP2040 sample topology.
// Lamp IDs 0x20..0x25 do not collide with QMK input/output report IDs.

static dl_state_t state = {.autonomous = true};
static const dl_lamp_info_t lamp_info[DL_LAMP_COUNT] = {
#include "lamp_info.inc"
};
typedef struct PACKED { uint8_t r, g, b; } pixel_t;
typedef struct PACKED { uint8_t mode, hue, sat, val, speed, enabled, flags; } native_state_t;
typedef struct PACKED {
    uint16_t sequence;
    uint8_t start, count, active, complete;
    union { pixel_t colors[8]; native_state_t native; };
} frame_packet_t;
typedef struct PACKED { uint8_t accepted, revision, left, flags; } frame_ack_t;
_Static_assert(sizeof(frame_packet_t) <= RPC_M2S_BUFFER_SIZE, "Split RPC buffer too small");

static pixel_t local_frame[DL_LAMP_COUNT], send_frame[DL_LAMP_COUNT];
static pixel_t raw_frame[DL_LAMP_COUNT];
static pixel_t remote_pending[DL_LAMP_COUNT], remote_frame[DL_LAMP_COUNT];
static bool local_active, remote_active, was_rendering;
static uint32_t remote_last_packet, last_packet, last_frame, last_render;
static uint16_t send_sequence, receive_sequence;
static uint8_t send_offset, receive_offset;
static bool sending, send_active;
static bool raw_active;
static uint8_t raw_device;
static uint32_t raw_last_packet;
// Wireless clients renew with RGB data or 0x25, at least once per second.
// A closed controller must release the battery-powered keyboard to native RGB.
#define RAW_WIRELESS_TIMEOUT_MS 3000
static uint32_t rpc_success, rpc_failure;
static frame_ack_t remote_ack;
static native_state_t native_pending;
static bool native_dirty;
static native_state_t sent_native;
static bool native_sent;
static uint32_t last_native;
static uint16_t rpc_interval = 10;
static uint8_t _Alignas(4) feature_out[DL_MAX_REPORT_SIZE];
static uint8_t _Alignas(4) feature_in[DL_MAX_REPORT_SIZE];
typedef struct PACKED {
    uint16_t tick, value, index, length;
    uint8_t request_type, request, status, received;
} usb_trace_t;
_Static_assert(sizeof(usb_trace_t) == 12, "USB trace wire layout");
static usb_trace_t usb_trace[32];
// Preserve the original 12-byte event ABI. Extra payload details are returned
// at bytes 20..23 of the existing event query (0x31, operation 1).
typedef struct PACKED { uint16_t lamp; uint8_t report_id, bytes; } usb_trace_detail_t;
static usb_trace_detail_t usb_trace_detail[32];
static uint8_t trace_next, trace_count;
static uint32_t trace_total, trace_attributes, trace_response, trace_request, trace_rejected;

// Only called in USB callbacks. Main-loop readers take the ChibiOS lock.
static void trace_usb(USBDriver *driver, uint8_t status, uint8_t received) {
    const usb_control_request_t *p = (const void *)driver->setup;
    if ((p->bmRequestType & 0x60) != 0x20) return;
    uint16_t lamp = UINT16_MAX;
    if ((status == 1 || status == 6) && p->wValue.lbyte == DL_REPORT_RESPONSE)
        lamp = feature_in[1] | ((uint16_t)feature_in[2] << 8);
    if (status == 3 && p->wValue.lbyte == DL_REPORT_REQUEST)
        lamp = feature_out[1] | ((uint16_t)feature_out[2] << 8);
    usb_trace_detail[trace_next] = (usb_trace_detail_t){lamp, p->wValue.lbyte, received};
    usb_trace[trace_next] = (usb_trace_t){timer_read32(), p->wValue.word, p->wIndex, p->wLength,
        p->bmRequestType, p->bRequest, status, received};
    trace_next = (trace_next + 1) % 32;
    if (trace_count < 32) ++trace_count;
    ++trace_total;
    if (!status) ++trace_rejected;
    if (status == 1 && p->wValue.lbyte == DL_REPORT_ATTRIBUTES) ++trace_attributes;
    if (status == 1 && p->wValue.lbyte == DL_REPORT_RESPONSE) ++trace_response;
    if (status == 3 && p->wValue.lbyte == DL_REPORT_REQUEST) ++trace_request;
}

void split65_lamp_usb_diagnostics(uint8_t *data) {
    const uint8_t operation = data[1], index = data[2];
    memset(data, 0, 32); data[0] = 0x31; data[1] = operation;
    chSysLock();
    if (operation == 2) {
        trace_next = trace_count = 0;
        trace_total = trace_attributes = trace_response = trace_request = trace_rejected = 0;
        data[2] = 1;
    } else if (operation == 1) {
        data[2] = index;
        if (index < trace_count) {
            data[3] = 1;
            memcpy(data + 4, &usb_trace[(trace_next + 32 - trace_count + index) % 32], sizeof(usb_trace_t));
            memcpy(data + 20, &usb_trace_detail[(trace_next + 32 - trace_count + index) % 32], sizeof(usb_trace_detail_t));
        }
        memcpy(data + 16, &trace_total, 4);
    } else {
        data[2] = 'T'; data[3] = 'R'; data[4] = DL_FIRMWARE_REVISION; data[5] = trace_count;
        data[6] = 2; // status 6: WB32 driver IN-completion callback, not a host API result
        memcpy(data + 8, &trace_total, 4);
        memcpy(data + 12, &trace_attributes, 4); memcpy(data + 16, &trace_response, 4);
        memcpy(data + 20, &trace_request, 4); memcpy(data + 24, &trace_rejected, 4);
        data[28] = state.requested_lamp;
    }
    chSysUnlock();
}

// Called from ChibiOS USB callbacks. No blocking calls, GPIO, EEPROM, or RPC here.
void split65_lamp_reset(void) {
    state.autonomous = true;
    state.requested_lamp = 0;
    // An unrelated USB reset must not cancel an active radio stream.
    if (raw_device == DEVS_USB) raw_active = false;
    ++state.generation;
}

void split65_lamp_mode_changed(void) {
    split65_lamp_reset();
    raw_active = false;
}

static bool raw_transport_ready(uint8_t device) {
    if (device == DEVS_USB) return USB_DRIVER.state == USB_ACTIVE;
    return device == DEVS_2G4 && *md_getp_state() == MD_STATE_CONNECTED;
}

static bool raw_stream_live(void) {
    return raw_active && raw_device == wireless_get_current_devs() &&
        raw_transport_ready(raw_device) &&
        (raw_device == DEVS_USB || timer_elapsed32(raw_last_packet) < RAW_WIRELESS_TIMEOUT_MS);
}

static void feature_sent(USBDriver *driver) {
    // WB32 overrides generic ChibiOS EP0 handling. This callback confirms the
    // driver IN completion, not a separately observed host status packet.
    const usb_control_request_t *setup = (const void *)driver->setup;
    if (setup->bmRequestType == 0xA1 && setup->bRequest == 1 &&
        setup->wIndex == LAMPARRAY_INTERFACE && setup->wValue.hbyte == 3)
        dl_feature_sent(&state, setup->wValue.lbyte, feature_in, driver->ep0n);
    trace_usb(driver, 6, driver->ep0n);
}
static void feature_received(USBDriver *driver) {
    const usb_control_request_t *setup = (const void *)driver->setup;
    const size_t received = usbGetReceiveTransactionSizeX(driver, 0);
    bool accepted = received == setup->wLength && dl_set_feature(&state, setup->wValue.lbyte, feature_out, received);
    trace_usb(driver, accepted ? 3 : 4, received);
}
bool split65_lamp_usb_request(USBDriver *driver) {
    const usb_control_request_t *setup = (const void *)driver->setup;
    const uint8_t id = setup->wValue.lbyte;
    if (setup->wIndex != LAMPARRAY_INTERFACE) return false;
    if (setup->wValue.hbyte != 3 || !dl_report_size(id)) { trace_usb(driver, 0, 0); return false; }
    if (setup->bmRequestType == 0xA1 && setup->bRequest == 1) { // GET_REPORT / Feature
        if (!setup->wLength) { trace_usb(driver, 0, 0); return false; }
        const size_t n = dl_get_feature(&state, id, feature_in, lamp_info);
        if (!n) { trace_usb(driver, 0, 0); return false; }
        // Match TinyUSB: return the actual report, clamped to host wLength.
        size_t transfer = n < setup->wLength ? n : setup->wLength;
        usbSetupTransfer(driver, feature_in, transfer, feature_sent);
        trace_usb(driver, 1, transfer);
        return true;
    }
    if (setup->bmRequestType == 0x21 && setup->bRequest == 9 &&
        (id == DL_REPORT_REQUEST || id == DL_REPORT_MULTI || id == DL_REPORT_RANGE || id == DL_REPORT_CONTROL) &&
        setup->wLength >= dl_report_size(id) && setup->wLength <= sizeof(feature_out)) {
        memset(feature_out, 0, sizeof(feature_out));
        usbSetupTransfer(driver, feature_out, setup->wLength, feature_received);
        trace_usb(driver, 2, 0);
        return true;
    }
    trace_usb(driver, 0, 0); return false;
}

static void receive_frame(uint8_t n, const void *data, uint8_t out_n, void *out) {
    if (out_n != sizeof(frame_ack_t)) return;
    frame_ack_t *ack = out;
    *ack = (frame_ack_t){.revision = DL_SPLIT_PROTOCOL_REVISION, .left = is_keyboard_left()};
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

void split65_lamp_init(void) {
    transaction_register_rpc(USER_SYNC_LAMPARRAY, receive_frame);
}

bool split65_lamp_active(void) {
    chSysLock();
    bool wired = wireless_get_current_devs() == DEVS_USB && USB_DRIVER.state == USB_ACTIVE;
    bool active = is_keyboard_master() ? (wired && !state.autonomous) || raw_stream_live() : remote_active;
    chSysUnlock();
    return active;
}

bool split65_lamp_stream(uint8_t start, uint8_t count, const uint8_t *rgb) {
    const uint8_t device = wireless_get_current_devs();
    if (!count || count > 9 || (unsigned)start + count > DL_LAMP_COUNT || !raw_transport_ready(device)) return false;
    chSysLock();
    memcpy(raw_frame + start, rgb, count * sizeof(pixel_t));
    raw_device = device;
    raw_last_packet = timer_read32();
    raw_active = true;
    chSysUnlock();
    return true;
}

bool split65_lamp_raw_enable(bool enable) {
    const uint8_t device = wireless_get_current_devs();
    if (enable && !raw_transport_ready(device)) return false;
    chSysLock();
    raw_active = enable;
    raw_device = device;
    raw_last_packet = timer_read32();
    chSysUnlock();
    return true;
}

void split65_lamp_diagnostics(uint8_t *data) {
    memset(data, 0, 32);
    data[0] = 0x30; data[1] = 'D'; data[2] = 'L'; data[3] = '2';
    data[4] = DL_FIRMWARE_REVISION; data[5] = 2; // Microsoft-style shared interface
    data[6] = (is_keyboard_master() ? 1 : 0) | (is_keyboard_left() ? 2 : 0);
    data[7] = remote_ack.revision; data[8] = DL_LAMP_COUNT;
    // Backend 0: preserve the upstream complete 68-entry WS2812 address space.
    data[9] = 0;
    for (unsigned i = 0; i < 4; ++i) {
        data[10 + i] = rpc_success >> (i * 8);
        data[14 + i] = rpc_failure >> (i * 8);
    }
    data[18] = remote_ack.flags; data[19] = state.autonomous;
    data[20] = raw_active; data[21] = wireless_get_current_devs();
    data[22] = rgb_matrix_is_enabled(); data[23] = rgb_matrix_get_mode();
    data[24] = remote_ack.left;
    data[25] = rpc_interval / 10;
    data[26] = 1; // Raw stream protocol: USB + 2.4G, renewable wireless lease.
    data[27] = raw_device;
    data[28] = RAW_WIRELESS_TIMEOUT_MS / 1000;
    data[29] = *md_getp_state();
}

void split65_lamp_suspend_slave(void) {
    // Invoked by the existing slave suspend RPC, which is already serialized
    // against the main loop by the split transport. Do not acquire nested locks.
    remote_active = false;
}

void split65_lamp_task(void) {
    if (!is_keyboard_master()) {
        native_state_t native;
        bool apply;
        chSysLock();
        if (timer_elapsed32(remote_last_packet) > 2000) remote_active = false;
        apply = native_dirty;
        native = native_pending;
        native_dirty = false;
        chSysUnlock();
        // Fn+Tab and VIA change the master's native configuration. Mirror it
        // explicitly without relying on RGB_MATRIX_SPLIT's LED remapping.
        if (apply) {
            if (rgb_matrix_get_mode() != native.mode) rgb_matrix_mode_noeeprom(native.mode);
            if (rgb_matrix_get_hue() != native.hue || rgb_matrix_get_sat() != native.sat || rgb_matrix_get_val() != native.val)
                rgb_matrix_sethsv_noeeprom(native.hue, native.sat, native.val);
            if (rgb_matrix_get_speed() != native.speed) rgb_matrix_set_speed_noeeprom(native.speed);
            if (rgb_matrix_get_flags() != native.flags) rgb_matrix_set_flags_noeeprom(native.flags);
            if (rgb_matrix_is_enabled() != native.enabled) {
                if (native.enabled) rgb_matrix_enable_noeeprom();
                else rgb_matrix_disable_noeeprom();
            }
            if (native.enabled && native.val) {
                gpio_write_pin_high(LED_POWER_EN_PIN);
                gpio_write_pin_high(LED_POWER_EN2_PIN);
            } else {
                gpio_write_pin_low(LED_POWER_EN_PIN);
                gpio_write_pin_low(LED_POWER_EN2_PIN);
            }
        }
        return;
    }
    dl_color_t snapshot[DL_LAMP_COUNT];
    bool lamp_owned;
    chSysLock();
    lamp_owned = !state.autonomous && wireless_get_current_devs() == DEVS_USB && USB_DRIVER.state == USB_ACTIVE;
    if (raw_active && !raw_stream_live()) raw_active = false;
    local_active = lamp_owned || raw_active;
    memcpy(snapshot, state.visible, sizeof(snapshot));
    memcpy(local_frame, raw_frame, sizeof(local_frame));
    chSysUnlock();
    for (unsigned i = 0; i < DL_LAMP_COUNT; ++i) {
        const dl_color_t c = lamp_owned ? snapshot[i] : (dl_color_t){local_frame[i].r, local_frame[i].g, local_frame[i].b, 255};
        const unsigned limit = RGB_MATRIX_MAXIMUM_BRIGHTNESS;
        local_frame[i] = (pixel_t){(uint32_t)c.r * limit / 255,
                                  (uint32_t)c.g * limit / 255,
                                  (uint32_t)c.b * limit / 255};
    }
    // Freeze each frame before sending. One <=30-byte RPC per 10 ms, keeping
    // individual inter-half blocking transfers short at the factory 115200 baud.
    const native_state_t current_native = {rgb_matrix_get_mode(), rgb_matrix_get_hue(), rgb_matrix_get_sat(),
        rgb_matrix_get_val(), rgb_matrix_get_speed(), rgb_matrix_is_enabled(), rgb_matrix_get_flags()};
    if (sending && send_active != local_active) sending = false;
    // Autonomous effects need state changes plus a slow keepalive, not a
    // blocking split RPC every 100 ms while the user is typing.
    if (!local_active && !sending && send_active == local_active && native_sent &&
        !memcmp(&current_native, &sent_native, sizeof(current_native)) && timer_elapsed32(last_native) < 500) return;
    if (!sending && (timer_elapsed32(last_frame) >= DL_INTERVAL_US / 1000 || send_active != local_active)) {
        memcpy(send_frame, local_frame, sizeof(send_frame));
        send_active = local_active;
        ++send_sequence;
        send_offset = 0;
        sending = true;
        last_frame = timer_read32();
    }
    if (!sending || timer_elapsed32(last_packet) < rpc_interval) return;
    last_packet = timer_read32();
    frame_packet_t p = {.sequence = send_sequence, .start = send_offset, .active = send_active};
    if (send_active) {
        p.count = DL_LAMP_COUNT - send_offset;
        if (p.count > 8) p.count = 8;
        memcpy(p.colors, send_frame + send_offset, p.count * sizeof(pixel_t));
        p.complete = send_offset + p.count == DL_LAMP_COUNT;
    } else {
        p.native = current_native;
    }
    frame_ack_t ack = {0};
    if (transaction_rpc_exec(USER_SYNC_LAMPARRAY, sizeof(p), &p, sizeof(ack), &ack) && ack.accepted && ack.revision == DL_SPLIT_PROTOCOL_REVISION) {
        remote_ack = ack;
        ++rpc_success;
        rpc_interval = 10;
        if (!send_active) { sent_native = p.native; native_sent = true; last_native = timer_read32(); }
        send_offset += p.count;
        if (!send_active || p.complete) sending = false;
    } else {
        ++rpc_failure;
        // Failed lighting synchronization must not retry a 20 ms UART timeout
        // every 10 ms and repeatedly delay matrix scanning in either mode.
        rpc_interval = rpc_interval < 100 ? 100 : (rpc_interval < 500 ? rpc_interval * 2 : 1000);
        // Restart a complete frozen frame after a lost/rebooted slave.
        send_offset = 0;
        ++send_sequence;
    }
}

bool split65_lamp_render(void) {
    pixel_t frame[DL_LAMP_COUNT];
    bool active;
    if (is_keyboard_master()) {
        active = split65_lamp_active();
        memcpy(frame, local_frame, sizeof(frame));
    } else {
        chSysLock();
        active = remote_active;
        memcpy(frame, remote_frame, sizeof(frame));
        chSysUnlock();
    }
    if (!active) {
        if (was_rendering) {
            // Start a fresh native effect, including static effects that otherwise
            // do not redraw after our overlay has replaced their LED buffer.
            rgb_matrix_mode_noeeprom(rgb_matrix_get_mode());
            if (!rgb_matrix_is_enabled()) {
                gpio_write_pin_low(LED_POWER_EN_PIN);
                gpio_write_pin_low(LED_POWER_EN2_PIN);
            }
        }
        was_rendering = false;
        return false;
    }
    was_rendering = true;
    if (timer_elapsed32(last_render) >= 10 && !rgb_matrix_get_suspend_state()) {
        last_render = timer_read32();
        gpio_write_pin_high(LED_POWER_EN_PIN);
        gpio_write_pin_high(LED_POWER_EN2_PIN);
        for (unsigned i = 0; i < DL_LAMP_COUNT; ++i) rgb_matrix_set_color(i, frame[i].r, frame[i].g, frame[i].b);
        rgb_matrix_update_pwm_buffers();
    }
    return true;
}
