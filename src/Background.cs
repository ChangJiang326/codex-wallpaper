using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CodexWallpaper {
    public static class LoginStartup {
        public const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ValueName = "CodexWallpaper";
        public static string Command(string executable) { return Bridge.Quote(Path.GetFullPath(executable)) + " --background"; }
        public static void Set(bool enabled, string executable) {
            using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath)) {
                Configure(key,enabled,executable);
            }
        }
        internal static void Configure(RegistryKey key,bool enabled,string executable) {
                if (enabled) {
                    if (!File.Exists(executable)) throw new FileNotFoundException("自动启动程序路径不存在。", executable);
                    key.SetValue(ValueName, Command(executable), RegistryValueKind.String);
                } else key.DeleteValue(ValueName, false);
        }
    }
    public sealed class InstanceSignals : IDisposable {
        const string Prefix = @"Local\CodexWallpaper-";
        readonly EventWaitHandle show,stop,restore;
        public InstanceSignals() : this(Prefix) { }
        internal InstanceSignals(string prefix) {
            show=new EventWaitHandle(false,EventResetMode.AutoReset,prefix+"show");
            stop=new EventWaitHandle(false,EventResetMode.AutoReset,prefix+"exit");
            restore=new EventWaitHandle(false,EventResetMode.AutoReset,prefix+"restore");
        }
        public bool ShowRequested() { return show.WaitOne(0); }
        public bool ExitRequested() { return stop.WaitOne(0); }
        public bool RestoreRequested() { return restore.WaitOne(0); }
        public static void Send(string action) { Send(action,Prefix); }
        internal static void Send(string action,string prefix) {
            for (int i=0;i<5;i++) {
                try { using (var e=EventWaitHandle.OpenExisting(prefix+action)) e.Set(); return; }
                catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(100); }
            }
        }
        public void Dispose() { show.Dispose(); stop.Dispose(); restore.Dispose(); }
    }
    // Clock supplied by the caller so retries and manual suppression can be tested.
    public sealed class AutoApplyGate {
        sealed class State { public int Failures; public double RetryAt; public bool Suppressed, Attempted; }
        readonly Dictionary<string,State> windows = new Dictionary<string,State>();
        State Get(string identity) { State s; if (!windows.TryGetValue(identity,out s)) windows.Add(identity,s=new State()); return s; }
        public bool CanAttempt(string identity,double now) { var s=Get(identity); return !s.Suppressed && s.Failures<3 && now>=s.RetryAt; }
        public bool FirstAttempt(string identity) { return !Get(identity).Attempted; }
        public void Begin(string identity) { Get(identity).Attempted=true; }
        public void Success(string identity,double now) { var s=Get(identity); s.Failures=0; s.RetryAt=now+3; s.Attempted=true; }
        public void Failure(string identity,double now) { var s=Get(identity); s.Failures++; s.RetryAt=now+10; }
        public void Suppress(string identity) { if (!String.IsNullOrEmpty(identity)) Get(identity).Suppressed=true; }
        public void Reset(string identity) { windows.Remove(identity); }
        public void Clear() { windows.Clear(); }
    }
    public static class BackgroundWatch {
        public static string Identity(Target t) {
            using (var p=Process.GetProcessById((int)Native.Pid(t.Handle)))
                return t.Handle.ToInt64()+":"+p.Id+":"+p.StartTime.ToUniversalTime().Ticks;
        }
        public static Wallpaper SavedWallpaper(Settings settings) {
            if (String.IsNullOrWhiteSpace(settings.LastProject) || !File.Exists(settings.LastProject))
                throw new FileNotFoundException("保存的壁纸不存在，请打开设置重新选择。",settings.LastProject);
            int skipped;
            var wallpapers=Library.Scan(new[]{Path.GetDirectoryName(settings.LastProject)},out skipped);
            foreach(var w in wallpapers) if(String.Equals(Path.GetFullPath(w.Project),Path.GetFullPath(settings.LastProject),StringComparison.OrdinalIgnoreCase)) return w;
            throw new InvalidOperationException("保存的壁纸项目无法读取，请重新选择壁纸。");
        }
        public static bool EngineRunning() {
            bool found=false;
            foreach(string name in new[]{"wallpaper64","wallpaper32"})
                foreach(var p in Process.GetProcessesByName(name)) { found=true;p.Dispose(); }
            return found;
        }
        public static async Task EnsureEngine(Settings settings,CancellationToken token) {
            token.ThrowIfCancellationRequested();
            if(EngineRunning())return;
            if(!File.Exists(settings.Engine))throw new FileNotFoundException("未找到 Wallpaper Engine，请在设置里重新选择程序路径。");
            Process.Start(new ProcessStartInfo(settings.Engine){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(settings.Engine)}).Dispose();
            for(int i=0;i<60;i++) { await Task.Delay(200,token); if(EngineRunning()){await Task.Delay(1000,token);return;} }
            throw new InvalidOperationException("Wallpaper Engine 未能启动，请检查 Steam 或手动打开引擎。");
        }
    }
}
