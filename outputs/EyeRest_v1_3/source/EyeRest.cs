using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("远眺 · 20-20-20 护眼提醒")]
[assembly: System.Reflection.AssemblyVersion("1.3.0.0")]

namespace EyeRest {
    public class Preferences {
        public int X = int.MinValue, Y = int.MinValue;
        public string Dock = "";
        public int IntervalMinutes = 20;
        public int RestSeconds = 20;
    }
    public enum Phase { Working, Waiting, Resting, Paused }
    public sealed class Clock {
        public Phase Phase = Phase.Working;
        public DateTime Due;
        public TimeSpan Remaining;
        public int IntervalMinutes = 20, RestSeconds = 20;
        public Clock(DateTime now) { Due = now.AddMinutes(20); }
        public void Configure(int minutes, int seconds, DateTime now) {
            if (minutes < 1 || minutes > 180 || seconds < 1 || seconds > 300) throw new ArgumentOutOfRangeException("duration");
            bool paused = Phase == Phase.Paused;
            IntervalMinutes = minutes; RestSeconds = seconds;
            Reset(now); if (paused) TogglePause(now);
        }
        public void Tick(DateTime now) {
            if (now < Due) return;
            if (Phase == Phase.Working) Phase = Phase.Waiting;
            else if (Phase == Phase.Resting) Reset(now);
        }
        public void Reset(DateTime now) { Phase = Phase.Working; Due = now.AddMinutes(IntervalMinutes); }
        public void StartRest(DateTime now) { Phase = Phase.Resting; Due = now.AddSeconds(RestSeconds); }
        public void Snooze(DateTime now) { Phase = Phase.Working; Due = now.AddMinutes(5); }
        public void TogglePause(DateTime now) {
            if (Phase == Phase.Paused) { Phase = Phase.Working; Due = now.Add(Remaining); }
            else { Remaining = Phase == Phase.Working ? Due - now : TimeSpan.FromMinutes(IntervalMinutes); if (Remaining < TimeSpan.Zero) Remaining = TimeSpan.Zero; Phase = Phase.Paused; }
        }
        public string Display(DateTime now) {
            if (Phase == Phase.Waiting) return RestSeconds + " 秒";
            TimeSpan left = Phase == Phase.Paused ? Remaining : Due - now;
            int seconds = Math.Max(0, (int)Math.Ceiling(left.TotalSeconds));
            return Phase == Phase.Resting ? seconds.ToString("00") + " 秒" : (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }
    }
    internal static class Program {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread] static void Main(string[] args) {
            bool verify = Array.IndexOf(args, "--verify") >= 0;
            using (System.Threading.Mutex mutex = new System.Threading.Mutex(true, verify ? "Local\\EyeRestV1Verify" : "Local\\EyeRestV1", out created)) {
                if (!created) { MessageBox.Show("护眼提醒已经在运行。请从右下角托盘打开。", "远眺"); return; }
                try {
                    SetProcessDPIAware();
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new MainWindow(verify, Array.IndexOf(args, "--startup") >= 0));
                } catch (Exception ex) { MessageBox.Show("程序未能运行：" + ex.Message, "远眺"); }
            }
        }
        static bool created;
    }
    public sealed class EdgeTab : Form {
        public EdgeTab() {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            BackColor = Color.FromArgb(30, 91, 75); Cursor = Cursors.Hand;
            AccessibleName = "展开护眼提醒";
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            using (Pen p = new Pen(Color.White, 2)) {
                int mid = Width / 2;
                e.Graphics.DrawLine(p, mid, Height / 2 - 13, mid, Height / 2 + 13);
            }
        }
    }
    public sealed class MainWindow : Form {
        const string StartupKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        const string StartupName = "EyeRest202020_v1";
        readonly Color ink = Theme.Ink, muted = Theme.Muted, green = Theme.Green;
        readonly Clock clock = new Clock(DateTime.UtcNow);
        readonly Timer timer = new Timer();
        readonly EdgeTab edge = new EdgeTab();
        readonly string folder;
        readonly bool verify, startupLaunch;
        Preferences prefs = new Preferences();
        Label title, number, detail, hint, dragBar;
        Button primary, later, pause, trayButton, settingsButton;
        CheckBox startup;
        NotifyIcon tray;
        ToolStripMenuItem pauseMenu, settingsMenu;
        bool quitting, dragging, loading, trayOnly;
        Point dragOffset;
        DateTime outsideSince = DateTime.UtcNow;
        double scale;
        int ticks;
        Stopwatch verification;
        bool verifyRestStarted, verifyCompleted;
        string verifyReport;

        public MainWindow(bool test, bool boot) {
            verify = test; startupLaunch = boot;
            folder = verify ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "work", "verification_v1_2") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeRest202020", "v1_2");
            Directory.CreateDirectory(folder);
            LoadPreferences();
            clock.Configure(prefs.IntervalMinutes, prefs.RestSeconds, DateTime.UtcNow);
            using (Graphics g = CreateGraphics()) scale = Math.Max(1, g.DpiX / 96.0);
            AutoScaleMode = AutoScaleMode.None;
            Text = "远眺 · 护眼提醒 V1.3"; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; BackColor = Theme.Surface;
            ForeColor = ink; ClientSize = new Size(S(352), S(416)); StartPosition = FormStartPosition.Manual;
            Font = new Font("Microsoft YaHei UI", 10);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            BuildUI(); BuildTray();
            edge.Size = new Size(S(12), S(90));
            edge.MouseEnter += delegate { Reveal(false); };
            edge.MouseClick += delegate { Reveal(true); };
            timer.Interval = 100; timer.Tick += OnTick;
            Shown += delegate {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                Location = prefs.X == int.MinValue ? new Point(area.Right - Width - S(32), area.Top + S(100)) : new Point(prefs.X, prefs.Y);
                ClampToScreen();
                if (verify) { RunChecks(); }
                else if (startupLaunch) { if (prefs.Dock != "") Collapse(); else ToTray(); }
                timer.Start(); Render();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; ToTray(); return; }
                SavePreferences(); timer.Stop(); tray.Visible = false; tray.Dispose(); edge.Dispose();
            };
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            FormClosed += delegate { SystemEvents.DisplaySettingsChanged -= DisplayChanged; };
        }
        int S(int n) { return (int)Math.Round(n * scale); }
        Label LabelAt(string text, int x, int y, int w, int h, float size, Color color, bool bold) {
            Label l = new Label { Text = text, Location = new Point(S(x), S(y)), Size = new Size(S(w), S(h)), ForeColor = color,
                Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular), TextAlign = ContentAlignment.MiddleLeft };
            Controls.Add(l); return l;
        }
        Button ButtonAt(string text, int x, int y, int w, EventHandler action, bool solid) {
            Button b = new Button { Text = text, Location = new Point(S(x), S(y)), Size = new Size(S(w), S(38)),
                FlatStyle = FlatStyle.Flat, BackColor = solid ? green : BackColor, ForeColor = solid ? Color.White : ink,
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false, AccessibleName = text };
            b.FlatAppearance.BorderSize = solid ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(173, 192, 180);
            b.Click += action; Controls.Add(b); return b;
        }
        void BuildUI() {
            dragBar = LabelAt("远眺   /   护眼提醒", 22, 12, 224, 31, 10, muted, false);
            dragBar.Cursor = Cursors.SizeAll;
            dragBar.MouseDown += BeginDrag; dragBar.MouseMove += MoveDrag; dragBar.MouseUp += EndDrag;
            MouseDown += BeginDrag; MouseMove += MoveDrag; MouseUp += EndDrag;
            trayButton = ButtonAt("托盘", 272, 12, 58, delegate { ToTray(); }, false); trayButton.Height = S(30);
            title = LabelAt("距离下次远眺", 22, 61, 308, 31, 14, ink, true);
            number = LabelAt("20:00", 18, 92, 312, 79, 40, green, true);
            detail = LabelAt("每20分钟，看约6米外至少20秒", 22, 177, 308, 29, 10, muted, false);
            primary = ButtonAt("立即休息", 22, 221, 183, delegate { MainAction(); }, true);
            later = ButtonAt("5分钟后提醒", 215, 221, 115, delegate { Snooze(); }, false);
            pause = ButtonAt("暂停", 22, 269, 90, delegate { TogglePause(); }, false);
            startup = new CheckBox { Text = "开机启动", Location = new Point(S(135), S(274)), Size = new Size(S(130), S(28)), AutoSize = false, ForeColor = muted };
            loading = true; startup.Checked = !verify && IsStartupEnabled(); loading = false;
            startup.CheckedChanged += delegate { if (!loading) ChangeStartup(); }; Controls.Add(startup);
            settingsButton = ButtonAt("时间设置", 22, 319, 308, delegate { OpenSettings(); }, false);
            hint = LabelAt("拖动顶部到左右边缘可隐藏 · V1.3", 22, 375, 308, 25, 9, muted, false);
            ContextMenuStrip context = new ContextMenuStrip();
            context.Items.Add("取消贴边", null, delegate { prefs.Dock = ""; Reveal(true); SavePreferences(); });
            context.Items.Add("退出程序", null, delegate { Quit(); }); ContextMenuStrip = context;
        }
        void BuildTray() {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("显示悬浮窗", null, delegate { Reveal(true); });
            settingsMenu = new ToolStripMenuItem("时间设置", null, delegate { OpenSettings(); }); menu.Items.Add(settingsMenu);
            menu.Items.Add("立即休息", null, delegate { MainAction(); });
            pauseMenu = new ToolStripMenuItem("暂停提醒", null, delegate { TogglePause(); }); menu.Items.Add(pauseMenu);
            menu.Items.Add("5分钟后提醒", null, delegate { Snooze(); });
            menu.Items.Add("取消贴边", null, delegate { prefs.Dock = ""; Reveal(true); SavePreferences(); });
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("退出程序", null, delegate { Quit(); });
            tray = new NotifyIcon { Icon = Icon, Text = "远眺 · 每20分钟提醒", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { Reveal(true); };
        }
        void MainAction() {
            if (clock.Phase == Phase.Resting) return;
            clock.StartRest(DateTime.UtcNow); Reveal(true); Render();
        }
        void Snooze() { clock.Snooze(DateTime.UtcNow); Render(); ReturnToHome(); }
        void TogglePause() { clock.TogglePause(DateTime.UtcNow); Render(); }
        void OpenSettings() {
            if (clock.Phase == Phase.Resting) return;
            Reveal(true); timer.Stop();
            try {
                using (TimeSettings page = new TimeSettings(prefs.IntervalMinutes, prefs.RestSeconds, scale)) {
                    Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
                    page.Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - page.Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - page.Height)));
                    if (page.ShowDialog(this) == DialogResult.OK) {
                        int oldMinutes = prefs.IntervalMinutes, oldSeconds = prefs.RestSeconds;
                        prefs.IntervalMinutes = page.IntervalMinutes; prefs.RestSeconds = page.RestSeconds;
                        if (SavePreferences()) clock.Configure(prefs.IntervalMinutes, prefs.RestSeconds, DateTime.UtcNow);
                        else {
                            prefs.IntervalMinutes = oldMinutes; prefs.RestSeconds = oldSeconds;
                            MessageBox.Show(this, "设置未能保存，请检查磁盘空间或文件权限后重试。", "远眺");
                        }
                    }
                }
            } finally { outsideSince = DateTime.UtcNow; if (!IsDisposed) { Render(); timer.Start(); } }
        }
        void Render() {
            Phase p = clock.Phase;
            number.Text = clock.Display(DateTime.UtcNow);
            title.Text = p == Phase.Waiting ? "该让眼睛休息一下了" : p == Phase.Resting ? "请看约6米外" : p == Phase.Paused ? "提醒已暂停" : "距离下次远眺";
            detail.Text = p == Phase.Waiting ? "准备好后点击开始，再远眺" + clock.RestSeconds + "秒" : p == Phase.Resting ? "放下屏幕，轻松眨眼，保持远眺" : p == Phase.Paused ? "点击继续，接着之前的时间提醒" : "每" + clock.IntervalMinutes + "分钟，看约6米外至少" + clock.RestSeconds + "秒";
            primary.Text = p == Phase.Waiting ? "开始远眺 · " + clock.RestSeconds + "秒" : p == Phase.Resting ? "正在远眺…" : "立即休息";
            settingsButton.Enabled = settingsMenu.Enabled = p != Phase.Resting;
            primary.Enabled = p != Phase.Resting;
            later.Enabled = p != Phase.Paused;
            pause.Text = p == Phase.Paused ? "继续" : "暂停";
            pauseMenu.Text = p == Phase.Paused ? "继续提醒" : "暂停提醒";
            tray.Text = "远眺 · " + (p == Phase.Paused ? "已暂停" : p == Phase.Waiting ? "该远眺了" : number.Text);
        }
        void OnTick(object sender, EventArgs e) {
            DateTime now = DateTime.UtcNow;
            Phase before = clock.Phase; clock.Tick(now);
            if (before != clock.Phase) {
                if (clock.Phase == Phase.Waiting) { Reveal(false); }
                if (before == Phase.Resting) { ReturnToHome(); }
            }
            Render();
            if (Visible && prefs.Dock != "" && !dragging && clock.Phase != Phase.Waiting && clock.Phase != Phase.Resting) {
                if (Bounds.Contains(Cursor.Position) || (ContextMenuStrip != null && ContextMenuStrip.Visible) || tray.ContextMenuStrip.Visible) outsideSince = now;
                else if ((now - outsideSince).TotalMilliseconds > 900) Collapse();
            }
            if (++ticks % 100 == 0 && !dragging && Visible) SavePreferences();
            if (verify) VerifyTick();
        }
        void BeginDrag(object sender, MouseEventArgs e) {
            if (e.Button != MouseButtons.Left) return;
            dragging = true; dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
            ((Control)sender).Capture = true;
        }
        void MoveDrag(object sender, MouseEventArgs e) { if (dragging) Location = new Point(Cursor.Position.X - dragOffset.X, Cursor.Position.Y - dragOffset.Y); }
        void EndDrag(object sender, MouseEventArgs e) {
            if (!dragging) return;
            dragging = false; ((Control)sender).Capture = false;
            Rectangle a = Screen.FromPoint(Cursor.Position).WorkingArea;
            prefs.Dock = Left <= a.Left + S(28) ? "left" : Right >= a.Right - S(28) ? "right" : "";
            ClampToScreen(); SavePreferences(); outsideSince = DateTime.UtcNow;
        }
        void ClampToScreen() {
            Rectangle a = Screen.FromRectangle(Bounds).WorkingArea;
            int x = Math.Max(a.Left, Math.Min(Left, a.Right - Width));
            if (prefs.Dock == "left") x = a.Left;
            if (prefs.Dock == "right") x = a.Right - Width;
            Location = new Point(x, Math.Max(a.Top, Math.Min(Top, a.Bottom - Height)));
        }
        void Collapse() {
            if (prefs.Dock == "" || clock.Phase == Phase.Waiting || clock.Phase == Phase.Resting) return;
            ClampToScreen(); SavePreferences(); Rectangle a = Screen.FromRectangle(Bounds).WorkingArea;
            edge.Location = new Point(prefs.Dock == "left" ? a.Left : a.Right - edge.Width, Math.Max(a.Top, Math.Min(Top + S(25), a.Bottom - edge.Height)));
            Hide(); edge.Show();
        }
        void Reveal(bool activate) {
            edge.Hide(); if (activate) trayOnly = false; ClampToScreen();
            if (activate) { Show(); Activate(); }
            else ShowWindowWithoutFocus();
            outsideSince = DateTime.UtcNow;
        }
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
        void ShowWindowWithoutFocus() { ShowWindow(Handle, 4); Visible = true; }
        void ToTray() { trayOnly = true; edge.Hide(); Hide(); }
        void ReturnToHome() { if (prefs.Dock != "") Collapse(); else if (trayOnly) Hide(); }
        void DisplayChanged(object sender, EventArgs e) { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { ClampToScreen(); if (edge.Visible) Collapse(); }); }
        void Quit() { quitting = true; Close(); }
        void LoadPreferences() {
            try { using (Stream s = File.OpenRead(Path.Combine(folder, "settings.xml"))) prefs = (Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(s); }
            catch { prefs = new Preferences(); }
            if (prefs.Dock != "left" && prefs.Dock != "right") prefs.Dock = "";
            if (prefs.IntervalMinutes < 1 || prefs.IntervalMinutes > 180) prefs.IntervalMinutes = 20;
            if (prefs.RestSeconds < 1 || prefs.RestSeconds > 300) prefs.RestSeconds = 20;
        }
        bool SavePreferences() {
            if (dragging) return false;
            prefs.X = Left; prefs.Y = Top;
            try {
                string target = Path.Combine(folder, "settings.xml"), temp = Path.Combine(folder, "settings.new.xml");
                using (Stream s = File.Create(temp)) new XmlSerializer(typeof(Preferences)).Serialize(s, prefs);
                if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
                return true;
            }
            catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
        }
        bool IsStartupEnabled() {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(StartupKey)) return k != null && Convert.ToString(k.GetValue(StartupName)) == "\"" + Application.ExecutablePath + "\" --startup"; }
            catch { return false; }
        }
        void ChangeStartup() {
            if (verify) return;
            try {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(StartupKey)) {
                    if (startup.Checked) k.SetValue(StartupName, "\"" + Application.ExecutablePath + "\" --startup");
                    else k.DeleteValue(StartupName, false);
                }
            } catch (Exception ex) {
                loading = true; startup.Checked = IsStartupEnabled(); loading = false;
                MessageBox.Show("无法更改开机启动，请稍后重试。\n" + ex.Message, "远眺");
            }
        }
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); verifyReport += "PASS " + message + Environment.NewLine; }
        void Snapshot(string name) {
            using (Bitmap b = new Bitmap(Width, Height)) { DrawToBitmap(b, new Rectangle(0, 0, Width, Height)); b.Save(Path.Combine(folder, name + ".png")); }
        }
        void RunChecks() {
            DateTime n = DateTime.UtcNow;
            Clock c = new Clock(n); c.Tick(n.AddMinutes(20)); Assert(c.Phase == Phase.Waiting, "20-minute reminder waits for consent");
            c.StartRest(n); c.Tick(n.AddSeconds(19)); Assert(c.Phase == Phase.Resting, "rest lasts at least 20 seconds");
            c.Tick(n.AddSeconds(20)); Assert(c.Phase == Phase.Working && c.Due == n.AddSeconds(20).AddMinutes(20), "completion starts a fresh 20-minute cycle");
            c.TogglePause(n); TimeSpan remaining = c.Remaining; c.Tick(n.AddHours(1)); Assert(c.Phase == Phase.Paused, "pause does not expire");
            c.TogglePause(n.AddHours(1)); Assert(c.Due == n.AddHours(1).Add(remaining), "resume preserves remaining duration");
            c.Snooze(n); Assert(c.Due == n.AddMinutes(5), "snooze is five minutes");
            prefs.Dock = "right"; Collapse(); Assert(edge.Visible && !Visible, "right docking hides main window");
            Reveal(false); Assert(Visible && !edge.Visible, "edge reveal restores main window");
            prefs.Dock = "left"; Collapse(); Assert(edge.Visible && edge.Left == Screen.FromRectangle(Bounds).WorkingArea.Left, "left docking uses working area");
            Reveal(false); prefs.Dock = ""; clock.Reset(n); Render(); Snapshot("working");
            ToTray(); clock.Phase = Phase.Waiting; Reveal(false); clock.Reset(n); ReturnToHome();
            Assert(!Visible && trayOnly, "tray reminder returns to tray after completion"); Reveal(true);
            clock.Due = n.AddMilliseconds(-1); clock.Tick(n); Render(); Snapshot("reminder");
            Assert(clock.Phase == Phase.Waiting, "visible reminder awaits start");
            startup.Enabled = false;
            verification = Stopwatch.StartNew();
        }
        void VerifyTick() {
            try {
                if (!verifyRestStarted && verification.Elapsed.TotalSeconds >= 1) {
                    MainAction(); verifyRestStarted = true; Snapshot("resting");
                }
                if (verifyRestStarted && !verifyCompleted && clock.Phase == Phase.Working) {
                    Assert(verification.Elapsed.TotalSeconds >= 21, "real timer completed 20-second countdown"); verifyCompleted = true;
                    Assert(primary.Enabled, "rest completion restores action");
                    TogglePause(); Assert(clock.Phase == Phase.Paused, "pause button action");
                    TogglePause(); Snooze(); Assert(clock.Phase == Phase.Working, "resume and snooze actions");
                    SavePreferences(); Assert(File.Exists(Path.Combine(folder, "settings.xml")), "isolated settings saved");
                    File.WriteAllText(Path.Combine(folder, "result.txt"), verifyReport); Quit();
                }
                if (verification.Elapsed.TotalSeconds > 27) throw new Exception("Verification timeout");
            } catch (Exception ex) { File.WriteAllText(Path.Combine(folder, "result.txt"), verifyReport + "FAIL " + ex); Quit(); }
        }
    }
}
