// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
namespace Split65Companion {
public static class I18n {
    public static volatile bool English = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "zh";
    static readonly Dictionary<string,string> Entries = new Dictionary<string,string> {
        {"Split65 Companion 已在运行，请查看任务栏。","Split65 Companion is already running. Check the taskbar."},
        {"Split65 Companion 0.5.0 候选","Split65 Companion 0.5.0 Candidate"},
        {"Split65 无线动态照明","Split65 Wireless Lighting"},
        {"正在检查桥接组件…","Checking the bridge component…"},
        {"先将左侧升级到 0.5.0 固件，再切到 2.4G。关闭测试网页、Hub 和 OpenRGB。\n首次启用需管理员权限安装驱动和本机签名证书；使用时保持本程序运行。","Flash firmware 0.5.0 on the left half, then switch to 2.4G. Close RGB test pages, Hub and OpenRGB.\nFirst setup needs admin access and a local signing certificate. Keep this app running."},
        {"启用动态照明","Enable lighting"},
        {"停止","Stop"},
        {"修复组件","Repair"},
        {"卸载组件","Uninstall"},
        {"导出日志","Logs ZIP"},
        {"导出源码","Source ZIP"},
        {"导出固件","Firmware"},
        {"已停止，键盘恢复内置灯效。","Stopped. The keyboard has returned to its built-in lighting."},
        {"停止失败：","Could not stop: "},
        {"组件已就绪，点击启用即可。","Component ready. Click Enable lighting."},
        {"桥接接口尚未就绪，点击启用将自动安装或修复。","Bridge not ready. Click Enable lighting to install or repair it."},
        {"组件检查失败：","Component check failed: "},
        {"正在申请管理员权限并安装/修复组件…","Requesting administrator access to install/repair the component…"},
        {"正在申请管理员权限并卸载组件…","Requesting administrator access to uninstall the component…"},
        {"管理员步骤未生成结果，exit=","Administrator setup returned no result; exit="},
        {"安装记录：","Setup log: "},
        {"Windows 要求重启。重启后打开本程序再次启用。","Windows requires a restart. Reopen this app and enable lighting afterwards."},
        {"组件操作失败；详细原因已记录，可直接导出日志。","Component operation failed. Details were recorded; export the logs."},
        {"驱动接口仍未通过检查；已保留记录，请导出日志。","Driver interface check still failed. Please export the recorded logs."},
        {"启用失败：","Could not enable lighting: "},
        {"桥接已停止；详情见下方记录。","Bridge stopped. See the log below for details."},
        {"正在连接 2.4G 接收器…","Connecting to the 2.4G receiver…"},
        {"接收器已连接。打开 Windows 个性化 → 动态照明。","Receiver connected. Open Windows Settings → Personalization → Dynamic Lighting."},
        {"Windows 已接管灯光，正在通过 2.4G 发送颜色。","Windows has acquired lighting control. Sending colors over 2.4G."},
        {"等待 0.5.0 接收器：请确认已切到 2.4G 并正常连接。","Waiting for a 0.5.0 keyboard. Check its 2.4G connection."},
        {"桥接仍在退出，请稍后重试。","The bridge is still shutting down. Try again shortly."},
        {"组件和本机签名证书已卸载。","The component and its local signing certificate were uninstalled."},
        {"卸载失败：","Uninstall failed: "},
        {"ZIP 日志|*.zip","Log ZIP|*.zip"},
        {"日志已导出：","Logs exported: "},
        {"ZIP 源码|*.zip","Source ZIP|*.zip"},
        {"完整源码已导出。","Complete source exported."},
        {"Split65 固件|*.bin","Split65 firmware|*.bin"},
        {"0.5.0 固件已导出。程序不会自动刷写键盘或接收器。","0.5.0 firmware exported. This app does not flash the keyboard or receiver."},
        {"正在操作组件，请等待完成后关闭。","A component operation is in progress. Wait for it to finish before closing."}
        ,{"需先将左侧升级到 0.5.0 固件，再启用无线动态照明。","Flash firmware 0.5.0 on the left half before enabling wireless lighting."}
        ,{"修复 Chroma 串灯","Fix Chroma coupling"}
        ,{"选择 Chroma 导出的配置，修复后导入新副本。","Export from Chroma, repair, then import the new copy."}
        ,{"Chroma 配置|*.ChromaEffects","Chroma profile|*.ChromaEffects"}
        ,{"选择从 Chroma Studio 导出的当前配置","Select the current export from Chroma Studio"}
        ,{"已生成分离布局副本。请在 Chroma Studio 导入并选中它，再测试冲突按键。原配置保留。","A separated layout copy was created. Import and select it in Chroma Studio, then test the affected keys. The original is preserved."}
        ,{"Chroma 配置修复失败：","Chroma layout repair failed: "}
        ,{"有线动态照明已连接；无线虚拟设备待机。","Wired Dynamic Lighting connected; wireless virtual lighting is idle."}
        ,{"无线暂时未应答，保留虚拟设备并重试。","Temporary radio timeout; retaining the virtual device while retrying."}
    };
    public static string Text(string chinese) {
        string english;
        return English && Entries.TryGetValue(chinese,out english) ? english : chinese;
    }
    public static string Convert(string value) {
        // Match the longest complete text/prefix so error details survive a live switch.
        string best=null,replacement=null;
        foreach(KeyValuePair<string,string> entry in Entries) {
            string from=English?entry.Key:entry.Value;
            if(value.StartsWith(from,StringComparison.Ordinal) && (best==null || from.Length>best.Length)) {
                best=from;replacement=English?entry.Value:entry.Key;
            }
        }
        return best==null?value:replacement+value.Substring(best.Length);
    }
}
}
