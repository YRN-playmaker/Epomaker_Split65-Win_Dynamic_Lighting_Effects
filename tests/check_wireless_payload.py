#!/usr/bin/env python3
"""Execute production packing code with QMK's actual shared/unshared types."""
import re
import subprocess
import sys
import tempfile
from pathlib import Path

root = Path(__file__).resolve().parents[1]
qmk = Path(sys.argv[1]).resolve()
source = (root / 'linker/wireless/wireless.c').read_text()
start = re.search(r'void wireless_send_keyboard\(report_keyboard_t \*report\) \{', source).start()
end = source.index('\nvoid wireless_send_nkro', start)
function = source[start:end]
test = r'''
#include "report.h"
#include <string.h>
#include <assert.h>
enum {MD_SND_CMD_KB_LEN=8, MD_STATE_PAIRING=1, MD_STATE_CONNECTED=2};
static uint8_t state=MD_STATE_CONNECTED, wls_devs, sent[8];
static unsigned sends;
uint8_t *md_getp_state(void) {return &state;}
void wireless_devs_change(uint8_t a,uint8_t b,bool c) {(void)a;(void)b;(void)c;assert(0);}
void md_send_keyboard_state(const uint8_t *p,const uint8_t *overflow) {assert(!overflow);memcpy(sent,p,8);++sends;}
FUNCTION
int main(void) {
    report_keyboard_t report={.mods=4,.reserved=0,.keys={4,5,6,7,8,9}};
#ifdef KEYBOARD_SHARED_EP
    report.report_id=0xAA;
    assert(sizeof(report)==9);
#else
    assert(sizeof(report)==8);
#endif
    const uint8_t expected[]={4,0,4,5,6,7,8,9};
    wireless_send_keyboard(&report); assert(!memcmp(sent,expected,8) && sends==1);
    wireless_send_keyboard(NULL); assert(!memcmp(sent,(uint8_t[8]){0},8) && sends==2);
    state=MD_STATE_PAIRING; wireless_send_keyboard(&report); assert(sends==2);
}
'''.replace('FUNCTION', function)
with tempfile.TemporaryDirectory() as directory:
    path = Path(directory)
    (path / 'test.c').write_text(test)
    for shared in (False, True):
        flags = ['-DKEYBOARD_SHARED_EP'] if shared else []
        subprocess.run(['gcc', '-std=gnu11', '-Wall', '-Wextra', '-Werror', '-I', str(qmk / 'tmk_core/protocol'), '-I', str(qmk / 'quantum'), *flags, str(path / 'test.c'), '-o', str(path / 'test')], check=True)
        subprocess.run([path / 'test'], check=True)
print('PASS: production wireless 6KRO packing, shared and unshared QMK report types, key release, pairing suppression')
