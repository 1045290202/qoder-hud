using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace QoderHud
{
    static class HookProgram
    {
        static void Main()
        {
            try
            {
                string text = null;
                Thread th = new Thread(delegate ()
                {
                    try
                    {
                        using (Stream stdin = Console.OpenStandardInput())
                        using (StreamReader sr = new StreamReader(stdin, new UTF8Encoding(false)))
                        {
                            text = sr.ReadToEnd();
                        }
                    }
                    catch
                    {
                    }
                });
                th.IsBackground = true;
                th.Start();
                if (!th.Join(800)) return;
                if (string.IsNullOrEmpty(text)) return;

                string chain = ParentChain();
                string env = EnvInfo();
                string origin = DetectOrigin(chain, env);
                text = Inject(text, origin, chain, env);

                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", "QoderHud", PipeDirection.Out))
                {
                    pipe.Connect(250);
                    byte[] data = Encoding.UTF8.GetBytes(text);
                    pipe.Write(data, 0, data.Length);
                    pipe.Flush();
                }
            }
            catch
            {
            }
        }

        static string DetectOrigin(string chain, string env)
        {
            string e = " " + env.ToLowerInvariant() + " ";
            if (e.Contains("qoder_hook_source=jb")) return "JetBrains";
            if (e.Contains("qoder_business_product=app") || e.Contains("qoder_session_type=app")
                || e.Contains("qoder_client_type=10"))
                return "Desktop";
            string[] names = chain.Split(new string[] { "<-" }, StringSplitOptions.None);
            foreach (string raw in names)
            {
                string n = raw.ToLowerInvariant();
                if (n.Length == 0) continue;
                if (n.StartsWith("qodercli")) return "CLI";
                if (n == "qoder.exe" || n == "qoder") return "Desktop";
                if (IsIde(n)) return "JetBrains";
            }
            if (e.Contains("qoder_hook_source=cli")) return "CLI";
            return "Unknown";
        }

        static bool IsIde(string name)
        {
            return name.StartsWith("webstorm") || name.StartsWith("idea")
                || name.StartsWith("pycharm") || name.StartsWith("goland")
                || name.StartsWith("clion") || name.StartsWith("phpstorm")
                || name.StartsWith("rubymine") || name.StartsWith("rider")
                || name.StartsWith("studio64") || name.StartsWith("fleet");
        }

        static string ParentChain()
        {
            Dictionary<uint, uint> parents = new Dictionary<uint, uint>();
            Dictionary<uint, string> names = new Dictionary<uint, string>();
            IntPtr snap = CreateToolhelp32Snapshot(0x00000002, 0);
            if (snap != IntPtr.Zero && snap != new IntPtr(-1))
            {
                try
                {
                    PROCESSENTRY32 pe = new PROCESSENTRY32();
                    pe.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                    if (Process32First(snap, ref pe))
                    {
                        do
                        {
                            parents[pe.th32ProcessID] = pe.th32ParentProcessID;
                            names[pe.th32ProcessID] = pe.szExeFile;
                        } while (Process32Next(snap, ref pe));
                    }
                }
                finally
                {
                    CloseHandle(snap);
                }
            }
            uint pid = (uint)Process.GetCurrentProcess().Id;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 10; i++)
            {
                string nm;
                if (!names.TryGetValue(pid, out nm)) break;
                if (sb.Length > 0) sb.Append("<-");
                sb.Append(nm);
                uint parent;
                if (!parents.TryGetValue(pid, out parent) || parent == 0 || parent == pid) break;
                pid = parent;
            }
            return sb.ToString();
        }

        static string EnvInfo()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
                {
                    string key = entry.Key as string;
                    if (string.IsNullOrEmpty(key)) continue;
                    string upper = key.ToUpperInvariant();
                    bool match = upper == "QODER_HOOK_SOURCE" || upper == "QODER_CLIENT_TYPE"
                        || upper == "QODER_BUSINESS_PRODUCT" || upper == "QODER_SESSION_TYPE"
                        || upper == "QODER_CLIENT_VERSION" || upper == "QODER_HOOK_VERSION"
                        || upper == "QODER_PRODUCT_ID" || upper == "QODER_IDE"
                        || upper == "JETBRAINS_IDE" || upper == "TERMINAL_EMULATOR"
                        || upper.StartsWith("IDEA_") || upper == "TERM_PROGRAM";
                    if (!match) continue;
                    if (upper.Contains("TOKEN") || upper.Contains("SECRET") || upper.Contains("PASSWORD")
                        || upper.Contains("AUTH") || upper.Contains("KEY") || upper.Contains("COOKIE")
                        || upper.Contains("CREDENTIAL"))
                        continue;
                    string value = entry.Value as string;
                    if (value == null) value = "";
                    value = value.Replace("\r", " ").Replace("\n", " ");
                    if (value.Length > 80) value = value.Substring(0, 80) + "…";
                    sb.Append(key).Append('=').Append(value).Append("; ");
                }
            }
            catch
            {
            }
            return sb.ToString();
        }

        static string Inject(string json, string origin, string chain, string env)
        {
            int i = json.LastIndexOf('}');
            if (i < 0) return json;
            StringBuilder sb = new StringBuilder(json.Length + 300);
            sb.Append(json.Substring(0, i));
            sb.Append(",\"_hud_origin\":\"").Append(JsonEscape(origin)).Append('"');
            sb.Append(",\"_hud_chain\":\"").Append(JsonEscape(chain)).Append('"');
            sb.Append(",\"_hud_env\":\"").Append(JsonEscape(env)).Append('"');
            sb.Append(json.Substring(i));
            return sb.ToString();
        }

        static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);
    }
}
