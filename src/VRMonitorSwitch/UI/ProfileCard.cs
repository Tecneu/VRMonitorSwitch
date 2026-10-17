using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using VRMonitorSwitch.Core;

namespace VRMonitorSwitch.UI
{
    /// <summary>
    /// A large clickable card representing one <see cref="Profile"/>: a glyph drawn in code
    /// (two screens / one ultra-wide screen / one 16:9 screen), the profile name and description,
    /// a resolution <see cref="ComboBox"/> and a big "use this profile" button.
    /// Clicking anywhere on the card (or pressing Enter/Space while focused) raises <see cref="Chosen"/>.
    /// </summary>
    public sealed class ProfileCard : Panel
    {
        // Logical (96 DPI) layout constants.
        private const int PadL = 18;
        private const int GlyphHeightL = 140;
        private const int GlyphMinHeightL = 56;
        private const int GapL = 8;
        private const int ButtonHeightL = 46;
        private const int ComboHeightL = 30;
        private const int BorderRadiusL = 14;

        private readonly Label _nameLabel;
        private readonly Label _descLabel;
        private readonly Label _resolutionLabel;
        private readonly ComboBox _modeCombo;
        private readonly Button _applyButton;

        private bool _hover;
        private bool _interactive = true;
        private int _glyphHeight;

        /// <summary>Raised when the user chooses this profile with the currently selected mode.</summary>
        public event Action<Profile, DisplayMode> Chosen;

        /// <summary>The profile this card represents.</summary>
        public Profile Profile { get; }

        public ProfileCard(Profile profile)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
            Font = Theme.FontBody;

