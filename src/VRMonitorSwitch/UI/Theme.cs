using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VRMonitorSwitch.UI
{
    /// <summary>
    /// Dark flat theme shared by every window and control of the app.
    /// Colors are fixed (the app is dark-only); fonts are expressed in points so they
    /// scale with the monitor DPI automatically.
    /// </summary>
    public static class Theme
    {
        public static readonly Color Background = ColorTranslator.FromHtml("#1E1F24");
        public static readonly Color Surface = ColorTranslator.FromHtml("#2A2C33");
        public static readonly Color SurfaceHover = ColorTranslator.FromHtml("#33363F");
        public static readonly Color Border = ColorTranslator.FromHtml("#3A3D47");
        public static readonly Color Accent = ColorTranslator.FromHtml("#7C5CFF");
        public static readonly Color AccentHover = ColorTranslator.FromHtml("#9B82FF");
        public static readonly Color AccentPressed = ColorTranslator.FromHtml("#6A4CE6");
        public static readonly Color Text = ColorTranslator.FromHtml("#F2F3F7");
        public static readonly Color Muted = ColorTranslator.FromHtml("#A0A4B0");
        public static readonly Color Danger = ColorTranslator.FromHtml("#FF5C5C");
        public static readonly Color DangerSurface = ColorTranslator.FromHtml("#3A2A2E");
        public static readonly Color Success = ColorTranslator.FromHtml("#5CD68A");

        public const string FontFamily = "Segoe UI";

        /// <summary>Body text, 10 pt.</summary>
        public static readonly Font FontBody = new Font(FontFamily, 10f, FontStyle.Regular, GraphicsUnit.Point);
        /// <summary>Emphasised body text, 10 pt bold.</summary>
        public static readonly Font FontBodyBold = new Font(FontFamily, 10f, FontStyle.Bold, GraphicsUnit.Point);
        /// <summary>Subtitles and card names, 12 pt.</summary>
        public static readonly Font FontSubtitle = new Font(FontFamily, 12f, FontStyle.Regular, GraphicsUnit.Point);
        /// <summary>Subtitles and card names, 12 pt bold.</summary>
        public static readonly Font FontSubtitleBold = new Font(FontFamily, 12f, FontStyle.Bold, GraphicsUnit.Point);
        /// <summary>Window / step titles, 18 pt.</summary>
        public static readonly Font FontTitle = new Font(FontFamily, 18f, FontStyle.Bold, GraphicsUnit.Point);

        /// <summary>Default corner radius (in logical pixels at 96 DPI).</summary>
        public const int CornerRadius = 10;

        /// <summary>Scales a 96-DPI logical pixel value to the DPI of <paramref name="control"/>.</summary>
        public static int Scale(Control control, int logicalPixels)
        {
            int dpi = 96;
            try
            {
                if (control != null) dpi = control.DeviceDpi;
            }
            catch (Exception)
            {
                dpi = 96;
            }
            if (dpi <= 0) dpi = 96;
            return (int)Math.Round(logicalPixels * dpi / 96.0);
        }

        /// <summary>Applies the dark background to a form and sets up DPI auto-scaling.</summary>
        public static void StyleForm(Form form)
        {
            if (form == null) return;
            form.BackColor = Background;
            form.ForeColor = Text;
            form.Font = FontBody;
            form.AutoScaleMode = AutoScaleMode.Dpi;
        }

        /// <summary>Gives a container the surface color and text color.</summary>
        public static void StyleSurface(Control control)
        {
            if (control == null) return;
            control.BackColor = Surface;
            control.ForeColor = Text;
        }

        /// <summary>Styles a label with the body font and theme text color.</summary>
        public static void StyleLabel(Label label, Font font = null, bool muted = false)
        {
            if (label == null) return;
            label.Font = font ?? FontBody;
            label.ForeColor = muted ? Muted : Text;
            label.BackColor = Color.Transparent;
            label.UseMnemonic = false;
        }

        /// <summary>
        /// Makes a button flat with rounded corners. Primary buttons use the accent color,
        /// secondary buttons use the surface color with a subtle border.
        /// </summary>
        public static void StyleButton(Button button, bool primary, bool danger = false)
        {
            if (button == null) return;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary || danger ? 0 : 1;
            button.FlatAppearance.BorderColor = Border;
            button.UseVisualStyleBackColor = false;
            button.Font = primary ? FontBodyBold : FontBody;
            button.Cursor = Cursors.Hand;
            button.TabStop = true;
            button.UseMnemonic = false;

            Color back = primary ? Accent : (danger ? DangerSurface : Surface);
            Color hover = primary ? AccentHover : (danger ? Danger : SurfaceHover);
            Color down = primary ? AccentPressed : (danger ? Danger : Border);

            button.BackColor = back;
            button.ForeColor = Text;
            button.FlatAppearance.MouseOverBackColor = hover;
            button.FlatAppearance.MouseDownBackColor = down;

            // Flat buttons keep their BackColor when disabled; mute them explicitly.
            button.EnabledChanged += (s, e) =>
            {
                button.BackColor = button.Enabled ? back : Border;
                button.ForeColor = button.Enabled ? Text : Muted;
            };

            ApplyRoundedRegion(button, CornerRadius);
        }

        /// <summary>Styles a combo box for the dark theme (flat, owner-drawn items so the list is dark too).</summary>
        public static void StyleComboBox(ComboBox combo)
        {
            if (combo == null) return;
            combo.FlatStyle = FlatStyle.Flat;
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.BackColor = Background;
            combo.ForeColor = Text;
            combo.Font = FontBody;
            combo.Cursor = Cursors.Hand;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = Math.Max(combo.ItemHeight, TextRenderer.MeasureText("Ag", FontBody).Height + Scale(combo, 8));
            combo.DrawItem -= Combo_DrawItem;
            combo.DrawItem += Combo_DrawItem;
        }

        private static void Combo_DrawItem(object sender, DrawItemEventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null) return;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool inList = (e.State & DrawItemState.ComboBoxEdit) != DrawItemState.ComboBoxEdit;
            Color back = selected && inList ? Accent : Background;
            using (var b = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(b, e.Bounds);
            }
            if (e.Index >= 0 && e.Index < combo.Items.Count)
            {
                string text = combo.GetItemText(combo.Items[e.Index]);
                var textBounds = new Rectangle(e.Bounds.X + Scale(combo, 6), e.Bounds.Y, Math.Max(1, e.Bounds.Width - Scale(combo, 8)), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, text, e.Font ?? combo.Font, textBounds, combo.Enabled ? Text : Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            if (selected && !inList && combo.Focused)
            {
                using (var pen = new Pen(Accent))
                {
                    var r = e.Bounds;
                    r.Width -= 1; r.Height -= 1;
                    e.Graphics.DrawRectangle(pen, r);
                }
            }
        }

        /// <summary>Styles a checkbox for the dark theme.</summary>
        public static void StyleCheckBox(CheckBox box)
        {
            if (box == null) return;
            box.FlatStyle = FlatStyle.Flat;
            box.ForeColor = Muted;
            box.BackColor = Color.Transparent;
            box.Font = FontBody;
            box.Cursor = Cursors.Hand;
            box.UseMnemonic = false;
        }

        /// <summary>Styles a link-looking label (used for "Volver").</summary>
        public static void StyleLink(LinkLabel link)
        {
            if (link == null) return;
            link.Font = FontBody;
            link.LinkColor = Accent;
            link.ActiveLinkColor = AccentHover;
            link.VisitedLinkColor = Accent;
            link.LinkBehavior = LinkBehavior.HoverUnderline;
            link.BackColor = Color.Transparent;
            link.UseMnemonic = false;
        }

        /// <summary>Builds a rounded rectangle path. The caller owns the returned path.</summary>
        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                path.AddRectangle(new Rectangle(0, 0, 1, 1));
                return path;
            }
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            if (d <= 1)
            {
                path.AddRectangle(bounds);
                return path;
            }
            var arc = new Rectangle(bounds.Location, new Size(d, d));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - d;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - d;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Clips a control to a rounded rectangle and keeps the region updated when it resizes.
        /// The radius is in logical pixels and is scaled to the control DPI.
        /// </summary>
        public static void ApplyRoundedRegion(Control control, int radius)
        {
            if (control == null) return;
            UpdateRegion(control, radius);
            control.SizeChanged += (s, e) => UpdateRegion(control, radius);
        }

        private static void UpdateRegion(Control control, int radius)
        {
            if (control == null || control.IsDisposed) return;
            if (control.Width <= 0 || control.Height <= 0) return;
            try
            {
                int r = Scale(control, radius);
                using (var path = RoundedRect(new Rectangle(0, 0, control.Width, control.Height), r))
                {
                    var old = control.Region;
                    control.Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            }
            catch (Exception ex)
            {
                VRMonitorSwitch.Core.Log.Warn("Theme.UpdateRegion failed: " + ex.Message);
            }
        }

        /// <summary>Creates a renderer that paints context menus in the dark palette.</summary>
        public static ToolStripRenderer CreateMenuRenderer()
        {
            return new DarkMenuRenderer();
        }

        /// <summary>Applies the dark renderer and text color to a context menu and its items.</summary>
        public static void StyleMenu(ContextMenuStrip menu)
        {
            if (menu == null) return;
            menu.Renderer = CreateMenuRenderer();
            menu.BackColor = Surface;
            menu.ForeColor = Text;
            menu.Font = FontBody;
            menu.ShowImageMargin = false;
            foreach (ToolStripItem item in menu.Items)
            {
                item.ForeColor = Text;
                item.BackColor = Surface;
            }
        }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color MenuItemSelected => SurfaceHover;
            public override Color MenuItemSelectedGradientBegin => SurfaceHover;
            public override Color MenuItemSelectedGradientEnd => SurfaceHover;
            public override Color MenuItemBorder => Accent;
            public override Color MenuItemPressedGradientBegin => Surface;
            public override Color MenuItemPressedGradientEnd => Surface;
            public override Color MenuBorder => Border;
            public override Color ToolStripDropDownBackground => Surface;
            public override Color ImageMarginGradientBegin => Surface;
            public override Color ImageMarginGradientMiddle => Surface;
            public override Color ImageMarginGradientEnd => Surface;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Surface;
            public override Color MenuStripGradientBegin => Surface;
            public override Color MenuStripGradientEnd => Surface;
        }

        private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkColorTable())
            {
                RoundedEdges = false;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Text : Muted;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = Text;
                base.OnRenderArrow(e);
            }
        }

        /// <summary>Fills a rounded rectangle with anti-aliasing.</summary>
        public static void FillRounded(Graphics g, Rectangle bounds, int radiusPx, Color fill, Color? border = null, int borderWidth = 1)
        {
            if (g == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (var path = RoundedRect(bounds, radiusPx))
                {
                    using (var brush = new SolidBrush(fill))
                    {
                        g.FillPath(brush, path);
                    }
                    if (border.HasValue && borderWidth > 0)
                    {
                        using (var pen = new Pen(border.Value, borderWidth))
                        {
                            g.DrawPath(pen, path);
                        }
                    }
                }
            }
            finally
            {
                g.SmoothingMode = oldMode;
            }
        }
    }
}
