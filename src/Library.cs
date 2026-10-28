using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CodexWallpaper {
    public class Settings {
        public string Engine = "";
        public List<string> ExtraRoots = new List<string>();
        public int Strength = 24;
        public int Ratio = 0;
        public bool Cover = false;
    }
    public static class Storage {
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexWallpaper");
        public static T Read<T>(string file) where T : class, new() {
            try { return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(Path.Combine(Root, file))) ?? new T(); }
            catch { return new T(); }
        }
        public static void Write(string file, object value) {
            Directory.CreateDirectory(Root);
            string path = Path.Combine(Root, file), temp = path + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(value));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static void Log(string message) {
            try { Directory.CreateDirectory(Root); File.AppendAllText(Path.Combine(Root, "app.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine); } catch { }
        }
    }
    public class Wallpaper {
        public string Title, Type, Project, Preview;
        public override string ToString() { return Title; }
    }
    public static class Library {
        public static string Text(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) ? Convert.ToString(v) : ""; }
        public static List<string> SteamRoots() {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam" }) {
                foreach (string value in new[] { "SteamPath", "InstallPath" }) {
                    var s = Registry.GetValue(key, value, null) as string;
                    if (!String.IsNullOrEmpty(s) && Directory.Exists(s)) roots.Add(Path.GetFullPath(s));
                }
            }
            foreach (string root in roots.ToArray()) {
                string file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                try {
                    foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"path\"\\s*\"([^\"]+)\"")) {
                        string p = m.Groups[1].Value.Replace(@"\\", @"\");
                        if (Directory.Exists(p)) roots.Add(Path.GetFullPath(p));
                    }
                } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return roots.ToList();
        }
        public static string DetectEngine() {
            foreach (string name in new[] { "wallpaper64", "wallpaper32" }) {
                foreach (var p in Process.GetProcessesByName(name)) {
                    using (p) { try { return p.MainModule.FileName; } catch { } }
                }
            }
            foreach (string root in SteamRoots()) foreach (string exe in new[] { "wallpaper64.exe", "wallpaper32.exe" }) {
                string path = Path.Combine(root, "steamapps", "common", "wallpaper_engine", exe);
                if (File.Exists(path)) return path;
            }
            return "";
        }
        public static List<string> Roots(Settings s) {
            var roots = SteamRoots().Select(r => Path.Combine(r, "steamapps", "workshop", "content", "431960")).ToList();
            if (File.Exists(s.Engine)) {
                string dir = Path.GetDirectoryName(s.Engine);
                roots.Add(Path.Combine(dir, "projects", "myprojects"));
                roots.Add(Path.Combine(dir, "projects", "defaultprojects"));
                // Also handles a portable Steam install with no registry entry.
                roots.Add(Path.GetFullPath(Path.Combine(dir, "..", "..", "workshop", "content", "431960")));
            }
            roots.AddRange(s.ExtraRoots ?? new List<string>());
            return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        public static string SafeAsset(string folder, string relative) {
            if (String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return "";
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(folder, relative));
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : "";
        }
        public static List<Wallpaper> Scan(IEnumerable<string> roots, out int skipped) {
            var list = new List<Wallpaper>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); skipped = 0;
            foreach (string root in roots) {
                string[] dirs;
                try { dirs = new[] { root }.Concat(Directory.GetDirectories(root)).ToArray(); } catch { skipped++; continue; }
                foreach (string dir in dirs) {
                    string project = Path.Combine(dir, "project.json");
                    if (!File.Exists(project) || !seen.Add(Path.GetFullPath(project))) continue;
                    try {
                        var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(project));
                        string type = Text(d, "type").ToLowerInvariant();
                        if (type != "scene" && type != "video" && type != "web") { skipped++; continue; }
                        string title = Text(d, "title");
                        list.Add(new Wallpaper { Title = title.Length > 0 ? title : Path.GetFileName(dir), Type = type, Project = project, Preview = SafeAsset(dir, Text(d, "preview")) });
                    } catch { skipped++; }
                }
            }
            return list.OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        public static Bitmap Thumbnail(string path, int width, int height) {
            var b = new Bitmap(width, height);
            using (var g = Graphics.FromImage(b)) {
                g.Clear(Color.FromArgb(35, 42, 50));
                try { using (var img = Image.FromFile(path)) {
                    var r = Fit(img.Width, img.Height, width, height, false);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, r);
                }} catch { }
            }
            return b;
        }
        public static double Aspect(Wallpaper w, int choice) {
            double[] presets = { 0, 16.0 / 9, 21.0 / 9, 16.0 / 10, 4.0 / 3, 9.0 / 16, 1 };
            if (choice > 0 && choice < presets.Length) return presets[choice];
            if (w.Type == "video") {
                double videoAspect = VideoAspect(w.Project);
                if (videoAspect > 0) return videoAspect;
            }
            try { using (var img = Image.FromFile(w.Preview)) { return Math.Max(0.25, Math.Min(4, (double)img.Width / img.Height)); } }
            catch { return 16.0 / 9; }
        }
        public static double VideoAspect(string project) {
            object shell = null, folder = null, item = null;
            try {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(project));
                string video = SafeAsset(Path.GetDirectoryName(project), Text(d, "file"));
                if (video.Length == 0) return 0;
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                folder = shell.GetType().InvokeMember("NameSpace", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { Path.GetDirectoryName(video) });
                item = folder.GetType().InvokeMember("ParseName", System.Reflection.BindingFlags.InvokeMethod, null, folder, new object[] { Path.GetFileName(video) });
                int width = Convert.ToInt32(item.GetType().InvokeMember("ExtendedProperty", System.Reflection.BindingFlags.InvokeMethod, null, item, new object[] { "System.Video.FrameWidth" }));
                int height = Convert.ToInt32(item.GetType().InvokeMember("ExtendedProperty", System.Reflection.BindingFlags.InvokeMethod, null, item, new object[] { "System.Video.FrameHeight" }));
                return width > 0 && height > 0 ? (double)width / height : 0;
            } catch { return 0; }
            finally { foreach (object obj in new[] { item, folder, shell }) if (obj != null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
        }
        public static Rectangle Fit(int sw, int sh, int dw, int dh, bool cover) {
            double scale = cover ? Math.Max((double)dw / sw, (double)dh / sh) : Math.Min((double)dw / sw, (double)dh / sh);
            int w = Math.Max(1, (int)Math.Round(sw * scale, MidpointRounding.AwayFromZero)), h = Math.Max(1, (int)Math.Round(sh * scale, MidpointRounding.AwayFromZero));
            return new Rectangle((dw - w) / 2, (dh - h) / 2, w, h);
        }
    }
}
