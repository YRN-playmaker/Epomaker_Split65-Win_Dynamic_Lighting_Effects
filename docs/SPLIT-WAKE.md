# 休眠后右侧按键无响应：原因与修复

更新：2026-10-18。状态：代码已改，**尚未编译与实机验证**。

## 症状

键盘进入休眠（含 2.4G 深睡）后，直接按右侧按键没有任何输入；必须先按一下左侧任意键，右侧才恢复正常。

## 根因

分体链路是 QMK 的 **master 轮询**模型：每次 `transaction_rpc_*` 都由左侧（master）发起，右侧（slave）只负责应答，从不主动发事务。相关代码见 `linker/wireless/lowpower.c`、`epomaker_split65.c` 的 `user_sync_mms_*`，以及 `transport.c`。

进入休眠时：

1. 左侧（master）在 `lpwr_is_allow_presleep_hook()` 里向右侧发 `0xAA`，双方随后都进入 `LPWR_STOP`；
2. 右侧的矩阵 EXTI 仍然有效，按右键会把它唤醒；
3. 但醒来后的右侧**没有任何可发的东西** —— master 还在 STOP，不会有事务请求，于是这次按键谁也收不到；
4. 按左侧时才不同：master 自己醒来，主动发 `0x55/0xCC` 同步，右半随之被唤醒并恢复输入。

所以「先按左边」不是巧合，而是唯一能触发 master 醒来的路径。

## 修复

从机醒来且唤醒源是矩阵时，在共享 UART 上主动发 3 个字节：

```c
if (!is_keyboard_master() && lpwr_get_sleep_wakeupcd() == LPWR_WAKEUP_MATRIX) {
    split65_wake_peer();   // sdPut(&SERIAL_USART_DRIVER, 0x00) × 3
}
```

左侧在进入 STOP 前已经对 `SERIAL_USART_RX_PIN` 启用了双边沿事件（`lpwr_wb32.c` 的 `lpwr_exti_init()`），所以任意一个起始位边沿都会把它唤醒。发的是合法 UART 字节而不是长时间低电平，避免给接收端留下 break/帧错误状态；分体协议按同步字节加校验解析，这 3 个字节没有同步头和校验，会被丢弃。

代码位于 `epomaker_split65.c` 的 `lpwr_wakeup_hook()` 开头。

## 验证步骤

1. 只刷**右侧**（修复在从机侧；左侧不需要动），保留左侧当前版本；
2. 正常使用到自动休眠（或按现有深睡流程），确认键盘已睡着；
3. **不要碰左侧**，直接按右侧任意键，观察是否立即出字；
4. 再测：休眠 → 按左侧（应保持原有行为）→ 右侧输入正常；
5. 回归：正常输入、两侧同时按键、休眠唤醒后的灯效、LampArray（有线）与 2.4G 串流。

## 已知限制与风险

- 唤醒链路多了约 200 ms（`LPWR_WAKEUP_DELAY`）+ 主机自身唤醒时间；首次按键可能感觉偏慢，可后续把发送点前移到 `lpwr_stop_hook_post()`。
- 如果两侧 UART 在休眠时是开漏且没有上拉，`sdPut` 的边沿可能不干净；实机若无效，改为短暂驱动 TX 为推挽输出再恢复复用功能。
- master 若恰好清醒，这 3 个字节可能挤掉一次分体事务；协议靠校验会丢弃，最坏情况丢一次矩阵同步，不影响后续输入。
- 本机没有 ARM 工具链与实机，**以上未经过编译和硬件验证**。回退方式：把右侧刷回 `firmware/split65-dynamic-lighting-v0.4.7.bin` 或 v0.4.8 原镜像。
