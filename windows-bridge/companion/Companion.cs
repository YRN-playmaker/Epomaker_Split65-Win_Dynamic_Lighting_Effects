// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Web.Script.Serialization;
using System.Text;
using System.Collections.Generic;
[assembly:AssemblyVersion("0.5.0")]
[assembly:AssemblyFileVersion("0.5.0")]
[assembly:AssemblyTitle("Split65 Companion")]
namespace Split65Companion {
public static class App {
    public static readonly string UserRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Split65Companion");
    public static readonly string Logs=Path.Combine(UserRoot,"Results");
    public static string Cache;
    public static byte[] Resource(string name) {
        using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
            if(s==null)throw new IOException("Missing embedded resource: "+name);
            using(MemoryStream copy=new MemoryStream()){s.CopyTo(copy);return copy.ToArray();}
        }
    }
    static byte[] Payload() {byte[] bytes=Resource("Payload.zip");if(Core.Hash(bytes)!=PayloadInfo.Hash)throw new IOException("Embedded payload hash mismatch.");return bytes;}
    static void SecureDirectory(string path) {
        Core.PlainAncestors(path);
        SecurityIdentifier admins=new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),system=new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null);
        if(Directory.Exists(path)) {
            IdentityReference owner=Directory.GetAccessControl(path).GetOwner(typeof(SecurityIdentifier));
            if(!owner.Equals(admins) && !owner.Equals(system))throw new IOException("Unexpected owner of setup directory.");
        }
        DirectorySecurity acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);acl.SetOwner(admins);
        foreach(SecurityIdentifier id in new SecurityIdentifier[]{admins,system})acl.AddAccessRule(new FileSystemAccessRule(id,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        Directory.CreateDirectory(path,acl);Directory.SetAccessControl(path,acl);
    }
    static int Admin(string action,string result) {
        if(action!="repair" && action!="uninstall")throw new IOException("Unknown setup action.");
        using(WindowsIdentity identity=WindowsIdentity.GetCurrent())
            if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new IOException("Setup requires administrator elevation.");
        string results=Path.GetDirectoryName(Path.GetFullPath(result));
        if(!results.EndsWith(Path.Combine("Split65Companion","Results"),StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(result).StartsWith("operation-",StringComparison.Ordinal) || Path.GetExtension(result)!=".json")throw new IOException("Unexpected setup result path.");
        Core.PlainAncestors(results);Directory.CreateDirectory(results);
        string protectedRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Split65CompanionSetup");
        SecureDirectory(protectedRoot);
        string stage=Path.Combine(protectedRoot,Core.Version);SecureDirectory(stage);Core.Extract(Payload(),stage);
        string script=Path.Combine(stage,action=="repair"?"Install.ps1":"Uninstall.ps1");
        string arguments="-NoProfile -ExecutionPolicy Bypass -File "+Core.Quote(script)+" -Elevated -Managed -ResultsDirectory "+Core.Quote(results)+" -ResultFile "+Core.Quote(result)+(action=="repair"?" -Repair":"");
        ProcessResult setup=Core.Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe"),arguments,300000);
        File.WriteAllText(Path.Combine(results,"Setup-output-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt"),setup.Text,Encoding.UTF8);
        if(!File.Exists(result))File.WriteAllText(result,new JavaScriptSerializer().Serialize(new {success=false,rebootRequired=false,error=setup.Text,version=Core.Version}),Encoding.UTF8);
        // The protected setup cache has no active driver binaries; the installed copy is elsewhere.
        try {Directory.Delete(stage,true);if(Directory.GetFileSystemEntries(protectedRoot).Length==0)Directory.Delete(protectedRoot);}catch{}
        return setup.Code;
    }
    [STAThread] public static int Main(string[] args) {
        try {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT || !Environment.Is64BitProcess)throw new IOException("Use Windows x64.");
            if(args.Length==3 && args[0]=="--admin-setup") {
                try {
                    bool setupCreated;using(Mutex setupLock=new Mutex(true,"Global\\Split65CompanionSetup",out setupCreated)) {
                        if(!setupCreated)throw new IOException("Another project setup operation is running.");
                        int code=Admin(args[1],args[2]);setupLock.ReleaseMutex();return code;
                    }
                }catch(Exception error){
                    string directory=Path.GetDirectoryName(Path.GetFullPath(args[2]));
                    if(directory.EndsWith(Path.Combine("Split65Companion","Results"),StringComparison.OrdinalIgnoreCase) && Path.GetFileName(args[2]).StartsWith("operation-",StringComparison.Ordinal) && Path.GetExtension(args[2])==".json") {
                        Core.PlainAncestors(directory);Directory.CreateDirectory(directory);
                        File.WriteAllText(args[2],new JavaScriptSerializer().Serialize(new {success=false,rebootRequired=false,error=error.ToString(),version=Core.Version}),Encoding.UTF8);
                    }
                    return 1;
                }
            }
            if(args.Length!=0)throw new IOException("Unknown option.");
            bool created;using(Mutex singleton=new Mutex(true,"Local\\Split65CompanionUI",out created)) {
                if(!created){MessageBox.Show(I18n.Text("Split65 Companion 已在运行，请查看任务栏。"),"Split65");return 0;}
                Core.PlainAncestors(UserRoot);Directory.CreateDirectory(Logs);
                Cache=Path.Combine(UserRoot,"Cache",Core.Version);Core.Extract(Payload(),Cache);
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainForm());singleton.ReleaseMutex();
            }
            return 0;
        }catch(Exception e) {
            try{Directory.CreateDirectory(Logs);File.AppendAllText(Path.Combine(Logs,"Launcher-error.txt"),DateTime.Now.ToString("o")+" "+e+Environment.NewLine);}catch{}
            MessageBox.Show(e.Message,"Split65 Companion",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;
        }
    }
}
public sealed class MainForm:Form {
    Label status=new Label(),hint=new Label();RichTextBox log=new RichTextBox();Button enable=new Button(),stop=new Button(),repair=new Button(),remove=new Button(),export=new Button(),source=new Button(),firmware=new Button(),chroma=new Button();
    ComboBox language=new ComboBox();
    bool busy,closing;Process worker;readonly object logGate=new object();readonly string sessionLog;
    string Client {get{return Path.Combine(App.Cache,"prebuilt","Split65Bridge.exe");}}
    public MainForm() {
        Text=I18n.Text("Split65 Companion 0.5.0 候选");ClientSize=new Size(760,620);MinimumSize=new Size(780,660);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(246,247,249);Font=new Font("Microsoft YaHei UI",10);
        Label title=new Label{Text=I18n.Text("Split65 无线动态照明"),Font=new Font("Microsoft YaHei UI",19,FontStyle.Bold),AutoSize=true,Location=new Point(24,20)};Controls.Add(title);
        LinkLabel repository=new LinkLabel{Text=Core.RepositoryUrl,AutoSize=false,Font=new Font("Segoe UI",8)};repository.SetBounds(26,52,700,18);
        repository.LinkClicked+=delegate{try{Process.Start(new ProcessStartInfo(Core.RepositoryUrl){UseShellExecute=true});}catch(Exception error){MessageBox.Show(error.Message);}};Controls.Add(repository);
        language.DropDownStyle=ComboBoxStyle.DropDownList;language.Items.AddRange(new object[]{"中文","English"});language.SetBounds(610,24,125,28);language.SelectedIndex=I18n.English?1:0;
        language.SelectedIndexChanged+=delegate{I18n.English=language.SelectedIndex==1;ApplyLanguage(this);};Controls.Add(language);
        status.SetBounds(26,70,700,28);status.Text=I18n.Text("正在检查桥接组件…");status.Font=new Font(Font,FontStyle.Bold);Controls.Add(status);
        hint.SetBounds(26,103,700,48);hint.Text=I18n.Text("先将左侧升级到 0.5.0 固件，再切到 2.4G。关闭测试网页、Hub 和 OpenRGB。\n首次启用需管理员权限安装驱动和本机签名证书；使用时保持本程序运行。");Controls.Add(hint);
        Button[] buttons={enable,stop,repair,remove,export,source,firmware};string[] labels={I18n.Text("启用动态照明"),I18n.Text("停止"),I18n.Text("修复组件"),I18n.Text("卸载组件"),I18n.Text("导出日志"),I18n.Text("导出源码"),I18n.Text("导出固件")};int[] widths={128,64,92,92,92,92,92};int x=24;
        for(int i=0;i<buttons.Length;i++){buttons[i].Text=labels[i];buttons[i].SetBounds(x,164,widths[i],38);buttons[i].FlatStyle=FlatStyle.Flat;buttons[i].BackColor=Color.White;Controls.Add(buttons[i]);x+=widths[i]+7;}
        enable.BackColor=Color.FromArgb(35,94,200);enable.ForeColor=Color.White;
        chroma.Text=I18n.Text("修复 Chroma 串灯");chroma.SetBounds(24,214,190,38);chroma.FlatStyle=FlatStyle.Flat;chroma.BackColor=Color.White;Controls.Add(chroma);
        Label chromaHint=new Label{Text=I18n.Text("选择 Chroma 导出的配置，修复后导入新副本。"),AutoSize=false};chromaHint.SetBounds(225,223,510,30);Controls.Add(chromaHint);
        log.SetBounds(24,270,712,322);log.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top|AnchorStyles.Bottom;log.ReadOnly=true;log.Font=new Font("Consolas",9);log.BackColor=Color.White;log.BorderStyle=BorderStyle.None;Controls.Add(log);
        sessionLog=Path.Combine(App.Logs,"Companion-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt");
        enable.Click+=async delegate{await Enable(false);};repair.Click+=async delegate{await Enable(true);};stop.Click+=async delegate{SetBusy(true);try{await Stop();SetStatus(I18n.Text("已停止，键盘恢复内置灯效。"));}catch(Exception e){SetStatus(I18n.Text("停止失败：")+e.Message);}finally{SetBusy(false);}};
        remove.Click+=async delegate{await Uninstall();};export.Click+=delegate{ExportLogs();};source.Click+=delegate{ExportSource();};firmware.Click+=delegate{ExportFirmware();};chroma.Click+=delegate{RepairChroma();};
        Shown+=async delegate {await InitialCheck();};FormClosing+=OnClose;
    }
    void ApplyLanguage(Control parent) {
        if(!(parent is RichTextBox) && !(parent is ComboBox))parent.Text=I18n.Convert(parent.Text);
        foreach(Control child in parent.Controls)ApplyLanguage(child);
    }
    void Write(string text) {
        lock(logGate)File.AppendAllText(sessionLog,DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine,Encoding.UTF8);
        if(IsDisposed || closing)return;
        if(InvokeRequired){BeginInvoke((Action)(()=>Append(text)));}else Append(text);
    }
    void Append(string text){if(!IsDisposed) {log.AppendText(text+Environment.NewLine);log.ScrollToCaret();}}
    void SetStatus(string text){status.Text=text;Write(text);}
    void SetBusy(bool value){busy=value;enable.Enabled=stop.Enabled=repair.Enabled=remove.Enabled=chroma.Enabled=!value;}
    async Task<ProcessResult> Probe(){ProcessResult result=await Task.Run(()=>Core.Run(Client,"--probe-driver",12000));Write(result.Text.Trim());return result;}
    async Task InitialCheck(){SetBusy(true);try{ProcessResult result=await Probe();SetStatus(result.Ready?I18n.Text("组件已就绪，点击启用即可。"):I18n.Text("桥接接口尚未就绪，点击启用将自动安装或修复。"));}catch(Exception e){SetStatus(I18n.Text("组件检查失败：")+e.Message);}finally{SetBusy(false);}}
    async Task<bool> Setup(string action) {
        string resultPath=Path.Combine(App.Logs,"operation-"+Guid.NewGuid().ToString("N")+".json");
        SetStatus(action=="repair"?I18n.Text("正在申请管理员权限并安装/修复组件…"):I18n.Text("正在申请管理员权限并卸载组件…"));
        int exit=await Task.Run(()=> {
            using(Process p=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--admin-setup "+action+" "+Core.Quote(resultPath)){UseShellExecute=true,Verb="runas"})) {
                if(p==null)throw new IOException("Could not start administrator setup.");p.WaitForExit();return p.ExitCode;
            }
        });
        if(!File.Exists(resultPath)){Write(I18n.Text("管理员步骤未生成结果，exit=")+exit);return false;}
        Dictionary<string,object> result=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(resultPath,Encoding.UTF8));
        if(result.ContainsKey("log"))Write(I18n.Text("安装记录：")+result["log"]);
        if(result.ContainsKey("rebootRequired") && (bool)result["rebootRequired"]){SetStatus(I18n.Text("Windows 要求重启。重启后打开本程序再次启用。"));return false;}
        if(!result.ContainsKey("success") || !(bool)result["success"]){SetStatus(I18n.Text("组件操作失败；详细原因已记录，可直接导出日志。"));if(result.ContainsKey("error"))Write(Convert.ToString(result["error"]));return false;}
        return exit==0;
    }
    async Task Enable(bool forceRepair) {
        if(busy)return;SetBusy(true);
        try {
            await Stop();ProcessResult probe=await Probe();
            if(forceRepair || !probe.Ready){if(!await Setup("repair"))return;probe=await Probe();}
            if(!probe.Ready){SetStatus(I18n.Text("驱动接口仍未通过检查；已保留记录，请导出日志。"));return;}
            StartWorker();
        }catch(Exception e){SetStatus(I18n.Text("启用失败：")+e.Message);}finally{SetBusy(false);}
    }
    void StartWorker() {
        worker=new Process();worker.StartInfo=new ProcessStartInfo(Client){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        worker.EnableRaisingEvents=true;worker.OutputDataReceived+=OnOutput;worker.ErrorDataReceived+=OnOutput;
        worker.Exited+=delegate{if(!closing && !IsDisposed)BeginInvoke((Action)(()=>{status.Text=I18n.Text("桥接已停止；详情见下方记录。");}));};
        worker.Start();worker.BeginOutputReadLine();worker.BeginErrorReadLine();SetStatus(I18n.Text("正在连接 2.4G 接收器…"));
    }
    void OnOutput(object sender,DataReceivedEventArgs e) {
        if(String.IsNullOrEmpty(e.Data))return;Write(e.Data);
        string text=null;if(e.Data.Contains("Virtual LampArray created"))text=I18n.Text("接收器已连接。打开 Windows 个性化 → 动态照明。");
        else if(e.Data.Contains("Windows lighting acquired"))text=I18n.Text("Windows 已接管灯光，正在通过 2.4G 发送颜色。");
        else if(e.Data.Contains("Firmware update required"))text=I18n.Text("需先将左侧升级到 0.5.0 固件，再启用无线动态照明。");
        else if(e.Data.Contains("No acknowledged"))text=I18n.Text("等待 0.5.0 接收器：请确认已切到 2.4G 并正常连接。");
        else if(e.Data.Contains("Wired LampArray active"))text=I18n.Text("有线动态照明已连接；无线虚拟设备待机。");
        else if(e.Data.Contains("Transient radio timeout"))text=I18n.Text("无线暂时未应答，保留虚拟设备并重试。");
        if(text!=null && !closing && !IsDisposed)BeginInvoke((Action)(()=>{status.Text=text;}));
    }
    async Task Stop() {
        await Task.Run(()=>Core.Run(Client,"--stop",5000));
        if(worker!=null){Process current=worker;await Task.Run(()=>{if(!current.HasExited && !current.WaitForExit(12000))throw new IOException(I18n.Text("桥接仍在退出，请稍后重试。"));});current.Dispose();worker=null;}
    }
    async Task Uninstall(){if(busy)return;SetBusy(true);try{await Stop();if(await Setup("uninstall"))SetStatus(I18n.Text("组件和本机签名证书已卸载。"));}catch(Exception e){SetStatus(I18n.Text("卸载失败：")+e.Message);}finally{SetBusy(false);}}
    void ExportLogs() {
        using(SaveFileDialog dialog=new SaveFileDialog{Filter=I18n.Text("ZIP 日志|*.zip"),FileName="Split65-Logs-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip"}) {
            if(dialog.ShowDialog()!=DialogResult.OK)return;
            try {using(FileStream stream=new FileStream(dialog.FileName,FileMode.Create))using(ZipArchive zip=new ZipArchive(stream,ZipArchiveMode.Create)) {
                foreach(string path in Directory.GetFiles(App.Logs)) {if(String.Equals(Path.GetFullPath(path),Path.GetFullPath(dialog.FileName),StringComparison.OrdinalIgnoreCase))continue;ZipArchiveEntry entry=zip.CreateEntry(Path.GetFileName(path),CompressionLevel.Optimal);using(Stream target=entry.Open())using(FileStream input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))input.CopyTo(target);}
            }Write(I18n.Text("日志已导出：")+dialog.FileName);}catch(Exception e){MessageBox.Show(e.Message);}
        }
    }
    void RepairChroma() {
        using(OpenFileDialog input=new OpenFileDialog{Filter=I18n.Text("Chroma 配置|*.ChromaEffects"),Title=I18n.Text("选择从 Chroma Studio 导出的当前配置")}) {
            if(input.ShowDialog()!=DialogResult.OK)return;
            using(SaveFileDialog output=new SaveFileDialog{Filter=I18n.Text("Chroma 配置|*.ChromaEffects"),FileName=Path.GetFileNameWithoutExtension(input.FileName)+"-Split65-separated.ChromaEffects",OverwritePrompt=true}) {
                if(output.ShowDialog()!=DialogResult.OK)return;
                try {
                    LayoutFixResult result=ChromaLayout.RepairFile(input.FileName,output.FileName);
                    Write("Chroma layout copy: profiles="+result.Profiles+", mice="+result.Mice+"; "+output.FileName);
                    MessageBox.Show(I18n.Text("已生成分离布局副本。请在 Chroma Studio 导入并选中它，再测试冲突按键。原配置保留。"),"Split65 Companion",MessageBoxButtons.OK,MessageBoxIcon.Information);
                }catch(Exception e){Write(e.ToString());MessageBox.Show(I18n.Text("Chroma 配置修复失败：")+e.Message,"Split65 Companion",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            }
        }
    }
    void ExportSource(){using(SaveFileDialog dialog=new SaveFileDialog{Filter=I18n.Text("ZIP 源码|*.zip"),FileName="Split65-Source-0.5.0.zip"}){if(dialog.ShowDialog()!=DialogResult.OK)return;try{File.WriteAllBytes(dialog.FileName,App.Resource("Source.zip"));Write(I18n.Text("完整源码已导出。"));}catch(Exception e){MessageBox.Show(e.Message);}}}
    void ExportFirmware(){using(SaveFileDialog dialog=new SaveFileDialog{Filter=I18n.Text("Split65 固件|*.bin"),FileName="split65-dynamic-lighting-v0.5.0.bin"}){if(dialog.ShowDialog()!=DialogResult.OK)return;try{
        using(MemoryStream memory=new MemoryStream(App.Resource("Payload.zip")))using(ZipArchive zip=new ZipArchive(memory,ZipArchiveMode.Read)) {
            ZipArchiveEntry entry=zip.GetEntry("firmware/split65-dynamic-lighting-v0.5.0.bin");
            if(entry==null)throw new IOException("Embedded firmware missing.");
            using(Stream input=entry.Open())using(FileStream output=new FileStream(dialog.FileName,FileMode.Create))input.CopyTo(output);
        }
        Write(I18n.Text("0.5.0 固件已导出。程序不会自动刷写键盘或接收器。"));
    }catch(Exception e){MessageBox.Show(e.Message);}}}
    async void OnClose(object sender,FormClosingEventArgs e) {
        if(closing)return;e.Cancel=true;if(busy){status.Text=I18n.Text("正在操作组件，请等待完成后关闭。");return;}
        SetBusy(true);try{await Stop();closing=true;Close();}catch(Exception error){SetStatus(I18n.Text("停止失败：")+error.Message);}finally{if(!closing)SetBusy(false);}
    }
}
}
