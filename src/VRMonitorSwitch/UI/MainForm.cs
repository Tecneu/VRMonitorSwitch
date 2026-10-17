using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VRMonitorSwitch.Core;
using VRMonitorSwitch.Display;
using VRMonitorSwitch.Vdd;

namespace VRMonitorSwitch.UI
{
    /// <summary>
    /// The single window of the app: a two-step wizard (laptop screen question → profile cards)
    /// plus the tray icon that keeps the session alive while the window is hidden.
    /// </summary>
    public sealed class MainForm : Form
    {
        private const string DriverFileName = "parsec-vdd-0.45.0.0.exe";
        private const int BalloonMs = 5000;

        private readonly AppSettings _settings;
        private readonly SessionController _controller;
        private readonly bool _startHidden;

        private bool _allowVisible;
        private bool _exiting;
        private bool _busy;
        private bool _hideExplained;
        private bool _hasInternalDisplay;
        /// <summary>The built-in display is connected but inactive (left off by a killed session, for example).</summary>
        private bool _internalInactive;
        private bool _step1Available;
        private bool _laptopOff;
        private VddStatus _driverStatus = VddStatus.Unknown;

        // Layout containers.
        private TableLayoutPanel _root;
        private Panel _header;
        private Label _titleLabel;
        private Panel _bannerPanel;
        private Label _bannerTitle;
        private Label _bannerText;
        private Button _installDriverButton;
        private Panel _recoverPanel;
        private Label _recoverTitle;
        private Label _recoverText;
        private Button _recoverButton;
        private Panel _content;
        private Panel _step1Panel;
        private Panel _step2Panel;
        private Panel _statusBar;
        private Label _stateLabel;
        private Label _progressLabel;
        private ProgressBar _marquee;

        // Step 1.
        private TableLayoutPanel _step1Box;
        private Button _laptopOffButton;
        private Button _laptopOnButton;
        private CheckBox _rememberBox;

        // Step 2.
        private LinkLabel _backLink;
        private TableLayoutPanel _cardsTable;
        private readonly List<ProfileCard> _cards = new List<ProfileCard>();

        // Tray.
        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayShow;
        private ToolStripMenuItem _trayChange;
        private ToolStripMenuItem _trayRestore;
        private ToolStripMenuItem _trayExit;

        public MainForm(AppSettings settings, SessionController controller, bool startHidden)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _startHidden = startHidden;
            _allowVisible = !startHidden;

            if (_settings.LastModeByProfile == null) _settings.LastModeByProfile = new Dictionary<string, string>();
            if (_settings.ExtraModes == null) _settings.ExtraModes = new List<string>();

            InitializeForm();
            BuildHeader();
            BuildBanner();
            BuildRecoverBanner();
            BuildContent();
            BuildStep1();
            BuildStep2();
            BuildStatusBar();
            BuildTray();

            _controller.Progress += Controller_Progress;
            _controller.StateChanged += Controller_StateChanged;

            DetectInternalDisplay();
            RefreshDriverBanner(QueryDriverStatusSafe());
            ApplyState(_controller.State);
            RefreshRecoverBanner();

            _laptopOff = _hasInternalDisplay && _settings.LaptopScreenOffDefault;
            _step1Available = _hasInternalDisplay && _settings.AskLaptopScreenEveryTime;
            ShowStep(_step1Available ? 1 : 2);

            if (_startHidden)
            {
                // Make sure a handle exists so cross-thread marshalling works before the first Show().
                if (!IsHandleCreated) CreateHandle();
            }
        }

        // =====================================================================================
        // Public API
        // =====================================================================================

