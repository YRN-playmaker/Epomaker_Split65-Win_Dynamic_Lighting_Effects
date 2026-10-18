当前版本为 0.5.0 候选；左侧需要更新固件，驱动由新版 Companion 更新。修改及验证边界以 [0.5.0 双语说明](../releases/v0.5.0-candidate.md) 为准。下文保留历史记录。

# 2.4G Windows 动态照明的实现边界

当前版本：0.4.9 candidate，2026-10-18。

原厂接收器没有 LampArray 顶层集合，因此只升级键盘不会让接收器自动变成 LampArray。已有 0x24/0x25/0x26 Raw HID 通道可以传输无线逐灯 RGB；0.4.8 的基本无线颜色已获用户确认。

不改接收器也有软件路线：Windows VHF 虚拟 HID 驱动提供 LampArray，伴侣程序读取虚拟灯态，并通过接收器的原有 Raw HID 通道发送给键盘。它需要安装驱动及保持程序运行，不是免安装的接收器原生支持。

微软 [Dynamic Lighting devices](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/dynamic-lighting-devices) 明确列出固件与 VHF 两条路线。微软 [DMF VirtualHidDeviceVhf](https://github.com/microsoft/DMF/blob/f9da490cd6e7a216f431ad245255548ab020d4f0/Dmf/Modules.Library/Dmf_VirtualHidDeviceVhf.c) 明确实现 DMF_USER_MODE 分支：在 PrepareHardware 阶段打开 WDFIOTARGET，以文件句柄初始化 VHF。因此“UMDF 必然不可行”并不是有效结论。

## 已知失败与本版修正

0.1.2 用户日志显示驱动已绑定，WUDFRd 已登记，但系统出现 UMDF 主机崩溃事件 10116/10120。此前日志缺少 Application/WER 的具体异常，最终根因未被证明。

本版将 VHF 下层打开从 DeviceAdd 移到 PrepareHardware，补全 ReleaseHardware 销毁顺序，去除灯态的 WDF 自旋锁对象依赖，新增加载阶段 NTSTATUS 事件并收集相关 Application/WER 原始 XML。控制接口仅在下层已准备好且 IOCTL 版本相符时视为就绪。模拟测试不能替代真实 Windows 驱动加载验证。

## 使用与限制

见 [Windows 伴侣程序](../windows-bridge/README.md)。0.4.8/0.4.9 均可连接。最多按 200 ms 整帧间隔发送，8 包逐包确认，20 ms 最小包间隔；实际帧率取决于无线 RTT，不承诺 5 fps。静态色、渐变及较慢效果为初步目标，不承诺 Chroma 高速动画表现。

无线键盘不用接 USB，不需要 ESP/RP2040。若改走外部硬件桥，也仍需明确颜色从该桥转发到原厂接收器的实现，不能只接一块板就视为完成。

若未来追求接收器完全即插即用、免伴侣软件，才需要研究接收器原生 LampArray。不能把键盘的 WB32 DFU 当成接收器也可 DFU 或可恢复的证据。
