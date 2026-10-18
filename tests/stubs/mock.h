// SPDX-License-Identifier: GPL-2.0-or-later
#pragma once
#include <stdbool.h>
#include <stdint.h>
#include <stddef.h>
#include <assert.h>
#include "../../linker/wireless/module.h"
#include "../../linker/wireless/transport.h"
#define PACKED __attribute__((packed))
#define RGB_MATRIX_LED_COUNT 68
#define RGB_MATRIX_MAXIMUM_BRIGHTNESS 150
#define RPC_M2S_BUFFER_SIZE 32
#define REPORT_ID_COUNT 9
#define SHARED_INTERFACE 1
#define LAMPARRAY_INTERFACE 0
#define USER_SYNC_LAMPARRAY 10
#define LED_POWER_EN_PIN 5
#define LED_POWER_EN2_PIN 8
#define WIRELESS_ENABLE
#define RAW_ENABLE 1
#define RAW_EPSIZE 32
#define USB_ENDPOINT_IN_RAW 1
#define USB_ACTIVE 1
typedef struct USBDriver {
    uint8_t setup[8];
    unsigned state;
    size_t received, ep0n;
    uint8_t *buffer;
    size_t length;
    void (*callback)(struct USBDriver *);
} USBDriver;
typedef struct PACKED {
    uint8_t bmRequestType, bRequest;
    union { struct {uint8_t lbyte, hbyte;}; uint16_t word; } wValue;
    uint16_t wIndex, wLength;
} usb_control_request_t;
extern USBDriver mock_usb;
#define USB_DRIVER mock_usb
extern bool mock_master, mock_enabled, mock_suspended, mock_locked, mock_rpc_fail, mock_old_revision;
extern uint8_t mock_native[2][7], mock_raw_reply[32];
extern unsigned mock_device, mock_mode_reset, mock_power_on, mock_flush;
extern unsigned mock_usb_replies, mock_wireless_replies;
extern uint8_t mock_radio_state;
extern uint32_t mock_clock;
extern uint8_t mock_pixels[68][3];
extern void (*mock_rpc)(uint8_t, const void *, uint8_t, void *);
static inline bool is_keyboard_master(void) { return mock_master; }
static inline bool is_keyboard_left(void) { return mock_master; }
static inline uint32_t timer_read32(void) { return mock_clock; }
static inline uint32_t timer_elapsed32(uint32_t t) { return mock_clock - t; }
static inline void chSysLock(void) { assert(!mock_locked); mock_locked = true; }
static inline void chSysUnlock(void) { assert(mock_locked); mock_locked = false; }
uint8_t wireless_get_current_devs(void);
static inline bool rgb_matrix_is_enabled(void) { return mock_native[!mock_master][5]; }
static inline bool rgb_matrix_get_suspend_state(void) { return mock_suspended; }
static inline unsigned rgb_matrix_get_mode(void) { return mock_native[!mock_master][0]; }
static inline void rgb_matrix_mode_noeeprom(unsigned m) { mock_native[!mock_master][0] = m; ++mock_mode_reset; }
static inline unsigned rgb_matrix_get_hue(void) { return mock_native[!mock_master][1]; }
static inline unsigned rgb_matrix_get_sat(void) { return mock_native[!mock_master][2]; }
static inline unsigned rgb_matrix_get_val(void) { return mock_native[!mock_master][3]; }
static inline unsigned rgb_matrix_get_speed(void) { return mock_native[!mock_master][4]; }
static inline unsigned rgb_matrix_get_flags(void) { return mock_native[!mock_master][6]; }
static inline void rgb_matrix_sethsv_noeeprom(uint8_t h, uint8_t s, uint8_t v) {
    mock_native[!mock_master][1] = h; mock_native[!mock_master][2] = s; mock_native[!mock_master][3] = v;
}
static inline void rgb_matrix_set_speed_noeeprom(uint8_t v) { mock_native[!mock_master][4] = v; }
static inline void rgb_matrix_set_flags_noeeprom(uint8_t v) { mock_native[!mock_master][6] = v; }
static inline void rgb_matrix_enable_noeeprom(void) { mock_native[!mock_master][5] = 1; }
static inline void rgb_matrix_disable_noeeprom(void) { mock_native[!mock_master][5] = 0; }
static inline void raw_hid_send(uint8_t *p, uint8_t n) { assert(n == 32); ++mock_usb_replies; for (unsigned i = 0; i < n; ++i) mock_raw_reply[i] = p[i]; }
static inline void send_report(unsigned endpoint, uint8_t *p, uint8_t n) { assert(endpoint == USB_ENDPOINT_IN_RAW); raw_hid_send(p, n); }
static inline void gpio_write_pin_low(unsigned p) { (void)p; }
static inline void gpio_write_pin_high(unsigned p) { (void)p; ++mock_power_on; }
static inline void rgb_matrix_update_pwm_buffers(void) { ++mock_flush; }
static inline void rgb_matrix_set_color(unsigned i, uint8_t r, uint8_t g, uint8_t b) {
    assert(i < 68); mock_pixels[i][0] = r; mock_pixels[i][1] = g; mock_pixels[i][2] = b;
}
static inline void transaction_register_rpc(unsigned id, void (*f)(uint8_t, const void *, uint8_t, void *)) {
    (void)id; mock_rpc = f;
}
static inline bool transaction_rpc_exec(unsigned id, uint8_t n, const void *p, uint8_t out_n, void *out) {
    (void)id; assert(mock_master); mock_master = false;
    if (mock_rpc_fail) { mock_master = true; return false; }
    mock_rpc(n, p, out_n, out);
    if (mock_old_revision && out_n >= 2) ((uint8_t *)out)[1] = 0x40;
    mock_master = true; return true;
}
static inline size_t usbGetReceiveTransactionSizeX(USBDriver *d, unsigned ep) { (void)ep; return d->received; }
static inline void usbSetupTransfer(USBDriver *d, uint8_t *p, size_t n, void (*f)(USBDriver *)) {
    d->ep0n = n; d->buffer = p; d->length = n; d->callback = f;
}