        /// <summary>Shows and activates the window (used by the tray menu and by a second instance).</summary>
        public void ShowFromTray()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ShowFromTray));
                return;
            }
            _allowVisible = true;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            FocusFirstCard();
        }

        /// <summary>Restores the displays (if a session is active) on a worker thread and then exits the app.</summary>
        public void RequestRestoreAndExit()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RequestRestoreAndExit));
                return;
            }
            if (_exiting) return;
            _exiting = true;
            Log.Info("UI: restore and exit requested.");

            var state = _controller.State;
            if (state == SessionState.Idle)
            {
                ExitNow();
                return;
            }

            ShowFromTray();
            SetBusy(true, Strings.Get("status.restoring"));
            _progressLabel.Text = Strings.Get("status.restoring");
            RunOnWorker(() => _controller.Restore(), ex =>
            {
                if (ex != null) Log.Error("Restore failed during exit", ex);
                ExitNow();
            });
        }

        /// <summary>
        /// Applies a profile programmatically (used by the CLI path). Works like clicking a card:
        /// shows progress, saves the settings on success and hides to the tray.
        /// </summary>
        public void ApplyProfile(Profile profile, DisplayMode mode, bool turnOffLaptopScreen)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ApplyProfile(profile, mode, turnOffLaptopScreen)));
                return;
            }
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (mode == null) mode = profile.DefaultMode;
            _laptopOff = turnOffLaptopScreen && _hasInternalDisplay;
            StartApply(profile, mode);
        }

        // =====================================================================================
        // Construction
        // =====================================================================================

        private void InitializeForm()
        {
            SuspendLayout();
            Theme.StyleForm(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = Strings.Get("app.title");
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 560);
            ClientSize = new Size(900, 620);
            KeyPreview = true;
            ShowInTaskbar = true;
            DoubleBuffered = true;
            Icon = LoadAppIcon();

            _root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = Theme.Background,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // header
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // driver banner
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // laptop-screen recovery banner
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // content
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // status bar
            Controls.Add(_root);
            ResumeLayout(false);
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch (Exception ex)
            {
                Log.Warn("MainForm: could not load the application icon: " + ex.Message);
            }
            return SystemIcons.Application;
        }

        private void BuildHeader()
        {
            _header = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Background,
                Padding = new Padding(24, 18, 24, 6),
                Margin = Padding.Empty
            };
            _titleLabel = new Label
            {
                Text = Strings.Get("app.title"),
                AutoSize = true,
                Dock = DockStyle.Top
            };
            Theme.StyleLabel(_titleLabel, Theme.FontTitle);
            _header.Controls.Add(_titleLabel);
            _root.Controls.Add(_header, 0, 0);
        }

        private void BuildBanner()
        {
            _bannerPanel = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.DangerSurface,
                Padding = new Padding(24, 12, 24, 12),
                Margin = new Padding(24, 6, 24, 6),
                Visible = false
            };
            Theme.ApplyRoundedRegion(_bannerPanel, Theme.CornerRadius);

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _bannerTitle = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = Strings.Get("driver.missing.title") };
            Theme.StyleLabel(_bannerTitle, Theme.FontSubtitleBold);
            _bannerTitle.ForeColor = Theme.Danger;
            _bannerTitle.BackColor = Theme.DangerSurface;

            _bannerText = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = Strings.Get("driver.missing.text"), MaximumSize = new Size(560, 0) };
            Theme.StyleLabel(_bannerText, Theme.FontBody);
            _bannerText.BackColor = Theme.DangerSurface;

            _installDriverButton = new Button
            {
                Text = Strings.Get("btn.installDriver"),
                AutoSize = true,
                Padding = new Padding(14, 8, 14, 8),
                Anchor = AnchorStyles.Right,
                Margin = new Padding(12, 0, 0, 0)
            };
            Theme.StyleButton(_installDriverButton, primary: true);
            _installDriverButton.Click += (s, e) => StartDriverInstall();

            table.Controls.Add(_bannerTitle, 0, 0);
            table.Controls.Add(_bannerText, 0, 1);
            table.Controls.Add(_installDriverButton, 1, 0);
            table.SetRowSpan(_installDriverButton, 2);
            _bannerPanel.Controls.Add(table);
            _bannerPanel.Resize += (s, e) => { _bannerText.MaximumSize = new Size(Math.Max(200, _bannerPanel.ClientSize.Width - _installDriverButton.Width - 80), 0); };
            _root.Controls.Add(_bannerPanel, 0, 1);
        }

        /// <summary>
        /// Banner offered when the built-in display is connected but inactive and no session is running
        /// (e.g. the process was killed while the laptop screen was off). Its button does not depend on
        /// any in-process session state.
        /// </summary>
        private void BuildRecoverBanner()
        {
            _recoverPanel = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Surface,
                Padding = new Padding(24, 12, 24, 12),
                Margin = new Padding(24, 6, 24, 6),
                Visible = false
            };
            Theme.ApplyRoundedRegion(_recoverPanel, Theme.CornerRadius);

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _recoverTitle = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = Strings.Get("recover.title") };
            Theme.StyleLabel(_recoverTitle, Theme.FontSubtitleBold);
            _recoverTitle.BackColor = Theme.Surface;

            _recoverText = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = Strings.Get("recover.text"), MaximumSize = new Size(560, 0) };
            Theme.StyleLabel(_recoverText, Theme.FontBody, muted: true);
            _recoverText.BackColor = Theme.Surface;

            _recoverButton = new Button
            {
                Text = Strings.Get("btn.recoverScreen"),
                AutoSize = true,
                Padding = new Padding(14, 8, 14, 8),
                Anchor = AnchorStyles.Right,
                Margin = new Padding(12, 0, 0, 0)
            };
            Theme.StyleButton(_recoverButton, primary: true);
            _recoverButton.Click += (s, e) => StartRecoverInternalDisplay();

            table.Controls.Add(_recoverTitle, 0, 0);
            table.Controls.Add(_recoverText, 0, 1);
            table.Controls.Add(_recoverButton, 1, 0);
            table.SetRowSpan(_recoverButton, 2);
            _recoverPanel.Controls.Add(table);
            _recoverPanel.Resize += (s, e) => { _recoverText.MaximumSize = new Size(Math.Max(200, _recoverPanel.ClientSize.Width - _recoverButton.Width - 80), 0); };
            _root.Controls.Add(_recoverPanel, 0, 2);
        }

        private void BuildContent()
        {
            _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Margin = Padding.Empty };
            _root.Controls.Add(_content, 0, 3);
        }

        private void BuildStep1()
        {
            _step1Panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Visible = false };

            _step1Box = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 5,
                BackColor = Theme.Background,
                Padding = new Padding(12)
            };
            _step1Box.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _step1Box.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 5; i++) _step1Box.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var title = new Label { Text = Strings.Get("step.laptop.title"), AutoSize = true, Anchor = AnchorStyles.None, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(3, 3, 3, 10) };
            Theme.StyleLabel(title, Theme.FontTitle);

            var question = new Label { Text = Strings.Get("step.laptop.question"), AutoSize = true, Anchor = AnchorStyles.None, TextAlign = ContentAlignment.MiddleCenter, MaximumSize = new Size(Theme.Scale(this, 640), 0), Margin = new Padding(3, 3, 3, 6) };
            Theme.StyleLabel(question, Theme.FontSubtitle);

            var hint = new Label { Text = Strings.Get("step.laptop.hint"), AutoSize = true, Anchor = AnchorStyles.None, TextAlign = ContentAlignment.MiddleCenter, MaximumSize = new Size(Theme.Scale(this, 640), 0), Margin = new Padding(3, 3, 3, 24) };
            Theme.StyleLabel(hint, Theme.FontBody, muted: true);

            _laptopOffButton = new Button { Text = Strings.Get("step.laptop.off"), Size = new Size(Theme.Scale(this, 300), Theme.Scale(this, 84)), Margin = new Padding(Theme.Scale(this, 12)) };
            Theme.StyleButton(_laptopOffButton, primary: true);
            _laptopOffButton.Font = Theme.FontSubtitleBold;
            _laptopOffButton.Click += (s, e) => AnswerLaptop(true);

            _laptopOnButton = new Button { Text = Strings.Get("step.laptop.on"), Size = new Size(Theme.Scale(this, 300), Theme.Scale(this, 84)), Margin = new Padding(Theme.Scale(this, 12)) };
            Theme.StyleButton(_laptopOnButton, primary: false);
            _laptopOnButton.Font = Theme.FontSubtitleBold;
            _laptopOnButton.Click += (s, e) => AnswerLaptop(false);

            _rememberBox = new CheckBox { Text = Strings.Get("label.remember"), AutoSize = true, Anchor = AnchorStyles.None, Margin = new Padding(3, 18, 3, 3) };
            Theme.StyleCheckBox(_rememberBox);

            _step1Box.Controls.Add(title, 0, 0); _step1Box.SetColumnSpan(title, 2);
            _step1Box.Controls.Add(question, 0, 1); _step1Box.SetColumnSpan(question, 2);
            _step1Box.Controls.Add(hint, 0, 2); _step1Box.SetColumnSpan(hint, 2);
            _step1Box.Controls.Add(_laptopOffButton, 0, 3);
            _step1Box.Controls.Add(_laptopOnButton, 1, 3);
            _step1Box.Controls.Add(_rememberBox, 0, 4); _step1Box.SetColumnSpan(_rememberBox, 2);

            _step1Panel.Controls.Add(_step1Box);
            _step1Panel.Resize += (s, e) => CenterIn(_step1Panel, _step1Box);
            _step1Box.SizeChanged += (s, e) => CenterIn(_step1Panel, _step1Box);
            _content.Controls.Add(_step1Panel);
        }

        private void BuildStep2()
        {
            _step2Panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Visible = false, Padding = new Padding(24, 6, 24, 12) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Background,
                Margin = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var titleRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Theme.Background,
                Margin = new Padding(0, 0, 0, 10)
            };
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var title = new Label { Text = Strings.Get("step.profile.title"), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            Theme.StyleLabel(title, Theme.FontSubtitleBold);

            _backLink = new LinkLabel { Text = Strings.Get("btn.back"), AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Margin = new Padding(3, 3, 3, 6) };
            Theme.StyleLink(_backLink);
            _backLink.LinkClicked += (s, e) => { if (!_busy && _step1Available) ShowStep(1); };

            titleRow.Controls.Add(title, 0, 0);
            titleRow.Controls.Add(_backLink, 1, 0);

            _cardsTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Theme.Background,
                Margin = Padding.Empty
            };
            for (int i = 0; i < 3; i++) _cardsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3F));
            _cardsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            int col = 0;
            foreach (var profile in Profiles.All)
            {
                if (col >= 3) break;
                var card = new ProfileCard(profile) { Dock = DockStyle.Fill, Margin = new Padding(8) };
                card.Chosen += Card_Chosen;
                _cards.Add(card);
                _cardsTable.Controls.Add(card, col, 0);
                col++;
            }
            RefreshCardModes();

            layout.Controls.Add(titleRow, 0, 0);
            layout.Controls.Add(_cardsTable, 0, 1);
            _step2Panel.Controls.Add(layout);
            _content.Controls.Add(_step2Panel);
        }

        private void BuildStatusBar()
        {
            _statusBar = new Panel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Surface,
                Padding = new Padding(24, 8, 24, 8),
                Margin = Padding.Empty
            };
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Theme.Surface,
                Margin = Padding.Empty
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _stateLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = Strings.Get("status.idle"), Margin = new Padding(0, 0, 16, 0) };
            Theme.StyleLabel(_stateLabel, Theme.FontBodyBold);
            _stateLabel.BackColor = Theme.Surface;

            _progressLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = string.Empty, AutoEllipsis = true };
            Theme.StyleLabel(_progressLabel, Theme.FontBody, muted: true);
            _progressLabel.BackColor = Theme.Surface;

            _marquee = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Size = new Size(Theme.Scale(this, 180), Theme.Scale(this, 14)), Anchor = AnchorStyles.Right, Visible = false, Margin = new Padding(16, 0, 0, 0) };

            table.Controls.Add(_stateLabel, 0, 0);
            table.Controls.Add(_progressLabel, 1, 0);
            table.Controls.Add(_marquee, 2, 0);
            _statusBar.Controls.Add(table);
            _root.Controls.Add(_statusBar, 0, 4);
        }

        private void BuildTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayShow = new ToolStripMenuItem(Strings.Get("tray.show"));
            _trayChange = new ToolStripMenuItem(Strings.Get("tray.change"));
            _trayRestore = new ToolStripMenuItem(Strings.Get("tray.restore"));
            _trayExit = new ToolStripMenuItem(Strings.Get("tray.exit"));

            _trayShow.Click += (s, e) => ShowFromTray();
            _trayChange.Click += (s, e) => { ShowFromTray(); if (!_busy) ShowStep(2); };
            _trayRestore.Click += (s, e) => RequestRestoreAndExit();
            _trayExit.Click += (s, e) => TrayExit();

            _trayMenu.Items.Add(_trayShow);
            _trayMenu.Items.Add(_trayChange);
            _trayMenu.Items.Add(_trayRestore);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(_trayExit);
            Theme.StyleMenu(_trayMenu);

            _tray = new NotifyIcon
            {
                Icon = Icon ?? SystemIcons.Application,
                Text = TrimTrayText(Strings.Get("app.title")),
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            // Double-click restores and exits only while a session is active (the balloon says so); in any other
            // state users expect the window to open, not the process to quit.
            _tray.DoubleClick += (s, e) =>
            {
                if (_controller.State == SessionState.Active) RequestRestoreAndExit();
                else ShowFromTray();
            };
            _tray.BalloonTipClicked += (s, e) => ShowFromTray();
        }

        // =====================================================================================
        // Wizard navigation
        // =====================================================================================

        private void ShowStep(int step)
        {
            if (step == 1 && !_step1Available) step = 2;
            _step1Panel.Visible = step == 1;
            _step2Panel.Visible = step == 2;
            _backLink.Visible = _step1Available;
            if (step == 1)
            {
                CenterIn(_step1Panel, _step1Box);
                var preferred = _laptopOff ? _laptopOffButton : _laptopOnButton;
                if (preferred.CanSelect) preferred.Select();
            }
            else
            {
                FocusFirstCard();
            }
        }

        private void FocusFirstCard()
        {
            if (!_step2Panel.Visible || _busy) return;
            ProfileCard target = null;
            foreach (var card in _cards)
            {
                if (card.Profile.Id == _settings.LastProfileId) { target = card; break; }
            }
            if (target == null && _cards.Count > 0) target = _cards[0];
            if (target != null && target.CanFocus) target.Focus();
        }

        private void AnswerLaptop(bool turnOff)
        {
            if (_busy) return;
            _laptopOff = turnOff;
            if (_rememberBox.Checked)
            {
                _settings.AskLaptopScreenEveryTime = false;
                _settings.LaptopScreenOffDefault = turnOff;
                _settings.Save();
                _step1Available = false;
                Log.Info("UI: laptop screen answer remembered (off=" + turnOff + ").");
            }
            ShowStep(2);
        }

        private void RefreshCardModes()
        {
            foreach (var card in _cards)
            {
                var profile = card.Profile;
                var list = new List<DisplayMode>(profile.Modes);
                foreach (var extra in _settings.ExtraModes)
                {
                    if (string.IsNullOrWhiteSpace(extra)) continue;
                    try
                    {
                        var m = DisplayMode.Parse(extra.Trim());
                        if (m != null && !list.Contains(m)) list.Add(m);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Settings.ExtraModes: cannot parse '" + extra + "': " + ex.Message);
                    }
                }

                DisplayMode preselect = null;
                string last;
                if (_settings.LastModeByProfile.TryGetValue(profile.Id, out last) && !string.IsNullOrWhiteSpace(last))
                {
                    try
                    {
                        preselect = DisplayMode.Parse(last.Trim());
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Settings.LastModeByProfile[" + profile.Id + "]: cannot parse '" + last + "': " + ex.Message);
                    }
                }
                card.SetModes(list, preselect ?? profile.DefaultMode);
            }
        }

        private static void CenterIn(Control host, Control child)
        {
            if (host == null || child == null) return;
            int x = Math.Max(0, (host.ClientSize.Width - child.Width) / 2);
            int y = Math.Max(0, (host.ClientSize.Height - child.Height) / 2);
            child.Location = new Point(x, y);
        }

        // =====================================================================================
        // Apply / restore
        // =====================================================================================

        private void Card_Chosen(Profile profile, DisplayMode mode)
        {
            if (_busy || _exiting) return;
            StartApply(profile, mode);
        }

        private void StartApply(Profile profile, DisplayMode mode)
        {
            if (_busy || _exiting) return;
            bool laptopOff = _laptopOff && _hasInternalDisplay;
            Log.Info("UI: applying profile '" + profile.Id + "' mode " + mode.ToKey() + " laptopOff=" + laptopOff);

            ShowStep(2);
            SetBusy(true, Strings.Get("status.applying"));
            _progressLabel.Text = Strings.Get("status.applying");

            bool restoreFirst = _controller.State == SessionState.Active;
            RunOnWorker(() =>
            {
                if (restoreFirst)
                {
                    Log.Info("UI: a session is active; restoring before applying the new profile.");
                    _controller.Restore();
                }
                _controller.Apply(profile, mode, laptopOff);
            },
            ex =>
            {
                if (_exiting)
                {
                    // Exit was requested while applying: a restore is queued behind the controller lock and
                    // ExitNow() follows. Do not hide to tray, pop balloons or re-enable the UI now.
                    Log.Info("UI: apply finished while exiting (" + (ex == null ? "ok" : ex.GetType().Name) + "); result ignored.");
                    return;
                }
                SetBusy(false, null);
                if (ex == null)
                {
                    OnApplySucceeded(profile, mode, laptopOff);
                }
                else
                {
                    Log.Error("Apply failed", ex);
                    RefreshDriverBanner(QueryDriverStatusSafe());
                    DetectInternalDisplay();
                    RefreshRecoverBanner();
                    // Show the window first: with the --profile path it has never been visible, and a modal
                    // dialog owned by an invisible window can end up behind other applications.
                    ShowFromTray();
                    ShowError(ex);
                    FocusFirstCard();
                }
            });
        }

        private void OnApplySucceeded(Profile profile, DisplayMode mode, bool laptopOff)
        {
            try
            {
                _settings.LastProfileId = profile.Id;
                _settings.LastModeByProfile[profile.Id] = mode.ToKey();
                if (_hasInternalDisplay) _settings.LaptopScreenOffDefault = laptopOff;
                _settings.Save();
            }
            catch (Exception ex)
            {
                Log.Error("Saving settings after apply failed", ex);
            }

            string profileName = Strings.Get("profile." + profile.Id + ".name");

            // Step 6 of Apply (laptop screen off) is non-fatal in the controller: surface it instead of claiming success.
            bool laptopStayedOn = laptopOff && !_controller.LaptopScreenOff;
            string text = Strings.Format(laptopStayedOn ? "msg.appliedLaptopOn" : "msg.applied", profileName);
            if (laptopStayedOn)
            {
                Log.Warn("UI: the profile is active but the laptop screen could not be turned off.");
            }
            _progressLabel.Text = text;
            HideToTray();
            ShowBalloon(Strings.Get("app.title"), text, laptopStayedOn ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        /// <summary>Turns the built-in display back on without any session state (see <see cref="SessionController.RecoverInternalDisplay"/>).</summary>
        private void StartRecoverInternalDisplay()
        {
            if (_busy || _exiting) return;
            Log.Info("UI: laptop screen recovery requested.");
            SetBusy(true, Strings.Get("recover.working"));
            _progressLabel.Text = Strings.Get("recover.working");

            RecoveryResult result = RecoveryResult.AlreadyOn;
            RunOnWorker(() => { result = SessionController.RecoverInternalDisplay(); }, ex =>
            {
                if (_exiting) return;
                SetBusy(false, null);
                DetectInternalDisplay();
                RefreshRecoverBanner();
                if (ex != null)
                {
                    Log.Error("Laptop screen recovery failed", ex);
                    _progressLabel.Text = Strings.Get("recover.failed");
                    ShowError(ex);
                    return;
                }
                string key;
                switch (result)
                {
                    case RecoveryResult.Restored: key = "recover.done"; break;
                    case RecoveryResult.NoInternalDisplay: key = "recover.noInternal"; break;
                    default: key = "recover.alreadyOn"; break;
                }
                _progressLabel.Text = Strings.Get(key);
            });
        }

        /// <summary>Shows the recovery banner only when the panel is inactive and no session owns the displays.</summary>
        private void RefreshRecoverBanner()
        {
            if (_recoverPanel == null) return;
            var state = _controller.State;
            bool show = _internalInactive && !_busy && (state == SessionState.Idle || state == SessionState.Faulted);
            if (_recoverPanel.Visible != show)
            {
                Log.Info("UI: laptop screen recovery banner " + (show ? "shown" : "hidden") + ".");
            }
            _recoverPanel.Visible = show;
        }

        private void TrayExit()
        {
            if (_exiting) return;
            var state = _controller.State;
            if (state == SessionState.Active || state == SessionState.Applying)
            {
                var answer = MessageBox.Show(this, Strings.Get("msg.confirmExit"), Strings.Get("app.title"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
                if (answer != DialogResult.Yes) return;
            }
            RequestRestoreAndExit();
        }

        private void ExitNow()
        {
            _exiting = true;
            try
            {
                if (_tray != null) _tray.Visible = false;
            }
            catch (Exception ex)
            {
                Log.Warn("Hiding tray icon failed: " + ex.Message);
            }
            Log.Info("UI: exiting application.");
            Application.Exit();
        }

        /// <summary>Runs <paramref name="work"/> on a worker thread and <paramref name="done"/> on the UI thread with the exception (or null).</summary>
        private void RunOnWorker(Action work, Action<Exception> done)
        {
            Task.Run(() =>
            {
                Exception error = null;
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                OnUi(() => done(error));
            });
        }

        private void OnUi(Action action)
        {
            if (action == null || IsDisposed) return;
            try
            {
                if (InvokeRequired)
                {
                    if (!IsHandleCreated) return;
                    BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch (ObjectDisposedException)
            {
                // Form closed while a worker finished; nothing to update.
            }
            catch (InvalidOperationException ex)
            {
                Log.Warn("MainForm.OnUi: " + ex.Message);
            }
        }

        private void SetBusy(bool busy, string stateText)
        {
            _busy = busy;
            foreach (var card in _cards) card.Interactive = !busy;
            _laptopOffButton.Enabled = !busy;
            _laptopOnButton.Enabled = !busy;
            _rememberBox.Enabled = !busy;
            _backLink.Enabled = !busy;
            _installDriverButton.Enabled = !busy;
            if (_recoverButton != null) _recoverButton.Enabled = !busy;
            _trayChange.Enabled = !busy;
            _trayRestore.Enabled = !busy && _controller.State == SessionState.Active;
            _marquee.Visible = busy;
            if (!string.IsNullOrEmpty(stateText)) _stateLabel.Text = stateText;
            if (!busy) ApplyState(_controller.State);
        }

        // =====================================================================================
        // Controller events
        // =====================================================================================

        private void Controller_Progress(string message)
        {
            OnUi(() => { _progressLabel.Text = message ?? string.Empty; });
        }

        private void Controller_StateChanged(SessionState state)
        {
            OnUi(() => ApplyState(state));
        }

        private void ApplyState(SessionState state)
        {
            string key;
            switch (state)
            {
                case SessionState.Applying: key = "status.applying"; break;
                case SessionState.Active: key = "status.active"; break;
                case SessionState.Restoring: key = "status.restoring"; break;
                case SessionState.Faulted: key = "status.error"; break;
                default: key = "status.idle"; break;
            }
            _stateLabel.Text = Strings.Get(key);
            _stateLabel.ForeColor = state == SessionState.Faulted ? Theme.Danger : (state == SessionState.Active ? Theme.Success : Theme.Text);

            if (state == SessionState.Active)
            {
                var profile = _controller.ActiveProfile;
                string name = profile != null ? Strings.Get("profile." + profile.Id + ".name") : string.Empty;
                _tray.Text = TrimTrayText(Strings.Format("tray.active", name));
            }
            else
            {
                _tray.Text = TrimTrayText(Strings.Get("app.title"));
            }
            _trayRestore.Enabled = !_busy && state == SessionState.Active;
            _trayChange.Enabled = !_busy;
            RefreshRecoverBanner();
        }

        private static string TrimTrayText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            // NotifyIcon.Text is limited to 63 characters on .NET Framework.
            return text.Length > 63 ? text.Substring(0, 60) + "..." : text;
        }

        // =====================================================================================
        // Tray / visibility
        // =====================================================================================

        protected override void SetVisibleCore(bool value)
        {
            if (!_allowVisible)
            {
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        private void HideToTray()
        {
            Hide();
            _tray.Visible = true;
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            try
            {
                _tray.Visible = true;
                _tray.ShowBalloonTip(BalloonMs, title, text, icon);
            }
            catch (Exception ex)
            {
                Log.Warn("Balloon tip failed: " + ex.Message);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            if (_exiting) return;

            if (e.CloseReason == CloseReason.UserClosing)
            {
                var state = _controller.State;
                if (state == SessionState.Active || state == SessionState.Applying || state == SessionState.Restoring)
                {
                    e.Cancel = true;
                    HideToTray();
                    if (!_hideExplained)
                    {
                        _hideExplained = true;
                        ShowBalloon(Strings.Get("app.title"), Strings.Get("msg.hiddenToTray"), ToolTipIcon.Info);
                    }
                    Log.Info("UI: window hidden to tray (session " + state + ").");
                    return;
                }
                _exiting = true;
                try { _tray.Visible = false; } catch (Exception ex) { Log.Warn("Hiding tray icon failed: " + ex.Message); }
                return;
            }

            // Windows shutdown (WM_QUERYENDSESSION, which can still be cancelled) / Application.Exit: let it close
            // without touching _exiting or the tray icon here; OnFormClosed runs only when the form really closes,
            // so a cancelled shutdown does not leave a tray-less zombie. Program.cs restores via ProcessExit.
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _exiting = true;
            try { if (_tray != null) _tray.Visible = false; } catch (Exception ex) { Log.Warn("Hiding tray icon failed: " + ex.Message); }
            base.OnFormClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled) return;
            if (e.KeyCode == Keys.Escape && _step2Panel.Visible && _step1Available && !_busy)
            {
                // Escape is also how an open ComboBox list is closed without changing the selection: let it through.
                var combo = FindFocusedControl() as ComboBox;
                if (combo != null && combo.DroppedDown) return;
                e.Handled = true;
                e.SuppressKeyPress = true;
                ShowStep(1);
            }
        }

        private Control FindFocusedControl()
        {
            Control c = ActiveControl;
            while (c is ContainerControl)
            {
                var next = ((ContainerControl)c).ActiveControl;
                if (next == null || next == c) break;
                c = next;
            }
            return c;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            CenterIn(_step1Panel, _step1Box);
            if (_step2Panel.Visible) FocusFirstCard();
        }

        // =====================================================================================
        // Display / driver detection
        // =====================================================================================

        private void DetectInternalDisplay()
        {
            try
            {
                var displays = DisplayManager.GetDisplays(true);
                var internalDisplay = DisplayManager.FindInternal(displays);
                _hasInternalDisplay = internalDisplay != null;
                _internalInactive = internalDisplay != null && !internalDisplay.IsActive;
                Log.Info("UI: internal display " + (_hasInternalDisplay ? (_internalInactive ? "found (INACTIVE)" : "found") : "not found")
                    + " (" + (displays != null ? displays.Count : 0) + " displays).");
            }
            catch (Exception ex)
            {
                _hasInternalDisplay = false;
                _internalInactive = false;
                Log.Error("UI: display enumeration failed; assuming no internal display", ex);
            }
        }

        private static VddStatus QueryDriverStatusSafe()
        {
            try
            {
                return ParsecVdd.QueryStatus();
            }
            catch (Exception ex)
            {
                Log.Error("ParsecVdd.QueryStatus failed", ex);
                return VddStatus.Unknown;
            }
        }

        private void RefreshDriverBanner(VddStatus status)
        {
            _driverStatus = status;
            Log.Info("UI: driver status = " + status);
            if (status == VddStatus.Ok)
            {
                _bannerPanel.Visible = false;
                return;
            }

            _bannerPanel.Visible = true;
            if (status == VddStatus.RestartRequired)
            {
                _bannerTitle.Text = Strings.Get("driver.notReady.title");
                _bannerText.Text = Strings.Get("driver.restartRequired");
                _installDriverButton.Visible = false;
            }
            else
            {
                // "Missing" only when it really is missing; Disabled/DriverError/... mean "present but not healthy".
                _bannerTitle.Text = Strings.Get(status == VddStatus.NotInstalled ? "driver.missing.title" : "driver.notReady.title");
                _bannerText.Text = Strings.Get("driver.missing.text") + "  [" + Strings.DriverStatus(status) + "]";
                _installDriverButton.Visible = true;
            }
        }

        private void StartDriverInstall()
        {
            if (_busy || _exiting) return;
            SetBusy(true, Strings.Get("driver.installing"));
            _progressLabel.Text = Strings.Get("driver.installing");

            RunOnWorker(InstallDriverWorker, ex =>
            {
                if (_exiting)
                {
                    Log.Info("UI: driver installation finished while exiting; result ignored.");
                    return;
                }
                var status = QueryDriverStatusSafe();
                SetBusy(false, null);
                RefreshDriverBanner(status);
                if (ex != null)
                {
                    Log.Error("Driver installation failed", ex);
                    _progressLabel.Text = Strings.Get("driver.installFailed");
                    ShowError(ex);
                    return;
                }
                if (status == VddStatus.RestartRequired)
                {
                    _progressLabel.Text = Strings.Get("driver.restartRequired");
                    MessageBox.Show(this, Strings.Get("driver.restartRequired"), Strings.Get("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (status == VddStatus.Ok)
                {
                    _progressLabel.Text = Strings.Get("driver.installed");
                }
                else
                {
                    _progressLabel.Text = Strings.Get("driver.installFailed") + "  [" + Strings.DriverStatus(status) + "]";
                }
            });
        }

        /// <summary>Worker thread: locate or download the driver installer, verify it and run it elevated.</summary>
        private void InstallDriverWorker()
        {
            string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "redist", DriverFileName);
            string installer = null;

            if (File.Exists(bundled))
            {
                string hash = ComputeSha256(bundled);
                if (string.Equals(hash, ParsecVdd.DriverSha256, StringComparison.OrdinalIgnoreCase))
                {
                    installer = bundled;
                    Log.Info("Driver: using bundled installer " + bundled);
                }
                else
                {
                    Log.Warn("Driver: bundled installer hash mismatch (" + hash + "); downloading instead.");
                }
            }

            if (installer == null)
            {
                OnUi(() => { _progressLabel.Text = Strings.Get("driver.downloading"); });
                string target = Path.Combine(Path.GetTempPath(), DriverFileName);
                Log.Info("Driver: downloading " + ParsecVdd.DriverDownloadUrl + " -> " + target);
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = new TimeoutWebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "VRMonitorSwitch";
                    try
                    {
                        client.DownloadFile(ParsecVdd.DriverDownloadUrl, target);
                    }
                    catch (WebException wex) when (wex.Status == WebExceptionStatus.Timeout)
                    {
                        try { File.Delete(target); } catch (Exception ex) { Log.Warn("Driver: could not delete partial download: " + ex.Message); }
                        throw new InvalidOperationException(Strings.Get("driver.downloadTimeout"), wex);
                    }
                }
                string hash = ComputeSha256(target);
                if (!string.Equals(hash, ParsecVdd.DriverSha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(target); } catch (Exception ex) { Log.Warn("Driver: could not delete bad download: " + ex.Message); }
                    throw new InvalidOperationException(Strings.Get("err.driverHashMismatch") + " (" + hash + ")");
                }
                installer = target;
                Log.Info("Driver: download verified (SHA-256 OK).");
            }

            OnUi(() => { _progressLabel.Text = Strings.Get("driver.installing"); });
            var psi = new ProcessStartInfo
            {
                FileName = installer,
                Arguments = "/S",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(installer) ?? Path.GetTempPath()
            };
            Log.Info("Driver: running elevated: " + installer + " /S");
            try
            {
                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        process.WaitForExit();
                        Log.Info("Driver: installer exited with code " + process.ExitCode);
                    }
                }
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: the user declined the UAC prompt
                {
                    throw new InvalidOperationException(Strings.Get("driver.installCancelled"), ex);
                }
                throw;
            }
        }

        /// <summary>
        /// WebClient with bounded timeouts (default WebClient waits forever on a stalled socket, which would keep the
        /// window busy with no cancel control): 30 s to connect/respond, 2 min total for the ~0.5 MB download.
        /// </summary>
        private sealed class TimeoutWebClient : WebClient
        {
            private const int ConnectTimeoutMs = 30000;
            private const int ReadWriteTimeoutMs = 120000;

            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                if (request != null)
                {
                    request.Timeout = ConnectTimeoutMs;
                    var http = request as HttpWebRequest;
                    if (http != null) http.ReadWriteTimeout = ReadWriteTimeoutMs;
                }
                return request;
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("X2"));
                return sb.ToString();
            }
        }

        // =====================================================================================
        // Errors
        // =====================================================================================

        private void ShowError(Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Strings.Get("err.generic"));
            sb.AppendLine();
            if (ex != null)
            {
                sb.AppendLine(ex.Message);
                var inner = ex.InnerException;
                if (inner != null && !string.IsNullOrEmpty(inner.Message) && inner.Message != ex.Message)
                {
                    sb.AppendLine(inner.Message);
                }
            }
            sb.AppendLine();
            sb.Append(Strings.Format("label.log", Log.FilePath));

            try
            {
                MessageBox.Show(this, sb.ToString(), Strings.Get("status.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception mbEx)
            {
                Log.Error("MessageBox failed", mbEx);
            }
        }

        // =====================================================================================
        // Cleanup
        // =====================================================================================

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _controller.Progress -= Controller_Progress;
                    _controller.StateChanged -= Controller_StateChanged;
                }
                catch (Exception ex)
                {
                    Log.Warn("MainForm.Dispose: unsubscribing failed: " + ex.Message);
                }
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                    _tray = null;
                }
                if (_trayMenu != null)
                {
                    _trayMenu.Dispose();
                    _trayMenu = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
