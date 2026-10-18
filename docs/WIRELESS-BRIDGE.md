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

2026-10-18 实机读取 `0x30` 应答（`tools/wireless-rgb/probe_status.py`，只读）：

```
device  : VID 342D PID E4C6 bcd 2001
raw hid : in=33 out=33 usage 0xFF60/0x0061
reply   : 00 30 44 4c 32 48 02 03 44 44 00 60 00 00 00 01 00 00 00 00 01 00 06 01 0d 00 01 01 00 03 02 00 00
signature 'DL2'  revision 0x48  lamps 68  device 6 (2.4G)  raw protocol 1  lease 3s  module state 2
```

结论：左侧 v0.4.8、2.4G 链路、接收器 Raw HID 转发均正常，host→键盘的逐灯通道可用。缺的只是 Windows 端的 LampArray 呈现。

实现提示：早期探测脚本在读超时后直接丢弃 OVERLAPPED 读请求，残留的 pending read 会吞掉随后的应答，表现为「写入成功但没有回复」。现在的实现会在超时后 `CancelIoEx` 并等待取消完成。


## 不刷接收器的两条可行方案

接收器描述符不能改、VHF 不能用在用户态、Secure Boot 又拦着自签内核驱动，因此「纯软件让 Windows 凭空看到 LampArray」这条路已经封闭。剩下两条都可行：

### 方案 1：接线混合模式（免硬件，但要插线）

USB 线接上时让键盘继续枚举，只暴露 LampArray 与 VIA，输入仍旧走 2.4G。这样 Windows 动态照明可用，蓝牙/2.4G 输入不受影响。代价是必须插着一根线（可以只是充电线），不是纯无线。纯固件改动，不需接收器固件、不需驱动、不需要关闭 Secure Boot。

### 方案 2：外部 LampArray 桥（真无线，需要一块小板）

自制一个 USB 设备（RP2040 / ESP32-S3 均可），把本项目的 68 灯 LampArray 描述符复制过去，Windows 会像对待有线键盘一样把它列进动态照明；PC 上一个小程序把颜色经 0x24 转发给现有接收器。接收器固件不动，也不装驱动。

实测带宽（`tools/wireless-rgb/bridge.py`，2026-10-18，2.4G）：

| 操作 | 耗时 | 含义 |
| --- | --- | --- |
| `0x25` 单包往返 | 19.8 ms | 链路基准 RTT |
| `0x24` 单包往返 | 24.9 ms | 带数据略高 |
| 68 灯整帧（逐包确认） | 182 ms | 约 5.5 fps |
| 68 灯整帧（4 包流水） | 109–121 ms | **约 8.3–9.2 fps**（已实现） |
| 8 包流水 | 丢 1/8 确认 | 窗口再深会丢包 |
| 免确认洪水发送 | 9–59 ms/帧 | 快，但随后 `0x25` 完全无确认 —— 数据被丢弃，数字不可信 |

结论：逐包确认是唯一可信的投递信号。可靠整帧吞吐约 **8.3 fps**（4 包流水 + 150 ms 短超时重发），静态色/慢速渐变可用，高帧率动画不够；单灯或局部变更约 45 次/秒，适合增量更新。要更高帧率只能动固件或接收器协议 —— 后者又回到刷接收器。

`tools/wireless-rgb/bridge.py` 已按该策略实现：4 包流水、短超时重发、每秒续期、退出时释放。

## 下一步（待确认）


1. 确认键盘处于 2.4G 且已刷 v0.4.8，重跑 `0x30` 探测，确认 `revision=0x48`、`device=6`、`raw proto=1`；
2. 进入 DFU，只读导出接收器固件（不改写），确认 MCU、射频芯片与协议形态；
3. 根据导出结果决定是否投入接收器固件重写。
