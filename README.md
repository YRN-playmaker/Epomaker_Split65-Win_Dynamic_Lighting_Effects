# EPOMAKER Split65 Windows Dynamic Lighting — 0.5.0 candidate

<img width="832" height="350" alt="image" src="https://github.com/user-attachments/assets/03dade2a-57f4-4cfd-a131-0f1a1ddb1665" />

English | [中文](README.zh.md)

QMK firmware for Split65 / WB32FQ95, with wired HID LampArray and a Windows 2.4G software bridge. The receiver is not reflashed.

## Install

Close old lighting controllers. Flash the supplied 0.5.0 BIN onto the LEFT half first, then run `Split65-Companion.exe` and enable dynamic lighting. The same app updates an older bridge driver and requests administrator access when needed. Existing right-half 0.4.9 firmware remains compatible (split protocol 0x44), or both halves can use the new BIN. The receiver is not flashed and no separate plugin installer is required. The UI switches between Chinese and English in the top-right corner.

0.5.0 fixes keyboard starvation behind continuous RGB replies, mode cleanup that could discard a wired takeover during USB startup, and ignored independent intensity. Wireless scheduling targets 100 ms instead of 200 ms, with at most two packets in flight; actual throughput depends on radio responses. Chroma layout repair remains included; import and select its generated copy in Studio.

**The input fix requires left-half firmware 0.5.0; updating only the EXE is insufficient.** See the [English release notes and validation limits](releases/v0.5.0-candidate.en.md).

## Validation

Production-code tests cover continuous RGB/full-queue key release, frozen keyboard pairs, auxiliary fairness, loss yielding, USB takeover ordering, 256-point intensity ramps, split compatibility and simulated sleep. Native code is built with the official Microsoft SDK/WDK; managed code uses official .NET Framework 4.8 references. Linked firmware USB descriptors were checked.

This environment cannot directly access the user's keyboard. Actual Windows setup, input latency, gradients and wired control on this build still need hardware validation. It is a candidate, not a claim of completed hardware certification. Bluetooth LampArray is unsupported.

## Build and license

See [firmware builds](docs/BUILD.en.md), [Windows builds](windows-bridge/README.en.md) and [Chroma layout evidence](docs/CHROMA.en.md). The EXE embeds complete corresponding source and licenses; a separate source ZIP is also supplied. Upstream: [EPOMAKER Split65](https://github.com/Epomaker/Split65), [SRGBmods/EpomakerQMK](https://github.com/SRGBmods/EpomakerQMK). LampArray follows [Microsoft's RP2040 sample](https://github.com/microsoft/RP2040MacropadHidSample) and TinyUSB. Project code is GPL-2.0-or-later; upstream notices are retained. This project is not affiliated with EPOMAKER, Microsoft or Razer.
