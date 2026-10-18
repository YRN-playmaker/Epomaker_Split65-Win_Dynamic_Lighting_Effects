当前版本为 0.5.0 候选；左侧需要更新固件，驱动由新版 Companion 更新。修改及验证边界以 [0.5.0 双语说明](../releases/v0.5.0-candidate.md) 为准。下文保留历史记录。

# v0.4.8 候选版

新增 2.4G Raw HID 逐灯串流及客户端验证页；无线回复、租期与控制权修正。已有软件检查通过，当前版未进行实机验证，接收器原生 Windows 动态照明或虚拟桥接均尚未实现。见 [完整说明](0.4.8-Wireless-RGB.md)。

---

# v0.4.7 实验版说明

整理日期：2026-10-18。固件版本和镜像未变；本次更新使用文档、通用 Chroma 布局工具和验证范围。

- USB 有线原生 HID LampArray：68 灯、出厂基础层按键绑定、分体颜色同步。
- 参考微软 RP2040 示例采用共享非 Boot HID 接口，并修正 WB32 EP0 Feature 传输及元数据枚举。
- 包含输入释放、无线队列及右侧休眠恢复修正；此前休眠修复已有用户确认，完整长期回归尚未完成。
- 用户确认移除 OpenRGB 后台后，Windows 原生识别恢复。
- 两半均为 v0.4.7 后 Chroma 仍串灯；随后仅调整 Chroma 布局，用户确认串灯消失，见 [Chroma 说明](CHROMA.md)。

推荐镜像 `firmware/split65-dynamic-lighting-v0.4.7.bin`，两半使用同一文件，由硬件引脚区分左右。已安装该版无需为本次整理重新刷机。

大小：78,776 bytes。SHA256：

```text
726045f71d0879576e029253cfbce577ceee371c6ea817f0b586f06c4239b5e7
```

无线动态照明未实现。实验 PID、BIOS/UEFI、VIA/Hub、验证范围见 [已知限制](KNOWN-ISSUES.md)。本项目没有微软 HLK 或 Razer 认证。
