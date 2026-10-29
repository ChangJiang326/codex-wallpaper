using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Forms;

namespace CodexWallpaper {
    static class Program {
        [STAThread] static void Main(string[] args) {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { Native.SetProcessDPIAware(); }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 4 && args[0] == "--watch") { Bridge.Watch(args); return; }
            bool created;
            using (var mutex = new Mutex(true, @"Local\CodexWallpaper-Portable-v1", out created)) {
                if (!created) { if (!args.Contains("--background")) InstanceSignals.Send(args.Contains("--exit") ? "exit" : args.Contains("--restore") ? "restore" : "show"); return; }
                Bridge.Recover();
                if (args.Contains("--restore") || args.Contains("--exit")) return;
                using (var app = new MainForm()) using (var context = new ApplicationContext()) {
                    Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { app.Restore(); Storage.Log(e.Exception.ToString()); MessageBox.Show(e.Exception.Message, "Codex Wallpaper"); };
                    app.FormClosed += delegate { context.ExitThread(); };
                    app.InitializeBackground();
                    if (!args.Contains("--background")) app.ShowSettings();
                    Application.Run(context);
                }
            }
        }
    }
    public class MainForm : Form {
        readonly Color bg = Color.FromArgb(18, 23, 29), surface = Color.FromArgb(27, 34, 42), muted = Color.FromArgb(155, 171, 184), accent = Color.FromArgb(99, 220, 185);
        readonly Settings settings;
        readonly Bridge bridge = new Bridge();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly ListView library = new ListView();
        readonly ImageList thumbnails = new ImageList();
        readonly TextBox search = new TextBox(), engine = new TextBox();
        readonly ComboBox targets = new ComboBox(), ratios = new ComboBox(), fit = new ComboBox();
        readonly PictureBox preview = new PictureBox();
        readonly Label selectedTitle = new Label(), status = new Label(), strengthText = new Label(), count = new Label();
        readonly TrackBar strength = new TrackBar();
        readonly Button apply;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly CheckBox startupAnimation = new CheckBox();
        readonly CheckBox autoApply = new CheckBox(), startWithWindows = new CheckBox();
        readonly InstanceSignals signals = new InstanceSignals();
        readonly AutoApplyGate gate = new AutoApplyGate();
        readonly Stopwatch monitorClock = Stopwatch.StartNew();
        CancellationTokenSource startupCancellation;
        BootOverlay boot;
        List<Wallpaper> wallpapers = new List<Wallpaper>();
        bool busy, closing, exitRequested, libraryLoaded;
        double nextProbe;
        string attemptIdentity;

        public MainForm() {
            settings = Storage.Read<Settings>("settings.json");
            if (!File.Exists(settings.Engine)) settings.Engine = Library.DetectEngine();
            Text = "Codex Wallpaper · 壁纸选择器"; MinimumSize = new Size(980, 710); ClientSize = new Size(1180, 790);
            StartPosition = FormStartPosition.CenterScreen; BackColor = bg; ForeColor = Color.WhiteSmoke;
            Font = new Font("Microsoft YaHei UI", 9F); AutoScaleMode = AutoScaleMode.Dpi;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 20, 26, 16), ColumnCount = 1, RowCount = 4, BackColor = bg };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); Controls.Add(root);
            var heading = new Panel { Dock = DockStyle.Fill };
            heading.Controls.Add(LabelAt("给工作，换一幅风景。", 0, 0, 650, 40, 24, Color.WhiteSmoke));
            heading.Controls.Add(LabelAt("CODEX WALLPAPER   /   本地壁纸 · 原生渲染 · 等比显示", 2, 48, 750, 24, 9, muted)); root.Controls.Add(heading, 0, 0);

            var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            engine.Text = settings.Engine; engine.ReadOnly = true; StyleText(engine); engine.Dock = DockStyle.Fill; engine.Margin = new Padding(0, 8, 12, 6); engine.AccessibleName = "Wallpaper Engine 路径";
            pathRow.Controls.Add(engine); pathRow.Controls.Add(Button("选择程序", ChooseEngine)); pathRow.Controls.Add(Button("添加壁纸目录", AddRoot)); root.Controls.Add(pathRow, 0, 1);

            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340)); root.Controls.Add(content, 0, 2);
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = new Padding(0, 0, 22, 0) };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) }; tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            StyleText(search); search.Dock = DockStyle.Fill; search.Margin = new Padding(0, 5, 12, 5); search.AccessibleName = "搜索壁纸"; search.TextChanged += delegate { RenderLibrary(); }; tools.Controls.Add(search);
            tools.Controls.Add(Button("重新扫描", async delegate { await Scan(); })); left.Controls.Add(tools);
            count.ForeColor = muted; count.Dock = DockStyle.Fill; left.Controls.Add(count);
            library.Dock = DockStyle.Fill; library.BackColor = bg; library.ForeColor = Color.WhiteSmoke; library.BorderStyle = BorderStyle.None; library.View = View.LargeIcon; library.MultiSelect = false; library.HideSelection = false;
            library.AccessibleName = "本地壁纸库"; thumbnails.ImageSize = new Size(192, 108); thumbnails.ColorDepth = ColorDepth.Depth32Bit; library.LargeImageList = thumbnails;
            library.SelectedIndexChanged += delegate { UpdateSelection(); }; left.Controls.Add(library); content.Controls.Add(left, 0, 0);

            var rightHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = surface, Margin = new Padding(0) };
            var right = new TableLayoutPanel { Dock = DockStyle.Top, Height = 712, ColumnCount = 1, RowCount = 16, Padding = new Padding(16, 12, 16, 12), BackColor = surface, Margin = new Padding(0) };
            int[] heights = { 118, 36, 20, 30, 20, 30, 22, 32, 20, 30, 34, 29, 29, 42, 38 };
            foreach (int height in heights) right.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            preview.Dock = DockStyle.Fill; preview.SizeMode = PictureBoxSizeMode.Zoom; preview.BackColor = Color.FromArgb(12, 17, 22); right.Controls.Add(preview);
            selectedTitle.Text = "选择一张壁纸"; selectedTitle.Dock = DockStyle.Fill; selectedTitle.Font = new Font(Font, FontStyle.Bold); selectedTitle.TextAlign = ContentAlignment.MiddleLeft; right.Controls.Add(selectedTitle);
            right.Controls.Add(SectionLabel("应用到哪个 Codex 窗口"));
            var targetRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) }; targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58)); StyleCombo(targets); targetRow.Controls.Add(targets); targetRow.Controls.Add(Button("刷新", delegate { RefreshTargets(); })); right.Controls.Add(targetRow);
            right.Controls.Add(SectionLabel("画面比例")); StyleCombo(ratios); ratios.Items.AddRange(new object[] { "自动（视频原尺寸 / 封面）", "16 : 9", "21 : 9", "16 : 10", "4 : 3", "9 : 16（竖屏）", "1 : 1" }); ratios.SelectedIndex = Math.Max(0, Math.Min(6, settings.Ratio)); ratios.SelectedIndexChanged += delegate { settings.Ratio = ratios.SelectedIndex; Save(); if (bridge.Active) status.Text = "比例已修改，点击“应用到 Codex”重新渲染。"; }; right.Controls.Add(ratios);
            strengthText.Text = "壁纸可见度  " + settings.Strength + "%"; strengthText.Dock = DockStyle.Fill; strengthText.ForeColor = muted; right.Controls.Add(strengthText);
            strength.Minimum = 5; strength.Maximum = 60; strength.Value = Math.Max(5, Math.Min(60, settings.Strength)); strength.TickStyle = TickStyle.None; strength.Dock = DockStyle.Fill; strength.AccessibleName = "壁纸可见度";
            strength.ValueChanged += delegate { settings.Strength = strength.Value; strengthText.Text = "壁纸可见度  " + settings.Strength + "%"; bridge.ApplyStrength(settings.Strength); Save(); }; right.Controls.Add(strength);
            right.Controls.Add(SectionLabel("缩放方式")); StyleCombo(fit); fit.Items.AddRange(new object[] { "完整显示 · 等比留边", "铺满窗口 · 等比裁切" }); fit.SelectedIndex = settings.Cover ? 1 : 0; fit.SelectedIndexChanged += delegate { settings.Cover = fit.SelectedIndex == 1; Save(); }; right.Controls.Add(fit);
            var animationRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            animationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); animationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            startupAnimation.Text = "启用启动动画"; startupAnimation.Checked = settings.StartupAnimation; startupAnimation.Dock = DockStyle.Fill;
            startupAnimation.CheckedChanged += delegate { settings.StartupAnimation = startupAnimation.Checked; Save(); };
            animationRow.Controls.Add(startupAnimation); animationRow.Controls.Add(Button("预览", async delegate { await PreviewAnimation(); })); right.Controls.Add(animationRow);
            autoApply.Text = "GPT / Codex 启动后自动应用"; autoApply.Checked = settings.AutoApply; autoApply.Dock = DockStyle.Fill;
            autoApply.CheckedChanged += delegate { settings.AutoApply = autoApply.Checked; gate.Clear(); if (!settings.AutoApply) Restore(); Save(); };
            right.Controls.Add(autoApply);
            startWithWindows.Text = "随 Windows 登录在后台启动"; startWithWindows.Checked = settings.StartWithWindows; startWithWindows.Dock = DockStyle.Fill;
            startWithWindows.CheckedChanged += delegate { settings.StartWithWindows = startWithWindows.Checked; Save(); try { LoginStartup.Set(settings.StartWithWindows, Application.ExecutablePath); } catch(Exception ex) { status.Text="登录启动设置失败："+ex.Message; } };
            right.Controls.Add(startWithWindows);
            apply = Button("保存并应用（以后自动）", async delegate { await Apply(); }); apply.BackColor = accent; apply.ForeColor = Color.FromArgb(13, 38, 32); apply.Font = new Font(Font, FontStyle.Bold); right.Controls.Add(apply); AcceptButton = apply;
            right.Controls.Add(Button("恢复原样  ·  Ctrl + Alt + F10", delegate { Restore(); }));
            var note = new Label { Text = "设置一次，之后从原 GPT 图标打开即可。\n点 × 收进托盘；托盘菜单可彻底退出。\n兼容模式也会影响文字透明度。", ForeColor = muted, Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 8), Padding = new Padding(0, 5, 0, 0) }; right.Controls.Add(note); rightHost.Controls.Add(right); content.Controls.Add(rightHost, 1, 0);
            status.Dock = DockStyle.Fill; status.ForeColor = accent; status.TextAlign = ContentAlignment.MiddleLeft; status.Text = "准备扫描本机壁纸库…"; root.Controls.Add(status, 0, 3);
            var trayMenu = new ContextMenuStrip(); trayMenu.Items.Add("打开壁纸设置", null, delegate { ShowSettings(); }); trayMenu.Items.Add("恢复原样（本次窗口暂停）", null, delegate { Restore(); }); trayMenu.Items.Add("退出并恢复", null, delegate { ExitApplication(); });
            tray.Icon = SystemIcons.Application; tray.Text = "Codex Wallpaper · 自动等待客户端"; tray.ContextMenuStrip = trayMenu; tray.Visible = true; tray.DoubleClick += delegate { ShowSettings(); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
            timer.Interval = 80; timer.Tick += async delegate {
                if (signals.ExitRequested()) { ExitApplication(); return; }
                if (signals.RestoreRequested()) Restore();
                if (signals.ShowRequested()) ShowSettings();
                try { bridge.Tick(settings); } catch (Exception ex) { StopEffects(); status.Text = ex.Message; Storage.Log(ex.ToString()); if(attemptIdentity!=null)gate.Failure(attemptIdentity,monitorClock.Elapsed.TotalSeconds); }
                if (monitorClock.Elapsed.TotalSeconds >= nextProbe) { nextProbe=monitorClock.Elapsed.TotalSeconds+.5; await ProbeClient(); }
            };
        }
        public void InitializeBackground() {
            Storage.Log("app-start v1.2.1 profile="+!String.IsNullOrWhiteSpace(settings.LastProject)+" animation="+settings.StartupAnimation);
            var hwnd=Handle;
            if (!Native.RegisterHotKey(hwnd,1,0x4003,0x79)) Storage.Log("Ctrl+Alt+F10 unavailable (already registered).");
            if (!String.IsNullOrWhiteSpace(settings.LastProject)) {
                try { LoginStartup.Set(settings.StartWithWindows,Application.ExecutablePath); } catch(Exception ex) { Storage.Log(ex.Message); }
            }
            timer.Start();
        }
        public async void ShowSettings() {
            Show(); WindowState=FormWindowState.Normal; Activate(); RefreshTargets();
            if (!libraryLoaded && !busy) await Scan();
        }
        public void ExitApplication() { exitRequested=true; Close(); }
        async Task ProbeClient() {
            if (closing || busy || !settings.AutoApply || bridge.Active || String.IsNullOrWhiteSpace(settings.LastProject)) return;
            try {
                // One client window at a time, matching the underlying compositor.
                var target=Native.Targets().FirstOrDefault(t => !Native.IsIconic(t.Handle));
                if(target==null)return;
                string identity=BackgroundWatch.Identity(target);
                if(!gate.CanAttempt(identity,monitorClock.Elapsed.TotalSeconds))return;
                bool foreground=Native.GetForegroundWindow()==target.Handle;
                if(gate.WaitForForeground(identity,monitorClock.Elapsed.TotalSeconds,foreground,settings.StartupAnimation))return;
                bool animate=settings.StartupAnimation && gate.FirstAttempt(identity) && foreground;
                Storage.Log("auto-detected animation="+animate+" foreground="+foreground);
                attemptIdentity=identity; gate.Begin(identity);
                await RunWallpaper(null,target,true,animate);
            } catch(Exception ex) {
                if(attemptIdentity!=null)gate.Failure(attemptIdentity,monitorClock.Elapsed.TotalSeconds);
                status.Text=ex.Message; Storage.Log("auto-apply: "+ex.Message);
                if(attemptIdentity!=null && !gate.CanAttempt(attemptIdentity,monitorClock.Elapsed.TotalSeconds+11)) tray.ShowBalloonTip(5000,"请检查保存的壁纸设置",ex.Message,ToolTipIcon.Warning);
            }
        }
        void StyleText(TextBox t) { t.BackColor = surface; t.ForeColor = Color.WhiteSmoke; t.BorderStyle = BorderStyle.FixedSingle; }
        void StyleCombo(ComboBox c) { c.DropDownStyle = ComboBoxStyle.DropDownList; c.BackColor = bg; c.ForeColor = Color.WhiteSmoke; c.FlatStyle = FlatStyle.Flat; c.Dock = DockStyle.Fill; }
        Label SectionLabel(string text) { return new Label { Text = text, Dock = DockStyle.Fill, ForeColor = muted, TextAlign = ContentAlignment.BottomLeft }; }
        Label LabelAt(string text, int x, int y, int w, int h, float size, Color color) { return new Label { Text = text, Location = new Point(x, y), Size = new Size(w, h), Font = new Font(Font.FontFamily, size), ForeColor = color }; }
        Button Button(string text, EventHandler action) { var b = new Button { Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = surface, ForeColor = Color.WhiteSmoke, Margin = new Padding(0, 2, 4, 4), Cursor = Cursors.Hand }; b.FlatAppearance.BorderColor = Color.FromArgb(55, 69, 80); b.Click += action; return b; }
        void Save() { try { Storage.Write("settings.json", settings); } catch (Exception ex) { Storage.Log(ex.Message); } }
        void ChooseEngine(object sender, EventArgs e) {
            using (var d = new OpenFileDialog { Title = "选择 wallpaper64.exe 或 wallpaper32.exe", Filter = "Wallpaper Engine|wallpaper64.exe;wallpaper32.exe" }) {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string name = Path.GetFileName(d.FileName).ToLowerInvariant();
                if (name != "wallpaper64.exe" && name != "wallpaper32.exe") { MessageBox.Show("请选择 Wallpaper Engine 主程序。"); return; }
                settings.Engine = d.FileName; engine.Text = settings.Engine; Save(); ScanFromEvent();
            }
        }
        void AddRoot(object sender, EventArgs e) { using (var d = new FolderBrowserDialog { Description = "选择 431960、myprojects，或包含 project.json 的单张壁纸目录" }) { if (d.ShowDialog(this) != DialogResult.OK) return; if (settings.ExtraRoots == null) settings.ExtraRoots = new List<string>(); settings.ExtraRoots.Add(d.SelectedPath); Save(); ScanFromEvent(); } }
        async void ScanFromEvent() { await Scan(); }
        async Task Scan() {
            if (busy) return; busy = true; apply.Enabled = false; status.Text = "正在读取本地壁纸…";
            try {
                var roots = Library.Roots(settings); int skipped = 0;
                var found = await Task.Run(delegate { return Library.Scan(roots, out skipped); });
                if (closing) return;
                wallpapers = found; libraryLoaded=true; thumbnails.Images.Clear();
                foreach (var w in wallpapers) { using (var b = Library.Thumbnail(w.Preview, 192, 108)) thumbnails.Images.Add(b); }
                RenderLibrary();
                status.Text = String.Format("已找到 {0} 张壁纸 · {1} 个目录{2}", wallpapers.Count, roots.Count, skipped > 0 ? " · 跳过 " + skipped + " 个损坏或非 scene/video/web 项目" : "");
                if (wallpapers.Count == 0) status.Text = "没有找到壁纸。请选择程序路径，或添加 Steam 的 431960 壁纸目录。";
            } catch (Exception ex) { status.Text = "扫描失败：" + ex.Message; Storage.Log(ex.ToString()); }
            finally { busy = false; if (!closing) apply.Enabled = true; }
        }
        void RenderLibrary() {
            string query = search.Text.Trim(); library.BeginUpdate(); library.Items.Clear();
            for (int i = 0; i < wallpapers.Count; i++) { var w = wallpapers[i]; if (query.Length > 0 && w.Title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0 && w.Type.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue; var item = new ListViewItem(w.Title + "\n" + w.Type.ToUpperInvariant(), i) { Tag = w, ToolTipText = w.Title }; library.Items.Add(item); }
            library.EndUpdate(); count.Text = "本地壁纸库  /  " + library.Items.Count + " 张   ·   上方输入名称或 scene / video / web 搜索";
            if (library.Items.Count > 0) { var remembered = library.Items.Cast<ListViewItem>().FirstOrDefault(i => ((Wallpaper)i.Tag).Project == settings.LastProject); (remembered ?? library.Items[0]).Selected = true; } else UpdateSelection();
        }
        void UpdateSelection() {
            var w = library.SelectedItems.Count > 0 ? library.SelectedItems[0].Tag as Wallpaper : null;
            var old = preview.Image; preview.Image = w == null ? null : Library.Thumbnail(w.Preview, 600, 330); if (old != null) old.Dispose();
            selectedTitle.Text = w == null ? "选择一张壁纸" : w.Title;
        }
        void RefreshTargets() { targets.Items.Clear(); foreach (var t in Native.Targets()) targets.Items.Add(t); if (targets.Items.Count > 0) targets.SelectedIndex = 0; else status.Text = "Codex 尚未打开，点击“启动 / 应用”将尝试启动客户端。"; }
        async Task PreviewAnimation() {
            if (busy) return;
            busy = true; apply.Enabled = false;
            try {
                boot = new BootOverlay(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "media", "startup.mp4"), null);
                boot.SetReady(null); boot.Show(); await boot.Finished;
                if (!closing) { Show(); Activate(); status.Text = boot.Failure ?? "预览结束。应用时会衔接到当前壁纸与真实 Codex 窗口。"; }
            } finally { if (boot != null) boot.Abort(); boot = null; busy = false; if (!closing) apply.Enabled = true; }
        }
        async Task Apply() {
            if (busy) return;
            if (library.SelectedItems.Count == 0) { status.Text = "请先在左侧选择壁纸。"; return; }
            if (targets.SelectedItem == null) RefreshTargets();
            var selected = targets.SelectedItem as Target;
            var wallpaper = (Wallpaper)library.SelectedItems[0].Tag;
            if (!File.Exists(settings.Engine)) { status.Text = "请先选择 Wallpaper Engine 程序路径。"; return; }
            // Save before starting: even if the client is slow or missing, configuration persists.
            settings.LastProject=wallpaper.Project;
            try { Storage.Write("settings.json",settings); LoginStartup.Set(settings.StartWithWindows,Application.ExecutablePath); }
            catch(Exception ex) { status.Text="设置保存失败："+ex.Message; return; }
            gate.Clear(); attemptIdentity=null;
            await RunWallpaper(wallpaper,selected,false,settings.StartupAnimation);
        }
        async Task RunWallpaper(Wallpaper wallpaper,Target selected,bool automatic,bool animate) {
            busy = true; apply.Enabled = false; status.Text = "正在启动专用于 Codex 的壁纸窗口…";
            startupCancellation = new CancellationTokenSource(); var token = startupCancellation.Token;
            try {
                if(wallpaper==null)wallpaper=await Task.Run(()=>BackgroundWatch.SavedWallpaper(settings),token);
                var target = await ClientStartup.WaitForClient(selected, token, !automatic);
                Storage.Log("client-ready");
                if(automatic && Native.GetForegroundWindow()!=target.Handle)animate=false;
                if (animate) {
                    boot = new BootOverlay(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "media", "startup.mp4"), target, automatic);
                    boot.FirstFrame += delegate { if (!closing && !automatic) Hide(); };
                    boot.Show();
                }
                attemptIdentity=BackgroundWatch.Identity(target); gate.Begin(attemptIdentity);
                await BackgroundWatch.EnsureEngine(settings,token);
                Storage.Log("engine-process-ready");
                await bridge.Start(settings, wallpaper, target, token);
                // Give the engine time to present frames before exposing the compositor.
                await Task.Delay(650, token); bridge.Tick(settings);
                if(!bridge.Active)throw new OperationCanceledException("客户端已关闭。",token);
                if (boot != null) { boot.SetReady(target); await boot.Finished; }
                token.ThrowIfCancellationRequested();
                if(!bridge.Active || !Native.IsWindow(target.Handle))throw new OperationCanceledException("客户端在交接前已关闭。",token);
                gate.Success(attemptIdentity,monitorClock.Elapsed.TotalSeconds);
                if (!closing) {
                    status.Text = boot != null && boot.Failure != null ? boot.Failure : "已应用 · 设置已记住，下次打开 GPT 自动生效";
                    Storage.Log(automatic ? "auto-apply-complete" : "saved-profile-applied");
                    if(!automatic) { Hide(); Native.SetForegroundWindow(target.Handle); }
                }
            }
            catch (OperationCanceledException) { bridge.Stop(); if (!closing) status.Text = "已取消启动，Codex 原样已恢复。"; }
            catch (Exception ex) { bridge.Stop(); if(attemptIdentity!=null)gate.Failure(attemptIdentity,monitorClock.Elapsed.TotalSeconds); if (!closing) { status.Text = ex.Message; if(!automatic){Show();Activate();} else if(!gate.CanAttempt(attemptIdentity,monitorClock.Elapsed.TotalSeconds+11))tray.ShowBalloonTip(5000,"自动应用暂未成功",ex.Message,ToolTipIcon.Warning); } Storage.Log(ex.ToString()); }
            finally {
                if (boot != null) boot.Abort(); boot = null;
                startupCancellation.Dispose(); startupCancellation = null;
                busy = false; if (!closing) apply.Enabled = true;
            }
        }
        void StopEffects() { if (startupCancellation != null) startupCancellation.Cancel(); if (boot != null) boot.Abort(); bridge.Stop(); }
        public void Restore() {
            // Suppress only this window; reopening the client creates a fresh identity.
            if(attemptIdentity!=null)gate.Suppress(attemptIdentity);
            foreach(var target in Native.Targets()) { try { gate.Suppress(BackgroundWatch.Identity(target)); } catch { } }
            StopEffects(); status.Text="已恢复原样，本次窗口暂停自动应用；下次打开 GPT 会重新应用。";
        }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x312 && m.WParam.ToInt32() == 1) Restore(); base.WndProc(ref m); }
        protected override void OnFormClosing(FormClosingEventArgs e) {
            if(!exitRequested && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); return; }
            closing = true; StopEffects(); timer.Stop(); bridge.Dispose(); Native.UnregisterHotKey(Handle, 1); tray.Visible = false; tray.Dispose(); Save(); base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); signals.Dispose(); bridge.Dispose(); thumbnails.Dispose(); if (preview.Image != null) preview.Image.Dispose(); } base.Dispose(disposing); }
    }
}
