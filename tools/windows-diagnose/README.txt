Split65 Windows 诊断 v6（支持 0.4.3 / 0.4.4）

优先使用浏览器直接读取：
1. 下载并解压 ZIP，使用桌面 Edge / Chrome 打开 Read-USB-Trace.html。
2. 键盘保持 USB 有线模式，USB 插左半边，两半正常连接；暂时拔掉接收器。退出 OpenRGB、Hub、VIA 和相关网页。
3. 先读取一次记录基线，打开 Windows“设置 → 个性化 → 动态光效”，再回到同一 HTML 页面读取一次；输出会显示新增计数。
4. 点击“选择 Split65 并读取”，在浏览器设备选择框选 Split65。
5. 点击“保存 TXT”，将下载的 Split65-USB-Trace.txt 发回（通常在“下载”文件夹）。

页面仅查询 0x30 固件状态、0x31 的操作 0/1 请求轨迹；不清空轨迹，不读写逐灯 Feature、不设置颜色、不写配置、不联网、不刷机。此页读取已有固件日志，本身不执行 Windows 原生 FromIdAsync。

PowerShell 检测作为另一条读取路径：
- 双击 Run-Diagnosis.cmd，输出 Split65-Windows-Diagnosis.txt 在 CMD 同一目录。
- v6 使用 1 字节 FileStream 缓冲，让 33 字节 HID 请求直接写出，并移除了 FlushAsync；某些 HID 句柄可能拒绝 FlushFileBuffers。
- v6 会记录实际失败阶段、内部异常与 HRESULT；v5 的 AggregateException 没有打印内部信息，因此不能仅凭该条日志认定具体失败阶段。
- 运行原生 LampArray 初始化前后分别读取已有轨迹；默认不遍历 68 灯属性，避免诊断改变灯位共享游标。
- 只对硬件版本 0x0042/0x0043/0x0044、FF60:0061、33 字节报告发送只读 0x30/0x31 操作 0/1 查询。
- 原生 API 30 秒截止仅表示操作未及时完成，不是设备返回的具体错误码。

两条路径都不需要管理员权限或更换固件。工具不修改注册表、不安装驱动、不发送灯光接管、EEPROM、改键或引导程序命令。输出包含设备实例标识符，不会自动上传。CMD 仅为本次 PowerShell 进程允许运行本地脚本，不更改保存的执行策略。

input-test.html 用于单独记录输入卡顿：Edge/Chrome 打开，开始后复现，结束时浏览器下载 TXT。它不记录输入框文字、不联网，不能直接测量物理松键或 USB 包。

软件检查：生产 WebHID JavaScript 的模拟设备收发、只读命令与异常清理；C# 5 编译、无缓冲 HID 读写、内部异常、HID ABI 和 PowerShell 语法。构建环境没有 Windows 实体键盘，尚不能声称两条路径已完成实机验证。

0.4.4 新增 stage 6 表示 WB32 驱动 IN 完成回调已执行（不能据此证明主机 API 或独立状态阶段完成）；stage 1 只表示应答缓冲区已准备。事件详情中的 lamp=65535 表示该事件没有灯号。旧 0.4.3 不具备完成记录，不能据此判断传输失败。
