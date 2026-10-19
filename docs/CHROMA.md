# Chroma 布局与串灯处理

[English](CHROMA.en.md) | 中文

0.4.9.3 Companion 已内置“修复 Chroma 串灯”。从 Chroma Studio 导出当前配置，在 Companion 中选择并保存分离副本，再导入 Chroma 并选中 `[Split65 separated]` 配置。无需 Python 或重刷键盘；原配置不覆盖。支持已观察到的 version 1、未旋转、0/40 像素灯格 JSON 导出。此工具依据下述模型处理完整矩阵，当前 `】` 效果及新版本实际导入仍需验证。

2026-10-18，用户确认导入分离设备布局后，原先的键盘按键与 Cobra Pro 鼠标串灯现象消失。两半均为 v0.4.7，处理时没有改动固件、驱动、灯号或按键绑定。

## 使用方法

1. 完全退出 OpenRGB 后台，确认 Windows「设置 → 个性化 → 动态照明」能看到 Split65。
2. 在 Windows 动态照明的后台控制优先级中将 Chroma 放到前面，保留当前正常工作的鼠标控制方式。
3. 在 Chroma Studio 先复制或导出原配置，用移动工具（M）把鼠标向右移远。若同时出现原生和 Windows 动态照明两个鼠标实例，两个都要移开，彼此也留出距离。
4. 保存布局，重新给此前冲突的键位设置单色并保存，检查鼠标是否跟随。只看设备外框没有交叠不足以排除逻辑灯位冲突。

本次成功导出中，键盘不动，原生鼠标 X 从 5360 移至 6120，WDL 鼠标 X 从 5360 移至 6640，Y 不变。数字仅适用于该导出；其他布局按设备矩阵留出空间。

## 依据与范围

Split65 导出的图形宽度为 660，而灯位矩阵有 28 列。根据鼠标定义中的 40 像素灯格推算，逻辑灯位范围比图形外框宽。

用 `全局位置 = 设备原点 + (列, 行) × 40` 展开灯位，可准确对应观察到的冲突：

| 鼠标路径 | 对应的 Split65 键位 |
| --- | --- |
| 原生 Chroma | P |
| Windows 动态照明 | `-`、`=`、`;`、`/`、右 Shift |

移动鼠标后，模型重叠从 8 对降至 0 对，用户反馈串灯消失。这支持 Chroma 画布布局或缩放导致逻辑灯位重叠的判断；没有 Chroma 内部源码或厂商确认，不能断言其内部实现。当前配置已验证，其他效果、版本、设备及长期运行仍需验证。

## 可选导出工具

正常使用不需要 Python。开发者可执行：

```bash
python3 tools/chroma-mapping/separate_canvas_layout.py input.ChromaEffects separated.ChromaEffects
```

接受已观察到的 version 1、单配置 JSON 导出。工具将鼠标移至键盘逻辑范围右侧，生成新标题、配置 ID 和图层 ID，保留设备身份、矩阵及效果目标。原文件不覆盖，目标文件已存在时停止。导入后选择 `Split65-Separated-Layout`；若原导出未保存效果区域，需重新选择灯位、设置效果并保存。

40 像素是本次导出的推断模型，不能保证适用所有格式或比例。请保留原配置；格式不符时停止，不要删除设备字段来强行导入。公开仓库不含用户原始配置、日志或设备序列号。

## 资料

- [Razer Chroma App 手册](https://dl.razerzone.com/master-guides/RazerChromaApp/RazerChromaApp-en.pdf)：Studio 移动工具与设备布局。
- [Razer 第三方设备控制 FAQ](https://mysupport.razer.com/app/answers/detail/a_id/13698/kw/off)：Windows 动态照明与后台优先级。

官方资料说明操作入口；坐标模型与处理结果来自用户导出及实测，并非官方缺陷说明。
