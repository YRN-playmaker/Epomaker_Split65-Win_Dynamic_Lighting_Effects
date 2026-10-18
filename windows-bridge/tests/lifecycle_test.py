#!/usr/bin/env python3
"""Exercise the production PnP callbacks with fake framework I/O targets.

This checks order and unwind behavior, not Windows driver loading or VHF itself.
"""
from pathlib import Path
import os, subprocess, tempfile
root=Path(__file__).resolve().parents[1]
source=(root/'driver/Split65LampBridge.c').read_text()
def function(declaration):
    start=source.index(declaration+' {');pos=source.index('{',start)+1;depth=1
    while depth:
        depth+=(source[pos]=='{')-(source[pos]=='}');pos+=1
    return source[start:pos]
add=function('NTSTATUS DeviceAdd(WDFDRIVER driver, PWDFDEVICE_INIT init)')
assert 'WdfIoTargetOpen(' not in add, 'Do not open VHF before PrepareHardware'
test=r'''
#include <stdbool.h>
#include <stddef.h>
#include <assert.h>
#include <stdio.h>
typedef int NTSTATUS;
typedef void *WDFDEVICE;
typedef void *WDFIOTARGET;
typedef void *WDFCMRESLIST;
typedef struct {void *ParentObject;} WDF_OBJECT_ATTRIBUTES;
typedef int WDF_IO_TARGET_OPEN_PARAMS;
typedef struct {void *device,*localTarget,*sessionLock;bool prepared,attached;} DEVICE_CONTEXT;
static DEVICE_CONTEXT context;
static int failure,creates,opens,deletes,closes,stops,locked,stages;
#define UNREFERENCED_PARAMETER(x) ((void)(x))
#define WDF_OBJECT_ATTRIBUTES_INIT(p) ((p)->ParentObject=NULL)
#define WDF_IO_TARGET_OPEN_PARAMS_INIT_OPEN_BY_FILE(p,f) (*(p)=0)
#define NT_SUCCESS(s) ((s)>=0)
#define STATUS_SUCCESS 0
#define FALSE false
static DEVICE_CONTEXT *GetContext(WDFDEVICE d){assert(d==&context);return d;}
static void Stage(const void *s,NTSTATUS status){(void)s;(void)status;++stages;}
static NTSTATUS WdfIoTargetCreate(WDFDEVICE d,WDF_OBJECT_ATTRIBUTES *a,WDFIOTARGET *target){
 assert(a->ParentObject==d);++creates;if(failure==1)return -1;*target=(void*)42;return 0;
}
static NTSTATUS WdfIoTargetOpen(WDFIOTARGET t,WDF_IO_TARGET_OPEN_PARAMS *p){
 (void)p;assert(t==(void*)42);++opens;return failure==2?-2:0;
}
static void WdfObjectDelete(WDFIOTARGET t){assert(t==(void*)42 && !locked);++deletes;}
static void WdfIoTargetClose(WDFIOTARGET t){assert(t==(void*)42 && !locked && !context.prepared && !context.attached);++closes;}
static void WdfWaitLockAcquire(void *l,void *timeout){(void)l;(void)timeout;assert(!locked);locked=1;}
static void WdfWaitLockRelease(void *l){(void)l;assert(locked);locked=0;}
static void StopVirtualDevice(DEVICE_CONTEXT *c){assert(locked);++stops;c->attached=false;}
CALLBACKS
int main(void){
 for(int i=0;i<3000;i++){
  failure=i%3;context=(DEVICE_CONTEXT){0};
  int old_open=opens,old_close=closes,old_delete=deletes;
  int status=PrepareHardware(&context,NULL,NULL);
  assert((status==0)==(failure==0));assert(context.prepared==(failure==0));
  if(failure==1)assert(opens==old_open && deletes==old_delete && !context.localTarget);
  if(failure==2)assert(opens==old_open+1 && deletes==old_delete+1 && !context.localTarget);
  if(failure==0){context.attached=true;assert(context.localTarget);}
  assert(ReleaseHardware(&context,NULL)==0 && !context.prepared && !context.attached && !context.localTarget);
  assert(closes==old_close+(failure==0));
  assert(ReleaseHardware(&context,NULL)==0 && !locked); // safe repeated teardown
 }
 assert(creates==3000 && opens==2000 && closes==1000 && deletes==2000 && stops==6000);
 puts("PASS: production Prepare/ReleaseHardware: 3000 success/failure cycles, lower-target open timing, failure unwind and close outside session lock");
}
'''
test=test.replace('CALLBACKS',function('NTSTATUS PrepareHardware(WDFDEVICE device, WDFCMRESLIST raw, WDFCMRESLIST translated)')+'\n'+function('NTSTATUS ReleaseHardware(WDFDEVICE device, WDFCMRESLIST translated)'))
with tempfile.TemporaryDirectory(prefix='split65-lifecycle-') as d:
    p=Path(d);(p/'test.c').write_text(test)
    subprocess.run(['gcc','-std=c11','-Wall','-Wextra','-Werror','-fsanitize=address,undefined',str(p/'test.c'),'-o',str(p/'test')],check=True)
    subprocess.run([str(p/'test')],check=True,env={**os.environ,'ASAN_OPTIONS':'detect_leaks=0'})
