using System;
using System.IO;

namespace QoderHud
{
    static class AppPaths
    {
        public static string ConfigDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QoderHud"); }
        }

        public static string ConfigFile
        {
            get { return Path.Combine(ConfigDir, "config.json"); }
        }

        public static string TopMostFile
        {
            get { return Path.Combine(ConfigDir, "topmost.txt"); }
        }

        public static string LocalDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QoderHud"); }
        }

        public static string LogFile
        {
            get { return Path.Combine(LocalDir, "hud.log"); }
        }

        public static string LegacyTopMostFile
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qoder", "hud", "topmost.txt");
            }
        }

        public static void Ensure()
        {
            Directory.CreateDirectory(ConfigDir);
            Directory.CreateDirectory(LocalDir);
        }
    }
}
