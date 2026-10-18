# 0.4.8 后续实现

已实现并通过软件回归：2.4G Raw RGB 接管、无线回复、3 秒租期、断连/模式切换释放、USB 重置隔离。有线 LampArray 保留，Windows 无线桥接尚未实现，硬件结果待确认。见 [0.4.8 说明](0.4.8-Wireless-RGB.md)。

以下保留 0.4.7 的协议调查；其中列出的旧版软件缺口已在本轮候选中处理。

---

# 2.4G 灯控能力核查

日期：2026-10-18。当前已交付固件仍为 v0.4.7；本次仅调查与修正文档，没有改动 BIN。

## 修正此前判断

用户确认官方 Hub 可在 2.4G 下调整 v0.4.7 的灯光。源码也存在双向 Raw HID / VIA 无线通道。因此不能将“当前没有无线 LampArray”描述为“无线灯控协议不存在”，也不能预先认定必须重刷接收器才能实现无线灯光桥接。

无线接收进入 `md_receive_raw_cb()`，再调用 `raw_hid_receive()`，与有线使用相同命令分发。`replaced_hid_send()` 在无线模式经 `md_send_raw()` 回传；VIA 翻译单元已经重定向到这个函数。

## 官方 Hub 当前 Split65 分支

检查来源：`https://hub.epomaker.com/lighting`，主脚本 `https://hub.epomaker.com/assets/index-ACf8N4CE.js`。

脚本大小 2,713,436 bytes；SHA256：`5db2b95d4f48cd728450e56e4a884ba3b0d32d85ad6c5e910e22257a961dfdfd`。线上脚本可能随时更新，这些是本次取得的版本。

Split65 的设备定义走 QMK 配置。其灯控使用 Report ID 0、32 字节 Raw HID，属于标准 VIA RGB Matrix 通道 3：

| 请求前缀 | 含义 |
| --- | --- |
| `07 03 01 value` | 设置全局亮度 |
| `07 03 02 value` | 设置内置效果编号 |
| `07 03 03 value` | 设置效果速度 |
| `07 03 04 hue saturation` | 设置全局颜色 |
| `08 03 value_id` | 读取上述参数 |
| `09 03` | 保存配置 |

当前 Split65 继承的 QMK 配置为 `ledCustomLayers: 0`、`isSupportLEDEdit: false`；QMK 灯控类的 `saveCustomLayer()` 与 `loadCustomLayer()` 返回不支持。这里的“可选自定义颜色”是全局颜色能力，不能据此当作已实现逐灯逐帧串流。

已经从原脚本抽取并离线执行 QMK 灯控类，以模拟传输核对实际生成的五个 32 字节包：效果、速度、亮度、全局 HSV、保存，与固件处理一致。该检查没有连接或向用户设备发送数据。

## v0.4.7 的实际缺口

- 键盘 USB 接口已有 LampArray，接收器通过现有 Raw HID 控制灯效不等于接收器已对 Windows 暴露 LampArray。尚未读取本轮接收器的实际描述符，不据此假定其完整功能。
- 固件已有社区兼容的 `0x24` RGB 数据、`0x25` 开启、`0x26` 关闭串流命令。`0x24` 每包至多 9 个 RGB 灯，68 灯一帧需至少 8 包。
- 但当前 `split65_lamp_task()` 和 `split65_lamp_active()` 将串流显示约束在 USB 活动状态；前者还明确要求 `DEVS_USB`。这也挡住了通过无线 Raw HID 到达的自定义串流。
- `compat.c` 自定义命令回复直接调用 `raw_hid_send()`，仅 VIA 翻译单元做了无线重定向。无线桥接还需核对并修正自定义命令回复路径。

优先研究复用现有 Raw HID 的 Windows LampArray 桥接。需要完善无线接管、断连/超时释放、模式切换、睡眠唤醒、双侧同步及回复路由，再验证无线吞吐和输入延迟。驱动侧可参考 Microsoft VHF；它需要 Windows 内核驱动实现，不能仅用普通用户态脚本宣称已创建原生 LampArray。

如果要求不安装桥接组件、插入接收器即被 Windows 原生识别，则需继续调查接收器自身 LampArray 描述符和 Feature 报告的扩展能力；目前没有验证这条路线。

当前结论：无线灯控通道已存在且用户确认参数控制可用；无线 Windows 动态照明尚未实现，逐灯连续传输能力尚未实测。保持已工作的 v0.4.7，不把协议调查包装成新的已验证固件。

参考：[Microsoft VHF](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/virtual-hid-framework--vhf-)。
