using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

namespace CodexWallpaper {
    public sealed class Bridge : IDisposable {
        public const string RecoveryFile = "recovery.json";
        Backdrop backdrop;
        Recovery recovery;
        IntPtr source;
        int sourceWidth, sourceHeight;
        bool suspended;
        public bool Active { get { return recovery != null; } }
        public IntPtr TargetHandle { get { return recovery == null ? IntPtr.Zero : new IntPtr(recovery.Handle); } }
        public static void Command(string engine, string args) {
            if (!File.Exists(engine)) throw new FileNotFoundException("请先选择 Wallpaper Engine 程序路径。", engine);
            Process.Start(new ProcessStartInfo(engine, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(engine) }).Dispose();
        }
        public static void CloseWallpaper(Recovery r) {
            if (r == null || String.IsNullOrEmpty(r.WindowName) || !r.WindowName.StartsWith("CodexWallpaper-", StringComparison.Ordinal)) return;
            try { Command(r.Engine, "-control closeWallpaper -location " + Quote(r.WindowName)); } catch (Exception e) { Storage.Log(e.Message); }
        }
        public static string Quote(string s) {
            if (s.IndexOf('"') >= 0 || s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0) throw new ArgumentException("路径包含无效字符。");
            return "\"" + s + "\"";
        }
        public async Task Start(Settings settings, Wallpaper wallpaper, Target target, CancellationToken token = default(CancellationToken)) {
            Stop();
            token.ThrowIfCancellationRequested();
            if (target == null || !Native.IsWindow(target.Handle)) throw new InvalidOperationException("请先打开 Codex，然后刷新窗口列表。");
            if (!File.Exists(wallpaper.Project)) throw new FileNotFoundException("壁纸文件已移动，请重新扫描。");
            bool engineRunning = Process.GetProcessesByName("wallpaper32").Length > 0 || Process.GetProcessesByName("wallpaper64").Length > 0;
            if (!engineRunning) throw new InvalidOperationException("请先启动 Wallpaper Engine，再应用壁纸。");
            int style = Native.GetWindowLong(target.Handle, Native.ExStyle);
            uint key, flags; byte alpha;
            bool attrs = Native.GetLayeredWindowAttributes(target.Handle, out key, out alpha, out flags);
            if ((style & Native.Layered) != 0 && !attrs) throw new InvalidOperationException("此窗口使用了不兼容的逐像素透明模式，已保持原样。");
            var r = new Recovery { Handle = target.Handle.ToInt64(), Pid = (int)Native.Pid(target.Handle), Style = style, Key = key, Flags = flags, Alpha = alpha, HadAttributes = attrs, Engine = settings.Engine, WindowName = "CodexWallpaper-" + Guid.NewGuid().ToString("N") };
            using (var process = Process.GetProcessById(r.Pid)) r.StartTicks = process.StartTime.ToUniversalTime().Ticks;
            Storage.Write(RecoveryFile, r);
            recovery = r;
            try {
                // A separate lightweight process restores opacity if this UI is killed.
                using (var parent = Process.GetCurrentProcess()) {
                    Process.Start(new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath,
                        "--watch " + parent.Id + " " + parent.StartTime.ToUniversalTime().Ticks + " " + r.WindowName) { UseShellExecute = false, CreateNoWindow = true }).Dispose();
                }
                double ratio = Library.Aspect(wallpaper, settings.Ratio);
                sourceWidth = ratio >= 1 ? 1600 : (int)Math.Round(1000 * ratio);
                sourceHeight = (int)Math.Round(sourceWidth / ratio);
                Command(settings.Engine, "-control openWallpaper -file " + Quote(wallpaper.Project) + " -playInWindow " + Quote(r.WindowName) +
                    " -width " + sourceWidth + " -height " + sourceHeight + " -x -16000 -y -16000 -borderless 1 -activate 0");
                for (int i = 0; i < 150; i++) {
                    await Task.Delay(100, token);
                    if (recovery != r) { CloseWallpaper(r); return; }
                    source = Native.FindTitle(r.WindowName);
                    if (source != IntPtr.Zero) break;
                }
                if (source == IntPtr.Zero) throw new InvalidOperationException("15 秒内没有找到壁纸渲染窗口。请检查 Wallpaper Engine 是否正常运行，以及所选壁纸能否播放。");
                Native.SetWindowLong(source, Native.ExStyle, (Native.GetWindowLong(source, Native.ExStyle) | 0x80 | 0x08000000) & ~0x40000);
                Native.SetWindowPos(source, new IntPtr(1), -16000, -16000, sourceWidth, sourceHeight, 0x10 | 0x20 | 0x40);
                Native.Rect client; Native.GetClientRect(source, out client);
                sourceWidth = client.Width; sourceHeight = client.Height;
                backdrop = new Backdrop(); backdrop.Connect(source);
                Native.SetWindowLong(target.Handle, Native.ExStyle, style | Native.Layered);
                ApplyStrength(settings.Strength);
                Tick(settings);
                Storage.Log("Applied wallpaper: " + wallpaper.Title);
            } catch { Stop(); throw; }
        }
        public void ApplyStrength(int strength) {
            if (recovery == null || !recovery.Matches()) return;
            byte alpha = (byte)Math.Round(255 * (1 - Math.Max(5, Math.Min(60, strength)) / 100.0));
            uint flags = recovery.HadAttributes ? recovery.Flags | 2u : 2u;
            if (!Native.SetLayeredWindowAttributes(new IntPtr(recovery.Handle), recovery.Key, alpha, flags))
                throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }
        public void Tick(Settings settings) {
            if (recovery == null || backdrop == null) return;
            if (!recovery.Matches()) { Stop(); return; }
            if (!Native.IsWindow(source)) { Stop(); throw new InvalidOperationException("壁纸渲染窗口已关闭，Codex 外观已恢复。"); }
            IntPtr target = new IntPtr(recovery.Handle);
            int cloaked; Native.DwmGetIntAttribute(target, 14, out cloaked, 4);
            if (Native.IsIconic(target) || !Native.IsWindowVisible(target) || cloaked != 0) {
                backdrop.Hide();
                // Avoid translucency exposing an unrelated desktop when switching virtual desktops.
                if (!suspended) { Native.SetLayeredWindowAttributes(target, 0, 255, 2); suspended = true; }
                return;
            }
            if (suspended) { ApplyStrength(settings.Strength); suspended = false; }
            Native.Rect rect;
            if (Native.DwmGetWindowAttribute(target, 9, out rect, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.Rect))) != 0) Native.GetWindowRect(target, out rect);
            if (rect.Width < 1 || rect.Height < 1) return;
            // Insert directly behind Codex, never make either window topmost or steal focus.
            Native.SetWindowPos(backdrop.Handle, target, rect.Left, rect.Top, rect.Width, rect.Height, 0x10 | 0x40);
            backdrop.LayoutVideo(sourceWidth, sourceHeight, settings.Cover);
        }
        public void Stop() {
            var r = recovery; recovery = null;
            if (r != null) {
                r.Restore(); CloseWallpaper(r);
                var saved = Storage.Read<Recovery>(RecoveryFile);
                if (saved.WindowName == r.WindowName) { try { File.Delete(Path.Combine(Storage.Root, RecoveryFile)); } catch { } }
            }
            if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
            source = IntPtr.Zero; suspended = false;
        }
        public void Dispose() { Stop(); }
        public static void Recover() {
            var r = Storage.Read<Recovery>(RecoveryFile); r.Restore(); CloseWallpaper(r);
            try { File.Delete(Path.Combine(Storage.Root, RecoveryFile)); } catch { }
        }
        public static void Watch(string[] args) {
            try {
                using (var p = Process.GetProcessById(Int32.Parse(args[1]))) {
                    if (p.StartTime.ToUniversalTime().Ticks != Int64.Parse(args[2])) return;
                    while (!p.WaitForExit(500)) {
                        if (Storage.Read<Recovery>(RecoveryFile).WindowName != args[3]) return;
                    }
                }
            } catch (ArgumentException) { }
            if (Storage.Read<Recovery>(RecoveryFile).WindowName == args[3]) Recover();
        }
    }
}
