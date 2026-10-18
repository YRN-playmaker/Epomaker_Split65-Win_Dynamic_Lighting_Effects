// SPDX-License-Identifier: GPL-2.0-or-later
#include "mock.h"
#include <stdio.h>
#include <string.h>
USBDriver mock_usb = {.state = USB_ACTIVE};
bool mock_master = true, mock_enabled = true, mock_suspended, mock_locked;
bool mock_rpc_fail, mock_old_revision;
uint8_t mock_native[2][7] = {{1, 0, 255, 150, 128, 1, 0xFF}, {1, 0, 255, 150, 128, 1, 0xFF}};
uint8_t mock_raw_reply[32];
unsigned mock_device, mock_mode_reset, mock_power_on, mock_flush;
unsigned mock_usb_replies, mock_wireless_replies;
uint8_t mock_radio_state = MD_STATE_CONNECTED;
uint8_t wireless_get_current_devs(void) { return mock_device; }
uint8_t *md_getp_state(void) { return &mock_radio_state; }
transport_t get_transport(void) { return mock_device == DEVS_USB ? TRANSPORT_USB : TRANSPORT_WLS; }
void md_send_raw(uint8_t *p, uint8_t n) { assert(n == 32); ++mock_wireless_replies; memcpy(mock_raw_reply, p, n); }
uint32_t mock_clock;
uint8_t mock_pixels[68][3];
void (*mock_rpc)(uint8_t, const void *, uint8_t, void *);
// Include the production adapter, so the test exercises USB and split callbacks.
#include "../dynamic_lighting/lamparray.c"
#include "../dynamic_lighting/compat.c"
#include "../linker/wireless/md_raw.c"
void raw_hid_receive(uint8_t *data, uint8_t length) { assert(via_command_kb(data, length)); }
#ifdef SPLIT65_TEST_SLAVE_V044
#    include "fixtures/receive_frame_v0.4.4.inc"
#endif