            _nameLabel = new Label
            {
                Text = Strings.Get("profile." + profile.Id + ".name"),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            Theme.StyleLabel(_nameLabel, Theme.FontSubtitleBold);

            _descLabel = new Label
            {
                Text = Strings.Get("profile." + profile.Id + ".desc"),
                AutoSize = false,
                TextAlign = ContentAlignment.TopLeft,
                Cursor = Cursors.Hand
            };
            Theme.StyleLabel(_descLabel, Theme.FontBody, muted: true);

            _resolutionLabel = new Label
            {
                Text = Strings.Get("label.resolution"),
                AutoSize = false,
                TextAlign = ContentAlignment.BottomLeft,
                Cursor = Cursors.Hand
            };
            Theme.StyleLabel(_resolutionLabel, Theme.FontBody, muted: true);

            _modeCombo = new ComboBox { TabStop = true };
            Theme.StyleComboBox(_modeCombo);
            _modeCombo.PreviewKeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) e.IsInputKey = true; };
            _modeCombo.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !_modeCombo.DroppedDown)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    RaiseChosen();
                }
            };

            _applyButton = new Button { Text = Strings.Get("btn.apply"), TabStop = true };
            Theme.StyleButton(_applyButton, primary: true);
            _applyButton.Click += (s, e) => RaiseChosen();

            Controls.Add(_nameLabel);
            Controls.Add(_descLabel);
            Controls.Add(_resolutionLabel);
            Controls.Add(_modeCombo);
            Controls.Add(_applyButton);

            // Whole card is clickable: labels forward clicks, every child participates in hover tracking.
            // The "Resolution" caption sits 4 px above the combo box, so a click there is an "adjust" gesture:
            // it opens the drop-down instead of applying the profile.
            foreach (Control c in Controls)
            {
                c.MouseEnter += Child_MouseEnter;
                c.MouseLeave += Child_MouseLeave;
            }
            _nameLabel.MouseClick += Child_MouseClick;
            _descLabel.MouseClick += Child_MouseClick;
            _resolutionLabel.MouseClick += ResolutionLabel_MouseClick;

            SetModes(profile.Modes, profile.DefaultMode);
            ApplySurfaceColor();
        }

        /// <summary>The mode currently selected in the combo box (never null once modes are set).</summary>
        public DisplayMode SelectedMode
        {
            get
            {
                var m = _modeCombo.SelectedItem as DisplayMode;
                if (m != null) return m;
                if (_modeCombo.Items.Count > 0) return _modeCombo.Items[0] as DisplayMode;
                return Profile.DefaultMode;
            }
        }

        /// <summary>
        /// Whether the card reacts to clicks and keyboard. Set to false while a session is being applied.
        /// </summary>
        public bool Interactive
        {
            get { return _interactive; }
            set
            {
                if (_interactive == value) return;
                _interactive = value;
                _modeCombo.Enabled = value;
                _applyButton.Enabled = value;
                Cursor = value ? Cursors.Hand : Cursors.Default;
                foreach (Control c in Controls)
                {
                    if (c is Label) c.Cursor = Cursor;
                }
                if (!value) _hover = false;
                ApplySurfaceColor();
                Invalidate();
            }
        }

        /// <summary>Replaces the resolution list; <paramref name="preselect"/> is selected when present (added if missing).</summary>
        public void SetModes(IEnumerable<DisplayMode> modes, DisplayMode preselect)
        {
            var list = new List<DisplayMode>();
            if (modes != null)
            {
                foreach (var m in modes)
                {
                    if (m != null && !list.Contains(m)) list.Add(m);
                }
            }
            if (preselect != null && !list.Contains(preselect)) list.Add(preselect);
            if (list.Count == 0 && Profile.DefaultMode != null) list.Add(Profile.DefaultMode);

            _modeCombo.BeginUpdate();
            try
            {
                _modeCombo.Items.Clear();
                foreach (var m in list) _modeCombo.Items.Add(m);
                int idx = preselect != null ? list.IndexOf(preselect) : -1;
                if (idx < 0 && Profile.DefaultMode != null) idx = list.IndexOf(Profile.DefaultMode);
                if (idx < 0 && list.Count > 0) idx = 0;
                _modeCombo.SelectedIndex = idx;
            }
            finally
            {
                _modeCombo.EndUpdate();
            }
        }

        private void RaiseChosen()
        {
            if (!_interactive) return;
            var mode = SelectedMode;
            if (mode == null)
            {
                Log.Warn("ProfileCard: no mode selected for profile " + Profile.Id);
                return;
            }
            var handler = Chosen;
            if (handler == null) return;
            try
            {
                handler(Profile, mode);
            }
            catch (Exception ex)
            {
                Log.Error("ProfileCard.Chosen handler failed", ex);
            }
        }

        // ---- mouse / keyboard -------------------------------------------------------------

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            SetHover(true);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!ClientRectangle.Contains(PointToClient(MousePosition))) SetHover(false);
        }

        private void Child_MouseEnter(object sender, EventArgs e)
        {
            SetHover(true);
        }

        private void Child_MouseLeave(object sender, EventArgs e)
        {
            if (!ClientRectangle.Contains(PointToClient(MousePosition))) SetHover(false);
        }

        private void Child_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Focus();
            RaiseChosen();
        }

        private void ResolutionLabel_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !_interactive) return;
            try
            {
                _modeCombo.Focus();
                _modeCombo.DroppedDown = true;
            }
            catch (Exception ex)
            {
                Log.Warn("ProfileCard: could not open the resolution list: " + ex.Message);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            RaiseChosen();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Enter || keyData == Keys.Space) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                RaiseChosen();
            }
        }

        private void SetHover(bool hover)
        {
            if (!_interactive) hover = false;
            if (_hover == hover) return;
            _hover = hover;
            ApplySurfaceColor();
            Invalidate();
        }

        private Color CurrentSurface
        {
            get { return _hover ? Theme.SurfaceHover : Theme.Surface; }
        }

        private void ApplySurfaceColor()
        {
            // Labels are opaque with the card surface color; this avoids WinForms' simulated
            // transparency (which does not replay the owner-drawn rounded background reliably).
            var c = CurrentSurface;
            _nameLabel.BackColor = c;
            _descLabel.BackColor = c;
            _resolutionLabel.BackColor = c;
            _nameLabel.ForeColor = _interactive ? Theme.Text : Theme.Muted;
        }

        // ---- layout -----------------------------------------------------------------------

        private int S(int logical)
        {
            return Theme.Scale(this, logical);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutChildren();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            PerformLayout();
            Invalidate();
        }

        private void LayoutChildren()
        {
            // Layout can be triggered from the base constructor (e.g. setting Font) before the children exist.
            if (_applyButton == null || _modeCombo == null || _nameLabel == null || _descLabel == null || _resolutionLabel == null) return;
            int pad = S(PadL);
            int gap = S(GapL);
            int inner = Math.Max(S(60), ClientSize.Width - pad * 2);
            int x = pad;

            // Bottom-up: button, combo, resolution label. Top-down: glyph, name, description.
            int buttonH = S(ButtonHeightL);
            int comboH = Math.Max(S(ComboHeightL), _modeCombo.PreferredHeight);
            int resLabelH = TextRenderer.MeasureText("Ag", _resolutionLabel.Font).Height + S(2);

            int bottom = ClientSize.Height - pad;
            _applyButton.SetBounds(x, bottom - buttonH, inner, buttonH);
            int comboY = bottom - buttonH - gap - comboH;
            _modeCombo.SetBounds(x, comboY, inner, comboH);
            int resY = comboY - S(4) - resLabelH;
            _resolutionLabel.SetBounds(x, resY, inner, resLabelH);

            int nameH = TextRenderer.MeasureText("Ag", _nameLabel.Font).Height + S(4);
            int descNeeded = 0;
            try
            {
                descNeeded = TextRenderer.MeasureText(_descLabel.Text ?? string.Empty, _descLabel.Font,
                    new Size(inner, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + S(2);
            }
            catch (Exception ex)
            {
                Log.Warn("ProfileCard.LayoutChildren: MeasureText failed: " + ex.Message);
            }

            // The glyph shrinks (down to a minimum) before the description does, so the text stays readable
            // on small windows.
            int fixedTop = pad + gap + nameH + S(2);
            int remaining = resY - gap - fixedTop;
            int glyphH = S(GlyphHeightL);
            int glyphMin = S(GlyphMinHeightL);
            if (remaining - glyphH < descNeeded) glyphH = Math.Max(glyphMin, remaining - descNeeded);
            _glyphHeight = Math.Max(0, glyphH);

            int glyphBottom = pad + _glyphHeight;
            int nameY = glyphBottom + gap;
            _nameLabel.SetBounds(x, nameY, inner, nameH);

            int descY = nameY + nameH + S(2);
            int descAvail = Math.Max(0, resY - gap - descY);
            _descLabel.SetBounds(x, descY, inner, Math.Min(descAvail, Math.Max(0, descNeeded)));
            _descLabel.AutoEllipsis = descNeeded > descAvail;
        }

        // ---- painting ---------------------------------------------------------------------

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Parent background (the card is drawn rounded on top of it).
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Background))
            {
                e.Graphics.FillRectangle(b, ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            Color border = Focused ? Theme.Accent : (_hover ? Theme.AccentHover : Theme.Border);
            int borderWidth = Focused || _hover ? 2 : 1;
            Theme.FillRounded(g, rect, S(BorderRadiusL), CurrentSurface, border, borderWidth);

            int pad = S(PadL);
            int glyphH = _glyphHeight > 0 ? _glyphHeight : S(GlyphHeightL);
            var glyphArea = new Rectangle(pad, pad, Math.Max(1, ClientSize.Width - pad * 2), glyphH);
            DrawGlyph(g, glyphArea);
        }

        /// <summary>Draws the profile glyph: two 16:9 screens, one 32:9 screen or one 16:9 screen.</summary>
        private void DrawGlyph(Graphics g, Rectangle area)
        {
            if (area.Width <= 4 || area.Height <= 4) return;

            Color fill = _interactive ? (_hover ? Theme.AccentHover : Theme.Accent) : Theme.Muted;
            Color screenBorder = Color.FromArgb(200, Theme.Text);
            int standH = Math.Max(2, area.Height / 10);
            int usableH = area.Height - standH - S(4);
            int radius = Math.Max(2, S(4));

            var screens = new List<Rectangle>();
            switch (Profile.Kind)
            {
                case ProfileKind.DualStandard:
                {
                    int gap = Math.Max(4, area.Width / 20);
                    int w = (area.Width - gap) / 2;
                    int h = w * 9 / 16;
                    if (h > usableH) { h = usableH; w = h * 16 / 9; }
                    int totalW = w * 2 + gap;
                    int x0 = area.Left + (area.Width - totalW) / 2;
                    int y0 = area.Top + (usableH - h) / 2;
                    screens.Add(new Rectangle(x0, y0, w, h));
                    screens.Add(new Rectangle(x0 + w + gap, y0, w, h));
                    break;
                }
                case ProfileKind.UltraWide:
                {
                    int w = area.Width;
                    int h = w * 9 / 32;
                    if (h > usableH) { h = usableH; w = h * 32 / 9; }
                    int x0 = area.Left + (area.Width - w) / 2;
                    int y0 = area.Top + (usableH - h) / 2;
                    screens.Add(new Rectangle(x0, y0, w, h));
                    break;
                }
                default:
                {
                    // Same visual weight as the other cards: a bit larger than one of the dual screens.
                    int w = area.Width * 3 / 5;
                    int h = w * 9 / 16;
                    if (h > usableH) { h = usableH; w = h * 16 / 9; }
                    int x0 = area.Left + (area.Width - w) / 2;
                    int y0 = area.Top + (usableH - h) / 2;
                    screens.Add(new Rectangle(x0, y0, w, h));
                    break;
                }
            }

            foreach (var s in screens)
            {
                // Screen body.
                Theme.FillRounded(g, s, radius, fill, screenBorder, 1);
                // Subtle inner "glass" highlight.
                var inner = Rectangle.Inflate(s, -Math.Max(2, s.Width / 14), -Math.Max(2, s.Height / 8));
                if (inner.Width > 2 && inner.Height > 2)
                {
                    using (var hb = new SolidBrush(Color.FromArgb(45, Theme.Text)))
                    using (var hp = Theme.RoundedRect(inner, Math.Max(1, radius / 2)))
                    {
                        g.FillPath(hb, hp);
                    }
                }
                // Stand: neck + base.
                int neckW = Math.Max(3, s.Width / 12);
                int neckH = Math.Max(2, standH / 2);
                int baseW = Math.Max(neckW * 3, s.Width / 3);
                int baseH = Math.Max(2, standH - neckH);
                int cx = s.Left + s.Width / 2;
                using (var sb = new SolidBrush(Theme.Muted))
                {
                    g.FillRectangle(sb, cx - neckW / 2, s.Bottom + 1, neckW, neckH);
                    using (var bp = Theme.RoundedRect(new Rectangle(cx - baseW / 2, s.Bottom + 1 + neckH, baseW, baseH), Math.Max(1, baseH / 2)))
                    {
                        g.FillPath(sb, bp);
                    }
                }
            }

            // Monitor count badge in the bottom-right corner of the glyph area.
            string badge = Profile.MonitorCount.ToString();
            var badgeSize = TextRenderer.MeasureText(badge, Theme.FontBodyBold);
            int bd = Math.Max(badgeSize.Height + S(4), badgeSize.Width + S(8));
            var badgeRect = new Rectangle(area.Right - bd, area.Bottom - bd, bd, bd);
            using (var bb = new SolidBrush(Theme.Background))
            {
                g.FillEllipse(bb, badgeRect);
            }
            using (var bp = new Pen(Theme.Border))
            {
                g.DrawEllipse(bp, badgeRect);
            }
            TextRenderer.DrawText(g, badge, Theme.FontBodyBold, badgeRect, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Chosen = null;
            }
            base.Dispose(disposing);
        }
    }
}
