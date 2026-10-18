#!/usr/bin/env python3
"""Exercise production split-command mailbox without running hardware effects."""
import os
import subprocess
import tempfile
from pathlib import Path
root=Path(__file__).resolve().parents[1]
s=(root/'epomaker_split65.c').read_text()
def function(decl):
 a=s.index(decl+' {') if decl+' {' in s else s.index(decl+'{')
 b=s.index('{',a);level=1;i=b+1
 while level:
  level+=(s[i]=='{')-(s[i]=='}');i+=1
 return s[a:i]
globals=s[s.index('static master_to_slave_t mms_queue[8];'):s.index('static void user_sync_mms_task(void) {')]
test=r'''
#include <stdbool.h>
#include <stdint.h>
#include <assert.h>
#include <string.h>
#define ARRAY_SIZE(x) (sizeof(x)/sizeof(*(x)))
typedef struct {uint8_t cmd,body[4];} master_to_slave_t;
typedef struct {uint8_t resp,body[4];} slave_to_master_t;
static bool locked;
static unsigned effects;
static uint8_t received;
static void chSysLock(void){assert(!locked);locked=true;}
static void chSysUnlock(void){assert(locked);locked=false;}
GLOBALS
static void apply_mms_command(const master_to_slave_t *p){assert(!locked);effects++;received=p->cmd;}
TASK
CALLBACK
int main(void){
 slave_to_master_t reply;master_to_slave_t p={.cmd=0x55};
 user_sync_mms_slave_handler(sizeof(p),&p,sizeof(reply),&reply);
 assert(reply.resp==0&&!effects&&mms_count==1);
 user_sync_mms_task();assert(effects==1&&received==0x55&&!mms_count);
 user_sync_mms_slave_handler(0,&p,sizeof(reply),&reply);assert(reply.resp==1&&!mms_count);
 for(unsigned i=0;i<8;i++){p.cmd=i;user_sync_mms_slave_handler(sizeof(p),&p,sizeof(reply),&reply);assert(!reply.resp);}
 user_sync_mms_slave_handler(sizeof(p),&p,sizeof(reply),&reply);assert(reply.resp==1&&mms_count==8);
 for(unsigned i=0;i<8;i++){user_sync_mms_task();assert(received==i);}
 assert(effects==9&&!mms_count);user_sync_mms_task();assert(effects==9);
}
'''.replace('GLOBALS',globals).replace('TASK',function('static void user_sync_mms_task(void)')).replace('CALLBACK',function('void user_sync_mms_slave_handler(uint8_t in_buflen, const void* in_data, uint8_t out_buflen, void* out_data)'))
with tempfile.TemporaryDirectory() as directory:
 p=Path(directory);(p/'test.c').write_text(test)
 subprocess.run(['gcc','-std=gnu11','-Wall','-Wextra','-Werror','-g','-fsanitize=address,undefined',str(p/'test.c'),'-o',str(p/'test')],check=True)
 subprocess.run([p/'test'],check=True,env={**os.environ,'ASAN_OPTIONS':'detect_leaks=0'})
print('PASS: production split callback only enqueues, ordered main-loop application, malformed/full rejection')
