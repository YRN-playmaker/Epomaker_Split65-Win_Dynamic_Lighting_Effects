// SPDX-License-Identifier: GPL-2.0-or-later
const assert = require('assert/strict');
const {Client, framePackets} = require('../tools/wireless-rgb/wireless-rgb.js');
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
class Device {
  constructor({version = 0x48, mode = 6, drop = false, reject = false} = {}) {
    Object.assign(this, {version, mode, drop, reject});
    this.sent = []; this.listeners = new Set(); this.opened = false;
  }
  async open() { this.opened = true; }
  async close() { this.opened = false; }
  addEventListener(type, callback) { this.listeners.add(callback); }
  removeEventListener(type, callback) { this.listeners.delete(callback); }
  async sendReport(reportId, packet) {
    assert.equal(reportId, 0); assert.equal(packet.length, 32);
    this.sent.push(packet.slice());
    if (this.drop) return;
    const reply = packet.slice();
    if (packet[0] === 0x30) {
      reply.set([0x30, 68, 76, 50, this.version, 2, 3, 0x44, 68]);
      reply[21] = this.mode; reply[26] = 1; reply[28] = 3; reply[29] = 2;
    } else if (this.reject) reply[1] = 253;
    // Simulate an input arriving before sendReport's promise completes.
    for (const callback of this.listeners) {
      callback({reportId: 1, data: new DataView(reply.buffer)}); // Wrong report is ignored.
      callback({reportId: 0, data: new DataView(reply.buffer)});
    }
  }
}
async function run() {
  const frame = new Uint8Array(204);
  for (let i = 0; i < frame.length; ++i) frame[i] = i;
  const packets = framePackets(frame);
  assert.equal(packets.length, 8);
  assert.equal(packets.at(-1)[1], 63); assert.equal(packets.at(-1)[2], 5);
  assert.deepEqual(Uint8Array.from(packets.flatMap(p => [...p.subarray(3, 3 + p[2] * 3)])), frame);
  assert.throws(() => framePackets(new Uint8Array(203)));
  for (const options of [{version: 0x47}, {mode: 1}, {drop: true}]) {
    const device = new Device(options), client = new Client(device, {timeoutMs: 10, packetGapMs: 0});
    await assert.rejects(() => client.open());
    assert.deepEqual(device.sent.map(p => p[0]), [0x30]);
    assert(!device.opened && !client.ready && device.listeners.size === 0);
  }
  const device = new Device(), client = new Client(device, {timeoutMs: 100, packetGapMs: 0, heartbeatMs: 10});
  assert.equal((await client.open())[21], 6);
  await Promise.all([client.setFrame(frame), client.status()]);
  assert.deepEqual(device.sent.slice(1,9).map(p => p[0]), Array(8).fill(0x24));
  await wait(25);
  assert(device.sent.some(p => p[0] === 0x25));
  await client.release();
  const count = device.sent.length; await wait(25);
  assert.equal(device.sent.length, count); assert.equal(device.sent.at(-1)[0], 0x26);
  device.reject = true;
  await assert.rejects(() => client.setFrame(frame));
  const rejectedCount = device.sent.length; await wait(25);
  assert.equal(device.sent.length, rejectedCount);
  await client.close(); assert(!device.opened && device.listeners.size === 0);
  // Lost replies stop the queue; subsequent writes cannot consume a stale ACK.
  const lostDevice = new Device(), lost = new Client(lostDevice, {timeoutMs: 10, packetGapMs: 0});
  await lost.open(); lostDevice.drop = true;
  await assert.rejects(() => lost.setFrame(frame));
  assert(lost.fault); const before = lostDevice.sent.length;
  await assert.rejects(() => lost.status()); assert.equal(lostDevice.sent.length, before);
  await lost.close();
  console.log('PASS: real wireless page client: version/mode guard, 8-packet frame, ACK serialization, keepalive/release, rejection, timeout, no writes after failure');
}
run().catch(error => {console.error(error); process.exitCode = 1;});
