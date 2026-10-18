Split65 0.5.0 Candidate · 中文 / English
https://github.com/YRN-playmaker/Epomaker_Split65-Win_Dynamic_Lighting_Effects

中文
1. 退出旧 Companion、OpenRGB、Hub 与 RGB 测试页。
2. 必须将左侧刷入本包 split65-dynamic-lighting-v0.5.0.bin：输入调度修复在固件中，仅换程序不能生效。当前右侧 0.4.9 可保留，也可以两侧同版；无需刷接收器。
3. 运行 Split65-Companion.exe，点击“启用动态照明”。程序会检查并更新旧版桥接驱动；需要时允许管理员授权。不必单独安装其他插件。
4. 测试无线输入、亮度渐变，以及切到有线后的 Windows/Chroma 控制。仍串灯时使用窗口内“修复 Chroma 串灯”，导出当前 Chroma 配置、生成副本，再在 Chroma 导入并选中它。

本版加入输入优先通道、有线接管时序修复、255 级亮度处理与 100 ms 无线调度目标，保留超时退避。代码与编译/回归验证通过；实际新驱动安装、延迟、渐变及有线控制仍需硬件确认。失败时导出新版日志。程序不会自动烧录任何硬件。
右上角可切换中文 / English；源码、固件、日志和卸载都在同一窗口。

English
1. Close the old Companion, OpenRGB, Hub and RGB test pages.
2. Flash split65-dynamic-lighting-v0.5.0.bin onto the LEFT half. The input scheduler fix requires firmware; replacing only the app is insufficient. Existing right-half 0.4.9 firmware remains compatible, or both halves may use the new BIN. Do not flash the receiver.
3. Run Split65-Companion.exe and click Enable lighting. The same app updates the old bridge driver when required; allow administrator access for setup. No separate plugin installer is needed.
4. Test wireless input, gradients and Windows/Chroma control after switching to wired. For layout coupling, use Fix Chroma coupling, then import/select the generated copy in Chroma Studio.

This build adds priority input, corrected wired takeover ordering, 255-level intensity and a 100 ms wireless scheduling target with retry backoff. Compilation and software regressions passed; actual driver installation, latency, gradients and wired behavior still need hardware testing. Export logs if problems persist. The app never flashes hardware automatically. 中文 / English, source, firmware, logs and uninstall are included.
