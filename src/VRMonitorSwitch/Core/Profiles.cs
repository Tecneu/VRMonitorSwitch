using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace VRMonitorSwitch.Core
{
    /// <summary>The three built-in monitor layouts offered by the wizard.</summary>
    public enum ProfileKind
    {
        /// <summary>Two standard 16:9 virtual monitors side by side.</summary>
        DualStandard = 0,
        /// <summary>One very wide (32:9 / 21:9) virtual monitor.</summary>
        UltraWide = 1,
        /// <summary>One standard virtual monitor.</summary>
        SingleStandard = 2
    }

    /// <summary>
    /// A display mode (resolution + refresh rate). Immutable by convention, value-equal.
    /// Textual form is <c>"5120x1440@60"</c> (see <see cref="ToKey"/> / <see cref="Parse"/>).
    /// </summary>
    public sealed class DisplayMode : IEquatable<DisplayMode>
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Hz { get; set; }

        public DisplayMode()
        {
        }

        public DisplayMode(int w, int h, int hz)
        {
            Width = w;
            Height = h;
            Hz = hz;
        }

        /// <summary>Pixel count, handy for "closest mode" comparisons.</summary>
        public long Area
        {
            get { return (long)Width * Height; }
        }

        /// <summary>
        /// Marketing aspect label: "16:9", "21:9", "32:9", "16:10", "24:10" (plus 4:3, 5:4, 3:2).
        /// Falls back to the reduced integer ratio for anything exotic.
        /// </summary>
        public string AspectLabel
        {
            get
            {
                if (Width <= 0 || Height <= 0)
                {
                    return "?";
                }

                double r = (double)Width / Height;

                // Order matters: 24:10 (2.40) sits right next to the 21:9 family (2.33 - 2.39).
                if (Math.Abs(r - 32.0 / 9.0) < 0.05) return "32:9";
                if (Math.Abs(r - 2.4) < 0.006) return "24:10";
                if (r >= 2.30 && r <= 2.40) return "21:9";
                if (Math.Abs(r - 1.6) < 0.01) return "16:10";
                if (Math.Abs(r - 16.0 / 9.0) < 0.03) return "16:9";
                if (Math.Abs(r - 1.5) < 0.01) return "3:2";
                if (Math.Abs(r - 4.0 / 3.0) < 0.01) return "4:3";
                if (Math.Abs(r - 1.25) < 0.01) return "5:4";

                int g = Gcd(Width, Height);
                return (Width / g).ToString(CultureInfo.InvariantCulture) + ":" + (Height / g).ToString(CultureInfo.InvariantCulture);
            }
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                int t = a % b;
                a = b;
                b = t;
            }
            return a == 0 ? 1 : a;
        }

        /// <summary>Human readable form, e.g. "5120 × 1440 @ 60 Hz (32:9)".</summary>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} × {1} @ {2} Hz ({3})", Width, Height, Hz, AspectLabel);
        }

        /// <summary>Compact, culture-independent key: "5120x1440@60".</summary>
        public string ToKey()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}x{1}@{2}", Width, Height, Hz);
        }

        /// <summary>
        /// Parses "5120x1440@60" (also tolerates "5120 × 1440 @ 60 Hz", "5120X1440", uppercase, spaces).
        /// The refresh rate is optional and defaults to 60.
        /// </summary>
        /// <exception cref="FormatException">The text is not a valid mode.</exception>
        public static DisplayMode Parse(string s)
        {
            DisplayMode mode;
            if (!TryParse(s, out mode))
            {
                throw new FormatException("Invalid display mode: '" + (s ?? "<null>") + "'. Expected WIDTHxHEIGHT[@HZ], e.g. 5120x1440@60.");
            }
            return mode;
        }

        /// <summary>Non-throwing variant of <see cref="Parse"/>.</summary>
        public static bool TryParse(string s, out DisplayMode mode)
        {
            mode = null;
            if (string.IsNullOrWhiteSpace(s))
            {
                return false;
            }

            string t = s.Trim().ToLowerInvariant().Replace(" ", "").Replace("×", "x").Replace("hz", "");
            int hz = 60;
            int at = t.IndexOf('@');
            if (at >= 0)
            {
                string hzText = t.Substring(at + 1);
                t = t.Substring(0, at);
                if (hzText.Length > 0 && !int.TryParse(hzText, NumberStyles.Integer, CultureInfo.InvariantCulture, out hz))
                {
                    return false;
                }
            }

            string[] parts = t.Split('x');
            if (parts.Length != 2)
            {
                return false;
            }

            int w, h;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out w) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out h))
            {
                return false;
            }

            if (w <= 0 || h <= 0 || hz <= 0)
            {
                return false;
            }

            mode = new DisplayMode(w, h, hz);
            return true;
        }

        public bool Equals(DisplayMode other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Width == other.Width && Height == other.Height && Hz == other.Hz;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DisplayMode);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Width;
                h = h * 31 + Height;
                h = h * 31 + Hz;
                return h;
            }
        }

        public static bool operator ==(DisplayMode a, DisplayMode b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (ReferenceEquals(a, null) || ReferenceEquals(b, null)) return false;
            return a.Equals(b);
        }

        public static bool operator !=(DisplayMode a, DisplayMode b)
        {
            return !(a == b);
        }
    }

    /// <summary>A monitor layout: how many virtual displays and which resolutions make sense for it.</summary>
    public sealed class Profile
    {
        public ProfileKind Kind { get; }

        /// <summary>Stable identifier used on the CLI and in settings: "dual", "ultrawide", "single".</summary>
        public string Id { get; }

        /// <summary>Number of virtual displays this profile creates (2, 1, 1).</summary>
        public int MonitorCount { get; }

        /// <summary>Resolution preselected for new users.</summary>
        public DisplayMode DefaultMode { get; }

        /// <summary><see cref="DefaultMode"/> first, then the alternatives, no duplicates.</summary>
        public IReadOnlyList<DisplayMode> Modes { get; }

        /// <summary>Localized display name (key <c>profile.&lt;id&gt;.name</c>).</summary>
        public string Name
        {
            get { return Strings.Get("profile." + Id + ".name"); }
        }

        /// <summary>Localized one-line description (key <c>profile.&lt;id&gt;.desc</c>).</summary>
        public string Description
        {
            get { return Strings.Get("profile." + Id + ".desc"); }
        }

        internal Profile(ProfileKind kind, string id, int monitorCount, DisplayMode defaultMode, params DisplayMode[] alternatives)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (defaultMode == null) throw new ArgumentNullException(nameof(defaultMode));
            if (monitorCount <= 0) throw new ArgumentOutOfRangeException(nameof(monitorCount));

            Kind = kind;
            Id = id;
            MonitorCount = monitorCount;
            DefaultMode = defaultMode;

            var list = new List<DisplayMode> { defaultMode };
            if (alternatives != null)
            {
                foreach (DisplayMode m in alternatives)
                {
                    if (m != null && !list.Contains(m))
                    {
                        list.Add(m);
                    }
                }
            }
            Modes = new ReadOnlyCollection<DisplayMode>(list);
        }

        public override string ToString()
        {
            return Id;
        }
    }

    /// <summary>The built-in profile catalogue.</summary>
    public static class Profiles
    {
        private static readonly ReadOnlyCollection<Profile> _all = new ReadOnlyCollection<Profile>(new List<Profile>
        {
            new Profile(ProfileKind.DualStandard, "dual", 2,
                new DisplayMode(1920, 1080, 60),
                new DisplayMode(2560, 1440, 60),
                new DisplayMode(1600, 900, 60),
                new DisplayMode(1280, 720, 60)),

            new Profile(ProfileKind.UltraWide, "ultrawide", 1,
                new DisplayMode(5120, 1440, 60),
                new DisplayMode(3840, 1080, 60),
                new DisplayMode(3840, 1600, 60),
                new DisplayMode(3440, 1440, 60),
                new DisplayMode(2560, 1080, 60)),

            new Profile(ProfileKind.SingleStandard, "single", 1,
                new DisplayMode(1920, 1080, 60),
                new DisplayMode(2560, 1440, 60),
                new DisplayMode(3840, 2160, 60),
                new DisplayMode(1920, 1200, 60),
                new DisplayMode(2560, 1600, 60))
        });

        /// <summary>All profiles in wizard order: dual, ultrawide, single.</summary>
        public static IReadOnlyList<Profile> All
        {
            get { return _all; }
        }

        public static Profile Get(ProfileKind kind)
        {
            foreach (Profile p in _all)
            {
                if (p.Kind == kind)
                {
                    return p;
                }
            }
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown profile kind.");
        }

        /// <summary>Case-insensitive lookup by <see cref="Profile.Id"/>; returns null when unknown.</summary>
        public static Profile FindById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            string wanted = id.Trim();
            foreach (Profile p in _all)
            {
                if (string.Equals(p.Id, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }
            return null;
        }
    }
}
