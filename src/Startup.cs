// Transition timing and easing adapted from Dragon Codex Boot (MIT).
// See third_party/dragon-codex-boot/LICENSE and THIRD_PARTY_NOTICES.md.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Brushes = System.Windows.Media.Brushes;

namespace CodexWallpaper {
    public static class StartupTiming {
        public const double HoldAt = 11.3, FadeStart = 12.7, FadeEnd = 13.65;
        public static double Ease(double value) { value = Math.Max(0, Math.Min(1, value)); return value * value * (3 - 2 * value); }
        public static double Fade(double position, bool ready) { return ready ? Ease((position - FadeStart) / (FadeEnd - FadeStart)) : 0; }
        public static bool Hold(double position, bool ready) { return position >= HoldAt && !ready; }
    }

    public static class ClientStartup {
        public static Rectangle Bounds(IntPtr target) {
            Native.Rect r;
            if (Native.DwmGetWindowAttribute(target, 9, out r, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.Rect))) != 0) Native.GetWindowRect(target, out r);
            return new Rectangle(r.Left, r.Top, Math.Max(1, r.Width), Math.Max(1, r.Height));
        }
        public static async Task<Target> WaitForClient(Target selected, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            Target target = selected != null && Native.IsWindow(selected.Handle) ? selected : null;
            if (target == null) { var found = Native.Targets(); if (found.Count > 0) target = found[0]; }
            if (target == null) {
                // The package identity is stable across versions; never launch the CLI helper.
                Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App") { UseShellExecute = true }).Dispose();
            }
            var clock = Stopwatch.StartNew(); IntPtr stable = IntPtr.Zero; double since = 0;
            while (clock.Elapsed.TotalSeconds < 60) {
                token.ThrowIfCancellationRequested();
                if (target == null || !Native.IsWindow(target.Handle)) { var found = Native.Targets(); target = found.Count > 0 ? found[0] : null; }
                if (target != null) {
                    if (Native.IsIconic(target.Handle)) Native.ShowWindow(target.Handle, 9);
                    IntPtr result;
                    bool responds = Native.SendMessageTimeout(target.Handle, 0, IntPtr.Zero, IntPtr.Zero, 2, 80, out result) != IntPtr.Zero;
                    Rectangle r = Bounds(target.Handle); int cloaked;
                    Native.DwmGetIntAttribute(target.Handle, 14, out cloaked, 4);
                    if (responds && cloaked == 0 && r.Width > 200 && r.Height > 200) {
                        if (stable != target.Handle) { stable = target.Handle; since = clock.Elapsed.TotalSeconds; }
                        else if (clock.Elapsed.TotalSeconds - since >= .6) return target;
                    } else stable = IntPtr.Zero;
                }
                await Task.Delay(120, token);
            }
            throw new InvalidOperationException("60 秒内未找到可用的 Codex 窗口。请手动打开 Codex，再点击启动 / 应用。壁纸设置已保留。");
        }
    }

    public sealed class BootOverlay : Window {
        readonly MediaElement media;
        readonly TextBlock status;
        readonly DispatcherTimer timer;
        readonly Stopwatch wall = new Stopwatch();
        readonly TaskCompletionSource<bool> finished = new TaskCompletionSource<bool>();
        readonly string video;
        IntPtr host, target;
        Rectangle initial;
        bool opened, ready, holding, skip, failed, done;
        double firstFrame = -1, forcedFade = -1, lastPosition = -1, lastProgress;
        public Task Finished { get { return finished.Task; } }
        public event Action FirstFrame;
        public string Failure { get; private set; }

        public BootOverlay(string videoPath, Target selected) {
            video = videoPath;
            Title = "Codex Wallpaper · 龙娘启动动画";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 23, 29));
            // No persistent topmost flag: normal window stacking and focus still work with Alt+Tab.
            Opacity = 0; WindowStartupLocation = WindowStartupLocation.Manual;
            if (selected != null && Native.IsWindow(selected.Handle) && !Native.IsIconic(selected.Handle)) initial = ClientStartup.Bounds(selected.Handle);
            else {
                var area = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
                int width = Math.Min(1280, (int)(area.Width * .9)), height = Math.Min((int)(width * 9.0 / 16), (int)(area.Height * .9));
                width = (int)(height * 16.0 / 9); initial = new Rectangle(area.Left + (area.Width-width)/2, area.Top+(area.Height-height)/2, width, height);
            }
            Width = initial.Width; Height = initial.Height;
            var grid = new Grid();
            media = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = Stretch.Uniform, ScrubbingEnabled = true, Volume = .25 };
            grid.Children.Add(media);
            status = new TextBlock { Text = "正在准备 Codex 与壁纸…", Foreground = Brushes.White, Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(170, 18, 23, 29)), Padding = new Thickness(12, 7, 12, 7), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(24), Visibility = Visibility.Collapsed };
            grid.Children.Add(status);
            var skipButton = new System.Windows.Controls.Button { Content = "跳过动画  Esc", HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16), Padding = new Thickness(12,6,12,6), Opacity = .7 };
            skipButton.Click += delegate { Skip(); }; grid.Children.Add(skipButton); Content = grid;
            PreviewKeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; Skip(); } };
            SourceInitialized += delegate { host = new WindowInteropHelper(this).Handle; Native.SetWindowPos(host, IntPtr.Zero, initial.Left, initial.Top, initial.Width, initial.Height, 0x14); };
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) }; timer.Tick += Tick;
            Loaded += delegate {
                wall.Start(); timer.Start();
                if (!File.Exists(video)) { MediaFailure("启动动画文件缺失，已直接进入 Codex。"); return; }
                media.Source = new Uri(Path.GetFullPath(video)); media.Play();
            };
            media.MediaOpened += delegate { opened = true; lastProgress = wall.Elapsed.TotalSeconds; Storage.Log("boot-media-opened"); };
            media.MediaFailed += delegate(object s, ExceptionRoutedEventArgs e) { MediaFailure("启动动画无法播放，已直接进入 Codex。" + e.ErrorException.Message); };
            media.MediaEnded += delegate { skip = true; };
            Closed += delegate { done = true; timer.Stop(); media.Close(); finished.TrySetResult(true); };
        }
        void MediaFailure(string message) { Failure = message; failed = true; skip = true; Storage.Log(message); }
        public void SetReady(Target t) {
            if (done) return;
            if (t != null && Native.IsWindow(t.Handle)) {
                target = t.Handle;
                // Put the real, wallpaper-composited client immediately beneath the video.
                // Keep the client's existing geometry and fade in-place, without stretching it.
                Native.SetWindowPos(target, host, 0, 0, 0, 0, 0x13);
            }
            ready = true;
        }
        public void Skip() { skip = true; status.Text = ready ? "" : "动画已跳过，正在等待 Codex 与壁纸…"; }
        public void Abort() { if (!done) Close(); }
        void Tick(object sender, EventArgs e) {
            if (done) return;
            double now = wall.Elapsed.TotalSeconds, pos = opened ? media.Position.TotalSeconds : 0;
            if (now > 80) { MediaFailure("启动过渡超时。"); Abort(); return; }
            if (!opened && now > 10 && !failed) MediaFailure("媒体组件未能及时打开动画，已跳过。");
            if (opened && !holding && pos > lastPosition + .01) { lastProgress = now; lastPosition = pos; }
            if (opened && !holding && now-lastProgress > 6 && !failed) MediaFailure("媒体播放停止推进，已跳过动画。");
            if (firstFrame < 0 && opened && pos > .03) { firstFrame = now; var callback = FirstFrame; if (callback != null) callback(); }
            if (StartupTiming.Hold(pos, ready) && !holding && !skip) { media.Pause(); media.Position = TimeSpan.FromSeconds(StartupTiming.HoldAt); holding = true; }
            if (holding && ready) { holding = false; lastProgress = now; media.Play(); }
            status.Visibility = (holding || skip && !ready) ? Visibility.Visible : Visibility.Collapsed;
            if (target != IntPtr.Zero && Native.IsWindow(target)) {
                Rectangle goal = ClientStartup.Bounds(target);
                // Ease the playback frame toward a newly opened client's size before handoff.
                double amount = .18;
                Rectangle r = new Rectangle((int)Math.Round(initial.X+(goal.X-initial.X)*amount), (int)Math.Round(initial.Y+(goal.Y-initial.Y)*amount), (int)Math.Round(initial.Width+(goal.Width-initial.Width)*amount), (int)Math.Round(initial.Height+(goal.Height-initial.Height)*amount));
                if (Math.Abs(r.X-goal.X)<5 && Math.Abs(r.Y-goal.Y)<5 && Math.Abs(r.Width-goal.Width)<5 && Math.Abs(r.Height-goal.Height)<5) r=goal;
                if (r != initial) { initial=r; Native.SetWindowPos(host, IntPtr.Zero, r.Left,r.Top,r.Width,r.Height,0x14); }
            }
            double fade = StartupTiming.Fade(pos, ready);
            if (skip && ready) { if (forcedFade < 0) forcedFade=now; fade = Math.Max(fade, StartupTiming.Ease((now-forcedFade)/.65)); }
            double appear = firstFrame < 0 ? 0 : StartupTiming.Ease((now-firstFrame)/.22);
            Opacity = appear*(1-fade);
            if (fade >= 1) { Storage.Log("boot-handoff-complete"); Abort(); }
        }
    }
}
