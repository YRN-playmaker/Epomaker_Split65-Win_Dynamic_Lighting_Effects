当前版本为 0.5.0 候选；左侧需要更新固件，驱动由新版 Companion 更新。修改及验证边界以 [0.5.0 双语说明](../releases/v0.5.0-candidate.md) 为准。下文保留历史记录。

# 当前 0.4.9

当前刷写选择见 [0.4.9 版本说明](0.4.9-Wireless-Bridge.md)。程序可导出 BIN，不会自动刷写。下面是历史版本步骤，不代表本版推荐混刷组合。

---

# 0.4.8 候选刷写

当前双侧 0.4.7 的用户：本轮只升级左侧为 firmware/split65-dynamic-lighting-v0.4.8.bin，右侧保留 0.4.7。协议仍为 0x44。无需编译环境；刷写工具与 WB32 DFU 操作同以前。若两侧都升级，使用同一 BIN。

本轮提供的是 2.4G Raw RGB 逐灯控制候选；Windows 无线自动识别尚未实现。刷完用 tools/wireless-rgb/Start-Wireless-RGB.html 验证。

回退文件 firmware/split65-dynamic-lighting-v0.4.7.bin 与此前交付字节一致。只需回退本轮更新的左侧。以下保留原 0.4.7 的硬件与驱动说明。

---

# 固件与刷写

当前已安装两侧 v0.4.7 的用户无需重新刷写。本次整理没有修改 BIN；Chroma 串灯的有效处理是移动应用内设备布局，见 [Chroma 说明](CHROMA.md)。

| 文件 | 用途 |
| --- | --- |
| split65-dynamic-lighting-v0.4.7.bin | 当前 Windows LampArray 版本；左右使用同一文件，硬件引脚自动区分半区 |
| split65-input-check-v0.4.7.bin | 同版关闭 LampArray，开发隔离输入使用，未经完整硬件回归 |
| split65-dynamic-lighting-v0.4.4.bin | 先前右侧休眠修复版本/已有组合回退对照 |
| split65-input-check-v0.4.4.bin | 先前关闭 LampArray 的输入隔离版本 |

这些是 WB32FQ95 的 BIN，不能当作 RP2040/AVR HEX 使用。MCU 不可通过更换 QMK Toolbox 的 AVR 下拉选择改变；必须检测到 WB32 DFU。DFU 设备 342D:DFA0 使用 WinUSB，正常键盘使用 HidUsb。不要把正常键盘 HID 驱动替换成 WinUSB。

首次安装前导出 VIA 配置并保存适合实际 PCB 的原厂恢复文件。本包不是原厂备份，不能保证兼容所有硬件修订。不要在当前可用的键盘上为串灯问题反复尝试不相关的固件。

进入 DFU 的硬件操作以 [官方讨论](https://github.com/Epomaker/Split65/issues/2) 与 [社区图示](https://github.com/gwangyi/Split65/blob/main/howto.jpg) 为准，先确认 PCB。右侧触点位置涉及上层 PCB 与轴体，不应凭文字猜测短接点。操作前断开电源与分体线；需要更新的半区须单独连接 USB。QMK Toolbox 确认 WB32 DFU 后选择正确 BIN，再执行 Flash，完成后移除短接并恢复硬件开关。

首次安装建议左右均使用 v0.4.7，这是用户最新测试组合。更新时逐个半区单独连接 USB 并进入 DFU；已经工作的 v0.4.7 无需反复刷写。分体灯光线协议仍为 0x44；软件测试也执行了旧 v0.4.4 接收回调，但不保证任意历史版本混用。

Windows 原生动态照明须 USB 有线连接。退出 OpenRGB 后台进程及相关访问，再查看“设置 → 个性化 → 动态照明”。Chroma 第三方控制仍需要 Windows 动态照明，原生 Razer 鼠标路径与第三方键盘路径并不相同。使用限制见 [KNOWN-ISSUES.md](KNOWN-ISSUES.md)。
