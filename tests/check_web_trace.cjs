// Execute the production HTML's JavaScript with a fake WebHID device.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../tools/windows-diagnose/Read-USB-Trace.html'),'utf8').match(/<script>([\s\S]*)<\/script>/)[1];
async function run(badMarker,revision=0x43){
 const elements=Object.fromEntries(['read','save','log'].map(id=>[id,{textContent:'',disabled:false,handlers:{},addEventListener(event,fn){this.handlers[event]=fn;}}]));
 const commands=[];let handler,closed=false;
 const device={productName:'Split65 test',vendorId:0x342D,productId:revision>=0x46?0xE4C7:0xE4C6,opened:false,
  async open(){this.opened=true;},async close(){this.opened=false;closed=true;},
  addEventListener(type,fn){assert.equal(type,'inputreport');handler=fn;},removeEventListener(){handler=null;},
  async sendReport(id,payload){
   assert.equal(id,0);assert.equal(payload.length,32);const [command,operation,index]=payload;commands.push([command,operation,index]);
   const data=new Uint8Array(32);data[0]=command;
   if(command===0x30){data.set([68,76,badMarker?49:50],1);data[4]=revision;data[6]=3;data[7]=Math.min(revision,0x44);data[8]=68;data[10]=7;}
   else if(command===0x31&&operation===0){data[2]=84;data[3]=82;data[5]=1;data[6]=revision>=0x44?2:0;data[8]=9;data[12]=1;data[24]=1;}
   else {assert.deepEqual([command,operation,index],[0x31,1,0]);data[3]=1;data[6]=1;data[7]=3;data[8]=2;data[10]=23;data[12]=0xA1;data[13]=1;data[14]=revision>=0x44?6:1;data[20]=67;data[22]=3;}
   queueMicrotask(()=>{handler({device,reportId:1,data:new DataView(data.buffer)});handler({device,reportId:0,data:new DataView(data.buffer)});});
  }
 };
 const context=vm.createContext({document:{getElementById(id){return elements[id];}},navigator:{hid:{async requestDevice(options){assert.equal(options.filters[0].usagePage,0xFF60);return [device];}}},Uint8Array,DataView,Date,Promise,Error,String,setTimeout,clearTimeout});
 vm.runInContext(source,context);await elements.read.handlers.click();assert(closed);assert(!elements.read.disabled&&!elements.save.disabled);
 if(badMarker){assert.equal(commands.length,1);assert(elements.log.textContent.includes('读取失败'));}
 else{assert.deepEqual(commands,[[0x30,0,0],[0x31,0,0],[0x31,1,0]]);assert(elements.log.textContent.includes('Peer=0x'+Math.min(revision,0x44).toString(16)));assert(elements.log.textContent.includes('USB total=9'));assert(elements.log.textContent.includes('stage='+(revision>=0x44?6:1)));assert(elements.log.textContent.includes('读取完成'));if(revision>=0x44)assert(elements.log.textContent.includes('lamp=67; report=3'));await elements.read.handlers.click();assert(elements.log.textContent.includes('Delta since previous read: success=0; failure=0; total=0'));}
}
(async()=>{await run(false);await run(false,0x44);await run(false,0x45);await run(false,0x46);await run(false,0x47);await run(false,0x48);await run(true);console.log('PASS: production WebHID trace framing, readonly commands, 0x43/0x44/0x45/0x46/0x47 completion details, baseline deltas, event filtering and cleanup');})().catch(e=>{console.error(e);process.exitCode=1;});
