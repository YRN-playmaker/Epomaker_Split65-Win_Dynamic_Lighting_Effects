// SPDX-License-Identifier: GPL-2.0-or-later
(function (root) {
  'use strict';
  const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
  function command(id, ...values) {
    const packet = new Uint8Array(32);
    packet[0] = id;
    packet.set(values.slice(0, 31), 1);
    return packet;
  }
  function framePackets(rgb) {
    if (!(rgb instanceof Uint8Array) || rgb.length !== 68 * 3) throw Error('需要完整的 68 灯 RGB 帧');
    const packets = [];
    for (let first = 0; first < 68; first += 9) {
      const count = Math.min(9, 68 - first);
      const packet = command(0x24, first, count);
      packet.set(rgb.subarray(first * 3, (first + count) * 3), 3);
      packets.push(packet);
    }
    return packets;
  }
  class Client {
    constructor(device, {timeoutMs = 2500, packetGapMs = 80, heartbeatMs = 1000} = {}) {
      this.device = device;
      this.timeoutMs = timeoutMs;
      this.packetGapMs = packetGapMs;
      this.heartbeatMs = heartbeatMs;
      this.queue = Promise.resolve();
      this.ready = false;
      this.fault = false;
      this.epoch = 0;
      this.lastSend = 0;
      this.onInput = event => {
        if (event.reportId !== 0 || event.data.byteLength !== 32 || !this.pending) return;
        const data = new Uint8Array(event.data.buffer, event.data.byteOffset, event.data.byteLength).slice();
        if (data[0] !== this.pending.id) return;
        this.pending.finish(null, data);
      };
    }
    enqueue(task) {
      const result = this.queue.then(task);
      this.queue = result.catch(() => {});
      return result;
    }
    async request(packet) {
      if (this.fault || !this.device.opened) throw Error('连接已中断，请重新连接');
      if (!this.ready && packet[0] !== 0x30) throw Error('尚未验证 0.4.8/0.4.9/0.5.0 固件，未发送颜色');
      const wait = this.packetGapMs - (Date.now() - this.lastSend);
      if (wait > 0) await delay(wait);
      return new Promise((resolve, reject) => {
        const finish = (error, data) => {
          if (this.pending?.finish !== finish) return;
          if (data) this.pending.data = data;
          if (!error && (!this.pending.sent || !this.pending.data)) return;
          data = this.pending.data;
          clearTimeout(timer);
          this.pending = null;
          if (error) {
            this.fault = true;
            this.stopHeartbeat();
            reject(error);
          } else if ([0x24, 0x25].includes(packet[0]) && data[1] === 253) {
            this.stopHeartbeat();
            reject(Error('固件拒绝串流，请检查键盘模式及 2.4G 连接'));
          } else resolve(data);
        };
        const timer = setTimeout(() => finish(Error('接收器回复超时；已停止续期，请重新连接')), this.timeoutMs);
        this.pending = {id: packet[0], finish, sent: false, data: null};
        this.lastSend = Date.now();
        this.device.sendReport(0, packet).then(() => {
          if (this.pending?.finish === finish) this.pending.sent = true;
          finish(null);
        }).catch(error => finish(error));
      });
    }
    async open() {
      if (!this.device.opened) await this.device.open();
      this.device.addEventListener('inputreport', this.onInput);
      try {
        const info = await this.request(command(0x30));
        if (info[1] !== 68 || info[2] !== 76 || info[3] !== 50 || (info[4] !== 0x48 && info[4] !== 0x49 && info[4] !== 0x50) || info[8] !== 68 || info[26] !== 1) {
          throw Error('未检测到支持本测试的 Split65 0.4.8/0.4.9/0.5.0；没有发送颜色');
        }
        if (![0, 6].includes(info[21])) throw Error('请切到有线或 2.4G 模式；没有发送颜色');
        this.ready = true;
        return info;
      } catch (error) {
        this.device.removeEventListener('inputreport', this.onInput);
        await this.device.close().catch(() => {});
        throw error;
      }
    }
    stopHeartbeat() {
      ++this.epoch;
      clearTimeout(this.heartbeat);
    }
    startHeartbeat() {
      const epoch = this.epoch;
      this.heartbeat = setTimeout(() => {
        this.enqueue(async () => {
          if (epoch !== this.epoch || !this.ready || this.fault) return;
          await this.request(command(0x25));
          if (epoch === this.epoch) this.startHeartbeat();
        }).catch(error => this.onError?.(error));
      }, this.heartbeatMs);
    }
    setFrame(rgb) {
      const packets = framePackets(rgb);
      return this.enqueue(async () => {
        this.stopHeartbeat();
        for (const packet of packets) await this.request(packet);
        this.startHeartbeat();
      });
    }
    status() { return this.enqueue(() => this.request(command(0x30))); }
    release() {
      this.stopHeartbeat();
      return this.enqueue(async () => {
        if (this.ready && !this.fault && this.device.opened) await this.request(command(0x26));
      });
    }
    async close() {
      try { await this.release(); }
      finally {
        this.ready = false;
        this.device.removeEventListener('inputreport', this.onInput);
        if (this.device.opened) await this.device.close();
      }
    }
  }
  const api = {Client, command, framePackets};
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.Split65WirelessRGB = api;
})(globalThis);
