// SPDX-License-Identifier: GPL-2.0-or-later
#pragma once
#include <stdbool.h>
#include "hal.h"
void split65_lamp_init(void);
void split65_lamp_task(void);
bool split65_lamp_render(void);
void split65_lamp_reset(void);
void split65_lamp_mode_changed(void);
bool split65_lamp_active(void);
void split65_lamp_suspend_slave(void);
bool split65_lamp_usb_request(USBDriver *driver);
bool split65_lamp_stream(uint8_t start, uint8_t count, const uint8_t *rgb);
bool split65_lamp_raw_enable(bool enable);
void split65_lamp_diagnostics(uint8_t *data);
void split65_lamp_usb_diagnostics(uint8_t *data);
