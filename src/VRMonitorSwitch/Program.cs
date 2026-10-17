using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using VRMonitorSwitch.Core;
using VRMonitorSwitch.Display;
using VRMonitorSwitch.UI;
using VRMonitorSwitch.Vdd;

namespace VRMonitorSwitch
{
    /// <summary>
    /// Entry point: command line parsing, single instance, shutdown hooks and the message loop.
    /// </summary>
    internal static class Program
    {
        private const string MutexName = @"Local\VRMonitorSwitch.SingleInstance";
        private const string ShowEventName = @"Local\VRMonitorSwitch.Show";
        private const string RestoreEventName = @"Local\VRMonitorSwitch.Restore";

        private static SessionController _controller;
        private static MainForm _form;
        private static readonly ManualResetEvent _shutdown = new ManualResetEvent(false);

        /// <summary>Parsed command line.</summary>
        private sealed class Options
        {
            public string ProfileId;
            public string ModeText;
            public string LaptopText;     // "off" | "on" | null
            public bool Diagnose;
            public bool Restore;
            public bool Help;
            public string Language;
            public readonly List<string> Unknown = new List<string>();
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Options opt = ParseArgs(args);

            AppSettings settings = AppSettings.Load();
            Strings.Init(opt.Language ?? settings.Language);

            Log.Info("==== VR Monitor Switch " + Version + " starting ====");
            Log.Info("OS: " + Environment.OSVersion + " (64-bit OS: " + Environment.Is64BitOperatingSystem + ", 64-bit process: " + Environment.Is64BitProcess + "), CLR " + Environment.Version);
            Log.Info("Culture: " + Strings.Culture + " (UI culture " + CultureInfo.CurrentUICulture.Name + "), args: " + (args.Length == 0 ? "<none>" : string.Join(" ", args)));
            foreach (string u in opt.Unknown)
            {
                Log.Warn(Strings.Format("cli.unknownSwitch", u));
            }

            if (opt.Help)
            {
                using (var console = ConsoleSession.Open())
                {
                    console.WriteLine(Strings.Get("cli.usage"));
                }
                return 0;
            }

            if (opt.Diagnose)
            {
                return RunDiagnose(settings);
            }

            if (opt.Restore)
            {
                return RunRestoreRequest();
            }

            // -------- single instance ----------------------------------------------------------
            bool createdNew;
            Mutex mutex = null;
            try
            {
                mutex = new Mutex(true, MutexName, out createdNew);
            }
            catch (UnauthorizedAccessException ex)
            {
                // The mutex exists but cannot be opened with full access: the typical case is an elevated instance
                // already running. We are the secondary instance; never drive the driver from two processes.
                Log.Warn("Single-instance mutex exists but is not accessible (another instance, probably elevated): " + ex.Message);
                createdNew = false;
            }
            catch (IOException ex)
            {
                Log.Warn("Single-instance mutex exists but could not be opened: " + ex.Message);
                createdNew = false;
            }
            catch (Exception ex)
            {
                // Anything else (e.g. a WaitHandleCannotBeOpenedException race): carry on as the only instance.
                Log.Warn("Single-instance mutex problem, continuing as primary: " + ex.Message);
                createdNew = true;
            }

            if (!createdNew)
            {
                Log.Info(Strings.Get("cli.alreadyRunning"));
                if (!string.IsNullOrEmpty(opt.ProfileId))
                {
                    Log.Warn("--profile ignored because another instance is running.");
                }
                if (mutex != null)
                {
                    try { mutex.Dispose(); } catch (Exception ex) { Log.Warn("Mutex dispose failed: " + ex.Message); }
                }
                AllowPrimaryToComeToForeground();
                SignalEvent(ShowEventName);
                return 0;
            }

