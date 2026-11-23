using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace QoderHud
{
    static class JsonProbe
    {
        public static Dictionary<string, string> ParseObject(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            Dictionary<string, string> map = new Dictionary<string, string>();
            int i = 0;
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '{') return null;
            i++;
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') break;
                if (s[i] != '"') break;
                string key = ReadString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') break;
                i++;
                SkipWs(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == '"')
                {
                    string val = ReadString(s, ref i);
                    if (key != null && val != null) map[key] = val;
                }
                else
                {
                    int start = i;
                    SkipValue(s, ref i);
                    if (key != null && i > start) map[key] = s.Substring(start, i - start);
                }
            }
            return map;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static string ReadString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return null;
            i++;
            StringBuilder sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 3 < s.Length)
                        {
                            int code;
                            if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, null, out code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        static void SkipValue(string s, ref int i)
        {
            if (i >= s.Length) return;
            char c = s[i];
            if (c == '"')
            {
                ReadString(s, ref i);
                return;
            }
            if (c == '{' || c == '[')
            {
                char open = c;
                char close = c == '{' ? '}' : ']';
                int depth = 0;
                while (i < s.Length)
                {
                    char x = s[i];
                    if (x == '"')
                    {
                        ReadString(s, ref i);
                        continue;
                    }
                    if (x == open) depth++;
                    else if (x == close)
                    {
                        depth--;
                        i++;
                        if (depth == 0) return;
                        continue;
                    }
                    i++;
                }
                return;
            }
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']' && !char.IsWhiteSpace(s[i])) i++;
        }
    }
}
