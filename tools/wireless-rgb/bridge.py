#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-2.0-or-later
"""Relay colour frames to the Split65 over the 2.4G receiver.

This is the host half of a future LampArray bridge: whatever produces a
68-lamp frame (a virtual device, a screen sampler, an effects engine) can
hand it to send_frame(). It speaks the same 0x24/0x25/0x26 commands as
tools/wireless-rgb/Start-Wireless-RGB.html and never writes EEPROM.

Usage:
  python bridge.py --effect rainbow --seconds 5
  python bridge.py --colour ff2020 --seconds 3
"""

import argparse
import colorsys
import sys
import time

import probe_status as P

LAMPS = 68
LAMPS_PER_PACKET = 9
REJECTED = 253


class WirelessLink:
    def __init__(self, timeout=2.0, window=4):
        receivers = P.find_receiver()
        if not receivers:
            raise SystemExit("no Split65 receiver found (usage 0xFF60 collection missing)")
        path, attrs, caps = receivers[0]
        self.path = path
        self.attrs = attrs
        self.caps = caps
        self.timeout = timeout
        self.window = window
        self.handle = P.open_device(path)
        if self.handle in (0, -1):
            raise SystemExit("could not open the raw HID collection")
        self.frames = 0
        self.packets = 0
        self.last_lease = 0.0

    def close(self):
        try:
            self.send(bytes([0x26] + [0] * 31), expect_reply=False)
        finally:
            P.kernel32.CloseHandle(self.handle)

    def _flush(self):
        """Discard queued reports so a late ack cannot be mistaken for the next one."""
        while P.read_report(self.handle, 64, 5) is not None:
            pass

    def send(self, payload, expect_reply=True):
        ok, info = P.write_report(self.handle, b"\x00" + payload)
        if not ok:
            raise RuntimeError("write failed (%s)" % info)
        self.packets += 1
        if not expect_reply:
            return None
        deadline = time.time() + self.timeout
        while time.time() < deadline:
            chunk = P.read_report(self.handle, 64, 400)
            if chunk:
                body = chunk[1:] if len(chunk) == 33 else chunk
                if body[0] == payload[0]:
                    if len(body) > 1 and body[1] == REJECTED:
                        raise RuntimeError("firmware rejected the stream (mode or link)")
                    return body
        raise RuntimeError("no acknowledgement for command 0x%02X" % payload[0])

    def status(self):
        return self.send(bytes([0x30] + [0] * 31))

    def require_ready(self):
        body = self.status()
        signature = bytes(body[1:4]).decode("ascii", "replace")
        revision = body[4]
        lamps = body[8]
        device = body[21]
        protocol = body[26]
        if signature != "DL2" or revision not in (0x48, 0x49, 0x50) or lamps != LAMPS or protocol != 1:
            raise SystemExit("unexpected firmware: %r rev=0x%02X lamps=%s proto=%s"
                             % (signature, revision, lamps, protocol))
        if device not in (0, 6):
            raise SystemExit("keyboard reports device=%s; switch to wired or 2.4G" % device)
        return body

    def keepalive(self, force=False):
        now = time.time()
        if not force and now - self.last_lease < 1.0:
            return
        self._flush()
        for attempt in range(3):
            try:
                self.send(bytes([0x25] + [0] * 31))
                self.last_lease = now
                return
            except RuntimeError:
                if attempt == 2:
                    raise
                time.sleep(0.02)

    def _drain(self, command, want, timeout):
        """Collect up to `want` acknowledgements for `command`."""
        got = 0
        deadline = time.time() + timeout
        while got < want and time.time() < deadline:
            chunk = P.read_report(self.handle, 64, 150)
            if not chunk:
                continue
            body = chunk[1:] if len(chunk) == 33 else chunk
            if body[0] != command:
                continue
            if len(body) > 1 and body[1] == REJECTED:
                raise RuntimeError("firmware rejected the stream (mode or link)")
            got += 1
        return got

    def send_frame(self, rgb, window=None):
        """Send one 68-lamp frame.

        The link acknowledges every packet and the receiver round trip is about
        20 ms, so a strictly serial frame costs ~180 ms. Keeping up to four
        packets in flight lifts that to ~9 fps; deeper windows lose
        acknowledgements, and on loss this falls back to one packet at a time.
        """
        if len(rgb) != LAMPS * 3:
            raise ValueError("frame must be %d bytes" % (LAMPS * 3))
        window = max(1, window or self.window)
        packets = []
        for first in range(0, LAMPS, LAMPS_PER_PACKET):
            count = min(LAMPS_PER_PACKET, LAMPS - first)
            payload = bytearray(32)
            payload[0] = 0x24
            payload[1] = first
            payload[2] = count
            payload[3:3 + count * 3] = rgb[first * 3:(first + count) * 3]
            packets.append(bytes(payload))

        sent = 0
        while sent < len(packets):
            batch = packets[sent:sent + window]
            for payload in batch:
                ok, info = P.write_report(self.handle, b"\x00" + payload)
                if not ok:
                    raise RuntimeError("write failed (%s)" % info)
                self.packets += 1
            sent += len(batch)
            got = self._drain(0x24, len(batch), 0.15)
            if got < len(batch):
                # One lost ack must not cost a full timeout: resend the batch
                # once, then move on — the next frame overwrites the colours.
                for payload in batch:
                    ok, info = P.write_report(self.handle, b"\x00" + payload)
                    if not ok:
                        raise RuntimeError("write failed (%s)" % info)
                    self.packets += 1
                self._drain(0x24, len(batch) - got, 0.15)
                window = 1

        self.frames += 1
        self.keepalive()


