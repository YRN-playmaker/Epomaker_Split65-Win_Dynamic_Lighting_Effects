Split65 / Chroma 只读映射检查 1.0

2026-10-18：当前用户两半均为 v0.4.7。OpenRGB 后台移除后 Windows
原生识别恢复；Chroma 串灯在移动应用内设备布局后消失。
正常使用方法见仓库 docs/CHROMA.md，本工具仅供仍需收集元数据时使用。

1. 保持键盘与鼠标连接，退出 OpenRGB 后台。
2. 双击 Run-Mapping-Check.cmd，无需管理员或安装软件。
3. 报告在 CMD 同目录：
   LATEST-Mapping-report.txt
   Split65-Chroma-Mapping-日期-编号.zip
   Split65-Chroma-Mapping-日期-编号 文件夹

通常几十秒，总超时约 100 秒。超时仍会打包已有结果。
脚本被策略阻止时保留原始错误，不必修改系统策略。

读取 Windows.Devices.Lights LampArray 的设备类型、灯位、坐标和
1..255 虚拟键映射；核对 Split65 的 68 灯与重点按键。
不写颜色、不设置使能或亮度、不停止进程或服务，不修改注册表、
驱动或固件，也不抓输入内容。报告含设备 ID 和相关 RGB 进程名/PID，
公开反馈前请移除序列号等个人信息。

相同灯号/按键出现在不同设备是允许的。本工具不能直接读取 Chroma
效果路由，不会把重复灯号当作根因。另附 separate_canvas_layout.py
可从特定 JSON 导出生成独立布局，适用格式与范围见 docs/CHROMA.md。

用户 Windows PowerShell 5.1 / WinRT 已成功执行映射检查：Split65
68 灯位置及核对绑定匹配，读取无错误。开发机上的 PowerShell 7
解析和模拟检查不代替真实 Windows、ETW 或硬件验证。

API 文档：
https://learn.microsoft.com/en-us/uwp/api/windows.devices.lights.lamparray
https://learn.microsoft.com/en-us/uwp/api/windows.devices.lights.lampinfo
https://learn.microsoft.com/en-us/uwp/api/windows.devices.lights.lamparray.getindicesforkey
