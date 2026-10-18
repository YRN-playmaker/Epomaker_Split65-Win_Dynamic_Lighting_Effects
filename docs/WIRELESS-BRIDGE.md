# 2.4G 下的 Windows 动态照明：可行性评估

更新：2026-10-18。本文记录一次针对性核查的结论与证据，不包含新的固件改动。

## 结论

在**只插接收器、键盘不接 USB** 的前提下，让 Windows 动态照明（Dynamic Lighting）原生识别 Split65，当前硬件上无法用「应用 + 用户态驱动」实现。只有两条真正可行的路线：

1. 让**接收器自己**在 USB 描述符里声明 LampArray 集合（需要替换接收器固件）；
2. 由**微软签名的内核驱动**创建虚拟 LampArray 设备。

用户态桥接（UMDF）不可行，原因见下。

## 实测证据：接收器没有 LampArray 集合

用 SetupAPI + `IOCTL_HID_GET_REPORT_DESCRIPTOR` 读取本机实时描述符（2026-10-18，接收器已插入，键盘处于 2.4G）：

| 设备 | 接口 | Usage | 说明 |
| --- | --- | --- | --- |
| 342D:E4C6 (bcdDevice 2001) | MI_00 | 0x0001/0x0006 | 键盘集合，9 字节输入 |
| | MI_00 Col02 | 0x0001/0x0002 | 鼠标 |
| | MI_00 Col03 | 0x000C/0x0001 | 消费者控制 |
| | MI_00 Col04 | 0x0001/0x0080 | 系统控制 |
| | MI_00 Col05 | 0x0001/0x0000 | 64 字节通用集合 |
| | MI_01 | 0x0001/0x0006 | 16 字节键盘（NKRO） |
| | MI_02 | 0xFF60/0x0061 | VIA Raw HID，33/33 字节 |

所有接口的 `LampArray (0x59)` 检测结果均为 **no**。作为对照，有线键盘 342D:E4C7 的 MI_00 下有 6 个集合，其中 Col06 是 LampArray。

Windows 动态照明只枚举 HID 描述符中声明了 LampArray 用途页的设备。描述符由接收器固件静态提供，键盘无法通过 2.4G 改变它，因此「插上接收器就出现在动态照明里」在现有接收器固件下不成立。

## 为什么 UMDF 桥接必然失败

联想的 Companion v0.1.2 尝试用 UMDF2 驱动 `Split65LampBridge.dll` 绑定 `ROOT\Split65LampBridge`，通过 `Include=hidvhf.inf` + `Needs=vhfservice.NT` 获得 VHF 能力，再用用户态 `Split65Bridge.exe` 通过 IOCTL 探测控制接口。

失败记录：

- `Install-20261018-143042.txt`：驱动包与根设备都创建成功（`problem=0`），但 `BRIDGE_PROBE=MISSING_INTERFACE`。
- `Driver-evidence-20261018-143353.txt`：系统事件 **10116**「由于用户模式驱动程序崩溃，设备 Split65 Wireless LampArray Bridge 已脱机」与 **10120**「主机进程已终止」。

根因不是签名或 INF 细节：虚拟 HID 框架（VHF）是**内核态 API**，`VhfCreate` 只能由内核驱动调用，`hidvhf.inf` 只是把 `vhf.sys` 作为服务/过滤器装好，并不会把 VHF 变成用户态可用接口。UMDF 主机加载该 DLL 后无法创建虚拟 HID 设备，进程崩溃、控制接口自然不存在。

补充约束：本机 `UEFISecureBootEnabled=1`，且未安装 WDK / Visual Studio，因此即便改写为 KMDF 驱动，本机也无法构建，自签名内核驱动在 Secure Boot 开启时同样无法加载。

## 三条路线的成本与风险

| 路线 | 产出 | 成本 | 风险 |
| --- | --- | --- | --- |
| A. 接收器固件 | 插上接收器即被 Windows 原生识别，最接近目标 | 需要先 dump 接收器固件、确认 MCU 与射频芯片、逆出 2.4G 协议（键盘侧源码已在手），再实现带 LampArray 的接收器固件 | 高；可能变砖，但 WB32 DFU（342D:DFA0）通常可救 |
| B. 微软签名内核驱动 | 虚拟 LampArray，键盘侧无需改动 | 需要 EV 证书 + Partner Center attestation；个人试验需关闭 Secure Boot + testsigning + WDK | 高中；安全降级，不适合分发 |
| C. 有线 WDL + 2.4G 应用灯效 | 今天可用：有线走原生 WDL，2.4G 由宿主程序通过 0x24 串流驱动灯效 | 低 | 不是 Windows 原生动态照明 |

建议顺序：**A**（先做只读 dump，不改任何东西）→ C 作为过渡 → B 仅在拿得到签名通道时考虑。

## 2.4G 通道现状

`0x24/0x25/0x26` 命令走接收器的 `0xFF60/0x61` Raw HID（33 字节报文，report id 0），与 Hub 的 VIA 通道 3 不冲突：Hub 灯效参数用 `07 03 xx` 等前缀，改键仍走原有 VIA 命令，v0.4.8 未改动 PID、描述符和 0x44 分体协议。

本轮用只读 `0x30` 状态查询探测该通道：33 字节写入成功，3 秒内没有收到输入报告。可能原因：键盘未切到 2.4G、未与当前接收器配对、或尚未刷入 v0.4.8。需要用 `tools/wireless-rgb/` 页面或同构探测脚本在实机上复核。

## 下一步（待确认）

1. 确认键盘处于 2.4G 且已刷 v0.4.8，重跑 `0x30` 探测，确认 `revision=0x48`、`device=6`、`raw proto=1`；
2. 进入 DFU，只读导出接收器固件（不改写），确认 MCU、射频芯片与协议形态；
3. 根据导出结果决定是否投入接收器固件重写。
