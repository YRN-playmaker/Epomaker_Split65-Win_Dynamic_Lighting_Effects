# EPOMAKER Split65 Windows Dynamic Lighting — 0.5.0 candidate

[中文](README.md) | English

QMK firmware for Split65 / WB32FQ95, with wired HID LampArray and a Windows 2.4G software bridge. The receiver is not reflashed.

Close old controllers, flash the supplied 0.5.0 BIN onto the LEFT half, then run Split65-Companion.exe and enable lighting. The same app updates the old bridge driver with administrator access when needed. Existing right-half 0.4.9 firmware is compatible (split protocol 0x44), or both halves can use the new BIN. No separate plugin installer is required. 中文 / English is selectable in the top-right corner.

0.5.0 fixes keyboard starvation behind continuous RGB replies, mode cleanup that could discard wired takeover during USB startup, and ignored independent intensity. Wireless scheduling targets 100 ms rather than 200 ms, with at most two packets in flight; real throughput depends on radio responses. Chroma layout repair remains included; import and select its generated copy in Studio.

**The input fix requires left-half firmware 0.5.0; updating only the EXE is insufficient.** See [bilingual release notes and validation limits](releases/v0.5.0-candidate.md).

Production-code tests cover continuous RGB/full-queue key release, frozen keyboard pairs, auxiliary fairness, loss yielding, USB takeover ordering, 256-point intensity ramps, split compatibility and simulated sleep. Native code is built using official Microsoft SDK/WDK; managed code uses official .NET Framework 4.8 references. Linked firmware USB descriptors were checked.

This environment cannot directly access the user's keyboard. Actual Windows setup, input latency, gradients and wired control on this new build still need hardware validation. It is a candidate, not a claim of completed hardware certification. Bluetooth LampArray is unsupported.

See [firmware builds](docs/BUILD.md), [Windows builds](windows-bridge/README.md) and [Chroma layout evidence](docs/CHROMA.md). Complete corresponding source and licenses are embedded in the EXE and supplied separately. Based on EPOMAKER Split65, SRGBmods/EpomakerQMK, Microsoft's RP2040 LampArray sample and TinyUSB; upstream notices are retained. Project code is GPL-2.0-or-later. This project is not affiliated with EPOMAKER, Microsoft or Razer.
