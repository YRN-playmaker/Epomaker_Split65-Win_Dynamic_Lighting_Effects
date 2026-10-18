// SPDX-License-Identifier: GPL-2.0-or-later
#include "../core/bridge_core.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
static const dl_lamp_info_t info[DL_LAMP_COUNT] = {
#include "../../dynamic_lighting/lamp_info.inc"
};
static unsigned u16(const uint8_t *p) { return p[0] | p[1] << 8; }
static uint32_t u32(const uint8_t *p) { return (uint32_t)p[0] | (uint32_t)p[1]<<8 | (uint32_t)p[2]<<16 | (uint32_t)p[3]<<24; }
static uint32_t rng=0x48facade;
static unsigned random32(void) { rng^=rng<<13; rng^=rng>>17; rng^=rng<<5; return rng; }
int main(void) {
    sb_state_t s; uint8_t b[51], snap[SB_SNAPSHOT_SIZE]; size_t written;
    sb_init(&s);
    assert(!sb_get_feature(&s,DL_REPORT_ATTRIBUTES,b,sizeof b,&written) && written==0);
    uint8_t control[]={DL_REPORT_CONTROL,0};
    assert(!sb_set_feature(&s,DL_REPORT_CONTROL,control,sizeof control));
    sb_snapshot(&s,snap); assert(snap[6]==0);
    sb_attach(&s,true);
    assert(sb_get_feature(&s,DL_REPORT_ATTRIBUTES,b,sizeof b,&written));
    assert(written==23 && u16(b+1)==68 && u32(b+19)==100000);
    for (unsigned i=23;i<51;i++) assert(b[i]==0);
    for (unsigned i=0;i<68;i++) {
        // A too-short GET must not consume the metadata cursor.
        memset(b,0xcd,sizeof b);
        assert(!sb_get_feature(&s,DL_REPORT_RESPONSE,b,28,&written) && written==0);
        for (unsigned j=0;j<51;j++) assert(b[j]==0xcd);
        assert(sb_get_feature(&s,DL_REPORT_RESPONSE,b,sizeof b,&written));
        assert(written==29 && u16(b+1)==i);
        assert(u32(b+3)==info[i].x && u32(b+7)==info[i].y && u32(b+11)==info[i].z);
        assert(u32(b+15)==100000 && b[26]==255 && b[27]==1 && b[28]==info[i].key);
    }
    assert(sb_get_feature(&s,DL_REPORT_RESPONSE,b,29,&written) && u16(b+1)==0);
    uint8_t request[51]={DL_REPORT_REQUEST,40,0};
    assert(sb_set_feature(&s,DL_REPORT_REQUEST,request,sizeof request));
    assert(sb_get_feature(&s,DL_REPORT_RESPONSE,b,29,&written)); assert(u16(b+1)==40 && b[28]==0x13);
    assert(sb_set_feature(&s,DL_REPORT_CONTROL,control,sizeof control));
    sb_snapshot(&s,snap); assert(snap[6]==3 && u32(snap+12)==100);
    uint32_t generation=u32(snap+8);
    uint8_t range[]={DL_REPORT_RANGE,0,0,0,67,0,0x11,0x22,0x33,255};
    assert(sb_set_feature(&s,DL_REPORT_RANGE,range,sizeof range));
    sb_snapshot(&s,snap);
    assert(u32(snap+8)==generation);
    for (unsigned i=16;i<SB_SNAPSHOT_SIZE;i++) assert(snap[i]==0);
    uint8_t multi[51]={DL_REPORT_MULTI,1,1}; multi[3]=40;
    multi[19]=0xff; multi[20]=0xfe; multi[21]=0xfd; multi[22]=255;
    assert(sb_set_feature(&s,DL_REPORT_MULTI,multi,sizeof multi));
    sb_snapshot(&s,snap); assert(u32(snap+8)==generation+1);
    for (unsigned i=0;i<68;i++) {
        assert(snap[16+3*i]==(i==40?0xff:0x11));
        assert(snap[17+3*i]==(i==40?0xfe:0x22));
        assert(snap[18+3*i]==(i==40?0xfd:0x33));
    }
    // Constant RGB with an intensity ramp must not collapse to constant RGB.
    range[1]=1;range[6]=255;range[7]=128;range[8]=64;
    for(unsigned intensity=0;intensity<256;++intensity){
        range[9]=intensity;assert(sb_set_feature(&s,DL_REPORT_RANGE,range,sizeof range));
        sb_snapshot(&s,snap);
        assert(snap[16]==intensity);
        assert(snap[17]==dl_channel(128,intensity));
        assert(snap[18]==dl_channel(64,intensity));
    }
    control[1]=1; assert(sb_set_feature(&s,DL_REPORT_CONTROL,control,sizeof control));
    sb_snapshot(&s,snap); assert(snap[6]==2);
    sb_attach(&s,false); sb_snapshot(&s,snap); assert(snap[6]==0);
    for (unsigned i=16;i<SB_SNAPSHOT_SIZE;i++) assert(snap[i]==0);
    sb_attach(&s,true); sb_snapshot(&s,snap); assert(snap[6]==2); // No stale frame on reconnect.
    for (unsigned i=0;i<200000;i++) {
        for (unsigned j=0;j<51;j++) b[j]=(uint8_t)random32();
        unsigned id=random32()&255, size=random32()%70;
        if (size<=51) (void)sb_set_feature(&s,id,b,size);
        else assert(!sb_set_feature(&s,id,b,size));
        (void)sb_get_feature(&s,id,b,size,&written);
        assert(s.lamps.requested_lamp<68);
        assert(s.lamps.autonomous==true || s.lamps.autonomous==false);
        sb_snapshot(&s,snap);
    }
    puts("Bridge core: 68 metadata entries, batch commit, ownership/reconnect and 200000 malformed reports passed.");
}
