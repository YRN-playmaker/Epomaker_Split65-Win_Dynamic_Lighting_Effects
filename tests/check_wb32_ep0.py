#!/usr/bin/env python3
"""Execute extracted production WB32 EP0 code against register/event models.
Compare the pinned upstream driver, patched enabled build and disabled build.
This checks driver state logic, not real USB silicon or Windows timing.
"""
from pathlib import Path
import os, subprocess, sys, tempfile
qmk=Path(sys.argv[1]).resolve()
rel='os/hal/ports/WB32/LLD/USBv1/hal_usb_lld.c'
repo=qmk/'lib/chibios-contrib'
old=subprocess.check_output(['git','-C',str(repo),'show','HEAD:'+rel],text=True)
new=(repo/rel).read_text()
def function(source,name):
 start=source.index(name+'(');start=source.rfind('\n',0,start)+1
 # Some functions have forward declarations; skip those.
 while ';' in source[start:source.index('{',start)]:
  start=source.index(name+'(',source.index(';',start)+1);start=source.rfind('\n',0,start)+1
 brace=source.index('{',start);depth=1;end=brace+1
 while depth:
  depth+=(source[end]=='{')-(source[end]=='}');end+=1
 return source[start:end]
base=r'''
#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include <assert.h>
#include <stdio.h>
#include <string.h>
#define USB_EP0_STP_WAITING 0
#define USB_EP0_ERROR 1
#define USB_EP0_IN_TX 2
#define USB_EP0_OUT_RX 3
#define USB_CSR0_OUTPKTRDY 1
#define USB_CSR0_INPKTRDY 2
#define USB_CSR0_SENTSTALL 4
#define USB_CSR0_DATAEND 8
#define USB_CSR0_SETUPEND 16
#define USB_CSR0_SVDOUTPKTRDY 64
#define USB_CSR0_SVDSETUPEND 128
#define USB_INCSR1_INPKTRDY 1
#define osalDbgAssert(c,m) assert(c)
typedef unsigned usbep_t;
typedef struct {size_t txcnt,txlast,txsize;const uint8_t *txbuf;} USBInEndpointState;
typedef struct {uint8_t *rxbuf;size_t rxcnt,rxsize,rxpkts;} USBOutEndpointState;
typedef struct {USBInEndpointState *in_state;USBOutEndpointState *out_state;size_t in_maxsize,out_maxsize;} USBEndpointConfig;
typedef struct {unsigned ep0state;uint8_t setup[8];const USBEndpointConfig *epc[1];} USBDriver;
static struct {uint8_t CSR0,INDEX,COUNT0,INCSR1;} registers;
#define WB32_USB (&registers)
static unsigned completed,setups,writes;
static size_t write_size;
static void usb_packet_write_from_buffer(usbep_t ep,const uint8_t *p,size_t n){(void)ep;(void)p;assert(n<=64);writes++;write_size=n;}
static size_t usb_packet_read_to_buffer(usbep_t ep,uint8_t*p){(void)ep;(void)p;return 0;}
static void _usb_isr_invoke_in_cb(USBDriver*d,usbep_t ep){(void)ep;completed++;d->ep0state=USB_EP0_STP_WAITING;}
static void _usb_isr_invoke_out_cb(USBDriver*d,usbep_t ep){(void)ep;d->ep0state=USB_EP0_STP_WAITING;}
static void _usb_isr_invoke_setup_cb(USBDriver*d,usbep_t ep){(void)ep;assert(registers.COUNT0==8);assert(registers.CSR0&1);setups++;registers.CSR0=0;d->ep0state=USB_EP0_IN_TX;}
FUNCTIONS
static USBInEndpointState in;
static USBOutEndpointState out;
static USBEndpointConfig config={&in,&out,64,64};
static USBDriver device;
static uint8_t bytes[256];
static void init(unsigned actual,unsigned requested){
 memset(&registers,0,sizeof(registers));memset(&device,0,sizeof(device));memset(&in,0,sizeof(in));
 completed=setups=writes=0;write_size=0;
 device.epc[0]=&config;device.ep0state=USB_EP0_IN_TX;device.setup[6]=requested;device.setup[7]=requested>>8;
 in.txsize=actual;in.txbuf=bytes;
}
int main(void){
 /* Ordinary one-packet reply. */
 init(29,51);usb_lld_start_in(&device,0);assert(write_size==29&&(registers.CSR0&8));
 registers.CSR0=0;usb_serve_ep0_handler(&device);assert(completed==1&&setups==0);
 /* Full IN completion plus a queued SETUP in the same IRQ snapshot. */
 init(29,51);usb_lld_start_in(&device,0);registers.CSR0=1;registers.COUNT0=8;
 usb_serve_ep0_handler(&device);assert(completed==1&&setups==EXPECTED_FIXED);
 /* A zero-byte OUT status must never be decoded as an eight-byte SETUP. */
 init(29,51);usb_lld_start_in(&device,0);registers.CSR0=1;registers.COUNT0=0;
 usb_serve_ep0_handler(&device);assert(completed==1&&setups==0);
 /* Exact 64-byte host read: mark DATAEND and do not queue an extra ZLP. */
 init(64,64);usb_lld_start_in(&device,0);assert(!!(registers.CSR0&8)==EXPECTED_FIXED);
 registers.CSR0=0;usb_serve_ep0_handler(&device);
 assert(completed==EXPECTED_FIXED);assert(writes==1);assert(!!(registers.CSR0&2)==!EXPECTED_FIXED);
 /* Short reply aligned to max packet: still requires a ZLP. */
 init(64,128);usb_lld_start_in(&device,0);assert(!(registers.CSR0&8));
 registers.CSR0=0;usb_serve_ep0_handler(&device);assert(!completed&&writes==1&&in.txlast==0&&(registers.CSR0&8));
 registers.CSR0=0;usb_serve_ep0_handler(&device);assert(completed==1);
 /* Multi-packet exact host length: final full packet ends data stage. */
 init(128,128);usb_lld_start_in(&device,0);assert(!(registers.CSR0&8));
 registers.CSR0=0;usb_serve_ep0_handler(&device);assert(write_size==64&&!!(registers.CSR0&8)==EXPECTED_FIXED);
 registers.CSR0=0;usb_serve_ep0_handler(&device);assert(completed==EXPECTED_FIXED);
 /* A packet still owned by hardware must not be completed or re-dispatched. */
 init(29,51);usb_lld_start_in(&device,0);registers.CSR0=3;registers.COUNT0=8;
 usb_serve_ep0_handler(&device);assert(!completed&&!setups);
 puts(EXPECTED_FIXED?"patched EP0 event cases pass":"upstream EP0 coalesced-SETUP/exact-length limitations reproduced");
}
'''
with tempfile.TemporaryDirectory() as temp:
 t=Path(temp)
 for name,source,enabled,expected in [('upstream',old,False,0),('patched',new,True,1),('disabled',new,False,0)]:
  code=base.replace('FUNCTIONS',function(source,'usb_serve_ep0_handler')+'\n'+function(source,'usb_lld_start_in'))
  p=t/(name+'.c');p.write_text(code)
  cmd=['gcc','-std=c11','-Wall','-Wextra','-Werror','-fsanitize=address,undefined','-g','-DEXPECTED_FIXED='+str(expected)]
  if enabled:cmd+=['-DSPLIT65_LAMPARRAY_ENABLE']
  subprocess.run(cmd+[str(p),'-o',str(t/name)],check=True)
  subprocess.run([str(t/name)],check=True,env={**os.environ,'ASAN_OPTIONS':'detect_leaks=0'})
print('PASS: production WB32 EP0 coalesced SETUP, exact-length 64/128 reads, required short-reply ZLP, ordinary/zero-status/busy events; disabled behavior preserved')
