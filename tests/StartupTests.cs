using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using CodexWallpaper;

class StartupTests {
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); }
    [STAThread] static int Main(string[] args) {
        if (args.Length == 4 && args[0] == "--watch") { Bridge.Watch(args); return 0; }
        try {
            Check(StartupTiming.Fade(100, false) == 0, "unready client never exposed by fade");
            Check(StartupTiming.Hold(11.3, false) && !StartupTiming.Hold(11.3, true), "waiting node gates on client AND wallpaper readiness");
            Check(StartupTiming.Fade(12.7, true) == 0 && StartupTiming.Fade(13.65, true) == 1, "transition has exact opacity endpoints");
            double previous=0;
            for (int i=0;i<=100;i++) { double value=StartupTiming.Fade(12.7+.95*i/100,true); if(value<previous)throw new Exception("non-monotonic fade"); previous=value; }
            Check(true,"smooth fade is monotonic");
            var serializer=new JavaScriptSerializer();
            Check(!serializer.Deserialize<Settings>("{\"StartupAnimation\":false}").StartupAnimation,"explicit disabled toggle persists");
            Check(serializer.Deserialize<Settings>("{\"Strength\":24}").StartupAnimation,"old settings migrate with startup animation default");
            var cancelled=new CancellationTokenSource(); cancelled.Cancel();
            using(var b=new Bridge()) { bool caught=false; try { b.Start(new Settings(),null,null,cancelled.Token).GetAwaiter().GetResult(); } catch(OperationCanceledException){caught=true;} Check(caught&&!b.Active,"cancel before start has no wallpaper side effects"); }
            if(args.Length>0 && args[0]=="--live") RunLive(args.Length>1?args[1]:"../media/startup.mp4");
            Console.WriteLine("All "+checks+" startup checks passed.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static void RunLive(string path) {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); Application.EnableVisualStyles();
        var target=new Form {Text="Codex Wallpaper · 过渡测试目标",BackColor=Color.FromArgb(23,65,58),ForeColor=Color.White,Width=1000,Height=700,StartPosition=FormStartPosition.CenterScreen};
        target.Controls.Add(new Label {Text="真实可操作窗口\n\n动画结束后显示这里。\n用这个窗口测试原位淡出、跳过和故障恢复。",Dock=DockStyle.Fill,Padding=new Padding(40),Font=new Font("Microsoft YaHei UI",20)});
        Exception failure=null;
        target.Shown+=async delegate {
            try {
                var t=new Target {Handle=target.Handle}; Rectangle before=ClientStartup.Bounds(target.Handle);
                using(var holder=new OverlayHolder(new BootOverlay(Path.GetFullPath(path),t))) {
                    holder.Value.Show(); holder.Value.SetReady(t);
                    await Timeout(holder.Value.Finished,20000);
                    Check(holder.Value.Failure==null,"bundled H.264 video decodes and natural handoff completes");
                }
                Check(ClientStartup.Bounds(target.Handle)==before,"handoff preserves target position and size");
                using(var holder=new OverlayHolder(new BootOverlay(Path.Combine(Path.GetTempPath(),"missing-animation-"+Guid.NewGuid()+".mp4"),t))) {
                    holder.Value.Show(); holder.Value.SetReady(t); await Timeout(holder.Value.Finished,3000);
                    Check(holder.Value.Failure!=null,"missing video falls through without blank-screen hang");
                }
                using(var holder=new OverlayHolder(new BootOverlay(Path.GetFullPath(path),t))) {
                    holder.Value.Show(); holder.Value.Skip(); await Task.Delay(900);
                    Check(!holder.Value.Finished.IsCompleted,"skip waits for client readiness");
                    holder.Value.SetReady(t); await Timeout(holder.Value.Finished,2500);
                    Check(true,"skip fades to ready client");
                }
                using(var holder=new OverlayHolder(new BootOverlay(Path.GetFullPath(path),t))) {
                    holder.Value.Show(); await Task.Delay(100); holder.Value.Abort();
                    await Timeout(holder.Value.Finished,1000); Check(true,"cancel closes media and completes pending await");
                }
            }catch(Exception ex){failure=ex;}finally{target.Close();}
        };
        Application.Run(target); if(failure!=null)throw failure;
    }
    static async Task Timeout(Task task,int ms){if(await Task.WhenAny(task,Task.Delay(ms))!=task)throw new Exception("Transition timeout");await task;}
    sealed class OverlayHolder:IDisposable {public BootOverlay Value;public OverlayHolder(BootOverlay v){Value=v;}public void Dispose(){Value.Abort();}}
}
