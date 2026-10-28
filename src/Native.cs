using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CodexWallpaper {
    public static class Native {
        public const int ExStyle = -20, Layered = 0x80000;
        public delegate bool EnumProc(IntPtr h, IntPtr param);
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; public int Width { get { return Right - Left; } } public int Height { get { return Bottom - Top; } } }
        [StructLayout(LayoutKind.Sequential)] public struct Size { public int Width, Height; }
        [StructLayout(LayoutKind.Sequential)] public struct ThumbnailProps {
            public uint Flags; public Rect Destination, Source; public byte Opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool Visible;
            [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly;
        }
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder b, int n);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder b, int n);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", SetLastError=true)] public static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll", SetLastError=true)] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(IntPtr h, out uint key, out byte alpha, out uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect rect);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out Rect rect);
        [DllImport("user32.dll", SetLastError=true)] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint ms, out IntPtr result);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Rect r, int size);
        [DllImport("dwmapi.dll", EntryPoint="DwmGetWindowAttribute")] public static extern int DwmGetIntAttribute(IntPtr h, int attr, out int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref ThumbnailProps props);
        public static uint Pid(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
        public static string Title(IntPtr h) { var s = new StringBuilder(1024); GetWindowText(h, s, s.Capacity); return s.ToString(); }
        public static IntPtr FindTitle(string name) {
            IntPtr result = IntPtr.Zero;
            EnumWindows(delegate(IntPtr h, IntPtr p) { if (Title(h) == name && IsEngine(h)) { result = h; return false; } return true; }, IntPtr.Zero);
            return result;
        }
        public static bool IsEngine(IntPtr h) {
            try { using (var p = Process.GetProcessById((int)Pid(h))) { return p.ProcessName == "wallpaper32" || p.ProcessName == "wallpaper64" || p.ProcessName == "webwallpaper32" || p.ProcessName == "webwallpaper64"; } } catch { return false; }
        }
        public static List<Target> Targets() {
            var result = new List<Target>();
            EnumWindows(delegate(IntPtr h, IntPtr _) {
                if (!IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero) return true;
                try { using (var p = Process.GetProcessById((int)Pid(h))) {
                    string name = p.ProcessName.ToLowerInvariant();
                    if (name != "chatgpt" && name != "codex") return true;
                    var cls = new StringBuilder(256); GetClassName(h, cls, 256);
                    if (!cls.ToString().StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal)) return true;
                    result.Add(new Target { Handle = h, Label = Title(h) + "  ·  " + p.Id });
                }} catch { }
                return true;
            }, IntPtr.Zero);
            return result;
        }
        public static Rect ToRect(Rectangle r) { return new Rect { Left=r.Left, Top=r.Top, Right=r.Right, Bottom=r.Bottom }; }
    }
    public class Target { public IntPtr Handle; public string Label; public override string ToString() { return Label; } }
    public class Recovery {
        public long Handle, StartTicks;
        public int Pid, Style;
        public uint Key, Flags;
        public byte Alpha;
        public bool HadAttributes;
        public string Engine, WindowName;
        public bool Matches() {
            IntPtr h = new IntPtr(Handle);
            if (!Native.IsWindow(h) || Native.Pid(h) != Pid) return false;
            try { using (var p = Process.GetProcessById(Pid)) { return p.StartTime.ToUniversalTime().Ticks == StartTicks; } } catch { return false; }
        }
        public void Restore() {
            if (!Matches()) return;
            IntPtr h = new IntPtr(Handle);
            if (HadAttributes) Native.SetLayeredWindowAttributes(h, Key, Alpha, Flags);
            else Native.SetLayeredWindowAttributes(h, 0, 255, 2);
            // Only restore the bit we changed; preserve unrelated concurrent style changes.
            int current = Native.GetWindowLong(h, Native.ExStyle);
            Native.SetWindowLong(h, Native.ExStyle, (current & ~Native.Layered) | (Style & Native.Layered));
        }
    }
    public class Backdrop : Form {
        IntPtr thumb;
        public Backdrop() {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; BackColor = Color.Black;
            StartPosition = FormStartPosition.Manual; Text = "Codex Wallpaper · 背景";
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80 | 0x20; return p; } }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x84) { m.Result = new IntPtr(-1); return; } base.WndProc(ref m); }
        public void Connect(IntPtr source) {
            int hr = Native.DwmRegisterThumbnail(Handle, source, out thumb);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        }
        public void LayoutVideo(int sw, int sh, bool cover) {
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w < 1 || h < 1 || thumb == IntPtr.Zero) return;
            Rectangle dest = Library.Fit(sw, sh, w, h, false);
            Rectangle src = new Rectangle(0, 0, sw, sh);
            if (cover) {
                double ratio = (double)w / h;
                if ((double)sw / sh > ratio) { int cw = (int)Math.Round(sh * ratio); src = new Rectangle((sw-cw)/2, 0, cw, sh); }
                else { int ch = (int)Math.Round(sw / ratio); src = new Rectangle(0, (sh-ch)/2, sw, ch); }
                dest = new Rectangle(0, 0, w, h);
            }
            var props = new Native.ThumbnailProps { Flags = 31, Destination = Native.ToRect(dest), Source = Native.ToRect(src), Opacity = 255, Visible = true, ClientOnly = true };
            int hr = Native.DwmUpdateThumbnailProperties(thumb, ref props);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        }
        protected override void Dispose(bool disposing) { if (thumb != IntPtr.Zero) { Native.DwmUnregisterThumbnail(thumb); thumb = IntPtr.Zero; } base.Dispose(disposing); }
    }
}
