using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EyeRest {
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
            BackColor = Color.FromArgb(18, 18, 20); ForeColor = Color.White;
            TabStop = true; Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.SpinButton;
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float row = RowHeight;
            using (Brush b = new SolidBrush(Color.FromArgb(44, 44, 46))) e.Graphics.FillRectangle(b, 0, row * 2, Width, row);
            using (Font f = new Font("Segoe UI", 28, FontStyle.Regular))
            using (Font small = new Font("Microsoft YaHei UI", 10))
            using (StringFormat fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) {
                for (int i = -3; i <= 3; i++) {
                    int n = value + i; if (n < 1 || n > maximum) continue;
                    float y = row * (i + 2) + offset;
                    float distance = Math.Abs((y + row / 2) - Height / 2f) / row;
                    Color color = distance < .45 ? Color.White : distance < 1.5 ? Color.FromArgb(152, 152, 158) : Color.FromArgb(92, 92, 99);
                    using (Brush b = new SolidBrush(color)) e.Graphics.DrawString(n.ToString("00"), f, b, new RectangleF(0, y, Width * .73f, row), fmt);
                }
                using (Brush b = new SolidBrush(Color.White)) e.Graphics.DrawString(unit, small, b, new RectangleF(Width * .68f, row * 2, Width * .3f, row), fmt);
            }
            if (Focused) { using (Pen p = new Pen(Color.FromArgb(255, 159, 10))) e.Graphics.DrawRectangle(p, 1, row * 2 + 1, Width - 3, row - 3); }
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
        public int IntervalMinutes { get { return interval.Value; } }
        public int RestSeconds { get { return rest.Value; } }
        int S(int n) { return (int)Math.Round(n * scale); }
        public TimeSettings(int minutes, int seconds, double dpiScale) {
            scale = dpiScale; AutoScaleMode = AutoScaleMode.None;
            Text = "时间设置"; FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(18, 18, 20); ForeColor = Color.White;
            ClientSize = new Size(S(352), S(424)); ShowInTaskbar = false;
            TopMost = true; StartPosition = FormStartPosition.Manual;
            Font = new Font("Microsoft YaHei UI", 10);
            Button cancel = Nav("取消", 12, DialogResult.Cancel);
            Button done = Nav("完成", 276, DialogResult.OK);
            AcceptButton = done; CancelButton = cancel;
            Label heading = TextAt("时间设置", 98, 16, 156, 34, 12, Color.White);
            heading.Font = new Font(Font, FontStyle.Bold);
            heading.Cursor = Cursors.SizeAll;
            heading.MouseDown += delegate(object sender, MouseEventArgs e) { dragging = true; dragPoint = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top); heading.Capture = true; };
            heading.MouseMove += delegate { if (dragging) Location = new Point(Cursor.Position.X - dragPoint.X, Cursor.Position.Y - dragPoint.Y); };
            heading.MouseUp += delegate { dragging = false; heading.Capture = false; };
            Color secondary = Color.FromArgb(174, 174, 182);
            TextAt("提醒间隔", 22, 78, 150, 28, 11, secondary);
            TextAt("休息时长", 180, 78, 150, 28, 11, secondary);
            interval = new TimeWheel(180, minutes, "分钟") { Location = new Point(S(22), S(114)), Size = new Size(S(150), S(240)), AccessibleName = "提醒间隔，1至180分钟" };
            rest = new TimeWheel(300, seconds, "秒") { Location = new Point(S(180), S(114)), Size = new Size(S(150), S(240)), AccessibleName = "休息时长，1至300秒" };
            Controls.Add(interval); Controls.Add(rest);
            TextAt("滚动或拖动数字 · 方向键微调", 16, 365, 320, 22, 9, secondary);
            TextAt("完成后保存，并重新计时", 16, 390, 320, 20, 9, secondary);
        }
        Button Nav(string text, int x, DialogResult result) {
            Button b = new Button { Text = text, DialogResult = result, FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(255, 159, 10), BackColor = BackColor,
                Location = new Point(S(x), S(14)), Size = new Size(S(64), S(38)), Cursor = Cursors.Hand, Font = new Font("Microsoft YaHei UI", 11) };
            b.FlatAppearance.BorderSize = 0; Controls.Add(b); return b;
        }
        Label TextAt(string text, int x, int y, int w, int h, int size, Color color) {
            Label l = new Label { Text = text, Location = new Point(S(x), S(y)), Size = new Size(S(w), S(h)), ForeColor = color,
                Font = new Font("Microsoft YaHei UI", size), TextAlign = ContentAlignment.MiddleCenter };
            Controls.Add(l); return l;
        }
    }
}
