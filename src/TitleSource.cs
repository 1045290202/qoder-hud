using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace QoderHud
{
    static class TitleSource
    {
        static readonly object DbLock = new object();
        static string dbPath;
        static DateTime dbCheckAt = DateTime.MinValue;
        static string qoderDataDir = "";
        static string jetBrainsLogDir = "";

        public static void SetOverrides(string qoderDir, string jetbrainsDir)
        {
            string q = (qoderDir ?? "").Trim();
            string j = (jetbrainsDir ?? "").Trim();
            lock (DbLock)
            {
                if (q == qoderDataDir && j == jetBrainsLogDir) return;
                qoderDataDir = q;
                jetBrainsLogDir = j;
                dbPath = null;
                dbCheckAt = DateTime.MinValue;
            }
        }

        public static string Lookup(string sid, string origin)
        {
            if (string.IsNullOrEmpty(sid)) return "";
            string title = FromDb(sid);
            if (title.Length > 0) return title;
            if (origin == "JetBrains") return FromIdeLog(sid);
            return "";
        }

        public static bool LastInterrupted(string sid, string origin, DateTime notBefore, out DateTime stoppedAt)
        {
            if (origin == "JetBrains" && LastJetBrainsStopped(sid, notBefore, out stoppedAt))
                return true;
            return LastAssistantInterrupted(sid, notBefore, out stoppedAt);
        }

        static bool LastJetBrainsStopped(string sid, DateTime notBefore, out DateTime stoppedAt)
        {
            stoppedAt = DateTime.MinValue;
            if (string.IsNullOrEmpty(sid)) return false;
            try
            {
                string marker = "task/status/sync received. sessionId=" + sid + ", status=Stopped";
                DateTime minWrite = notBefore.AddSeconds(-5);
                List<string> logs = FindIdeLogs();
                int scanned = 0;
                foreach (string path in logs)
                {
                    if (scanned++ >= 6) break;
                    DateTime write;
                    try { write = File.GetLastWriteTime(path); }
                    catch { continue; }
                    if (write < minWrite) continue;

                    string text = ReadTail(path, 512 * 1024);
                    int idx = text.LastIndexOf(marker, StringComparison.Ordinal);
                    if (idx < 0) continue;
                    int start = text.LastIndexOf('\n', idx);
                    if (start < 0) start = 0;
                    else start++;
                    int end = text.IndexOf('\n', idx);
                    if (end < 0) end = text.Length;
                    string line = text.Substring(start, Math.Min(end - start, 2048));

                    DateTime when;
                    if (line.Length >= 23
                        && DateTime.TryParseExact(line.Substring(0, 23), "yyyy-MM-dd HH:mm:ss,fff",
                            CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out when))
                    {
                        if (when > stoppedAt) stoppedAt = when;
                    }
                    else if (write > stoppedAt)
                    {
                        stoppedAt = write;
                    }
                }
                return stoppedAt != DateTime.MinValue;
            }
            catch
            {
                return false;
            }
        }

        public static bool LastAssistantInterrupted(string sid, DateTime notBefore, out DateTime updatedAt)
        {
            updatedAt = DateTime.MinValue;
            if (string.IsNullOrEmpty(sid)) return false;
            try
            {
                lock (DbLock)
                {
                    string path = FindDb();
                    if (path == null) return false;
                    IntPtr db;
                    if (sqlite3_open_v2(U(path), out db, 0x00000002, IntPtr.Zero) != 0)
                    {
                        if (db != IntPtr.Zero) sqlite3_close(db);
                        return false;
                    }
                    try
                    {
                        sqlite3_busy_timeout(db, 400);
                        IntPtr st;
                        if (sqlite3_prepare_v2(db,
                                U("select status, updated_at from chat_session_messages where session_id=?1 and role='assistant' and updated_at>=?2 order by updated_at desc limit 1"),
                                -1, out st, IntPtr.Zero) != 0)
                            return false;
                        try
                        {
                            sqlite3_bind_text(st, 1, U(sid), -1, new IntPtr(-1));
                            sqlite3_bind_int64(st, 2, ToUnixMs(notBefore));
                            if (sqlite3_step(st) != 100) return false;
                            IntPtr p = sqlite3_column_text(st, 0);
                            int n = sqlite3_column_bytes(st, 0);
                            if (p == IntPtr.Zero || n <= 0) return false;
                            byte[] b = new byte[n];
                            Marshal.Copy(p, b, 0, n);
                            string status = Encoding.UTF8.GetString(b).Trim();
                            if (!status.Equals("interrupted", StringComparison.OrdinalIgnoreCase))
                                return false;
                            long ms = sqlite3_column_int64(st, 1);
                            if (ms <= 0) return false;
                            updatedAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                                .AddMilliseconds(ms).ToLocalTime();
                            return true;
                        }
                        finally
                        {
                            sqlite3_finalize(st);
                        }
                    }
                    finally
                    {
                        sqlite3_close(db);
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        static long ToUnixMs(DateTime value)
        {
            if (value == DateTime.MinValue) return 0;
            DateTime utc = value.ToUniversalTime();
            return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        static string FromDb(string sid)
        {
            try
            {
                lock (DbLock)
                {
                    string path = FindDb();
                    if (path == null) return "";
                    IntPtr db;
                    if (sqlite3_open_v2(U(path), out db, 0x00000002, IntPtr.Zero) != 0)
                    {
                        if (db != IntPtr.Zero) sqlite3_close(db);
                        return "";
                    }
                    try
                    {
                        sqlite3_busy_timeout(db, 400);
                        IntPtr st;
                        if (sqlite3_prepare_v2(db, U("select title from chat_sessions where session_id=?1"),
                                -1, out st, IntPtr.Zero) != 0) return "";
                        try
                        {
                            sqlite3_bind_text(st, 1, U(sid), -1, new IntPtr(-1));
                            if (sqlite3_step(st) != 100) return "";
                            IntPtr p = sqlite3_column_text(st, 0);
                            int n = sqlite3_column_bytes(st, 0);
                            if (p == IntPtr.Zero || n <= 0) return "";
                            byte[] b = new byte[n];
                            Marshal.Copy(p, b, 0, n);
                            return Encoding.UTF8.GetString(b).Trim();
                        }
                        finally
                        {
                            sqlite3_finalize(st);
                        }
                    }
                    finally
                    {
                        sqlite3_close(db);
                    }
                }
            }
            catch
            {
                return "";
            }
        }

        static string FindDb()
        {
            string custom = qoderDataDir;
            if (!string.IsNullOrEmpty(custom))
            {
                try
                {
                    if (File.Exists(custom)) return custom;
                    if (Directory.Exists(custom))
                    {
                        string direct = Path.Combine(custom, "main.sqlite");
                        if (File.Exists(direct)) return direct;
                        DateTime bestCustom = DateTime.MinValue;
                        string bestCustomPath = null;
                        foreach (string dir in Directory.GetDirectories(custom, "com.qoder.app*"))
                        {
                            string p = Path.Combine(dir, "main.sqlite");
                            if (!File.Exists(p)) continue;
                            DateTime w = File.GetLastWriteTimeUtc(p);
                            if (w > bestCustom)
                            {
                                bestCustom = w;
                                bestCustomPath = p;
                            }
                        }
                        if (bestCustomPath != null) return bestCustomPath;
                    }
                }
                catch
                {
                }
            }

            DateTime now = DateTime.Now;
            if (dbPath != null && File.Exists(dbPath)) return dbPath;
            if (dbPath != null && (now - dbCheckAt).TotalSeconds < 30) return null;
            dbCheckAt = now;
            dbPath = null;
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                DateTime best = DateTime.MinValue;
                foreach (string dir in Directory.GetDirectories(appData, "com.qoder.app*"))
                {
                    string p = Path.Combine(dir, "main.sqlite");
                    if (!File.Exists(p)) continue;
                    DateTime w = File.GetLastWriteTimeUtc(p);
                    if (w > best)
                    {
                        best = w;
                        dbPath = p;
                    }
                }
            }
            catch
            {
            }
            return dbPath;
        }

        static string FromIdeLog(string sid)
        {
            try
            {
                List<string> logs = FindIdeLogs();
                if (logs.Count == 0) return "";
                Regex re = new Regex("sessionId=" + Regex.Escape(sid) + @"[^\r\n]*?title=([^\r\n]+)");
                int scanned = 0;
                foreach (string p in logs)
                {
                    if (scanned++ >= 6) break;
                    MatchCollection ms = re.Matches(ReadTail(p, 16 * 1024 * 1024));
                    if (ms.Count > 0)
                    {
                        string title = ms[ms.Count - 1].Groups[1].Value.Trim();
                        if (title.Length > 0) return title;
                    }
                }
            }
            catch
            {
            }
            return "";
        }

        static List<string> FindIdeLogs()
        {
            List<string> logs = new List<string>();
            try
            {
                string custom = jetBrainsLogDir;
                if (!string.IsNullOrEmpty(custom))
                {
                    if (File.Exists(custom)) logs.Add(custom);
                    else if (Directory.Exists(custom))
                    {
                        string direct = Path.Combine(custom, "idea.log");
                        if (File.Exists(direct)) logs.Add(direct);
                        string productLog = Path.Combine(custom, "log", "idea.log");
                        if (File.Exists(productLog)) logs.Add(productLog);
                        foreach (string prod in Directory.GetDirectories(custom))
                        {
                            string p = Path.Combine(prod, "log", "idea.log");
                            if (File.Exists(p)) logs.Add(p);
                        }
                    }
                }
                else
                {
                    string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string jb = Path.Combine(local, "JetBrains");
                    if (Directory.Exists(jb))
                    {
                        foreach (string prod in Directory.GetDirectories(jb))
                        {
                            string p = Path.Combine(prod, "log", "idea.log");
                            if (File.Exists(p)) logs.Add(p);
                        }
                    }
                }
                logs.Sort(delegate (string a, string b)
                {
                    return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
                });
            }
            catch
            {
            }
            return logs;
        }

        static string ReadTail(string path, int maxBytes)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long len = fs.Length;
                long start = Math.Max(0, len - maxBytes);
                if (start > 0) fs.Seek(start, SeekOrigin.Begin);
                byte[] buf = new byte[len - start];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = fs.Read(buf, read, buf.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return Encoding.UTF8.GetString(buf, 0, read);
            }
        }

        static byte[] U(string s)
        {
            return Encoding.UTF8.GetBytes(s + "\0");
        }

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int n, IntPtr destructor);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_step(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_column_bytes(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern long sqlite3_column_int64(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_close(IntPtr db);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    }
}