static void set_request(uint8_t id, const uint8_t *p, size_t n) {
    usb_control_request_t setup = {.bmRequestType = 0x21, .bRequest = 9,
        .wValue = {.word = 0x300 | id}, .wIndex = LAMPARRAY_INTERFACE, .wLength = n};
    memcpy(mock_usb.setup, &setup, 8);
    assert(split65_lamp_usb_request(&mock_usb));
    assert(mock_usb.length == n && mock_usb.callback);
    memcpy(mock_usb.buffer, p, n); mock_usb.received = n; mock_usb.callback(&mock_usb);
}
int main(void) {
    split65_lamp_init();
#ifdef SPLIT65_TEST_SLAVE_V044
    // Run the 0.4.7 master against the actual delivered 0.4.4 target callback.
    mock_rpc = receive_frame_v044;
#endif
    assert(!split65_lamp_active());
    uint8_t control[] = {DL_REPORT_CONTROL, 0};
    set_request(control[0], control, 2);
    split65_lamp_task();
    assert(split65_lamp_active());
    uint8_t range[] = {DL_REPORT_RANGE, 1, 0, 0, 67, 0, 255, 0, 0, 255};
    set_request(range[0], range, 10);
    // It takes nine small packets to commit an entire 68-lamp frame.
    for (unsigned i = 1; i <= 18; ++i) {
        mock_clock = i * 10; split65_lamp_task();
        if (i < 9) assert(!remote_active);
        if (i == 9) assert(remote_active && remote_frame[0].r == 0);
    }
    assert(remote_active && remote_frame[0].r == 150 && remote_frame[67].r == 150);
    assert(split65_lamp_render() && mock_flush == 1 && mock_pixels[67][0] == 150);
    mock_master = false;
    assert(split65_lamp_active());
    mock_clock += 2100; split65_lamp_task();
    assert(!split65_lamp_active()); // Transport watchdog releases control.
    remote_active = true; split65_lamp_suspend_slave(); assert(!remote_active);
    mock_master = true;
    control[1] = 1; set_request(control[0], control, 2);
    assert(!split65_lamp_render() && mock_mode_reset == 1);
    // A malformed setup is rejected before receiving into any buffer.
    usb_control_request_t invalid = {.bmRequestType = 0x21, .bRequest = 9,
        .wValue = {.word = 0x300 | DL_REPORT_MULTI}, .wIndex = LAMPARRAY_INTERFACE, .wLength = 65535};
    memcpy(mock_usb.setup, &invalid, 8); assert(!split65_lamp_usb_request(&mock_usb));
    invalid.wLength = 51; invalid.wIndex = 5;
    memcpy(mock_usb.setup, &invalid, 8); assert(!split65_lamp_usb_request(&mock_usb));
    invalid.wIndex = LAMPARRAY_INTERFACE; invalid.wValue.hbyte = 2;
    memcpy(mock_usb.setup, &invalid, 8); assert(!split65_lamp_usb_request(&mock_usb));
    // GET_REPORT routes feature reports without affecting QMK keyboard reports.
    invalid = (usb_control_request_t){.bmRequestType = 0xA1, .bRequest = 1,
        .wValue = {.word = 0x300 | DL_REPORT_ATTRIBUTES}, .wIndex = LAMPARRAY_INTERFACE, .wLength = 51};
    memcpy(mock_usb.setup, &invalid, 8); assert(split65_lamp_usb_request(&mock_usb));
    assert(mock_usb.length == 23 && mock_usb.buffer[1] == 68);
    // The WB32 driver completion callback is distinct from merely arming IN.
    // The test does not simulate the USB controller or Windows completion.
    assert(mock_usb.callback == feature_sent);
    unsigned trace_before = trace_total;
    mock_usb.callback(&mock_usb);
    assert(trace_total == trace_before + 1);
    assert(usb_trace[(trace_next + 31) % 32].status == 6);
    assert(usb_trace[(trace_next + 31) % 32].received == 23);
    // Record the actual lamp ID in the reply, not the already advanced cursor.
    state.requested_lamp = 67; invalid.wValue.lbyte = DL_REPORT_RESPONSE;
    memcpy(mock_usb.setup, &invalid, 8); assert(split65_lamp_usb_request(&mock_usb));
    mock_usb.callback(&mock_usb);
    assert(usb_trace_detail[(trace_next + 31) % 32].lamp == 67);
    assert(usb_trace_detail[(trace_next + 31) % 32].bytes == 29);
    assert(state.requested_lamp == 0);
    // Return each report's actual length like TinyUSB, clamped to host wLength.
    // Oversized requests cannot expose RAM beyond that report.
    const uint8_t ids[] = {DL_REPORT_ATTRIBUTES, DL_REPORT_RESPONSE, DL_REPORT_CONTROL};
    const unsigned lengths[] = {0, 1, 2, 23, 29, 51, 64, 65535};
    for (unsigned i = 0; i < 3; ++i) for (unsigned j = 0; j < 8; ++j) {
        invalid.wValue.lbyte = ids[i]; invalid.wLength = lengths[j];
        memcpy(mock_usb.setup, &invalid, 8);
        bool accepted = split65_lamp_usb_request(&mock_usb);
        assert(accepted == !!lengths[j]);
        if (!accepted) continue;
        unsigned size = dl_report_size(ids[i]);
        unsigned actual = lengths[j] > size ? size : lengths[j];
        assert(mock_usb.length == actual && mock_usb.callback == feature_sent);
        for (unsigned k = dl_report_size(ids[i]); k < actual; ++k) assert(!mock_usb.buffer[k]);
        mock_usb.callback(&mock_usb);
    }
    // Actual USB adapter: length probes and aborted INs must not consume IDs.
    invalid.wValue.lbyte = DL_REPORT_RESPONSE;
    for (unsigned length = 0; length < 29; ++length) {
        state.requested_lamp = 17;
        invalid.wLength = length;
        memcpy(mock_usb.setup, &invalid, 8);
        bool accepted = split65_lamp_usb_request(&mock_usb);
        assert(accepted == !!length && state.requested_lamp == 17);
        if (accepted) {
            assert(mock_usb.buffer[1] == 17);
            mock_usb.callback(&mock_usb);
            assert(state.requested_lamp == 17);
        }
    }
    state.requested_lamp = 17; invalid.wLength = 51;
    for (unsigned retry = 0; retry < 100; ++retry) {
        memcpy(mock_usb.setup, &invalid, 8);
        assert(split65_lamp_usb_request(&mock_usb));
        assert(mock_usb.buffer[1] == 17 && state.requested_lamp == 17);
        // Aborted: deliberately do not run the controller completion callback.
    }
    for (unsigned index = 0; index < 136; ++index) {
        assert(split65_lamp_usb_request(&mock_usb));
        assert(mock_usb.buffer[1] == (17 + index) % 68);
        assert(state.requested_lamp == (17 + index) % 68);
        mock_usb.callback(&mock_usb);
        assert(state.requested_lamp == (18 + index) % 68);
    }
    state.requested_lamp = 53;
    split65_lamp_reset();
    assert(state.requested_lamp == 0);
    // Standard VIA commands must reach QMK unchanged; only our extension and
    // legacy stream are handled here. Manual lighting releases raw streaming.
    uint8_t via[32] = {1};
    assert(!via_command_kb(via, sizeof(via)) && via[0] == 1);
    via[0] = 0x24; via[1] = 67; via[2] = 1; via[3] = 0; via[4] = 255; via[5] = 0;
    assert(via_command_kb(via, sizeof(via)) && raw_active);
    mock_clock += 100; split65_lamp_task();
    assert(local_frame[67].g == 150 && local_frame[67].r == 0);
    via[1] = 67; via[2] = 2;
    assert(via_command_kb(via, sizeof(via)) && via[1] == 253);
    via[0] = 7; via[1] = 3; via[2] = 2; via[3] = 9;
    assert(!via_command_kb(via, sizeof(via)) && !raw_active && via[3] == 9);
    // Native mode, HSV, brightness, enable, speed and flags cross the link.
    memcpy(mock_native[0], (uint8_t[]){9, 25, 220, 70, 42, 0, 3}, 7);
    mock_clock += 100; split65_lamp_task();
    mock_master = false; split65_lamp_task(); mock_master = true;
    assert(!memcmp(mock_native[0], mock_native[1], 7));
    unsigned reset_count = mock_mode_reset;
    uint32_t successes = rpc_success;
    mock_clock += 100; split65_lamp_task();
    mock_master = false; split65_lamp_task(); mock_master = true;
    assert(mock_mode_reset == reset_count && rpc_success == successes); // No redundant native RPC.
    uint32_t failures = rpc_failure;
    mock_old_revision = true; mock_clock += 500; split65_lamp_task(); mock_old_revision = false;
    assert(rpc_failure == failures + 1);
    mock_rpc_fail = true; mock_clock += 100; split65_lamp_task(); mock_rpc_fail = false;
    assert(rpc_failure == failures + 2);
    for (unsigned i = 0; i < 19; ++i) { mock_clock += 10; split65_lamp_task(); }
    assert(rpc_failure == failures + 2); // Backoff leaves the matrix loop free.
    mock_clock += 10; split65_lamp_task(); assert(rpc_success == successes + 1);
    via[0] = 0x30;
    assert(via_command_kb(via, sizeof(via)) && !memcmp(via + 1, "DL2", 3));
    assert(via[4] == 0x50 && via[7] == 0x44 && via[8] == 68 && via[24] == 0);
    // Observe actual USB transactions without changing light ownership or RGB.
    via[0] = 0x31; via[1] = 2;
    assert(via_command_kb(via, sizeof(via)) && via[2] == 1);
    invalid = (usb_control_request_t){.bmRequestType = 0xA1, .bRequest = 1,
        .wValue = {.word = 0x300 | DL_REPORT_ATTRIBUTES}, .wIndex = LAMPARRAY_INTERFACE, .wLength = 51};
    for (unsigned i = 0; i < 40; ++i) {
        memcpy(mock_usb.setup, &invalid, 8); assert(split65_lamp_usb_request(&mock_usb));
    }
    via[0] = 0x31; via[1] = 0;
    assert(via_command_kb(via, sizeof(via)) && via[2] == 'T' && via[3] == 'R' && via[5] == 32 && via[8] == 40 && via[12] == 40);
    via[0] = 0x31; via[1] = 1; via[2] = 0;
    assert(via_command_kb(via, sizeof(via)) && via[3] == 1 && via[14] == 1);
    via[0] = 0x31; via[1] = 1; via[2] = 32;
    assert(via_command_kb(via, sizeof(via)) && via[3] == 0);
    control[1] = 0; set_request(control[0], control, 2); split65_lamp_task();
    assert(split65_lamp_active()); split65_lamp_reset(); assert(!split65_lamp_active());
    // Real wireless receive and reply wrappers: no active keyboard USB needed.
    mock_device = DEVS_2G4; mock_usb.state = 0;
    split65_lamp_mode_changed();
    unsigned usb_replies = mock_usb_replies, wireless_replies = mock_wireless_replies;
    memset(via, 0, sizeof(via)); via[0] = 0x24; via[1] = 67; via[2] = 1; via[5] = 255;
    md_receive_raw_cb(via, sizeof(via));
    assert(mock_wireless_replies == wireless_replies + 1 && mock_usb_replies == usb_replies);
    for (unsigned i = 0; i < 20; ++i) { mock_clock += 10; split65_lamp_task(); }
    assert(split65_lamp_active() && local_frame[67].b == 150 && remote_frame[67].b == 150);
    assert(split65_lamp_render() && mock_pixels[67][2] == 150);
    // A USB-only reset cannot take ownership away from a radio client.
    split65_lamp_reset(); assert(split65_lamp_active());
    mock_usb.state = USB_ACTIVE;
    control[1] = 0; set_request(control[0], control, 2);
    split65_lamp_task(); assert(local_frame[67].b == 150); // Ignore stale USB colors in radio mode.
    split65_lamp_reset(); assert(split65_lamp_active());
    // Lease renewal keeps a static frame alive without retransmitting all LEDs.
    via[0] = 0x25; via[1] = 0;
    for (unsigned i = 0; i < 5; ++i) {
        mock_clock += 2000; md_receive_raw_cb(via, sizeof(via)); split65_lamp_task();
        assert(split65_lamp_active());
    }
    // Timer rollover and exact expiry boundary; native effects resume on both halves.
    mock_clock = UINT32_MAX - 1000; assert(split65_lamp_raw_enable(true));
    mock_clock += 2999; assert(split65_lamp_active());
    ++mock_clock; assert(!split65_lamp_active());
    for (unsigned i = 0; i < 20; ++i) { mock_clock += 10; split65_lamp_task(); }
    assert(!raw_active && !remote_active && !split65_lamp_render());
    // Radio loss releases immediately. Reconnect alone must not replay stale RGB.
    assert(split65_lamp_raw_enable(true)); split65_lamp_task();
    mock_radio_state = 0; assert(!split65_lamp_active()); split65_lamp_task();
    mock_radio_state = MD_STATE_CONNECTED; assert(!split65_lamp_active());
    // The disconnected and Bluetooth paths reject streaming instead of faking success.
    mock_radio_state = 0; via[0] = 0x25; via[1] = 0;
    md_receive_raw_cb(via, sizeof(via)); assert(mock_raw_reply[1] == 253);
    mock_radio_state = MD_STATE_CONNECTED; mock_device = 1; split65_lamp_mode_changed();
    via[0] = 0x24; via[1] = 67; via[2] = 1;
    md_receive_raw_cb(via, sizeof(via)); assert(mock_raw_reply[1] == 253 && !split65_lamp_active());
    mock_device = DEVS_2G4; split65_lamp_mode_changed(); assert(split65_lamp_raw_enable(true));
    mock_device = DEVS_USB; split65_lamp_mode_changed();
    assert(!raw_active && state.autonomous && !split65_lamp_active());
    // Wired static streams retain their existing non-expiring behavior.
    assert(split65_lamp_raw_enable(true)); mock_clock += 60000; split65_lamp_task();
    assert(split65_lamp_active()); mock_usb.state = 0; split65_lamp_task(); assert(!raw_active);
    mock_usb.state = USB_ACTIVE; assert(!split65_lamp_active());
    assert(!split65_lamp_stream(0, 0, via));
    // Independent intensity must affect wired rendering and both split halves.
    // A delayed raw release from the dongle must not release wired ownership.
    mock_device = DEVS_USB; mock_usb.state = USB_ACTIVE;
    split65_lamp_mode_changed();control[1]=0;set_request(control[0],control,2);
    memset(via,0,sizeof(via));via[0]=0x26;md_receive_raw_cb(via,sizeof(via));
    assert(!state.autonomous && split65_lamp_active());
    range[6]=255;range[7]=128;range[8]=64;
    for(unsigned intensity=0;intensity<256;++intensity){
        range[9]=intensity;set_request(range[0],range,sizeof(range));
        mock_clock+=100;split65_lamp_task();
        assert(local_frame[0].r==intensity*150/255);
        assert(local_frame[0].g==dl_channel(128,intensity)*150/255);
        assert(local_frame[0].b==dl_channel(64,intensity)*150/255);
    }
    range[9]=128;set_request(range[0],range,sizeof(range));
    for(unsigned i=0;i<20;++i){mock_clock+=10;split65_lamp_task();}
    assert(remote_frame[67].r==local_frame[67].r && remote_frame[67].r==75);
    puts("PASS: USB LampArray, native sync, 0.4.9 / 0.4.4 split compatibility; production wireless Raw HID replies, both-half RGB, lease/rollover/disconnect/mode/reset/USB regression");
}
