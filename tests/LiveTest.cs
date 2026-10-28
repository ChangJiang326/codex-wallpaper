using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodexWallpaper;

class LiveTest {
    [STAThread] static void Main(string[] args) {
        if (args.Length == 4 && args[0] == "--watch") { Bridge.Watch(args); return; }
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        Application.EnableVisualStyles();
        var form = new Form { Text = "Codex Wallpaper · 实机合成测试", BackColor = Color.FromArgb(25,25,25), ForeColor = Color.White, Width = 980, Height = 680, StartPosition = FormStartPosition.CenterScreen };
        var label = new Label { Text = "Windows / Wallpaper Engine 实机测试\n\n这是一扇测试窗口。\n检查背景中的真实动画、留边和文字可读性。\n\n自动测试：横屏 → 竖屏 → 最小化 → 恢复 → 退出。", Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 18), Padding = new Padding(40), BackColor = Color.Transparent };
        form.Controls.Add(label);
        var bridge = new Bridge();
        var timer = new Timer { Interval = 80 };
        var settings = new Settings { Engine = Library.DetectEngine(), Strength = 45, Ratio = 1 };
        timer.Tick += delegate { try { bridge.Tick(settings); } catch (Exception ex) { File.AppendAllText("live-results.txt", ex.ToString()); timer.Stop(); } };
        form.FormClosed += delegate { timer.Stop(); bridge.Dispose(); };
        form.Shown += async delegate {
            try {
                int skipped; var list = Library.Scan(Library.Roots(settings), out skipped);
                var wallpaper = args.Length > 0 && args[0] == "--video" ? list.First(x => x.Type == "video") : list.First(x => x.Title == "Alone");
                int original = Native.GetWindowLong(form.Handle, Native.ExStyle);
                await bridge.Start(settings, wallpaper, new Target { Handle = form.Handle }); timer.Start();
                File.WriteAllText("live-results.txt", "APPLIED " + wallpaper.Title + "\n");
                await Task.Delay(60000);
                form.Size = new Size(650,850); await Task.Delay(1200);
                form.WindowState = FormWindowState.Minimized; await Task.Delay(1200);
                form.WindowState = FormWindowState.Normal; await Task.Delay(1200);
                uint key, flags; byte alpha;
                if (!Native.GetLayeredWindowAttributes(form.Handle, out key, out alpha, out flags) || alpha != 140) throw new Exception("Opacity not restored after minimize");
                bridge.Stop();
                if (Native.GetWindowLong(form.Handle, Native.ExStyle) != original) throw new Exception("Style not restored");
                File.AppendAllText("live-results.txt", "PASS: source opened, resized, minimized, restored; opacity reset\n");
            } catch (Exception ex) { File.AppendAllText("live-results.txt", "FAIL: " + ex + "\n"); }
            finally { form.Close(); }
        };
        Application.Run(form);
    }
}
