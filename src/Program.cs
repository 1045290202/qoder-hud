using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Qoder HUD")]
[assembly: System.Reflection.AssemblyDescription("Qoder task dashboard for a secondary display")]
[assembly: System.Reflection.AssemblyProduct("Qoder HUD")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace QoderHud
{
    static class Program
    {
        public const string Version = "1.0.0";
        static readonly object LogLock = new object();

        public static void WriteLog(string msg)
        {
            try
            {
                AppPaths.Ensure();
                string path = AppPaths.LogFile;
                lock (LogLock)
                {
                    FileInfo fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 512 * 1024) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Native.SetProcessDPIAware();
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                WriteLog("fatal exception: " + e.ExceptionObject);
            };
            WriteLog("Qoder HUD " + Version + " starting");

            bool detailView;
            string screenshotPath = ParseScreenshotPath(args, out detailView);
            if (screenshotPath != null)
            {
                RunScreenshot(screenshotPath, detailView);
                return;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\QoderHud", out createdNew))
            {
                if (!createdNew) return;
                Forms.Application.EnableVisualStyles();
                Forms.Application.SetCompatibleTextRenderingDefault(false);
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += delegate (object s, DispatcherUnhandledExceptionEventArgs e)
                {
                    WriteLog("dispatcher exception: " + e.Exception);
                    e.Handled = true;
                };
                try
                {
                    app.Run(new HudWindow());
                }
                catch (Exception ex)
                {
                    WriteLog("fatal: " + ex);
                }
            }
        }

        static string ParseScreenshotPath(string[] args, out bool detailView)
        {
            detailView = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] ?? "";
                string path = null;
                bool detail = false;
                if (a.Equals("--screenshot", StringComparison.OrdinalIgnoreCase)
                    || a.Equals("--shot", StringComparison.OrdinalIgnoreCase)
                    || a.Equals("-screenshot", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && !string.IsNullOrEmpty(args[i + 1])
                        && !args[i + 1].StartsWith("-"))
                    {
                        path = args[++i];
                    }
                }
                else if (a.Equals("--screenshot-detail", StringComparison.OrdinalIgnoreCase)
                    || a.Equals("--shot-detail", StringComparison.OrdinalIgnoreCase))
                {
                    detail = true;
                    if (i + 1 < args.Length && !string.IsNullOrEmpty(args[i + 1])
                        && !args[i + 1].StartsWith("-"))
                    {
                        path = args[++i];
                    }
                }
                else if (a.StartsWith("--screenshot=", StringComparison.OrdinalIgnoreCase))
                {
                    path = a.Substring("--screenshot=".Length);
                }
                else if (a.StartsWith("--shot=", StringComparison.OrdinalIgnoreCase))
                {
                    path = a.Substring("--shot=".Length);
                }
                else if (a.StartsWith("--screenshot-detail=", StringComparison.OrdinalIgnoreCase))
                {
                    detail = true;
                    path = a.Substring("--screenshot-detail=".Length);
                }
                else if (a.StartsWith("--shot-detail=", StringComparison.OrdinalIgnoreCase))
                {
                    detail = true;
                    path = a.Substring("--shot-detail=".Length);
                }
                else continue;

                path = (path ?? "").Trim().Trim('"');
                if (path.Length == 0) path = detail ? "hud-detail-preview.png" : "hud-preview.png";
                try { path = Path.GetFullPath(path); }
                catch { }
                detailView = detail;
                return path;
            }
            return null;
        }

        static void RunScreenshot(string path, bool detailView)
        {
            WriteLog("screenshot mode path=" + path + " view=" + (detailView ? "detail" : "main"));
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                HudWindow window = new HudWindow(true);
                window.LoadDemoData(detailView);
                window.SaveScreenshot(path, 2.0);
                WriteLog("screenshot saved " + path);
            }
            catch (Exception ex)
            {
                WriteLog("screenshot failed: " + ex);
                Environment.ExitCode = 1;
                MessageBox.Show("截图失败：\n" + ex.Message, "Qoder HUD 截图模式",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
