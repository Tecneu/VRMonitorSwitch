using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace VRMonitorSwitch.Core
{
    /// <summary>
    /// User preferences persisted as JSON in <c>%APPDATA%\VRMonitorSwitch\settings.json</c>.
    /// <see cref="Load"/> and <see cref="Save"/> never throw; problems are logged and defaults are used.
    /// </summary>
    public sealed class AppSettings
    {
        private const int MinKeepAliveMs = 20;
        private const int MaxKeepAliveMs = 95;     // the driver drops displays after ~100 ms without UPDATE
        private const int MinAppearTimeoutMs = 3000;
        private const int MaxAppearTimeoutMs = 120000;

        private static readonly string _filePath = ResolvePath();

        /// <summary>"dual" | "ultrawide" | "single" — last profile the user applied (null on first run).</summary>
        public string LastProfileId { get; set; }

        /// <summary>profileId → "WxH@Hz" last chosen for that profile.</summary>
        public Dictionary<string, string> LastModeByProfile { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>When false the wizard skips the laptop-screen question and uses <see cref="LaptopScreenOffDefault"/>.</summary>
        public bool AskLaptopScreenEveryTime { get; set; } = true;

        /// <summary>Preselected / remembered answer to "turn the laptop screen off?".</summary>
        public bool LaptopScreenOffDefault { get; set; } = true;

        /// <summary>Extra "WxH@Hz" modes appended to every profile's list (power users: 72 / 90 Hz, odd sizes).</summary>
        public List<string> ExtraModes { get; set; } = new List<string>();

        /// <summary>Parsec VDD keep-alive period. The driver needs one UPDATE at least every 100 ms.</summary>
        public int KeepAliveIntervalMs { get; set; } = 50;

        /// <summary>How long to wait for Windows to show the new virtual displays.</summary>
        public int DisplayAppearTimeoutMs { get; set; } = 30000;

        /// <summary>null = follow Windows; "es" or "en" to force.</summary>
        public string Language { get; set; }

        /// <summary><c>%APPDATA%\VRMonitorSwitch\settings.json</c></summary>
        public static string FilePath
        {
            get { return _filePath; }
        }

        private static string ResolvePath()
        {
            try
            {
                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(baseDir))
                {
                    baseDir = Path.GetTempPath();
                }
                return Path.Combine(baseDir, "VRMonitorSwitch", "settings.json");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "VRMonitorSwitch", "settings.json");
            }
        }

        /// <summary>Reads the settings file. Never throws: any problem is logged and defaults are returned.</summary>
        public static AppSettings Load()
        {
            AppSettings result = null;
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var serializer = new JavaScriptSerializer();
                        result = serializer.Deserialize<AppSettings>(json);
                    }
                    Log.Info("Settings loaded from " + _filePath);
                }
                else
                {
                    Log.Info("Settings file not found, using defaults: " + _filePath);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not read settings from " + _filePath + "; using defaults.", ex);
                result = null;
            }

            if (result == null)
            {
                result = new AppSettings();
            }
            result.Sanitize();
            return result;
        }

        /// <summary>Writes the settings file atomically (temp file + replace). Never throws.</summary>
        public void Save()
        {
            try
            {
                Sanitize();
                string dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var serializer = new JavaScriptSerializer();
                string json = PrettyPrint(serializer.Serialize(this));

                string tmp = _filePath + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(_filePath))
                {
                    File.Replace(tmp, _filePath, null);
                }
                else
                {
                    File.Move(tmp, _filePath);
                }
                Log.Info("Settings saved to " + _filePath);
            }
            catch (Exception ex)
            {
                Log.Error("Could not save settings to " + _filePath, ex);
            }
        }

        /// <summary>Repairs nulls and clamps numeric values to safe ranges.</summary>
        private void Sanitize()
        {
            if (LastModeByProfile == null)
            {
                LastModeByProfile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            else if (!(LastModeByProfile.Comparer is StringComparer))
            {
                // JavaScriptSerializer builds a case-sensitive dictionary; rebuild with our comparer.
                LastModeByProfile = new Dictionary<string, string>(LastModeByProfile, StringComparer.OrdinalIgnoreCase);
            }

            if (ExtraModes == null)
            {
                ExtraModes = new List<string>();
            }
            else
            {
                var clean = new List<string>();
                foreach (string s in ExtraModes)
                {
                    DisplayMode m;
                    if (DisplayMode.TryParse(s, out m))
                    {
                        string key = m.ToKey();
                        if (!clean.Contains(key))
                        {
                            clean.Add(key);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(s))
                    {
                        Log.Warn("Ignoring invalid ExtraModes entry: '" + s + "'");
                    }
                }
                ExtraModes = clean;
            }

            if (KeepAliveIntervalMs < MinKeepAliveMs || KeepAliveIntervalMs > MaxKeepAliveMs)
            {
                Log.Warn("KeepAliveIntervalMs=" + KeepAliveIntervalMs + " out of range, using 50.");
                KeepAliveIntervalMs = 50;
            }

            if (DisplayAppearTimeoutMs < MinAppearTimeoutMs || DisplayAppearTimeoutMs > MaxAppearTimeoutMs)
            {
                Log.Warn("DisplayAppearTimeoutMs=" + DisplayAppearTimeoutMs + " out of range, using 15000.");
                DisplayAppearTimeoutMs = 15000;
            }

            if (!string.IsNullOrWhiteSpace(Language))
            {
                string lang = Language.Trim().ToLowerInvariant();
                Language = lang.StartsWith("es", StringComparison.Ordinal) ? "es" : "en";
            }
            else
            {
                Language = null;
            }

            if (!string.IsNullOrWhiteSpace(LastProfileId) && Profiles.FindById(LastProfileId) == null)
            {
                Log.Warn("Unknown LastProfileId '" + LastProfileId + "' in settings; ignoring.");
                LastProfileId = null;
            }
        }

        // ------------------------------------------------------------------------------------------
        // Convenience helpers for the UI / CLI (not part of the persisted shape).
        // ------------------------------------------------------------------------------------------

        /// <summary>Last mode used with <paramref name="profile"/>, or its default when none/invalid.</summary>
        public DisplayMode GetLastMode(Profile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            string key;
            DisplayMode mode;
            if (LastModeByProfile != null && LastModeByProfile.TryGetValue(profile.Id, out key) && DisplayMode.TryParse(key, out mode))
            {
                return mode;
            }
            return profile.DefaultMode;
        }

        /// <summary>Remembers <paramref name="mode"/> for <paramref name="profile"/> and marks it as the last profile. Does not save.</summary>
        public void SetLastMode(Profile profile, DisplayMode mode)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (mode == null) throw new ArgumentNullException(nameof(mode));

            if (LastModeByProfile == null)
            {
                LastModeByProfile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            LastModeByProfile[profile.Id] = mode.ToKey();
            LastProfileId = profile.Id;
        }

        /// <summary>The profile's built-in modes followed by any valid <see cref="ExtraModes"/> (distinct).</summary>
        public List<DisplayMode> GetModesFor(Profile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var list = new List<DisplayMode>(profile.Modes);
            if (ExtraModes != null)
            {
                foreach (string s in ExtraModes)
                {
                    DisplayMode m;
                    if (DisplayMode.TryParse(s, out m) && !list.Contains(m))
                    {
                        list.Add(m);
                    }
                }
            }
            return list;
        }

        // ------------------------------------------------------------------------------------------
        // JavaScriptSerializer emits a single line; indent it so the file can be edited by hand.
        // ------------------------------------------------------------------------------------------
        private static string PrettyPrint(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return json;
            }

            var sb = new StringBuilder(json.Length * 2);
            int indent = 0;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (inString)
                {
                    sb.Append(c);
                    if (escape)
                    {
                        escape = false;
                    }
                    else if (c == '\\')
                    {
                        escape = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        sb.Append(c);
                        break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        // Keep empty containers compact: {} / []
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']'))
                        {
                            sb.Append(json[i + 1]);
                            i++;
                        }
                        else
                        {
                            indent++;
                            sb.AppendLine().Append(' ', indent * 2);
                        }
                        break;
                    case '}':
                    case ']':
                        indent = Math.Max(0, indent - 1);
                        sb.AppendLine().Append(' ', indent * 2).Append(c);
                        break;
                    case ',':
                        sb.Append(c).AppendLine().Append(' ', indent * 2);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        if (!char.IsWhiteSpace(c))
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }

            sb.AppendLine();
            return sb.ToString();
        }
    }
}