def solid(rgb):
    return bytes(rgb) * LAMPS


def rainbow(phase):
    out = bytearray()
    for i in range(LAMPS):
        h = (phase + i / LAMPS) % 1.0
        r, g, b = colorsys.hsv_to_rgb(h, 1.0, 1.0)
        out += bytes([int(r * 255), int(g * 255), int(b * 255)])
    return bytes(out)


def main():
    parser = argparse.ArgumentParser(description="Split65 2.4G colour relay")
    parser.add_argument("--colour", default=None, help="RRGGBB for a solid frame")
    parser.add_argument("--effect", choices=["rainbow", "solid"], default="rainbow")
    parser.add_argument("--seconds", type=float, default=5.0)
    parser.add_argument("--fps", type=float, default=20.0)
    args = parser.parse_args()

    link = WirelessLink()
    print("device  : VID %04X PID %04X bcd %04X"
          % (link.attrs.VendorID, link.attrs.ProductID, link.attrs.VersionNumber))
    body = link.require_ready()
    print("ready   : rev 0x%02X, %d lamps, device=%s" % (body[4], body[8], body[21]))

    colour = bytes.fromhex(args.colour) if args.colour else (255, 32, 32)
    interval = 1.0 / args.fps if args.fps > 0 else 0.0
    started = time.time()
    phase = 0.0
    frame_times = []
    try:
        while time.time() - started < args.seconds:
            frame_start = time.time()
            rgb = rainbow(phase) if args.effect == "rainbow" else solid(colour)
            link.send_frame(rgb)
            frame_times.append(time.time() - frame_start)
            phase = (phase + 1 / 60.0) % 1.0
            sleep = interval - (time.time() - frame_start)
            if sleep > 0:
                time.sleep(sleep)
    except KeyboardInterrupt:
        pass
    finally:
        link.close()

    elapsed = time.time() - started
    if frame_times:
        avg = sum(frame_times) / len(frame_times)
        worst = max(frame_times)
        print("sent    : %d frames, %d packets in %.2fs"
              % (link.frames, link.packets, elapsed))
        print("per frame: avg %.1f ms, worst %.1f ms, effective %.1f fps"
              % (avg * 1000, worst * 1000, 1.0 / avg if avg else 0))
        print("per packet: %.2f ms" % (avg * 1000 / 8.0))
    return 0


if __name__ == "__main__":
    sys.exit(main())
