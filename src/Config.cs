using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace QoderHud
{
    sealed class HudConfig
    {
        public string Display = "auto";
        public string Position = "auto";
        public double Scale = 1.0;
        public bool TopMost = true;
        public string QoderDataDir = "";
        public string JetBrainsLogDir = "";
    }

    static class HudConfigStore
    {
        const string Template =
            "{\r\n" +
            "  \"display\": \"auto\",\r\n" +
            "  \"position\": \"auto\",\r\n" +
            "  \"scale\": 1.0,\r\n" +
            "  \"topmost\": true,\r\n" +
            "  \"qoderDataDir\": \"\",\r\n" +
            "  \"jetbrainsLogDir\": \"\"\r\n" +
            "}\r\n";

        public static void EnsureFile()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.ConfigDir);
                if (!File.Exists(AppPaths.ConfigFile))
                    File.WriteAllText(AppPaths.ConfigFile, Template, new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        public static HudConfig Load()
        {
            HudConfig cfg = new HudConfig();
            try
            {
                if (!File.Exists(AppPaths.ConfigFile)) return Normalize(cfg);
                string text = File.ReadAllText(AppPaths.ConfigFile, Encoding.UTF8);
                System.Collections.Generic.Dictionary<string, string> f = JsonProbe.ParseObject(text);
                if (f != null)
                {
                    string v;
                    if (f.TryGetValue("display", out v) && !string.IsNullOrEmpty(v)) cfg.Display = v.Trim();
                    if (f.TryGetValue("position", out v) && !string.IsNullOrEmpty(v)) cfg.Position = v.Trim();
                    if (f.TryGetValue("qoderDataDir", out v)) cfg.QoderDataDir = (v ?? "").Trim();
                    if (f.TryGetValue("jetbrainsLogDir", out v)) cfg.JetBrainsLogDir = (v ?? "").Trim();

                    if (f.TryGetValue("scale", out v) && !string.IsNullOrEmpty(v))
                    {
                        double d;
                        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) cfg.Scale = d;
                    }
                    if (f.TryGetValue("topmost", out v) && !string.IsNullOrEmpty(v))
                    {
                        bool b;
                        string t = v.Trim().ToLowerInvariant();
                        if (bool.TryParse(t, out b)) cfg.TopMost = b;
                        else if (t == "1") cfg.TopMost = true;
                        else if (t == "0") cfg.TopMost = false;
                    }
                }
            }
            catch
            {
            }
            return Normalize(cfg);
        }

        static HudConfig Normalize(HudConfig cfg)
        {
            if (cfg.Scale < 0.5) cfg.Scale = 0.5;
            if (cfg.Scale > 2.0) cfg.Scale = 2.0;
            string p = (cfg.Position ?? "auto").Trim().ToLowerInvariant();
            if (p != "auto" && p != "center" && p != "top-left" && p != "top-right"
                && p != "bottom-left" && p != "bottom-right")
                p = "auto";
            cfg.Position = p;
            string d = (cfg.Display ?? "auto").Trim();
            cfg.Display = d.Length == 0 ? "auto" : d;
            cfg.QoderDataDir = (cfg.QoderDataDir ?? "").Trim();
            cfg.JetBrainsLogDir = (cfg.JetBrainsLogDir ?? "").Trim();
            return cfg;
        }
    }
}
