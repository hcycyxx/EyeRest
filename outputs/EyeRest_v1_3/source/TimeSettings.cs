using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EyeRest {
    internal static class Theme {
        public static readonly Color Surface = Color.FromArgb(248, 250, 247);
        public static readonly Color Ink = Color.FromArgb(25, 53, 46);
        public static readonly Color Muted = Color.FromArgb(79, 101, 94);
        public static readonly Color Green = Color.FromArgb(30, 91, 75);
        public static readonly Color Selection = Color.FromArgb(226, 237, 226);
        public static readonly Color Distant = Color.FromArgb(105, 125, 114);
    }
    // Native, locally painted wheel: no browser, packages or network requests.
    public sealed class TimeWheel : Control {
        readonly int maximum;
        readonly string unit;
        int value, originValue, originY, wheelDelta;
        bool dragging;
        float offset;
        public int Value { get { return value; } set { this.value = Math.Max(1, Math.Min(maximum, value)); Invalidate(); } }
        float RowHeight { get { return Height / 5f; } }
        public TimeWheel(int max, int initial, string suffix) {
            maximum = max; value = initial; unit = suffix;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            BackColor = Theme.Surface; ForeColor = Theme.Ink;
            TabStop = true; Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.SpinButton;
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float row = RowHeight;
            using (Brush b = new SolidBrush(Theme.Selection)) e.Graphics.FillRectangle(b, 0, row * 2, Width, row);
            using (Font f = new Font("Segoe UI", 28, FontStyle.Regular))
            using (Font small = new Font("Microsoft YaHei UI", 10))
            using (StringFormat fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) {
                for (int i = -3; i <= 3; i++) {
                    int n = value + i; if (n < 1 || n > maximum) continue;
                    float y = row * (i + 2) + offset;
                    float distance = Math.Abs((y + row / 2) - Height / 2f) / row;
                    Color color = distance < .45 ? Theme.Green : distance < 1.5 ? Theme.Muted : Theme.Distant;
                    using (Brush b = new SolidBrush(color)) e.Graphics.DrawString(n.ToString("00"), f, b, new RectangleF(0, y, Width * .73f, row), fmt);
                }
                using (Brush b = new SolidBrush(Theme.Green)) e.Graphics.DrawString(unit, small, b, new RectangleF(Width * .68f, row * 2, Width * .3f, row), fmt);
            }
            if (Focused) { using (Pen p = new Pen(Theme.Green)) e.Graphics.DrawRectangle(p, 1, row * 2 + 1, Width - 3, row - 3); }
        }
        protected override void OnMouseWheel(MouseEventArgs e) {
            base.OnMouseWheel(e); wheelDelta += e.Delta;
            int steps = wheelDelta / SystemInformation.MouseWheelScrollDelta;
            wheelDelta %= SystemInformation.MouseWheelScrollDelta;
            Value -= steps;
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); if (FindForm().ContainsFocus) Focus(); }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            Focus(); Capture = true; dragging = true; originY = e.Y; originValue = value;
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e); if (!dragging) return;
            float rows = (originY - e.Y) / RowHeight;
            int steps = (int)Math.Round(rows);
            Value = originValue + steps;
            offset = (value == 1 || value == maximum) ? 0 : (steps - rows) * RowHeight;
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e); if (!dragging) return;
            if (Math.Abs(e.Y - originY) < 4) Value += (int)Math.Floor(e.Y / RowHeight) - 2;
            dragging = false; offset = 0; Capture = false; Invalidate();
        }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) { dragging = false; offset = 0; Invalidate(); } }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Up) Value--;
            else if (e.KeyCode == Keys.Down) Value++;
            else if (e.KeyCode == Keys.PageUp) Value -= 10;
            else if (e.KeyCode == Keys.PageDown) Value += 10;
            else if (e.KeyCode == Keys.Home) Value = 1;
            else if (e.KeyCode == Keys.End) Value = maximum;
            else return;
            e.Handled = true; e.SuppressKeyPress = true;
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }
    public sealed class TimeSettings : Form {
        readonly double scale;
        readonly TimeWheel interval, rest;
        Point dragPoint;
        bool dragging;
        readonly Timer transition = new Timer { Interval = 15 };
        readonly Stopwatch elapsed = new Stopwatch();
        readonly bool animate = SystemInformation.IsMenuAnimationEnabled && !SystemInformation.TerminalServerSession;
        Point targetLocation, fromLocation;
        double fromOpacity;
        bool opening, closing, allowClose;
        DialogResult pendingResult = DialogResult.Cancel;
        public int IntervalMinutes { get { return interval.Value; } }
        public int RestSeconds { get { return rest.Value; } }
        int S(int n) { return (int)Math.Round(n * scale); }
        public TimeSettings(int minutes, int seconds, double dpiScale) {
            scale = dpiScale; AutoScaleMode = AutoScaleMode.None;
            Text = "时间设置"; FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Surface; ForeColor = Theme.Ink;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            if (animate) Opacity = 0;
            transition.Tick += AnimateFrame;
            ClientSize = new Size(S(352), S(424)); ShowInTaskbar = false;
            TopMost = true; StartPosition = FormStartPosition.Manual;
            Font = new Font("Microsoft YaHei UI", 10);
            Button cancel = Nav("取消", 12, DialogResult.Cancel);
            Button done = Nav("完成", 276, DialogResult.OK);
            AcceptButton = done; CancelButton = cancel;
            // Escape uses our click handler; defer the modal result until fade-out ends.
            cancel.DialogResult = DialogResult.None;
            Label heading = TextAt("时间设置", 98, 16, 156, 34, 12, Theme.Ink);
            heading.Font = new Font(Font, FontStyle.Bold);
            heading.Cursor = Cursors.SizeAll;
            heading.MouseDown += delegate(object sender, MouseEventArgs e) { if (opening || closing || e.Button != MouseButtons.Left) return; dragging = true; dragPoint = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top); heading.Capture = true; };
            heading.MouseMove += delegate { if (dragging) Location = new Point(Cursor.Position.X - dragPoint.X, Cursor.Position.Y - dragPoint.Y); };
            heading.MouseUp += delegate { dragging = false; heading.Capture = false; };
            Color secondary = Theme.Muted;
            TextAt("提醒间隔", 22, 78, 150, 28, 11, secondary);
            TextAt("休息时长", 180, 78, 150, 28, 11, secondary);
            interval = new TimeWheel(180, minutes, "分钟") { Location = new Point(S(22), S(114)), Size = new Size(S(150), S(240)), AccessibleName = "提醒间隔，1至180分钟" };
            rest = new TimeWheel(300, seconds, "秒") { Location = new Point(S(180), S(114)), Size = new Size(S(150), S(240)), AccessibleName = "休息时长，1至300秒" };
            Controls.Add(interval); Controls.Add(rest);
            TextAt("滚动或拖动数字 · 方向键微调", 16, 365, 320, 22, 9, secondary);
            TextAt("完成后保存，并重新计时", 16, 390, 320, 20, 9, secondary);
        }
        Button Nav(string text, int x, DialogResult result) {
            Button b = new Button { Text = text, FlatStyle = FlatStyle.Flat, ForeColor = Theme.Green, BackColor = BackColor,
                Location = new Point(S(x), S(14)), Size = new Size(S(64), S(38)), Cursor = Cursors.Hand, Font = new Font("Microsoft YaHei UI", 11) };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Theme.Selection;
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(210, 226, 214);
            b.Click += delegate { BeginClose(result); };
            Controls.Add(b); return b;
        }
        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            targetLocation = Location;
            if (!animate) { Opacity = 1; return; }
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            fromLocation = new Point(Left, Math.Min(Top + S(10), Math.Max(area.Top, area.Bottom - Height)));
            Location = fromLocation; fromOpacity = 0;
            opening = true; elapsed.Restart(); transition.Start();
        }
        void BeginClose(DialogResult result) {
            if (closing || allowClose) return;
            pendingResult = result;
            if (!animate) { allowClose = true; DialogResult = result; Close(); return; }
            transition.Stop(); opening = false; closing = true;
            dragging = false;
            fromLocation = Location; fromOpacity = Opacity;
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            targetLocation = new Point(Left, Math.Min(Top + S(6), Math.Max(area.Top, area.Bottom - Height)));
            // Prevent duplicate save/cancel requests while the closing frames finish.
            interval.Enabled = rest.Enabled = false;
            elapsed.Restart(); transition.Start();
        }
        void AnimateFrame(object sender, EventArgs e) {
            double t = Math.Min(1, elapsed.Elapsed.TotalMilliseconds / (opening ? 180.0 : 120.0));
            double eased = 1 - Math.Pow(1 - t, 3);
            Location = new Point((int)Math.Round(fromLocation.X + (targetLocation.X - fromLocation.X) * eased),
                (int)Math.Round(fromLocation.Y + (targetLocation.Y - fromLocation.Y) * eased));
            Opacity = opening ? eased : fromOpacity * (1 - eased);
            if (t < 1) return;
            transition.Stop(); elapsed.Stop();
            if (opening) { opening = false; Location = targetLocation; Opacity = 1; }
            else { allowClose = true; DialogResult = pendingResult; Close(); }
        }
        protected override void OnFormClosing(FormClosingEventArgs e) {
            if (!allowClose && animate && e.CloseReason == CloseReason.UserClosing) {
                DialogResult requested = DialogResult == DialogResult.OK ? DialogResult.OK : DialogResult.Cancel;
                e.Cancel = true; DialogResult = DialogResult.None;
                BeginClose(requested);
            }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing) {
            if (disposing) { transition.Stop(); transition.Dispose(); elapsed.Stop(); }
            base.Dispose(disposing);
        }
        Label TextAt(string text, int x, int y, int w, int h, int size, Color color) {
            Label l = new Label { Text = text, Location = new Point(S(x), S(y)), Size = new Size(S(w), S(h)), ForeColor = color,
                Font = new Font("Microsoft YaHei UI", size), TextAlign = ContentAlignment.MiddleCenter };
            Controls.Add(l); return l;
        }
    }
}
