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
        static void Main()
        {
            Native.SetProcessDPIAware();
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                WriteLog("fatal exception: " + e.ExceptionObject);
            };
            WriteLog("Qoder HUD " + Version + " starting");

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
    }
}
