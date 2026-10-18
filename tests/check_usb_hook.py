#!/usr/bin/env python3
"""Execute QMK's real request hook; mocks stop only at USB transport calls."""
import os
import subprocess
import sys
import tempfile
from pathlib import Path

qmk = Path(sys.argv[1]).resolve()
source = (qmk / 'tmk_core/protocol/chibios/usb_main.c').read_text()
a = source.index('static bool usb_requests_hook_cb(USBDriver *usbp) {')
b = source.index('\nstatic const USBConfig usbcfg', a)
hook = source[a:b]
test = r'''
#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include <assert.h>
#include <string.h>
#define SPLIT65_LAMPARRAY_ENABLE
#define KEYBOARD_SHARED_EP
#define LAMPARRAY_INTERFACE 0
#define KEYBOARD_INTERFACE 0
#define USB_RTYPE_TYPE_MASK 0x60
#define USB_RTYPE_RECIPIENT_MASK 0x1F
#define USB_RTYPE_TYPE_CLASS 0x20
#define USB_RTYPE_RECIPIENT_INTERFACE 1
#define USB_RTYPE_DIR_MASK 0x80
#define USB_RTYPE_DIR_DEV2HOST 0x80
#define USB_RTYPE_DIR_HOST2DEV 0
#define USB_REQ_GET_DESCRIPTOR 6
#define HID_REQ_GetReport 1
#define HID_REQ_GetIdle 2
#define HID_REQ_GetProtocol 3
#define HID_REQ_SetReport 9
#define HID_REQ_SetIdle 10
#define HID_REQ_SetProtocol 11
#define USB_ENDPOINT_IN_COUNT 1
typedef struct __attribute__((packed)) {
 uint8_t bmRequestType,bRequest;
 union {struct {uint8_t lbyte,hbyte;};uint16_t word;} wValue;
 uint16_t wIndex,wLength;
} usb_control_request_t;
typedef struct {unsigned ud_size;const uint8_t *ud_string;} USBDescriptor;
typedef struct USBDriver USBDriver;
typedef struct {const USBDescriptor *(*get_descriptor_cb)(USBDriver*,uint8_t,uint8_t,uint16_t);} Config;
struct USBDriver {uint8_t setup[8];Config *config;const uint8_t *buffer;unsigned length;};
static unsigned lamp_calls,descriptor_calls,keyboard_calls;
static uint8_t set_report_buf[2],report_descriptor[292],hid_descriptor[9];
static USBDescriptor report={292,report_descriptor},hid={9,hid_descriptor};
static struct {bool (*usb_requests_cb)(USBDriver*);} usb_endpoints_in[1];
static void usbSetupTransfer(USBDriver*d,uint8_t*p,unsigned n,void(*cb)(USBDriver*)){(void)cb;d->buffer=p;d->length=n;}
static bool split65_lamp_usb_request(USBDriver*d){++lamp_calls;return ((usb_control_request_t*)d->setup)->bmRequestType==0xA1;}
static bool usb_get_report_cb(USBDriver*d){(void)d;++keyboard_calls;return true;}
static bool usb_get_idle_cb(USBDriver*d){(void)d;return true;}
static bool usb_set_idle_cb(USBDriver*d){(void)d;return true;}
static uint8_t usb_device_state_get_protocol(void){return 1;}
static void usb_device_state_set_protocol(uint8_t n){(void)n;}
static void usb_device_state_set_idle_rate(uint8_t n){(void)n;}
static void set_led_transfer_cb(USBDriver*d){(void)d;}
static const USBDescriptor *get_descriptor(USBDriver*d,uint8_t type,uint8_t index,uint16_t iface){
 // QMK's production callback decodes usbp->setup, not dtype/dindex arguments.
 (void)type;(void)index;usb_control_request_t *s=(void*)d->setup;
 assert(iface==0);++descriptor_calls;
 return s->wValue.word==0x2200?&report:s->wValue.word==0x2100?&hid:NULL;
}
FUNCTION
static bool request(USBDriver*d,uint8_t type,uint8_t req,uint16_t value,uint16_t iface,uint16_t length){
 usb_control_request_t s={type,req,{.word=value},iface,length};memcpy(d->setup,&s,8);return usb_requests_hook_cb(d);
}
int main(void){
 Config c={get_descriptor};USBDriver d={.config=&c};
 assert(request(&d,0x81,6,0x2200,0,292));assert(d.length==292&&d.buffer==report_descriptor);
 assert(request(&d,0x81,6,0x2100,0,9));assert(d.length==9&&d.buffer==hid_descriptor);
 assert(descriptor_calls==2&&!lamp_calls);
 assert(request(&d,0xA1,1,0x0320,0,51)&&lamp_calls==1);
 assert(request(&d,0xA1,1,0x0101,0,9)&&keyboard_calls==1&&lamp_calls==1);
 assert(request(&d,0x21,9,0x0201,0,2)&&d.buffer==set_report_buf&&lamp_calls==1);
 assert(request(&d,0xA1,2,0,0,1)&&lamp_calls==1);
 assert(request(&d,0x21,10,0,0,0)&&lamp_calls==1);
 /* Device GET_DESCRIPTOR has wIndex=0; don't dispatch by report ID alone. */
 assert(!request(&d,0x80,6,0x0100,0,18));
}
'''.replace('FUNCTION',hook)
with tempfile.TemporaryDirectory() as directory:
 p=Path(directory);(p/'test.c').write_text(test)
 subprocess.run(['gcc','-std=gnu11','-Wall','-Wextra','-Wno-implicit-fallthrough','-fsanitize=address,undefined','-g',str(p/'test.c'),'-o',str(p/'test')],check=True)
 subprocess.run([p/'test'],check=True,env={**os.environ,'ASAN_OPTIONS':'detect_leaks=0'})
print('PASS: actual QMK hook serves LampArray HID/report descriptors, feature routing and keyboard fallback')
