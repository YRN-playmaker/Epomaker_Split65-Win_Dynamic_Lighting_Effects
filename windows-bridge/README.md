# Split65 Companion 0.5.0 候选

同一中英 EXE 提供驱动安装/更新、无线桥接、Chroma 布局修复、源码/固件/日志导出和卸载。

先给左半边刷入随包的 0.5.0 BIN，再运行新版 EXE 并启用动态照明。旧驱动将自动提示管理员授权以更新。输入调度修复在固件中；当前右侧 0.4.9 仍兼容，不刷接收器。

本版增加优先输入通道、USB 启动前状态清理、255 级亮度合成和 100 ms 无线调度目标，最多两包在途。完整修改与测试范围见 [双语说明](../releases/v0.5.0-candidate.md)。新版本实际输入、渐变和有线控制仍需硬件验证；真实吞吐不承诺 10 fps。

## 安装范围

支持 Windows 11 24H2/25H2 x64，build 26100+。普通运行不需要管理员；安装、修复和卸载通过同一个 EXE 请求 UAC。

- 驱动安装：`%ProgramData%\Split65LampBridge`。UMDF2 DLL 运行在用户态，系统自带 VHF / WUDFRd 提供底层驱动，不分发自定义 SYS。
- 管理员步骤将内置文件解压到管理员和 SYSTEM 可写的临时安装目录，校验预编译文件后安装；步骤结束清理该安装缓存。
- 用户缓存和日志：`%LOCALAPPDATA%\Split65Companion`。内置运行文件会按嵌入内容核对并恢复；原始 EXE 内附完整源代码 ZIP。
- 创建本机唯一的一年期、仅代码签名用途的证书，加入 LocalMachine 的 Root / TrustedPublisher；签名完成删除私钥，卸载按收据中的精确指纹删除证书。没有分发共享私钥。
- 只绑定 `ROOT\Split65LampBridge`，不替换物理键盘、鼠标或接收器驱动，不添加键盘/鼠标输入集合，不改固件、EEPROM 或系统类筛选器。
- 不修改安全启动、测试签名模式或代码完整性设置。若现有系统策略拒绝该开发构建，显示原始错误并停止。
- 需要重启时明确显示，重启后打开同一 EXE 再启用。修复失败会按本次新建收据尝试回滚，保留错误记录。

本地自签名不等于微软或厂商发行签名。此程序是开发候选，未通过 HLK/WHQL/Razer 认证。

## 开发构建

完整对应源代码在 EXE 内，通过“导出源码”获取。已装 VS 2022 C++ 与 SDK/WDK 26100 的 x64 Developer PowerShell 可运行 Build-Windows.ps1 构建驱动和后台；普通用户无需这些工具。

Linux 的 Build-Client-Cross.py 使用 Microsoft.NETFramework.ReferenceAssemblies.net48 1.0.3，强制 `-noconfig -nostdlib+`，避免引用 Mono 运行库新增的 API。Build-Companion.py 支持 Windows csc 或 Mono mcs；必须先提交与预编译驱动/后台对应的干净源码，再嵌入 `git archive HEAD` 生成最终 EXE。输出在 dist/，不提交输出以避免递归嵌入。

```text
python windows-bridge/Build-Companion.py --refs <微软 net48 参考程序集目录> --csc <Windows csc.exe>
```

跨平台构建用 --mono、--mono-config、--mcs 替代 --csc。底层检查见 tests/，GUI 本身仍需要真实 Windows 验证。原 CMD 和 PS1 在对应源码中作为内部构建/调试入口保留，发行用户无需操作。

## 技术依据及许可

- [Microsoft UMDF filter 安装说明](https://learn.microsoft.com/en-us/windows-hardware/drivers/wdf/installing-a-umdf-filter-driver)：设备专属 UpperFilters / WUDFRd。
- [Microsoft PnPUtil](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax)：添加成功并不保证强制选择该驱动。
- [Microsoft UpdateDriverForPlugAndPlayDevices](https://learn.microsoft.com/en-us/windows/win32/api/newdev/nf-newdev-updatedriverforplugandplaydevicesw)：按精确硬件 ID 更新驱动。
- [Microsoft VHF_CONFIG](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/vhf/ns-vhf-_vhf_config) / [DMF 对照](https://github.com/microsoft/DMF/blob/f9da490cd6e7a216f431ad245255548ab020d4f0/Dmf/Modules.Library/Dmf_VirtualHidDeviceVhf.c)：用户态 FileHandle / 本地 WDFIOTARGET 路径。
- [微软 RP2040 LampArray 示例](https://github.com/microsoft/RP2040MacropadHidSample)：沿用已验证的 68 灯标准报告、按键绑定及几何描述。

新增源码 GPL-2.0-or-later，组合项目及上游许可证完整随源码 ZIP 提供。虚拟 VID:PID 342D:E4C8 为临时实验身份，未声称获得分配。本项目不隶属于 Microsoft、EPOMAKER 或 Razer。
