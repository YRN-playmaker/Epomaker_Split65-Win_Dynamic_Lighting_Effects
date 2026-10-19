# EPOMAKER Split65 Windows 动态照明 — 0.5.0 候选
<img width="832" height="350" alt="image" src="https://github.com/user-attachments/assets/03dade2a-57f4-4cfd-a131-0f1a1ddb1665" />

中文 | [English](README.en.md)

基于 EPOMAKER Split65 / WB32FQ95 的 QMK 固件，提供有线 HID LampArray 和 2.4G Windows 软件桥接。接收器固件不变。

## 安装

下载中英发行包，关闭旧灯控程序，先用包内 BIN 更新左半边，再运行 Split65-Companion.exe 并启用动态照明。旧版驱动由同一程序更新，需要时请求管理员权限。当前右侧 0.4.9 可保留，分体协议仍为 0x44；也可两侧同版。无需刷接收器或分别安装插件。右上角支持中文 / English。

0.5.0 修复无线灯光队列阻塞输入、有线 USB 启动时接管被清理覆盖，以及独立亮度未被应用。无线调度目标由 200 ms 改为 100 ms，最多两包在途，实际吞吐由无线应答决定。Chroma 布局修复仍内置，生成配置副本后须在 Chroma 导入并选中。

**无线输入修复需要左侧 0.5.0 固件，不能只更新 EXE。** 详细步骤与验证边界见 [双语版本说明](releases/v0.5.0-candidate.md)。

## 验证

生产代码的持续 RGB/满队列松键、冻结报告对、辅助公平性、丢包让路、USB 接管竞态、256 点亮度坡道、分体兼容及模拟休眠测试通过。原生驱动使用官方 Microsoft SDK/WDK 编译，GUI/后台使用官方 .NET Framework 4.8 引用程序集。链接后的 BIN USB 描述符与键盘集合已检查。

本环境未直接操作用户键盘。新版本的真实 Windows 安装、输入延迟、渐变和有线控制仍待实机确认；软件回归不等于硬件问题已全部证实消失。按候选版发布，不宣称 HLK/WHQL/Razer 认证。不支持蓝牙 LampArray。

## 构建与许可

[固件构建](docs/BUILD.md) · [Windows 实现与构建](windows-bridge/README.md) · [Chroma 布局依据](docs/CHROMA.md)

EXE 内含完整对应源码和许可证，也提供单独源码 ZIP。上游：[EPOMAKER Split65](https://github.com/Epomaker/Split65)、[SRGBmods/EpomakerQMK](https://github.com/SRGBmods/EpomakerQMK)。LampArray 参考 [Microsoft RP2040 sample](https://github.com/microsoft/RP2040MacropadHidSample) 和 TinyUSB。GPL-2.0-or-later；上游声明保留。本项目不隶属于 EPOMAKER、Microsoft 或 Razer。
