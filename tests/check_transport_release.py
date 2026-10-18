#!/usr/bin/env python3
"""Verify real mode-switch USB release reports with QMK's actual structures."""
import os
import subprocess
import sys
import tempfile
from pathlib import Path
root=Path(__file__).resolve().parents[1];qmk=Path(sys.argv[1]).resolve()
s=(root/'linker/wireless/transport.c').read_text();a=s.index('void usb_transport_enable(bool enable) {');b=s.index('\nvoid set_transport',a)
test=r'''
#include "report.h"
#include <assert.h>
#include <string.h>
#define KEEP_USB_CONNECTION_IN_WIRELESS_MODE
#define USB_ACTIVE 1
static struct {int state;} mock_usb={1};
#define USB_DRIVER mock_usb
static unsigned keyboard_count,nkro_count;
static void keyboard(report_keyboard_t*p){assert(p);assert(!p->mods&&!p->reserved);for(unsigned i=0;i<6;i++)assert(!p->keys[i]);
#ifdef KEYBOARD_SHARED_EP
 assert(p->report_id==REPORT_ID_KEYBOARD);
#endif
 keyboard_count++;
}
static void nkro(report_nkro_t*p){assert(p&&p->report_id==REPORT_ID_NKRO&&!p->mods);for(unsigned i=0;i<NKRO_REPORT_BITS;i++)assert(!p->bits[i]);nkro_count++;}
typedef struct {void(*send_keyboard)(report_keyboard_t*);void(*send_nkro)(report_nkro_t*);} host_driver_t;
static host_driver_t chibios_driver={keyboard,nkro},*active;
bool last_suspend_state;
static host_driver_t *host_get_driver(void){return active;}
static void host_set_driver(host_driver_t*d){active=d;}
FUNCTION
int main(void){usb_transport_enable(false);assert(keyboard_count==1&&nkro_count==1);mock_usb.state=0;usb_transport_enable(false);assert(keyboard_count==1);usb_transport_enable(true);assert(active==&chibios_driver&&last_suspend_state);}
'''.replace('FUNCTION',s[a:b])
with tempfile.TemporaryDirectory() as directory:
 p=Path(directory);(p/'test.c').write_text(test)
 for shared in (False,True):
  flags=['-DKEYBOARD_SHARED_EP'] if shared else []
  subprocess.run(['gcc','-std=gnu11','-Wall','-Wextra','-Werror','-g','-fsanitize=address,undefined','-DNKRO_ENABLE',*flags,'-I',str(qmk/'tmk_core/protocol'),'-I',str(qmk/'quantum'),str(p/'test.c'),'-o',str(p/'test')],check=True)
  subprocess.run([p/'test'],check=True,env={**os.environ,'ASAN_OPTIONS':'detect_leaks=0'})
print('PASS: production USB mode-switch releases use valid zeroed buffers and correct shared/unshared report IDs')
