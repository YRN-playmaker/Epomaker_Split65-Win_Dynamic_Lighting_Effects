#!/usr/bin/env python3
"""Exercise real wireless conversion and retry code with QMK report types."""
import os
import subprocess
import sys
import tempfile
from pathlib import Path

root = Path(__file__).resolve().parents[1]
qmk = Path(sys.argv[1]).resolve()

def function(path, declaration):
    source = path.read_text()
    start = source.index(declaration + ' {')
    begin = source.index('{', start)
    depth = 1
    end = begin + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]

converter = function(root / 'linker/wireless/wireless.c', 'void wireless_send_nkro(report_nkro_t *report)')
retry = function(root / 'linker/wireless/module.c', 'static void md_send_pkt_task(void)')
mode_change = function(root / 'linker/wireless/wireless.c', 'void wireless_devs_change(uint8_t old_devs, uint8_t new_devs, bool reset)')
mode_test = r'''
#include <stdint.h>
#include <stdbool.h>
#include <assert.h>
#define SPLIT65_LAMPARRAY_ENABLE
enum {DEVS_USB=0,TRANSPORT_WLS=1,TRANSPORT_USB=2,MD_STATE_DISCONNECTED=0};
static uint8_t wls_devs,radio_state,indicator;
static unsigned releases,transports,expected_new;
static uint8_t *md_getp_state(void){return &radio_state;}
static uint8_t *md_getp_indicator(void){return &indicator;}
static void set_transport(unsigned mode){assert(mode==(expected_new==DEVS_USB?TRANSPORT_USB:TRANSPORT_WLS));++transports;}
static void split65_lamp_mode_changed(void){assert(wls_devs==expected_new);++releases;}
static void last_matrix_activity_trigger(void){}
static void md_devs_change(uint8_t mode,bool reset){(void)reset;assert(mode==expected_new);}
static void wireless_devs_change_kb(uint8_t a,uint8_t b,bool c){(void)a;(void)b;(void)c;}
static void wireless_devs_change_user(uint8_t a,uint8_t b,bool c){(void)a;(void)b;(void)c;}
FUNCTION
int main(void){
    const uint8_t modes[]={0,6,6,1,6,0,0,3};
    unsigned expected_releases=0,expected_transports=0;
    for(unsigned i=0;i<10000;++i){
        uint8_t old=wls_devs,new=modes[i%8];bool reset=(i%7)==0;
        expected_new=new;expected_releases+=old!=new||reset;
        expected_transports+=(old==0)!=(new==0);
        wireless_devs_change(old,new,reset);
        assert(releases==expected_releases&&transports==expected_transports);
    }
}
'''.replace('FUNCTION', mode_change)
conversion_test = r'''
#include "report.h"
#include <assert.h>
#include <string.h>
enum {MD_SND_CMD_NKRO_LEN=14,MD_STATE_PAIRING=1,MD_STATE_CONNECTED=2};
static uint8_t state=MD_STATE_CONNECTED,wls_devs,overflow[14];
static report_keyboard_t keyboard;
static uint8_t *md_getp_state(void){return &state;}
static void wireless_devs_change(uint8_t a,uint8_t b,bool c){(void)a;(void)b;(void)c;assert(0);}
uint8_t biton(uint8_t n){return (uint8_t)(31-__builtin_clz((unsigned)n));}
void del_key_bit(report_nkro_t *p,uint8_t k){p->bits[k/8]&=~(1u<<(k%8));}
static bool smsg_is_busy(void){return false;}
static void wireless_task(void){assert(0);}
static void md_send_keyboard_state(const uint8_t *p,const uint8_t *o){memcpy(&keyboard.mods,p,8);memcpy(overflow,o,sizeof(overflow));}
FUNCTION
static void send(unsigned mask){
    report_nkro_t r={0};
    for(unsigned i=0;i<10;++i)if(mask&(1u<<i))r.bits[(4+i)/8]|=1u<<((4+i)%8);
    wireless_send_nkro(&r);
    unsigned actual=0;
    for(unsigned i=0;i<6;++i)if(keyboard.keys[i]){assert(keyboard.keys[i]>=4&&keyboard.keys[i]<14);actual|=1u<<(keyboard.keys[i]-4);}
    for(unsigned i=4;i<14;++i)if(overflow[i/8]&(1u<<(i%8)))actual|=1u<<(i-4);
    assert(actual==mask);
}
int main(void){
    send(3);send(1);send(0); /* two overlapping keys, release one, release all */
    unsigned seq[]={7,6,14,8,24,16,0,0x3ff,0x3fe,0x1ff,0,1,2,0};
    for(unsigned i=0;i<sizeof(seq)/sizeof(seq[0]);++i)send(seq[i]);
    unsigned random=1;
    for(unsigned i=0;i<10000;++i){random=random*1664525+1013904223;send((random>>16)&0x3ff);}
    wireless_send_nkro(NULL);assert(!keyboard.mods);
    for(unsigned i=0;i<6;++i)assert(!keyboard.keys[i]);
}
'''.replace('FUNCTION', converter)
retry_test = r'''
#include <stdint.h>
#include <assert.h>
enum {smsg_state_free,smsg_state_busy,smsg_state_retry,smsg_state_replied};
enum {MD_SNED_PKT_TIMEOUT=10,MD_SEND_PKT_RETRY=40};
static unsigned state,queued=1,pops,sends;
static uint32_t clock;
static uint8_t md_pkt_payload[40];
static unsigned smsg_get_state(void){return state;}
static void smsg_set_state(unsigned s){state=s;}
static void smsg_pop(void){assert(queued);--queued;++pops;}
static uint32_t smsg_peek(uint8_t *p){(void)p;return queued?4:0;}
static uint32_t sync_timer_read32(void){return clock;}
static uint32_t sync_timer_elapsed32(uint32_t before){return clock-before;}
static void md_send_pkt(uint8_t *p,uint32_t n){(void)p;assert(n==4);++sends;}
FUNCTION
int main(void){
    for(unsigned i=0;i<200;++i){md_send_pkt_task();clock+=11;}
    assert(pops==1&&queued==0&&sends==41); /* no ACK must eventually drop */
    queued=1;state=smsg_state_free;md_send_pkt_task();
    state=smsg_state_replied;md_send_pkt_task();assert(pops==2&&!queued);
}
'''.replace('FUNCTION', retry)
with tempfile.TemporaryDirectory() as directory:
    path = Path(directory)
    failed = False
    for name, source, flags in [('conversion', conversion_test, []), ('conversion-shared', conversion_test, ['-DKEYBOARD_SHARED_EP']), ('retry', retry_test, []), ('mode-light-release', mode_test, [])]:
        (path / (name + '.c')).write_text(source)
        subprocess.run(['gcc', '-std=gnu11', '-Wall', '-Wextra', '-Wno-unused-function', '-g',
                        '-fsanitize=address,undefined', '-DNKRO_ENABLE', *flags,
                        '-I', str(qmk / 'tmk_core/protocol'), '-I', str(qmk / 'quantum'),
                        str(path / (name + '.c')), '-o', str(path / name)], check=True)
        result = subprocess.run([path / name], env={**os.environ, 'ASAN_OPTIONS': 'detect_leaks=0'}, capture_output=True, text=True)
        if result.returncode:
            print('FAIL: production ' + name, flush=True)
            print('\n'.join(result.stderr.splitlines()[:24]), flush=True)
            failed = True
        else:
            print('PASS: production ' + name, flush=True)
    sys.exit(1 if failed else 0)
