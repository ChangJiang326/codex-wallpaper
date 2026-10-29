using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CodexWallpaper;

class Tests {
    static int checks;
    static void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
    [STAThread] static int Main(string[] args) {
        string root = Path.Combine(Path.GetTempPath(), "CodexWallpaperTest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            var rect = Library.Fit(1920, 1080, 1000, 1000, false);
            Check(rect.Width == 1000 && rect.Height == 563 && rect.Y == 218, "16:9 in square keeps full frame");
            rect = Library.Fit(1080, 1920, 1200, 700, false);
            Check(rect.Height == 700 && rect.Width == 394 && rect.X == 403, "portrait in landscape keeps full frame");
            rect = Library.Fit(1920, 1080, 1000, 1000, true);
            Check(rect.Width == 1778 && rect.Height == 1000 && rect.X == -389, "cover preserves aspect and crops");
            string project = Path.Combine(root, "中文 空格"); Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, "project.json"), "{\"title\":\"山川 日落\",\"type\":\"scene\",\"preview\":\"preview.jpg\"}");
            using (var b = new Bitmap(320, 180)) b.Save(Path.Combine(project, "preview.jpg"), System.Drawing.Imaging.ImageFormat.Jpeg);
            string broken = Path.Combine(root, "broken"); Directory.CreateDirectory(broken); File.WriteAllText(Path.Combine(broken, "project.json"), "{");
            string app = Path.Combine(root, "app"); Directory.CreateDirectory(app); File.WriteAllText(Path.Combine(app, "project.json"), "{\"type\":\"application\"}");
            int skipped; var result = Library.Scan(new[] { root, project }, out skipped);
            Check(result.Count == 1 && skipped == 2 && result[0].Title == "山川 日落", "unicode, deduplication, damaged and executable projects");
            Check(Math.Abs(Library.Aspect(result[0], 0) - 16.0/9) < 0.001, "preview aspect is detected");
            Check(Math.Abs(Library.Aspect(result[0], 5) - 9.0/16) < 0.001, "manual portrait aspect overrides preview");
            File.WriteAllText(Path.Combine(root, "private.txt"), "outside");
            Check(Library.SafeAsset(project, "../private.txt") == "", "preview cannot escape project folder");
            Check(Library.SafeAsset(project, Path.Combine(root, "private.txt")) == "", "absolute asset paths rejected");
            Check(Bridge.Quote("C:\\中文 空格\\project.json") == "\"C:\\中文 空格\\project.json\"", "Windows command quoting preserves unicode and spaces");
            bool rejected = false; try { Bridge.Quote("bad\" -control stop"); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "command argument delimiter rejected");
            Application.EnableVisualStyles();
            using (var form = new Form()) using (var source = new Form()) {
                form.Text = "CodexWallpaper Test Target"; source.Text = "CodexWallpaper Test Source";
                source.BackColor = Color.Teal; form.Show(); source.Show(); Application.DoEvents();
                int style = Native.GetWindowLong(form.Handle, Native.ExStyle);
                var recovery = new Recovery { Handle = form.Handle.ToInt64(), Pid = Process.GetCurrentProcess().Id, StartTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, Style = style };
                Native.SetWindowLong(form.Handle, Native.ExStyle, style | Native.Layered);
                Check(Native.SetLayeredWindowAttributes(form.Handle, 0, 180, 2), "native opacity applied to test window");
                uint key, flags; byte alpha;
                Check(Native.GetLayeredWindowAttributes(form.Handle, out key, out alpha, out flags) && alpha == 180, "native opacity read back");
                recovery.Restore(); Check(Native.GetWindowLong(form.Handle, Native.ExStyle) == style, "original style restored");
                using (var backdrop = new Backdrop()) { backdrop.Connect(source.Handle); backdrop.Show(); backdrop.LayoutVideo(640, 480, false); backdrop.LayoutVideo(640, 480, true); Check(true, "DWM compositor accepts contain and cropped source"); }
                recovery.StartTicks--; Check(!recovery.Matches(), "reused process identity rejected");
            }
            if (args.Contains("--local")) {
                var settings = new Settings { Engine = Library.DetectEngine() }; int ignored;
                var video = Library.Scan(Library.Roots(settings), out ignored).First(x => x.Type == "video");
                double asyncAspect=Library.AspectAsync(video,0,System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Check(Math.Abs(asyncAspect-Library.Aspect(video,0))<.0001,"worker STA reads the same video ratio as synchronous metadata");
                double ratio = Library.VideoAspect(video.Project);
                Check(ratio > 0, "Windows reads actual local video dimensions: " + ratio.ToString("0.0000"));
            }
            Console.WriteLine("All " + checks + " checks passed."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(root, true); }
    }
}
