#!/usr/bin/env python3
"""Run complete production module/queue code with a deterministic fake UART."""
import os
import subprocess
import tempfile
from pathlib import Path
root = Path(__file__).resolve().parents[1]
test = r'''
#include <assert.h>
#include "quantum.h"
static uint32_t clock;
static uint8_t input[10000],last_keyboard[8],last_overflow[14];
static unsigned read_at,input_size,keyboard_sends,overflow_sends,max_sent,raw_sends;
static bool verify_pairs;
uint32_t sync_timer_read32(void){return clock;}
uint32_t sync_timer_elapsed32(uint32_t t){return clock-t;}
void uart_init(uint32_t baud){assert(baud==115200);}
bool uart_available(void){return read_at<input_size;}
uint8_t uart_read(void){assert(uart_available());return input[read_at++];}
void uart_transmit(const uint8_t*p,uint16_t n){
 if(n>max_sent)max_sent=n;
 if(p[0]==0xA1){assert(n==10);memcpy(last_keyboard,p+1,8);keyboard_sends++;}
 if(p[0]==0xA2){assert(n==16);memcpy(last_overflow,p+1,14);overflow_sends++;if(verify_pairs)assert(last_overflow[0]==last_keyboard[0]);}
 if(p[0]==0xAF)raw_sends++;
}
#include "MODULE"
static void feed(const uint8_t*p,unsigned n){assert(n<=sizeof(input));memcpy(input,p,n);read_at=0;input_size=n;}
static void acknowledge(void){if(smsg_get_state()==smsg_state_busy)smsg_set_state(smsg_state_replied);}
int main(void){
 md_init();
 /* Arbitrary raw length previously overran the receive buffer. */
 uint8_t malformed[200]={0xAF,0x60,0xFF};
 malformed[197]=0x5C;malformed[198]=77;malformed[199]=0x5C+77;
 feed(malformed,sizeof(malformed));
 while(uart_available()){unsigned before=read_at;md_main_task();assert(read_at-before<=64);clock++;}
 assert(*md_getp_bat()==77);
 /* A truncated packet expires, allowing the next valid status to parse. */
 feed((uint8_t[]){0xAF,0x60,32,0},4);md_main_task();clock+=51;
 feed((uint8_t[]){0x5C,78,0x5C+78},3);md_main_task();assert(*md_getp_bat()==78);
 /* Retain the final all-up state even with a full queue and no ACK. */
 for(unsigned i=0;i<100;i++)md_send_devctrl(0x53);
 assert(!smsg_free_slots());
 uint8_t keys[8]={0,0,4},overflow[14]={1};
 md_send_keyboard_state(keys,overflow);md_main_task();assert(keyboard_sends==1&&!keyboard_pending);
 memset(keys,0,sizeof(keys));memset(overflow,0,sizeof(overflow));
 md_send_keyboard_state(keys,overflow);
 acknowledge();md_main_task();assert(last_overflow[0]==1); /* Frozen pair, despite the newer all-up state. */
 for(unsigned i=0;i<200;i++){acknowledge();md_main_task();clock++;}
 assert(!keyboard_pending&&!smsg_is_busy());
 assert(keyboard_sends==2&&overflow_sends==2);
 assert(!memcmp(last_keyboard,keys,8)&&!memcmp(last_overflow,overflow,14));
 /* Continuous RGB replies must not starve either press or release. */
 md_init();keyboard_sends=overflow_sends=raw_sends=0;
 uint8_t raw[32]={0x30},press[8]={0,0,4},extra[14]={1};
 md_send_keyboard_state(press,extra);
 for(unsigned i=0;i<200;i++){
  md_send_raw(raw,32);
  if(i==100)md_send_keyboard_state(keys,overflow);
  acknowledge();md_main_task();clock++;
 }
 assert(keyboard_sends==2&&overflow_sends==2&&raw_sends>0);
 assert(!keyboard_pending&&!memcmp(last_keyboard,keys,8)&&!memcmp(last_overflow,overflow,14));
 /* Continuous input still gives RGB progress, and every frozen pair matches. */
 md_init();keyboard_sends=overflow_sends=raw_sends=0;verify_pairs=true;
 for(unsigned i=0;i<200;i++){
  press[0]=extra[0]=(uint8_t)(i+1);md_send_keyboard_state(press,extra);md_send_raw(raw,32);
  acknowledge();md_main_task();clock++;
 }
 assert(keyboard_sends>50&&overflow_sends>50&&raw_sends>50);
 md_send_keyboard_state(keys,overflow);
 for(unsigned i=0;i<6;i++){acknowledge();md_main_task();clock++;}
 assert(!keyboard_pending&&!memcmp(last_keyboard,keys,8)&&!memcmp(last_overflow,overflow,14));
 verify_pairs=false;
 /* An unacknowledged RGB packet yields to input in a bounded time. */
 md_init();keyboard_sends=overflow_sends=0;
 md_send_raw(raw,32);md_main_task();md_send_keyboard_state(keys,overflow);
 uint32_t began=clock;
 while(!keyboard_sends&&clock-began<60){clock++;md_main_task();}
 assert(keyboard_sends==1&&clock-began<60);
 /* Auxiliary traffic with no waiting input retains the bounded factory retry. */
 md_init();raw_sends=0;md_send_raw(raw,32);
 for(unsigned i=0;i<200;i++){md_main_task();clock+=11;}
 assert(raw_sends==41&&!smsg_is_busy()&&tx_kind==MD_TX_NONE);
 md_init();
 /* Longest supported product packet fits the real transmit staging buffer. */
 char name[46]={0};md_send_product(name,46);md_main_task();assert(max_sent==49);
 acknowledge();md_main_task();
 /* Variable-length queue wrap and full/empty transitions preserve FIFO data. */
 smsg_init();unsigned head=0,tail=0,r=1;
 static uint8_t expected[20000][50],sizes[20000];uint8_t out[50];
 for(unsigned t=0;t<20000;t++){
  r=r*1664525+1013904223;
  if(smsg_free_slots()&&((r>>16)&3)){
   unsigned n=3+(r>>24)%47;memset(expected[tail],tail%251,n);sizes[tail]=n;
   assert(smsg_push(expected[tail],n));tail++;
  }else if(tail>head){unsigned n=smsg_peek(out);assert(n==sizes[head]&&!memcmp(expected[head],out,n));smsg_pop();head++;}
 }
}
'''.replace('MODULE',str(root/'linker/wireless/module.c'))
with tempfile.TemporaryDirectory() as directory:
 p=Path(directory)
 (p/'quantum.h').write_text('''#pragma once
#include <stdint.h>
#include <stdbool.h>
#include <string.h>
#define PRODUCT "Split65"
#define MANUFACTURER "EPOMAKER"
#define VENDOR_ID 0x342D
#define PRODUCT_ID 0xE4C6
uint32_t sync_timer_read32(void);
uint32_t sync_timer_elapsed32(uint32_t);
''')
 (p/'uart.h').write_text('''#pragma once
void uart_init(uint32_t);
bool uart_available(void);
uint8_t uart_read(void);
void uart_transmit(const uint8_t*,uint16_t);
''')
 (p/'test.c').write_text(test)
 subprocess.run(['gcc','-std=gnu11','-g','-fsanitize=address,undefined','-I',str(p),str(p/'test.c'),str(root/'linker/wireless/smsg.c'),'-o',str(p/'test')],check=True)
 subprocess.run([p/'test'],check=True,env={**os.environ,'ASAN_OPTIONS':'detect_leaks=0'})
print('PASS: production UART, priority input under continuous RGB/full FIFO, frozen keyboard pairs, bounded lost-RGB yield and FIFO ordering')
