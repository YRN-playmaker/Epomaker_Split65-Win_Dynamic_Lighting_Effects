# Split65 Windows Dynamic Lighting

[简体中文](#简体中文) · [English](#english)

---

## 简体中文

基于 EPOMAKER Split65 / WB32FQ95 的 QMK 实验固件，参考微软 RP2040 LampArray 示例。

**v0.4.8 候选版：新增 2.4G Raw HID 逐灯 RGB 串流。有线 Windows 动态照明保留。无线接收器自动接入 Windows 动态照明的桥接尚未实现，本版尚无硬件实测。**

v0.4.7 是已有用户验证的基线：退出 OpenRGB 后 Windows 能识别；Chroma 串灯通过分离鼠标布局消失。v0.4.8 保持相同 VID/PID、灯位、LampArray 描述符及 0x44 分体协议，USB 固件版本改为 0048。

| 能力 | v0.4.8 状态 |
| --- | --- |
| 有线 Windows 动态照明 | 沿用 v0.4.7，软件与链接镜像回归通过，当前版待实测 |
| 2.4G 全灯/单灯 RGB 串流 | 已实现，复用现有 Raw HID，无线回复、双侧同步和失联释放有软件检查 |
| 2.4G 原生 Windows 动态照明枚举 | 尚未实现接收器接口或 Windows 桥接；不能仅刷本版后宣称可识别 |
| Hub 2.4G 内置灯效参数控制 | 原有 VIA 路径保留；用户确认 v0.4.7 可用，0.4.8 待实测 |
| 输入与休眠 | 保留先前修复，回归检查通过，当前版本长期硬件回归未完成 |
| 蓝牙 RGB 串流 | 本版明确拒绝，避免在未验证的通道假报成功 |
| VIA / Hub、BIOS/UEFI | 实验 PID 过滤、非 Boot 接口等限制仍见已知问题 |

### 使用

无需搭建编译环境，推荐候选文件：`firmware/split65-dynamic-lighting-v0.4.8.bin`。

当前双侧 v0.4.7 的用户，本轮只升级左侧即可；右侧保留 0.4.7，分体协议未变。两侧若均更新也使用同一 BIN，由硬件引脚区分左右。保留 v0.4.7 以便回退。

无线验证：保留整个 `tools/wireless-rgb/` 文件夹，使用 Edge/Chrome 打开 `Start-Wireless-RGB.html`，按页内步骤切到 2.4G 并移除键盘直连数据线。连接时先读取固件信息，确认 0.4.8 后才开放颜色按钮。逐包确认、低速发送，客户端每秒续期；3 秒无数据、断连或模式切换会释放无线控制。测试页不会安装驱动、修改按键或写灯光 EEPROM。

- [0.4.8 版本说明](docs/0.4.8-Wireless-RGB.md)
- [刷写与回退](docs/FLASHING.md)
- [Chroma 布局处理](docs/CHROMA.md)
- [无线协议调查](docs/WIRELESS.md)
- [无线动态照明桥接评估](docs/WIRELESS-BRIDGE.md)
- [已知问题](docs/KNOWN-ISSUES.md)
- [构建](docs/BUILD.md) / [验证范围](docs/LOCAL-VALIDATION.md)
- [构建清单](docs/build-manifest.json) / [固件 SHA256](firmware/SHA256SUMS)

有线 Windows 动态照明使用前，完全退出 OpenRGB 后台；Chroma 保持鼠标与键盘的逻辑灯位分离。当前没有 VHF、SYS、INF 或 Windows 安装器。

### 源码结构

`dynamic_lighting/`：LampArray 协议、68 灯位、分体同步和兼容 Raw RGB。`linker/wireless/`：现有无线输入与 Raw HID 通道。`tools/wireless-rgb/`：候选版无线验证页。`tests/`：生产函数、协议、输入、休眠和客户端模拟检查。

LampArray 报告 ID 0x20–0x25，与键盘/鼠标/多媒体/NKRO 共用非 Boot HID 接口 0；VIA Raw HID 为接口 1。Raw HID 负载中的 0x24/0x25/0x26 是另一接口内的命令，不与 LampArray 报告 ID 混用。342D:E4C7 为临时实验身份。

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

Experimental QMK firmware for the EPOMAKER Split65 / WB32FQ95, following Microsoft's RP2040 LampArray sample.

**v0.4.8 candidate: adds 2.4G Raw HID per-lamp RGB streaming. Wired Windows Dynamic Lighting is retained. A bridge that would make the wireless receiver appear automatically in Windows Dynamic Lighting is not implemented, and this build has no hardware verification yet.**

v0.4.7 is the user-verified baseline: Windows recognises the keyboard once OpenRGB is closed, and the Chroma stray-strip problem disappears when the mouse layout is separated. v0.4.8 keeps the same VID/PID, lamp positions, LampArray descriptor, and 0x44 split protocol; the USB firmware revision becomes 0048.

| Capability | v0.4.8 status |
| --- | --- |
| Wired Windows Dynamic Lighting | Unchanged from v0.4.7; software and linked-image regression pass, current build not yet verified on hardware |
| 2.4G full-frame / single-lamp RGB streaming | Implemented over the existing Raw HID channel; wireless replies, split-side sync, and lease release are covered by software checks |
| Native Windows Dynamic Lighting enumeration over 2.4G | Not implemented — neither a receiver-side interface nor a Windows bridge; flashing this build alone does not make the receiver appear |
| Hub 2.4G built-in effect parameter control | Existing VIA path retained; user-confirmed on v0.4.7, v0.4.8 not yet tested |
| Input and sleep behaviour | Earlier fixes retained, regression checks pass, long-running hardware regression not complete |
| Bluetooth RGB streaming | Explicitly rejected in this build rather than faking success on an unverified channel |
| VIA / Hub, BIOS/UEFI | Experimental-PID filtering and the non-Boot interface limitations remain as listed in known issues |

### Usage

No build environment is required for normal use; the recommended candidate is `firmware/split65-dynamic-lighting-v0.4.8.bin`.

Users currently on v0.4.7 on both halves only need to upgrade the left half this round; keep the right half on v0.4.7 because the split protocol is unchanged. Both halves use the same BIN when updated, and the hardware pins determine the side. Keep v0.4.7 for rollback.

Wireless check: keep the whole `tools/wireless-rgb/` folder together and open `Start-Wireless-RGB.html` in Edge/Chrome, then follow the on-page steps to switch to 2.4G and remove the keyboard's direct USB cable. The page reads the firmware information first and only offers colour controls after confirming 0.4.8. Packets are acknowledged one by one at a low rate and the client renews once per second; no data for 3 seconds, a disconnect, or a mode switch releases wireless control. The page installs no driver, changes no keymap, and does not write the lighting EEPROM.

- [v0.4.8 release notes](docs/0.4.8-Wireless-RGB.md)
- [Flashing and rollback](docs/FLASHING.md)
- [Chroma layout handling](docs/CHROMA.md)
- [Wireless protocol investigation](docs/WIRELESS.md)
- [Wireless Dynamic Lighting bridge assessment](docs/WIRELESS-BRIDGE.md)
- [Known issues](docs/KNOWN-ISSUES.md)
- [Build](docs/BUILD.md) / [Verification scope](docs/LOCAL-VALIDATION.md)
- [Build manifest](docs/build-manifest.json) / [firmware SHA256](firmware/SHA256SUMS)

Fully exit the OpenRGB background process before using wired Windows Dynamic Lighting, and keep the mouse and keyboard logical lamp layouts separated in Chroma. There is currently no VHF, SYS, INF, or Windows installer.

> The documents under `docs/`, the wireless test page, and the diagnostic tools are currently written in Simplified Chinese.

### Source layout

`dynamic_lighting/`: LampArray protocol, 68 lamp positions, split sync, and the compatible Raw RGB path. `linker/wireless/`: the existing wireless input and Raw HID channel. `tools/wireless-rgb/`: the candidate wireless verification page. `tests/`: production functions, protocol, input, sleep, and client-simulation checks.

LampArray uses report IDs 0x20–0x25 on the non-Boot HID interface 0 shared with keyboard/mouse/consumer/NKRO; VIA Raw HID is interface 1. The 0x24/0x25/0x26 commands live inside Raw HID payloads on the other interface and are never mixed with LampArray report IDs. 342D:E4C7 is a temporary experimental identity.

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
