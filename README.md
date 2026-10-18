# Split65 Windows Dynamic Lighting

[简体中文](#简体中文) · [English](#english)

---

## 简体中文

基于 EPOMAKER Split65 的 QMK 实验固件，为 WB32FQ95 键盘增加原生 HID LampArray 支持。参考微软 RP2040 示例，使用标准 Windows.Devices.Lights 接口，无额外 Windows 驱动。

**当前 v0.4.7 实验版：Windows 原生识别及基本灯光已有用户实测；Chroma 串灯通过分离设备布局得到用户确认解决。本次整理没有改动固件镜像。**

| 功能 | 状态 |
| --- | --- |
| USB 有线 Windows 动态照明 | 用户结束 OpenRGB 后台进程后确认识别，基本亮灭可用 |
| 68 灯及左右同步 | 已实现，用户报告内置灯效切换可用；全灯逐色与长时回归未完成 |
| 输入/右侧休眠恢复 | 已实现，右侧休眠修复有用户确认 |
| Chroma Studio | 用户当前配置中，移开鼠标布局后串灯消失；其他效果及长期运行未完整验证 |
| 2.4G / 蓝牙动态照明 | 未实现；常规无线输入代码保留 |
| VIA / EPOMAKER Hub | 原有路径保留，新实验 PID 可能不匹配旧设备过滤；未完整验证 |
| BIOS / UEFI | 当前非 Boot 接口，未保证可用 |

正常使用无需搭建编译环境，使用 `firmware/split65-dynamic-lighting-v0.4.7.bin` 即可。两半使用同一 BIN，硬件引脚自动区分左右。已安装两侧 v0.4.7 的用户无需为本次整理重刷。

使用 Windows 动态照明前完全退出 OpenRGB 后台，再查看「设置 → 个性化 → 动态照明」。使用 Chroma 时，若部分按键带着鼠标变色，请按 [Chroma 布局说明](docs/CHROMA.md) 移开鼠标设备；视觉外框分开仍可能存在逻辑灯位重叠。

- [已知问题与验证边界](docs/KNOWN-ISSUES.md)
- [Chroma 布局与串灯处理](docs/CHROMA.md)
- [版本说明](docs/RELEASE-NOTES.md)
- [构建方法](docs/BUILD.md)
- [固件与刷写说明](docs/FLASHING.md)
- [依赖版本与镜像校验](docs/build-manifest.json)
- [固件 SHA256](firmware/SHA256SUMS)

### 源码结构

`dynamic_lighting/` 包含协议、68 灯位表、分体同步及 QMK/WB32 补丁。`linker/wireless/`、`wls/` 保留社区无线支持及输入释放/休眠修复。`tests/` 包含协议、输入、旧右侧接收回调及描述符检查。`tools/` 包含构建和诊断工具，不会自动刷写设备。

LampArray 使用报告 ID 0x20–0x25，键盘/鼠标/多媒体/NKRO/LampArray 共享非 Boot HID 接口 0，VIA Raw HID 为接口 1。USB 342D:E4C7 是临时实验身份，不声称由厂商正式分配。没有 VHF、SYS、INF 或 Windows 安装器。

### 来源与许可

| 来源 | 固定版本 / 用途 |
| --- | --- |
| [Epomaker/Split65](https://github.com/Epomaker/Split65) | `8e253c005ee0bbacac0db3a09538d94ce3650ac5`，官方硬件与厂商代码 |
| [SRGBmods/EpomakerQMK](https://github.com/SRGBmods/EpomakerQMK) | `c64b964e0785a0345e8901fc15120a607cface9b`，QMK 移植与无线依赖 |
| [gwangyi/Split65](https://github.com/gwangyi/Split65) | `3978e3e18e3de9cf95ee1256ca6c1754804678a5`，社区修正与 DFU 参考 |
| [microsoft/RP2040MacropadHidSample](https://github.com/microsoft/RP2040MacropadHidSample) | `4666e34dcb87d6500230a55e0d34ce696bea4c4f`，共享 HID、TinyUSB 描述符与 Feature 传输参考 |
| [microsoft/ArduinoHidForWindows](https://github.com/microsoft/ArduinoHidForWindows) | `deb7e5e20d96bab5571ee2629d634375002f5513`，早期 HID 协议参考 |
| [FrameworkComputer/qmk_firmware](https://github.com/FrameworkComputer/qmk_firmware/tree/fl16-2025-hidlamp) | `44d802b99082ffc4c47f5a85c8adb42ce95cea51`，QMK LampArray 实现对照 |

新增 C 代码按 GPL-2.0-or-later；组合 QMK 构建包含 GPLv3 组件，按根目录 GPLv3 分发。上游版权声明保留；Microsoft/TinyUSB MIT 许可位于 dynamic_lighting/，GPLv2 文本位于 docs/。本项目不隶属于 EPOMAKER、Microsoft 或 Razer，没有 HLK 或 Razer 认证。

完整构建依赖由固定提交下载，不在此目录重复分发；对应源码引用和补丁随附。

---

## English

Experimental QMK firmware for the EPOMAKER Split65 that adds native HID LampArray support to the WB32FQ95 keyboard. It follows Microsoft's RP2040 sample and uses the standard `Windows.Devices.Lights` API, so no extra Windows driver is needed.

**Current v0.4.7 experimental build: native Windows recognition and basic lighting have been verified by users; the Chroma stray-strip problem is resolved by separating the device layout, confirmed by the user. No firmware images were changed by this cleanup.**

| Feature | Status |
| --- | --- |
| USB wired Windows Dynamic Lighting | Recognition confirmed after the user closed the OpenRGB background process; basic on/off works |
| 68 lamps and left/right sync | Implemented; the user reports on-board effect switching works; full per-lamp color and long-running regression are not complete |
| Input / right-half sleep recovery | Implemented; the right-half sleep fix is user-confirmed |
| Chroma Studio | In the user's current layout the stray strip disappears after moving the mouse device away; other effects and long-run behavior are not fully verified |
| 2.4G / Bluetooth dynamic lighting | Not implemented; the regular wireless input code is retained |
| VIA / EPOMAKER Hub | Original code paths retained; the new experimental PID may not match older device filters; not fully verified |
| BIOS / UEFI | Currently a non-Boot interface; availability is not guaranteed |

No build environment is needed for normal use — flash `firmware/split65-dynamic-lighting-v0.4.7.bin`. Both halves use the same BIN and the hardware pins determine the side automatically. Users already running v0.4.7 on both halves do not need to reflash because of this cleanup.

Fully exit the OpenRGB background process before using Windows Dynamic Lighting, then open Settings → Personalization → Dynamic Lighting. When using Chroma, if some keys change color together with the mouse, move the mouse device away as described in the [Chroma layout notes](docs/CHROMA.md); separating the visual frame may still leave logical lamp overlap.

> The documents under `docs/`, as well as the diagnostic tools, are currently written in Simplified Chinese.

- [Known issues and verification limits](docs/KNOWN-ISSUES.md)
- [Chroma layout and stray-strip handling](docs/CHROMA.md)
- [Release notes](docs/RELEASE-NOTES.md)
- [Build instructions](docs/BUILD.md)
- [Firmware and flashing](docs/FLASHING.md)
- [Dependency versions and image verification](docs/build-manifest.json)
- [Firmware SHA256](firmware/SHA256SUMS)

### Source layout

`dynamic_lighting/` holds the protocol, the 68-lamp table, split-half synchronization, and the QMK/WB32 patches. `linker/wireless/` and `wls/` retain community wireless support plus input-release and sleep fixes. `tests/` covers the protocol, input, the legacy right-half receive callback, and descriptor checks. `tools/` contains build and diagnostic tools; none of them flash the device automatically.

LampArray uses report IDs 0x20–0x25. Keyboard, mouse, consumer, NKRO, and LampArray traffic share non-Boot HID interface 0; VIA Raw HID is interface 1. USB `342D:E4C7` is a temporary experimental identity and is not claimed to be officially assigned by the vendor. There is no VHF, SYS, INF, or Windows installer.

### Sources and license

| Source | Pinned version / purpose |
| --- | --- |
| [Epomaker/Split65](https://github.com/Epomaker/Split65) | `8e253c005ee0bbacac0db3a09538d94ce3650ac5`, official hardware and vendor code |
| [SRGBmods/EpomakerQMK](https://github.com/SRGBmods/EpomakerQMK) | `c64b964e0785a0345e8901fc15120a607cface9b`, QMK port and wireless dependency |
| [gwangyi/Split65](https://github.com/gwangyi/Split65) | `3978e3e18e3de9cf95ee1256ca6c1754804678a5`, community fixes and DFU reference |
| [microsoft/RP2040MacropadHidSample](https://github.com/microsoft/RP2040MacropadHidSample) | `4666e34dcb87d6500230a55e0d34ce696bea4c4f`, shared HID, TinyUSB descriptors, and feature-transfer reference |
| [microsoft/ArduinoHidForWindows](https://github.com/microsoft/ArduinoHidForWindows) | `deb7e5e20d96bab5571ee2629d634375002f5513`, early HID protocol reference |
| [FrameworkComputer/qmk_firmware](https://github.com/FrameworkComputer/qmk_firmware/tree/fl16-2025-hidlamp) | `44d802b99082ffc4c47f5a85c8adb42ce95cea51`, QMK LampArray implementation reference |

New C code is licensed GPL-2.0-or-later; the combined QMK build includes GPLv3 components and is distributed under GPLv3 (root `LICENSE`). Upstream copyright notices are preserved; the Microsoft/TinyUSB MIT licenses live in `dynamic_lighting/`, and the GPLv2 text is in `docs/`. This project is not affiliated with EPOMAKER, Microsoft, or Razer, and has no HLK or Razer certification.

Full build dependencies are downloaded from pinned commits and are not redistributed in this repository; the corresponding source references and patches are included.