            // -------- validate the CLI apply request before building the UI --------------------
            Profile cliProfile = null;
            DisplayMode cliMode = null;
            bool cliLaptopOff = settings.LaptopScreenOffDefault;
            if (!string.IsNullOrEmpty(opt.ProfileId))
            {
                cliProfile = Profiles.FindById(opt.ProfileId);
                if (cliProfile == null)
                {
                    string msg = Strings.Format("cli.unknownProfile", opt.ProfileId);
                    Log.Error(msg);
                    MessageBox.Show(msg, Strings.Get("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 2;
                }

                if (!string.IsNullOrEmpty(opt.ModeText))
                {
                    if (!DisplayMode.TryParse(opt.ModeText, out cliMode))
                    {
                        string msg = Strings.Format("cli.invalidMode", opt.ModeText);
                        Log.Error(msg);
                        MessageBox.Show(msg, Strings.Get("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return 2;
                    }
                }
                else
                {
                    cliMode = settings.GetLastMode(cliProfile);
                }

                if (!string.IsNullOrEmpty(opt.LaptopText))
                {
                    string l = opt.LaptopText.Trim().ToLowerInvariant();
                    if (l == "off" || l == "0" || l == "false" || l == "no")
                    {
                        cliLaptopOff = true;
                    }
                    else if (l == "on" || l == "1" || l == "true" || l == "yes")
                    {
                        cliLaptopOff = false;
                    }
                    else
                    {
                        string msg = Strings.Format("cli.invalidLaptop", opt.LaptopText);
                        Log.Error(msg);
                        MessageBox.Show(msg, Strings.Get("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return 2;
                    }
                }
            }

            // -------- WinForms bootstrap -------------------------------------------------------
            // High-DPI awareness comes from app.manifest (PerMonitorV2); Application.SetHighDpiMode does not exist on net48.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            _controller = new SessionController(settings);

            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            SystemEvents.SessionEnding += OnSessionEnding;

            int exitCode = 0;
            try
            {
                bool startHidden = cliProfile != null;
                _form = new MainForm(settings, _controller, startHidden);

                // Force handle creation so cross-thread BeginInvoke works even while the window is hidden.
                IntPtr handle = _form.Handle;
                Log.Info("Main window created (hidden=" + startHidden + ", hwnd=0x" + handle.ToInt64().ToString("X") + ")");

                StartIpcListener();

                if (cliProfile != null)
                {
                    StartCliApply(settings, cliProfile, cliMode, cliLaptopOff);
                }

                Application.Run(_form);
            }
            catch (Exception ex)
            {
                Log.Error("Fatal error in the message loop.", ex);
                exitCode = 1;
            }
            finally
            {
                _shutdown.Set();
                SafeRestore("exit");
                try { _controller.Dispose(); } catch (Exception ex) { Log.Error("Controller dispose failed.", ex); }
                try { settings.Save(); } catch (Exception ex) { Log.Error("Settings save failed.", ex); }

                SystemEvents.SessionEnding -= OnSessionEnding;
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

                if (mutex != null)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                    try { mutex.Dispose(); } catch { }
                }
                Log.Info("==== VR Monitor Switch exiting (" + exitCode + ") ====");
            }

            return exitCode;
        }

        // ------------------------------------------------------------------------------------------
        // Command line
        // ------------------------------------------------------------------------------------------

        private static Options ParseArgs(string[] args)
        {
            var opt = new Options();
            if (args == null)
            {
                return opt;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i];
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                string key = raw.Trim();
                string value = null;

                // Accept --key, -key, /key and --key=value / --key:value.
                while (key.StartsWith("-", StringComparison.Ordinal) || key.StartsWith("/", StringComparison.Ordinal))
                {
                    key = key.Substring(1);
                }
                int sep = key.IndexOfAny(new[] { '=', ':' });
                if (sep >= 0)
                {
                    value = key.Substring(sep + 1);
                    key = key.Substring(0, sep);
                }
                key = key.ToLowerInvariant();

                Func<string> takeValue = () =>
                {
                    if (value != null)
                    {
                        return value;
                    }
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal) && !args[i + 1].StartsWith("/", StringComparison.Ordinal))
                    {
                        i++;
                        return args[i];
                    }
                    return null;
                };

                switch (key)
                {
                    case "profile":
                    case "p":
                        opt.ProfileId = takeValue();
                        break;
                    case "mode":
                    case "m":
                    case "resolution":
                        opt.ModeText = takeValue();
                        break;
                    case "laptop":
                    case "l":
                        opt.LaptopText = takeValue();
                        break;
                    case "diagnose":
                    case "diag":
                        opt.Diagnose = true;
                        break;
                    case "restore":
                        opt.Restore = true;
                        break;
                    case "lang":
                    case "language":
                        opt.Language = takeValue();
                        break;
                    case "help":
                    case "h":
                    case "?":
                        opt.Help = true;
                        break;
                    default:
                        opt.Unknown.Add(raw);
                        break;
                }
            }
            return opt;
        }

        // ------------------------------------------------------------------------------------------
        // --diagnose
        // ------------------------------------------------------------------------------------------

        private static int RunDiagnose(AppSettings settings)
        {
            using (var console = ConsoleSession.Open())
            {
                try
                {
                    console.WriteLine();
                    console.WriteLine(Strings.Get("diag.title"));
                    console.WriteLine(new string('=', 60));
                    console.WriteLine(Strings.Get("diag.version") + ": " + Version);
                    console.WriteLine(Strings.Get("diag.os") + ": " + Environment.OSVersion + (Environment.Is64BitOperatingSystem ? " x64" : " x86"));
                    console.WriteLine(Strings.Get("diag.culture") + ": " + Strings.Culture);
                    console.WriteLine(Strings.Get("diag.logFile") + ": " + Log.FilePath);
                    console.WriteLine(Strings.Get("diag.settingsFile") + ": " + AppSettings.FilePath);
                    console.WriteLine();

                    // Driver
                    try
                    {
                        VddStatus status = ParsecVdd.QueryStatus();
                        console.WriteLine(Strings.Get("diag.driver") + ": " + status + " — " + Strings.DriverStatus(status));
                        Log.Info("Diagnose: driver status " + status);
                        if (status == VddStatus.Ok)
                        {
                            // Open starts the keep-alive, Dispose stops it; no display is added.
                            try
                            {
                                using (var vdd = new ParsecVdd())
                                {
                                    vdd.Open();
                                    console.WriteLine(Strings.Get("diag.driverVersion") + ": " + vdd.Version);
                                    Log.Info("Diagnose: driver minor version " + vdd.Version);
                                }
                            }
                            catch (Exception ex)
                            {
                                console.WriteLine(Strings.Get("diag.driverVersion") + ": " + Strings.Format("diag.error", ex.Message));
                                Log.Error("Diagnose: could not read the driver version.", ex);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        console.WriteLine(Strings.Get("diag.driver") + ": " + Strings.Format("diag.error", ex.Message));
                        Log.Error("Diagnose: QueryStatus failed.", ex);
                    }
                    console.WriteLine();

                    // Displays
                    console.WriteLine(Strings.Get("diag.displays") + ":");
                    List<DisplayInfo> displays = null;
                    try
                    {
                        displays = DisplayManager.GetDisplays(true);
                    }
                    catch (Exception ex)
                    {
                        console.WriteLine("  " + Strings.Format("diag.error", ex.Message));
                        Log.Error("Diagnose: GetDisplays failed.", ex);
                    }

                    if (displays == null || displays.Count == 0)
                    {
                        if (displays != null)
                        {
                            console.WriteLine("  " + Strings.Get("diag.none"));
                        }
                    }
                    else
                    {
                        foreach (DisplayInfo d in displays)
                        {
                            var flags = new List<string>();
                            flags.Add(Strings.Get(d.IsActive ? "diag.active" : "diag.inactive"));
                            if (d.IsPrimary) flags.Add(Strings.Get("diag.primary"));
                            if (d.IsInternal) flags.Add(Strings.Get("diag.internal"));
                            if (d.IsParsecVirtual) flags.Add(Strings.Get("diag.virtual"));

                            var sb = new StringBuilder();
                            sb.Append("  ").Append(d.DeviceName ?? "?").Append("  [").Append(string.Join(", ", flags)).Append("]");
                            if (!string.IsNullOrEmpty(d.MonitorName)) sb.Append("  \"").Append(d.MonitorName).Append("\"");
                            if (!string.IsNullOrEmpty(d.AdapterString)) sb.Append("  (").Append(d.AdapterString).Append(")");
                            if (d.CurrentMode != null) sb.Append("  ").Append(d.CurrentMode.ToKey());
                            if (d.IsActive) sb.Append("  @").Append(d.Bounds.X).Append(",").Append(d.Bounds.Y).Append(" ").Append(d.Bounds.Width).Append("x").Append(d.Bounds.Height);
                            console.WriteLine(sb.ToString());
                            console.WriteLine("      adapter 0x" + d.AdapterIdValue.ToString("X") + "  target " + d.TargetId + "  source " + d.SourceId);
                            if (!string.IsNullOrEmpty(d.MonitorDevicePath))
                            {
                                console.WriteLine("      " + d.MonitorDevicePath);
                            }

                            if (!string.IsNullOrEmpty(d.DeviceName))
                            {
                                try
                                {
                                    List<DisplayMode> modes = DisplayManager.GetSupportedModes(d.DeviceName);
                                    var keys = new List<string>();
                                    if (modes != null)
                                    {
                                        foreach (DisplayMode m in modes) keys.Add(m.ToKey());
                                    }
                                    console.WriteLine("      " + Strings.Get("diag.modes") + " (" + keys.Count + "): " + (keys.Count == 0 ? Strings.Get("diag.none") : WrapList(keys, 6, "        ")));
                                }
                                catch (Exception ex)
                                {
                                    console.WriteLine("      " + Strings.Get("diag.modes") + ": " + Strings.Get("diag.noModes") + " — " + ex.Message);
                                    Log.Warn("Diagnose: GetSupportedModes(" + d.DeviceName + ") failed: " + ex.Message);
                                }
                            }
                            console.WriteLine();
                        }
                    }

                    console.WriteLine(Strings.Get("diag.done"));
                    Log.Info("Diagnose finished.");
                }
                catch (Exception ex)
                {
                    console.WriteLine(Strings.Format("diag.error", ex.Message));
                    Log.Error("Diagnose failed.", ex);
                }
            }
            return 0;
        }

        private static string WrapList(List<string> items, int perLine, string indent)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(i % perLine == 0 ? "," + Environment.NewLine + indent : ", ");
                }
                else
                {
                    sb.Append(Environment.NewLine).Append(indent);
                }
                sb.Append(items[i]);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------------------------------
        // --restore and inter-process events
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// --restore: asks the running instance to restore and exit. With no instance running there is no session
        /// state to undo, so it performs the state-independent recovery instead: turn the laptop screen back on if
        /// Windows reports it connected but inactive (e.g. the process was killed while the screen was off).
        /// </summary>
        private static int RunRestoreRequest()
        {
            if (SignalEvent(RestoreEventName))
            {
                Log.Info(Strings.Get("cli.restoreSent"));
                return 0;
            }

            Log.Warn(Strings.Get("cli.noInstance"));
            using (var console = ConsoleSession.Open(allocIfNeeded: false))
            {
                console.WriteLine(Strings.Get("cli.noInstance"));
                console.WriteLine(Strings.Get("cli.recovering"));

                string resultText;
                bool ok;
                try
                {
                    RecoveryResult result = SessionController.RecoverInternalDisplay();
                    switch (result)
                    {
                        case RecoveryResult.Restored: resultText = Strings.Get("recover.done"); break;
                        case RecoveryResult.NoInternalDisplay: resultText = Strings.Get("recover.noInternal"); break;
                        default: resultText = Strings.Get("recover.alreadyOn"); break;
                    }
                    Log.Info("--restore recovery: " + result);
                    ok = true;
                }
                catch (Exception ex)
                {
                    Log.Error("--restore recovery failed.", ex);
                    resultText = Strings.Get("recover.failed") + Environment.NewLine + Strings.Format("err.detail", ex.Message);
                    ok = false;
                }

                console.WriteLine(resultText);
                if (!console.HasOutput)
                {
                    // Started from a shortcut (no console): tell the user what happened.
                    try
                    {
                        MessageBox.Show(resultText, Strings.Get("app.title"), MessageBoxButtons.OK,
                            ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Could not show the recovery result.", ex);
                    }
                }
                return ok ? 0 : 1;
            }
        }

        /// <summary>
        /// Secondary instance: lets the primary process call SetForegroundWindow successfully when it shows its
        /// window (Windows denies it to a process that did not receive the last input).
        /// </summary>
        private static void AllowPrimaryToComeToForeground()
        {
            try
            {
                int me = System.Diagnostics.Process.GetCurrentProcess().Id;
                string name = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                {
                    using (p)
                    {
                        if (p.Id == me)
                        {
                            continue;
                        }
                        if (!NativeMethods.AllowSetForegroundWindow(p.Id))
                        {
                            Log.Warn("AllowSetForegroundWindow(" + p.Id + ") failed, error " + Marshal.GetLastWin32Error());
                        }
                        else
                        {
                            Log.Info("Foreground permission granted to process " + p.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("AllowPrimaryToComeToForeground failed: " + ex.Message);
            }
        }

        /// <summary>Sets a named event owned by the running instance. Returns false when no instance exists.</summary>
        private static bool SignalEvent(string name)
        {
            EventWaitHandle evt;
            try
            {
                if (!EventWaitHandle.TryOpenExisting(name, out evt))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not open event " + name, ex);
                return false;
            }

            using (evt)
            {
                try
                {
                    evt.Set();
                    Log.Info("Signalled " + name);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error("Could not signal event " + name, ex);
                    return false;
                }
            }
        }

        /// <summary>Primary instance: owns the Show/Restore events and forwards them to the form on the UI thread.</summary>
        private static void StartIpcListener()
        {
            EventWaitHandle showEvent;
            EventWaitHandle restoreEvent;
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                restoreEvent = new EventWaitHandle(false, EventResetMode.AutoReset, RestoreEventName);
            }
            catch (Exception ex)
            {
                Log.Error("Could not create IPC events; --restore and bring-to-front will not work for this instance.", ex);
                return;
            }

            var thread = new Thread(() =>
            {
                var handles = new WaitHandle[] { _shutdown, showEvent, restoreEvent };
                try
                {
                    while (true)
                    {
                        int signalled = WaitHandle.WaitAny(handles);
                        if (signalled == 0)
                        {
                            break;
                        }

                        bool restore = signalled == 2;
                        Log.Info("IPC event received: " + (restore ? "Restore" : "Show"));
                        MainForm form = _form;
                        if (form == null || form.IsDisposed || !form.IsHandleCreated)
                        {
                            Log.Warn("IPC event ignored: main window not available.");
                            continue;
                        }

                        try
                        {
                            form.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    if (restore)
                                    {
                                        form.RequestRestoreAndExit();
                                    }
                                    else
                                    {
                                        form.ShowFromTray();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Error("IPC handler failed on the UI thread.", ex);
                                }
                            }));
                        }
                        catch (Exception ex)
                        {
                            Log.Error("BeginInvoke failed for IPC event.", ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("IPC listener thread crashed.", ex);
                }
                finally
                {
                    try { showEvent.Dispose(); } catch { }
                    try { restoreEvent.Dispose(); } catch { }
                }
            });
            thread.IsBackground = true;
            thread.Name = "VRMonitorSwitch.Ipc";
            thread.Start();
            Log.Info("IPC listener started (" + ShowEventName + ", " + RestoreEventName + ").");
        }

        // ------------------------------------------------------------------------------------------
        // --profile: apply on a worker thread while the window stays hidden
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Queues the CLI apply on the message loop. <see cref="MainForm.ApplyProfile"/> runs the exact same flow as
        /// clicking a card (worker thread, progress, settings saved on success, balloon + tray; window shown with the
        /// error on failure), so the CLI and the GUI can never drift apart.
        /// </summary>
        private static void StartCliApply(AppSettings settings, Profile profile, DisplayMode mode, bool laptopOff)
        {
            if (mode == null)
            {
                mode = profile.DefaultMode;
            }
            Log.Info(Strings.Format("cli.applying", profile.Id, mode.ToKey()));

            MainForm form = _form;
            if (form == null || form.IsDisposed || !form.IsHandleCreated)
            {
                Log.Error("Command-line apply skipped: main window not available.");
                return;
            }

            try
            {
                // BeginInvoke so the apply starts once Application.Run() is pumping messages.
                form.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        form.ApplyProfile(profile, mode, laptopOff);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Command-line apply failed to start.", ex);
                        try
                        {
                            form.ShowFromTray();
                            MessageBox.Show(form, Strings.Format("err.detail", ex.Message), Strings.Get("app.title"),
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        catch (Exception ex2)
                        {
                            Log.Error("Could not report the CLI apply failure.", ex2);
                        }
                    }
                }));
            }
            catch (Exception ex)
            {
                Log.Error("BeginInvoke failed for the command-line apply.", ex);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Safety net: always put the displays back
        // ------------------------------------------------------------------------------------------

        private static void SafeRestore(string reason)
        {
            SessionController c = _controller;
            if (c == null)
            {
                return;
            }
            try
            {
                // Restore() is idempotent and cheap when there is nothing to undo.
                if (c.State != SessionState.Idle)
                {
                    Log.Info("Restore triggered by: " + reason);
                }
                c.Restore();
            }
            catch (Exception ex)
            {
                Log.Error("Restore (" + reason + ") failed.", ex);
            }
        }

        private static void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            Log.Warn("Windows session ending (" + e.Reason + ").");
            SafeRestore("SessionEnding");
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            SafeRestore("ProcessExit");
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            // UnhandledExceptionMode.CatchException keeps the process alive, so a benign WinForms exception
            // (paint, layout, balloon tip) must not tear down the VR session the user is inside. Only log and
            // tell the user; the session is restored on real termination (UnhandledException, ProcessExit).
            Log.Error("Unhandled UI thread exception (session kept).", e.Exception);
            try
            {
                MessageBox.Show(Strings.Format("err.uiError", e.Exception.Message), Strings.Get("err.fatal.title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                Log.Error("Could not show the error dialog.", ex);
            }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Error("Unhandled exception (terminating=" + e.IsTerminating + ").", e.ExceptionObject as Exception);
            SafeRestore("UnhandledException");
        }

        // ------------------------------------------------------------------------------------------

        private static string Version
        {
            get
            {
                try
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    var info = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(asm, typeof(AssemblyInformationalVersionAttribute));
                    if (info != null && !string.IsNullOrEmpty(info.InformationalVersion))
                    {
                        return info.InformationalVersion;
                    }
                    return asm.GetName().Version.ToString(3);
                }
                catch
                {
                    return "?";
                }
            }
        }

        /// <summary>
        /// Attaches to the parent console (so output lands in the cmd/PowerShell window that launched us) or
        /// allocates a new one. A WinExe has no console by default.
        /// </summary>
        private sealed class ConsoleSession : IDisposable
        {
            private readonly bool _allocated;
            private readonly bool _attached;
            private readonly bool _redirected;
            private readonly TextWriter _out;

            private ConsoleSession(bool attached, bool allocated, bool redirected = false)
            {
                _attached = attached;
                _allocated = allocated;
                _redirected = redirected;
                TextWriter writer = null;
                if (attached || allocated || redirected)
                {
                    try
                    {
                        var stream = Console.OpenStandardOutput();
                        var sw = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                        Console.SetOut(sw);
                        writer = sw;
                        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Console output redirection failed: " + ex.Message);
                    }
                }
                _out = writer;
            }

            /// <summary>True when <see cref="WriteLine(string)"/> reaches a console, file or pipe.</summary>
            public bool HasOutput
            {
                get { return _out != null; }
            }

            public static ConsoleSession Open(bool allocIfNeeded = true)
            {
                bool attached = false;
                bool allocated = false;
                try
                {
                    // "VRMonitorSwitch.exe --diagnose > report.txt": stdout is already a file/pipe, use it as is.
                    if (IsStdOutRedirected())
                    {
                        return new ConsoleSession(false, false, redirected: true);
                    }

                    attached = NativeMethods.AttachConsole(NativeMethods.AttachParentProcess);
                    if (!attached && allocIfNeeded)
                    {
                        allocated = NativeMethods.AllocConsole();
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("AttachConsole/AllocConsole failed: " + ex.Message);
                }
                return new ConsoleSession(attached, allocated);
            }

            private static bool IsStdOutRedirected()
            {
                try
                {
                    IntPtr h = NativeMethods.GetStdHandle(NativeMethods.StdOutputHandle);
                    if (h == IntPtr.Zero || h == NativeMethods.InvalidHandleValue)
                    {
                        return false;
                    }
                    uint type = NativeMethods.GetFileType(h);
                    return type == NativeMethods.FileTypeDisk || type == NativeMethods.FileTypePipe;
                }
                catch
                {
                    return false;
                }
            }

            public void WriteLine()
            {
                WriteLine(string.Empty);
            }

            public void WriteLine(string text)
            {
                if (_out == null)
                {
                    return;
                }
                try
                {
                    _out.WriteLine(text);
                }
                catch (Exception ex)
                {
                    Log.Warn("Console write failed: " + ex.Message);
                }
            }

            public void Dispose()
            {
                if (_allocated)
                {
                    // We created a brand-new window: give the user a chance to read it.
                    WriteLine();
                    WriteLine(Strings.Get("cli.pressEnter"));
                    try { Console.ReadLine(); } catch { }
                }
                else if (_attached)
                {
                    // The shell prompt was already printed before we attached; move to a fresh line.
                    WriteLine();
                }
                try { _out?.Flush(); } catch { }
                if (_attached || _allocated)
                {
                    try { NativeMethods.FreeConsole(); } catch { }
                }
            }
        }

        private static class NativeMethods
        {
            public const int AttachParentProcess = -1;
            public const int StdOutputHandle = -11;
            public const uint FileTypeDisk = 0x0001;
            public const uint FileTypePipe = 0x0003;
            public static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr GetStdHandle(int nStdHandle);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern uint GetFileType(IntPtr hFile);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AttachConsole(int dwProcessId);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AllocConsole();

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool FreeConsole();

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AllowSetForegroundWindow(int processId);
        }
    }
}
