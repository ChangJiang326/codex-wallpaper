using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using CodexWallpaper;

class RegressionTests {
    static int failures;
    static void Check(bool ok,string label) { Console.WriteLine((ok?"PASS: ":"FAIL: ")+label); if(!ok)failures++; }
    static void Set(object o,string field,object value) { o.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,value); }
    sealed class ObservedBackdrop:Backdrop {
        public int Moves;
        protected override void WndProc(ref Message m) { if(m.Msg==0x46)Moves++; base.WndProc(ref m); }
    }
    [STAThread] static int Main() {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); Application.EnableVisualStyles();
        // Replay the launch race: the shell still has focus when the main window is first seen.
        var gate=new AutoApplyGate(); string id="cold-launch";
        bool foreground=false;
        if(!gate.WaitForForeground(id,0,foreground,true)) { gate.Begin(id); gate.Success(id,0); }
        foreground=true;
        Check(!gate.WaitForForeground(id,1,foreground,true) && gate.FirstAttempt(id),"animation remains eligible after the launcher briefly owns foreground");
        gate.Begin(id);gate.Success(id,1);
        Check(!gate.FirstAttempt(id),"successful handoff never repeats animation on focus changes");
        Check(gate.WaitForForeground("background",0,false,true)&&!gate.WaitForForeground("background",10,false,true),"background client eventually attaches without stealing focus");
        Check(!gate.WaitForForeground("no-animation",0,false,false),"disabled animation does not add foreground wait");
        using(var target=new Form{Width=640,Height=400,Text="Wallpaper regression target"})
        using(var source=new Form{Width=640,Height=400,Text="Wallpaper regression source"})
        using(var backdrop=new ObservedBackdrop())
        using(var bridge=new Bridge())
        using(var process=Process.GetCurrentProcess()) {
            target.Show(); source.Show(); backdrop.Connect(source.Handle);
            Set(bridge,"recovery",new Recovery{Handle=target.Handle.ToInt64(),Pid=process.Id,StartTicks=process.StartTime.ToUniversalTime().Ticks,Style=Native.GetWindowLong(target.Handle,Native.ExStyle)});
            Native.SetWindowLong(target.Handle,Native.ExStyle,Native.GetWindowLong(target.Handle,Native.ExStyle)|Native.Layered);
            Set(bridge,"source",source.Handle); Set(bridge,"sourceWidth",640); Set(bridge,"sourceHeight",400); Set(bridge,"backdrop",backdrop);
            var settings=new Settings(); bridge.Tick(settings); Application.DoEvents(); backdrop.Moves=0;
            var clock=Stopwatch.StartNew();
            for(int i=0;i<100;i++)bridge.Tick(settings);
            Console.WriteLine("100 idle ticks: "+clock.Elapsed.TotalMilliseconds.ToString("F1")+" ms; window position updates: "+backdrop.Moves);
            Check(backdrop.Moves<=1,"stationary client does not continuously reposition its background");
            target.Left+=30; bridge.Tick(settings);
            Check(backdrop.Moves>=1,"moving client still updates its background");
            Native.ShowWindow(target.Handle,6);bridge.Tick(settings);
            Check(!Native.IsWindowVisible(backdrop.Handle),"minimized client hides background after cached layout");
            Native.ShowWindow(target.Handle,9);bridge.Tick(settings);
            Check(Native.IsWindowVisible(backdrop.Handle),"restored client shows background again");
        }
        return failures==0?0:1;
    }
}
