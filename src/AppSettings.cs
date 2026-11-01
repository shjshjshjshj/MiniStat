using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MiniStat
{
    public class AppSettings
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string PathName { get; private set; }

        public int X = int.MinValue;
        public int Y = int.MinValue;
        public float Scale;
        public int OpacityPercent = 92;
        public bool AlwaysOnTop = true;
        public bool ClickThrough;
        public int RefreshMs = 1000;
        public int MonitorIndex = -1;
        public bool FpsEnabled = true;
        public bool CompactMode;
        public bool HoverExpand = true;

        public static string DefaultPath
        {
            get
            {
                return DefaultPathFor("");
            }
        }

        // A named profile gives a second copy its own settings file, which keeps
        // a test instance from overwriting the user's saved position.
        public static string DefaultPathFor(string profile)
        {
            string name = string.IsNullOrEmpty(profile) ? "settings.ini" : "settings-" + profile + ".ini";
            {
                foreach (string root in CandidateRoots())
                {
                    try
                    {
                        string dir = Path.Combine(root, "MiniStat");
                        Directory.CreateDirectory(dir);
                        string file = Path.Combine(dir, name);
                        using (File.Open(file, FileMode.Append, FileAccess.Write)) { }
                        return file;
                    }
                    catch { }
                }
                return Path.Combine(Path.GetTempPath(), "MiniStat", name);
            }
        }

        private static IEnumerable<string> CandidateRoots()
        {
            string appData = null;
            string localAppData = null;
            string userProfile = null;
            try { appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } catch { }
            try { localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } catch { }
            try { userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch { }
            if (!string.IsNullOrEmpty(appData)) yield return appData;
            if (!string.IsNullOrEmpty(localAppData)) yield return localAppData;
            if (!string.IsNullOrEmpty(userProfile)) yield return userProfile;
            string temp = null;
            try { temp = Path.GetTempPath(); } catch { }
            if (!string.IsNullOrEmpty(temp)) yield return temp;
        }

        public static AppSettings Load()
        {
            return Load("");
        }

        public static AppSettings Load(string profile)
        {
            var s = new AppSettings();
            s.PathName = DefaultPathFor(profile);
            try
            {
                if (File.Exists(s.PathName))
                {
                    foreach (string raw in File.ReadAllLines(s.PathName))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("[")) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        s._values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                }
            }
            catch { }
            s.X = s.GetInt("x", int.MinValue);
            s.Y = s.GetInt("y", int.MinValue);
            s.Scale = (float)s.GetDouble("scale", 0);
            s.OpacityPercent = s.GetInt("opacity", 92);
            s.AlwaysOnTop = s.GetBool("alwaysOnTop", true);
            s.ClickThrough = s.GetBool("clickThrough", false);
            s.RefreshMs = s.GetInt("refreshMs", 1000);
            s.MonitorIndex = s.GetInt("monitor", -1);
            s.FpsEnabled = s.GetBool("fps", true);
            s.CompactMode = s.GetBool("compact", false);
            s.HoverExpand = s.GetBool("hoverExpand", true);
            if (s.Scale < 0.5f || s.Scale > 3.0f) s.Scale = 0;
            return s;
        }

        public void Save()
        {
            try
            {
                _values["x"] = X.ToString(CultureInfo.InvariantCulture);
                _values["y"] = Y.ToString(CultureInfo.InvariantCulture);
                _values["scale"] = Scale.ToString("0.###", CultureInfo.InvariantCulture);
                _values["opacity"] = OpacityPercent.ToString(CultureInfo.InvariantCulture);
                _values["alwaysOnTop"] = AlwaysOnTop ? "1" : "0";
                _values["clickThrough"] = ClickThrough ? "1" : "0";
                _values["refreshMs"] = RefreshMs.ToString(CultureInfo.InvariantCulture);
                _values["monitor"] = MonitorIndex.ToString(CultureInfo.InvariantCulture);
                _values["fps"] = FpsEnabled ? "1" : "0";
                _values["compact"] = CompactMode ? "1" : "0";
                _values["hoverExpand"] = HoverExpand ? "1" : "0";
                var lines = new List<string>();
                lines.Add("[MiniStat]");
                foreach (var kv in _values) lines.Add(kv.Key + "=" + kv.Value);
                string dir = Path.GetDirectoryName(PathName);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(PathName, lines.ToArray());
            }
            catch { }
        }

        private int GetInt(string key, int fallback)
        {
            string v;
            int n;
            if (_values.TryGetValue(key, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return fallback;
        }

        private double GetDouble(string key, double fallback)
        {
            string v;
            double d;
            if (_values.TryGetValue(key, out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return fallback;
        }

        private bool GetBool(string key, bool fallback)
        {
            string v;
            if (_values.TryGetValue(key, out v)) return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
            return fallback;
        }
    }
}
