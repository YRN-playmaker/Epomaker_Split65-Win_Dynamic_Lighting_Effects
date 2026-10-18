# 构建与检查

已验证环境：Linux、QMK CLI 1.2.0、ARM GCC 13.2.1 20231009。需要 Git、Python 3、GNU Make、ARM bare-metal 工具链及 newlib；协议测试还需要本机 GCC。可在配置了这些工具的 WSL/Linux 环境构建。Windows 检查 CMD 不需要构建环境。

在专用 Python 虚拟环境可安装固定 CLI：

```sh
python3 -m venv .venv
. .venv/bin/activate
python3 -m pip install qmk==1.2.0
bash tools/build.sh /absolute/path/to/a/dedicated/qmk-build
```

工具链需要加入 PATH。脚本固定下载 SRGBmods/EpomakerQMK c64b964e 及四个子模块，应用本项目的 QMK/WB32 补丁，复制键盘源码，再构建 default / inputcheck。固定版本、依赖与已交付镜像校验见 build-manifest.json。不同编译器可能生成不同字节，未声称跨工具链可得到相同 SHA256。

不要把个人已有修改的 QMK checkout 当成构建目录。脚本会更新专用目录的键盘源码；只识别本项目旧补丁。构建只产出文件，不会连接设备或自动刷机。

```sh
bash tests/run.sh /absolute/path/to/qmk-build
node tests/check_wireless_rgb.cjs
python3 tests/check_linked_usb.py /absolute/path/to/qmk-build
```

可选的诊断工具本地检查使用 PowerShell 7（仅测试环境）：

```sh
pwsh -NoProfile -File tests/check_chroma_mapping.ps1
pwsh -NoProfile -File tests/check_deep_diagnostics.ps1
```

面向用户的 CMD 则显式调用 Windows PowerShell 5.1。Linux 上的脚本语法/模拟测试不能替代真实 Windows WinRT、ETW 或硬件验证。用户已在当前 Chroma 配置中确认分离布局消除串灯；本地软件检查不能扩展该实测范围。
