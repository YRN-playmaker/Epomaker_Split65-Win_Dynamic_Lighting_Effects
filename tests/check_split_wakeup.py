#!/usr/bin/env python3
"""Run the production sleep/wake callbacks and timeout policy with fake hardware."""
import os
import subprocess
import tempfile
from pathlib import Path

root = Path(__file__).resolve().parents[1]
keyboard = (root / 'epomaker_split65.c').read_text()
wls = (root / 'wls/wls.c').read_text()
lowpower = (root / 'linker/wireless/lowpower.c').read_text()

def function(source, declaration):
    a = source.index(declaration + ' {')
    b = source.index('{', a)
    depth, end = 1, b + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[a:end]

test = r'''
#include <stdint.h>
#include <stdbool.h>
#include <string.h>
#include <assert.h>
#define SPLIT65_LAMPARRAY_ENABLE
#define DEVS_USB 0
#define USB_ACTIVE 1
#define USB_SUSPENDED 2
#define USER_SYNC_MMS 10
#define SERIAL_USART_RX_PIN 10
#define SERIAL_USART_TX_PIN 9
#define PAL_OUTPUT_TYPE_OPENDRAIN 1
#define A5 5
#define A8 8
#define dprintf(...) ((void)0)
#define dprint(...) ((void)0)
typedef struct { uint8_t cmd, body[4]; } master_to_slave_t;
typedef struct { uint8_t resp, body[4]; } slave_to_master_t;
enum { LPWR_NORMAL, LPWR_PRESLEEP, LPWR_STOP, LPWR_WAKEUP };
enum { LPWR_WAKEUP_USB, LPWR_WAKEUP_CABLE, LPWR_WAKEUP_UART };
static struct { unsigned devs; } confinfo;
static struct { unsigned state; } usb;
#define USB_DRIVER usb
static bool master, manual_timeout, lamp_active, lower_sleep;
static uint32_t now, split_peer_activity, idle, timeout = 300000;
static bool split_sleep_requested;
static unsigned mode, state, wake_reason, rpc_count, pin_init, master_init, slave_init, clears;
static uint32_t timer_read32(void) { return now; }
static uint32_t timer_elapsed32(uint32_t t) { return now - t; }
static bool is_keyboard_master(void) { return master; }
static bool split65_lamp_active(void) { return lamp_active; }
static unsigned wireless_get_current_devs(void) { return mode; }
static void wireless_devs_change(unsigned old, unsigned current, bool reset) { (void)old; (void)reset; mode = current; }
static void last_matrix_activity_trigger(void) { idle = 0; }
static unsigned lpwr_get_state(void) { return state; }
static void lpwr_set_state(unsigned s) { state = s; }
static void lpwr_set_timeout_manual(bool value) { manual_timeout = value; }
static uint32_t lpwr_timeout_value_read(void) { return timeout; }
static uint32_t last_input_activity_elapsed(void) { return idle; }
static unsigned lpwr_get_sleep_wakeupcd(void) { return wake_reason; }
static void usart_init(void) { ++pin_init; }
static void serial_transport_driver_master_init(void) { ++master_init; }
static void serial_transport_driver_slave_init(void) { ++slave_init; }
static void serial_transport_driver_clear(void) { ++clears; }
static void palSetLineMode(unsigned pin, unsigned value) { (void)pin; (void)value; }
static void gpio_write_pin_low(unsigned pin) { (void)pin; }
static void gpio_write_pin_high(unsigned pin) { (void)pin; }
static void split65_lamp_suspend_slave(void) { lamp_active = false; }
static void hs_reset_settings(void) {}
static bool transaction_rpc_exec(unsigned id, unsigned n, const void *p, unsigned out_n, void *out) {
    // This is the failure in the old wake callback: a target starts an
    // initiator transaction while its receive thread owns the same UART.
    assert(master && id == USER_SYNC_MMS && n == 5 && out_n == 5);
    assert(((const master_to_slave_t *)p)->cmd != 0);
    memset(out, 0, out_n); ++rpc_count; return true;
}
FUNCTIONS
int main(void) {
    master = false;
    suspend_power_down_user(); assert(rpc_count == 0);
    master_sync_mms_slave(0, 0, 0); assert(rpc_count == 0);
    for (unsigned dev = 0; dev < 2; ++dev) {
        mode = confinfo.devs = dev; usb.state = USB_SUSPENDED;
        for (unsigned i = 0; i < 10000; ++i) {
            now += 10000; state = LPWR_STOP; manual_timeout = true;
            split_sleep_requested = true;
            unsigned before = slave_init;
            lpwr_stop_hook_post(); assert(slave_init == before + 1 && clears);
            suspend_wakeup_init_user();
            assert(pin_init && !manual_timeout && !split_sleep_requested && !rpc_count);
            manual_timeout = true;
            assert(!lpwr_is_allow_timeout() && !manual_timeout);
            // Periodic heartbeat prevents an unrelated radio/USB suspend
            // notification from sleeping the target during active use.
            master_to_slave_t p = {.cmd = 0x55, .body = {0, dev}};
            now += 2001; state = LPWR_PRESLEEP; apply_mms_command(&p);
            assert(state == LPWR_NORMAL && !split_sleep_requested);
            manual_timeout = true; assert(!lpwr_is_allow_timeout());
            // Explicit coordinated sleep still works, even with RGB owned.
            p.cmd = 0xAA; apply_mms_command(&p); lamp_active = true;
            assert(lpwr_is_allow_timeout());
            p.cmd = 0xCC; state = LPWR_PRESLEEP; apply_mms_command(&p);
            assert(state == LPWR_NORMAL && !split_sleep_requested && !manual_timeout);
            lamp_active = false;
        }
    }
    now += 5001; manual_timeout = true; assert(lpwr_is_allow_timeout());
    // Lost peer: preserve ordinary battery idle sleep and timer wrap behavior.
    idle = timeout; assert(lpwr_is_allow_timeout()); idle = 0;
    split_peer_activity = UINT32_MAX - 1000; now = 1000;
    manual_timeout = true; assert(!lpwr_is_allow_timeout());
    master = true; mode = DEVS_USB; assert(!lpwr_is_allow_timeout());
    suspend_power_down_user(); suspend_wakeup_init_user(); assert(rpc_count == 2);
    lpwr_stop_hook_post(); assert(master_init == 1);
}
'''
declarations = [
    'void master_sync_mms_slave(uint8_t last_mode, uint8_t now_mode, uint8_t reset)',
    'void suspend_wakeup_init_user(void)',
    'void suspend_power_down_user(void)',
    'bool lpwr_is_allow_timeout_hook(void)',
    'static void apply_mms_command(const master_to_slave_t *m2s)',
]
functions = '\n'.join(function(keyboard, d) for d in declarations)
functions += '\n' + function(lowpower, 'bool lpwr_is_allow_timeout(void)')
functions += '\n' + function(wls, 'void lpwr_stop_hook_post(void)')
with tempfile.TemporaryDirectory() as directory:
    p = Path(directory)
    (p / 'test.c').write_text(test.replace('FUNCTIONS', functions))
    subprocess.run(['gcc', '-std=gnu11', '-Wall', '-Wextra', '-Werror', '-g',
                    '-fsanitize=address,undefined', str(p / 'test.c'), '-o', str(p / 'test')], check=True)
    subprocess.run([p / 'test'], check=True, env={**os.environ, 'ASAN_OPTIONS': 'detect_leaks=0'})
print('PASS: production target callbacks never initiate RPC; 20,000 USB/radio sleep-wake cycles, UART restart, peer sleep coordination, idle/wrap behavior')
