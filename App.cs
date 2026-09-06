// ============================================================================
// DocEngine Enterprise — Корпоративный движок обработки документов
// Разработчик: Николенко А. И. (ООО «ЛУКОЙЛ-Волгоградэнерго»)
// Чистый C# (.NET Framework 4.0/4.5/4.8) — Сборка через csc.exe без сторонних библиотек
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace DocEngine.App
{
    #region Модели и Конфигурация

    public class AppConfig
    {
        public string appearance_mode = "Dark";
        public string out_dir = "Рядом с исходным файлом";
        public string compress_choice = "Баланс (150 DPI, рекомендуется для почты)";
        public bool auto_tables = true;
        public bool remove_blanks = false;
        public bool auto_open = true;

        public static string GetConfigPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        }

        public static AppConfig Load()
        {
            string path = GetConfigPath();
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var ser = new JavaScriptSerializer();
                    return ser.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
                catch { }
            }
            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                var ser = new JavaScriptSerializer();
                string json = ser.Serialize(this);
                File.WriteAllText(GetConfigPath(), json, Encoding.UTF8);
            }
            catch { }
        }
    }

    public class DocumentFileInfo
    {
        public string FilePath { get; set; }
        public string FileName { get { return Path.GetFileName(FilePath); } }
        public string Extension { get { return (Path.GetExtension(FilePath) ?? "").ToLowerInvariant(); } }
        public long FileSizeBytes { get; set; }
        public double FileSizeMb { get { return Math.Round((double)FileSizeBytes / (1024.0 * 1024.0), 2); } }
        public int PageCount { get; set; }
        public string FormatName { get; set; }
        public string Orientation { get; set; }

        public static DocumentFileInfo FromPath(string path)
        {
            var info = new DocumentFileInfo
            {
                FilePath = path,
                FileSizeBytes = File.Exists(path) ? new FileInfo(path).Length : 0,
                PageCount = 1,
                FormatName = "А4",
                Orientation = "Книжная"
            };

            if (info.Extension == ".pdf")
            {
                try
                {
                    info.PageCount = PdfEngine.CountPdfPages(path);
                }
                catch
                {
                    info.PageCount = 1;
                }
            }
            else if (info.Extension == ".docx" || info.Extension == ".doc")
            {
                info.FormatName = "Word DOCX";
                info.PageCount = 1;
            }

            return info;
        }
    }

    #endregion

    #region Криптографическая защита и HWID Лицензирование

    public static class LicenseCore
    {
        public const string MasterSecretSeed = "DOCENGINE_ENTERPRISE_CRYPTOGRAPHIC_MASTER_KEY_2026_AI_NIKOLENKO_SECRET_SALT_984310";
        public const int LicenseVersion = 2;

        public static string GetAppDataDir()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "DocEngineEnterprise");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            return dir;
        }

        public static string GetLicenseFilePath()
        {
            return Path.Combine(GetAppDataDir(), "license.dat");
        }

        public static string GetTimeAnchorFilePath()
        {
            return Path.Combine(GetAppDataDir(), "security_anchor.dat");
        }

        public static string GetMachineHwid()
        {
            List<string> parts = new List<string>();
            try
            {
                object guid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", "");
                if (guid != null && !string.IsNullOrEmpty(guid.ToString().Trim()))
                    parts.Add("GUID:" + guid.ToString().Trim());
            }
            catch { }

            try
            {
                object cpu = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "");
                if (cpu != null && !string.IsNullOrEmpty(cpu.ToString().Trim()))
                    parts.Add("CPU:" + cpu.ToString().Trim());
            }
            catch { }

            try
            {
                object board = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardSerialNumber", "");
                if (board != null && !string.IsNullOrEmpty(board.ToString().Trim()))
                    parts.Add("BOARD:" + board.ToString().Trim());
            }
            catch { }

            if (parts.Count == 0)
            {
                parts.Add(Environment.MachineName + "-WIN");
            }

            string combined = string.Join("||", parts.ToArray()) + "||DOCENGINE_HWID_SALT_2026";
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(combined));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash)
                    sb.Append(b.ToString("X2"));
                string hex = sb.ToString();
                return "LK-" + hex.Substring(0, 4) + "-" + hex.Substring(4, 4) + "-" + hex.Substring(8, 4);
            }
        }

        public static byte[] GetSigningKey()
        {
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(MasterSecretSeed));
            }
        }

        public static string Base64UrlEncode(byte[] input)
        {
            string base64 = Convert.ToBase64String(input);
            return base64.Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        public static byte[] Base64UrlDecode(string input)
        {
            string incoming = input.Replace('-', '+').Replace('_', '/');
            switch (incoming.Length % 4)
            {
                case 2: incoming += "=="; break;
                case 3: incoming += "="; break;
            }
            return Convert.FromBase64String(incoming);
        }

        public static bool CheckTimeTampering()
        {
            try
            {
                long currentTs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                string anchorPath = GetTimeAnchorFilePath();
                if (File.Exists(anchorPath))
                {
                    string txt = File.ReadAllText(anchorPath, Encoding.UTF8).Trim();
                    long lastTs;
                    if (long.TryParse(txt, out lastTs))
                    {
                        if (currentTs < (lastTs - 7200))
                            return false;
                    }
                }
                File.WriteAllText(anchorPath, currentTs.ToString(), Encoding.UTF8);
            }
            catch { }
            return true;
        }

        public static bool VerifyLicenseKey(string keyStr, out string message, out Dictionary<string, object> payload)
        {
            payload = new Dictionary<string, object>();
            if (string.IsNullOrWhiteSpace(keyStr))
            {
                message = "Лицензия не активирована.";
                return false;
            }

            string cleaned = keyStr.Trim();
            if (!cleaned.StartsWith("DOCENG-"))
            {
                message = "Неверный формат ключа активации.";
                return false;
            }

            int firstDash = cleaned.IndexOf('-');
            int lastDash = cleaned.LastIndexOf('-');
            if (firstDash < 0 || lastDash <= firstDash)
            {
                message = "Ключ поврежден.";
                return false;
            }

            string payloadB64 = cleaned.Substring(firstDash + 1, lastDash - firstDash - 1);
            string providedSig = cleaned.Substring(lastDash + 1).ToUpperInvariant();

            string expectedSig;
            using (HMACSHA256 hmac = new HMACSHA256(GetSigningKey()))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash)
                    sb.Append(b.ToString("X2"));
                expectedSig = sb.ToString().Substring(0, 16).ToUpperInvariant();
            }

            if (!string.Equals(providedSig, expectedSig, StringComparison.OrdinalIgnoreCase))
            {
                message = "Ошибка цифровой подписи лицензии.";
                return false;
            }

            try
            {
                byte[] bytes = Base64UrlDecode(payloadB64);
                string json = Encoding.UTF8.GetString(bytes);
                var serializer = new JavaScriptSerializer();
                payload = serializer.Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception ex)
            {
                message = "Ошибка чтения данных ключа: " + ex.Message;
                return false;
            }

            int v = payload.ContainsKey("v") ? Convert.ToInt32(payload["v"]) : 0;
            if (v != LicenseVersion)
            {
                message = "Версия лицензии не поддерживается.";
                return false;
            }

            string licenseHwid = payload.ContainsKey("hw") ? payload["hw"].ToString().ToUpperInvariant() : "ANY";
            string currentHwid = GetMachineHwid().ToUpperInvariant();

            if (licenseHwid != "ANY" && licenseHwid != currentHwid)
            {
                message = string.Format("Лицензия предназначена для другого ПК (HWID: {0}).", licenseHwid);
                return false;
            }

            if (!CheckTimeTampering())
            {
                message = "Обнаружен откат системного времени. Проверьте дату и время на ПК.";
                return false;
            }

            long expiryTs = payload.ContainsKey("ex") ? Convert.ToInt64(payload["ex"]) : 0L;
            long currentTs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            string expStr = payload.ContainsKey("ex_str") ? payload["ex_str"].ToString() : "Бессрочно";

            if (currentTs > expiryTs)
            {
                message = string.Format("Срок действия лицензии истек ({0}).", expStr);
                return false;
            }

            string client = payload.ContainsKey("cl") ? payload["cl"].ToString() : "Пользователь";
            message = string.Format("Лицензия активна для «{0}» до {1}", client, expStr);
            return true;
        }

        public static bool LoadSavedLicense(out string message, out Dictionary<string, object> payload)
        {
            string path = GetLicenseFilePath();
            if (!File.Exists(path))
            {
                message = "Лицензия не активирована.";
                payload = new Dictionary<string, object>();
                return false;
            }

            try
            {
                string key = File.ReadAllText(path, Encoding.UTF8).Trim();
                return VerifyLicenseKey(key, out message, out payload);
            }
            catch (Exception ex)
            {
                message = "Ошибка чтения лицензии: " + ex.Message;
                payload = new Dictionary<string, object>();
                return false;
            }
        }

        public static bool SaveLicense(string keyStr, out string message)
        {
            try
            {
                File.WriteAllText(GetLicenseFilePath(), keyStr.Trim(), Encoding.UTF8);
                message = "Лицензия успешно сохранена и активирована!";
                return true;
            }
            catch (Exception ex)
            {
                message = "Ошибка сохранения лицензии: " + ex.Message;
                return false;
            }
        }
    }

    #endregion

    #region Автономный Движок Документов (PDF / Word / Excel)

    public static class PdfEngine
    {
        public class TextItem
        {
            public double X { get; set; }
            public double Y { get; set; }
            public string Text { get; set; }
            public int Page { get; set; }
        }

        public class PdfObject
        {
            public int Id;
            public int Gen;
            public byte[] RawData;
            public string HeaderDict;
            public bool IsPage;
            public bool IsPagesRoot;
            public bool IsCatalog;
        }

        public class ParsedPdf
        {
            public List<PdfObject> Objects = new List<PdfObject>();
            public List<int> PageObjectIds = new List<int>();
            public int CatalogId;
            public int PagesRootId;
        }

        public static int CountPdfPages(string pdfPath)
        {
            if (!File.Exists(pdfPath)) return 1;
            try
            {
                byte[] bytes = File.ReadAllBytes(pdfPath);
                string content = Encoding.ASCII.GetString(bytes);

                Match mCount = Regex.Match(content, @"/Type\s*/Pages.*?/Count\s+(\d+)", RegexOptions.Singleline);
                if (mCount.Success)
                {
                    int c;
                    if (int.TryParse(mCount.Groups[1].Value, out c) && c > 0)
                        return c;
                }

                MatchCollection matches = Regex.Matches(content, @"/Type\s*/Page\b");
                if (matches.Count > 0)
                    return matches.Count;
            }
            catch { }
            return 1;
        }

        public static List<int> ParsePageRange(string rangeStr, int totalPages)
        {
            if (string.IsNullOrWhiteSpace(rangeStr) || totalPages <= 0)
                return Enumerable.Range(1, totalPages).ToList();

            string clean = rangeStr.Trim().ToLowerInvariant();
            if (clean == "все" || clean == "all" || clean == "*" || clean == "полностью")
                return Enumerable.Range(1, totalPages).ToList();

            HashSet<int> pages = new HashSet<int>();
            string[] parts = clean.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string p in parts)
            {
                string part = p.Trim();
                if (part.Contains("-"))
                {
                    string[] sub = part.Split('-');
                    if (sub.Length == 2)
                    {
                        int start, end;
                        if (int.TryParse(sub[0].Trim(), out start) && int.TryParse(sub[1].Trim(), out end))
                        {
                            start = Math.Max(1, Math.Min(start, totalPages));
                            end = Math.Max(1, Math.Min(end, totalPages));
                            if (start <= end)
                            {
                                for (int i = start; i <= end; i++) pages.Add(i);
                            }
                        }
                    }
                }
                else
                {
                    int single;
                    if (int.TryParse(part, out single) && single >= 1 && single <= totalPages)
                        pages.Add(single);
                }
            }

            var result = pages.OrderBy(x => x).ToList();
            return result.Count > 0 ? result : Enumerable.Range(1, totalPages).ToList();
        }

        public static byte[] DecodeAscii85(byte[] data)
        {
            if (data == null || data.Length == 0) return new byte[0];
            using (MemoryStream ms = new MemoryStream())
            {
                int count = 0;
                uint val = 0;
                for (int i = 0; i < data.Length; i++)
                {
                    byte b = data[i];
                    char c = (char)b;
                    if (c == '~') break;
                    if (c >= '!' && c <= 'u')
                    {
                        val = val * 85 + (uint)(c - '!');
                        count++;
                        if (count == 5)
                        {
                            ms.WriteByte((byte)((val >> 24) & 0xFF));
                            ms.WriteByte((byte)((val >> 16) & 0xFF));
                            ms.WriteByte((byte)((val >> 8) & 0xFF));
                            ms.WriteByte((byte)(val & 0xFF));
                            count = 0;
                            val = 0;
                        }
                    }
                    else if (c == 'z' && count == 0)
                    {
                        ms.Write(new byte[4], 0, 4);
                    }
                }
                if (count > 1)
                {
                    int pad = 5 - count;
                    for (int i = 0; i < pad; i++) val = val * 85 + 84;
                    for (int i = 0; i < count - 1; i++)
                    {
                        ms.WriteByte((byte)((val >> (24 - 8 * i)) & 0xFF));
                    }
                }
                return ms.ToArray();
            }
        }

        public static byte[] DecompressFlate(byte[] data)
        {
            if (data == null || data.Length == 0) return new byte[0];
            int offset = 0;
            if (data.Length >= 2 && data[0] == 0x78) offset = 2;
            try
            {
                using (MemoryStream ms = new MemoryStream(data, offset, data.Length - offset))
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                using (MemoryStream outMs = new MemoryStream())
                {
                    ds.CopyTo(outMs);
                    return outMs.ToArray();
                }
            }
            catch
            {
                return data;
            }
        }

        public static byte[] DecodeStream(byte[] rawStream, string filterInfo)
        {
            byte[] current = rawStream;
            string f = (filterInfo ?? "").ToLowerInvariant();

            if (f.Contains("ascii85") || f.Contains("a85"))
            {
                current = DecodeAscii85(current);
            }

            if (f.Contains("flate") || (current.Length >= 2 && current[0] == 0x78))
            {
                current = DecompressFlate(current);
            }

            return current;
        }

        public static string DecodePdfString(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            if (raw.StartsWith("<") && raw.EndsWith(">"))
            {
                string hex = raw.Substring(1, raw.Length - 2).Trim();
                if (hex.Length % 2 != 0) hex += "0";
                byte[] bytes = new byte[hex.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    try { bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16); } catch { }
                }
                if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                    return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
                if (bytes.Length >= 2 && bytes[0] == 0 && bytes[1] > 0)
                    return Encoding.BigEndianUnicode.GetString(bytes);
                return DecodeBytes(bytes);
            }

            List<byte> rawBytes = new List<byte>();
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '\\' && i + 1 < raw.Length)
                {
                    i++;
                    char c = raw[i];
                    if (c == 'n') rawBytes.Add((byte)'\n');
                    else if (c == 'r') rawBytes.Add((byte)'\r');
                    else if (c == 't') rawBytes.Add((byte)'\t');
                    else if (c == 'b') rawBytes.Add((byte)'\b');
                    else if (c == 'f') rawBytes.Add((byte)'\f');
                    else if (c == '(') rawBytes.Add((byte)'(');
                    else if (c == ')') rawBytes.Add((byte)')');
                    else if (c == '\\') rawBytes.Add((byte)'\\');
                    else if (c >= '0' && c <= '7')
                    {
                        int octal = c - '0';
                        if (i + 1 < raw.Length && raw[i + 1] >= '0' && raw[i + 1] <= '7')
                        {
                            i++;
                            octal = octal * 8 + (raw[i] - '0');
                            if (i + 1 < raw.Length && raw[i + 1] >= '0' && raw[i + 1] <= '7')
                            {
                                i++;
                                octal = octal * 8 + (raw[i] - '0');
                            }
                        }
                        rawBytes.Add((byte)octal);
                    }
                    else
                    {
                        rawBytes.Add((byte)c);
                    }
                }
                else
                {
                    rawBytes.Add((byte)raw[i]);
                }
            }

            return DecodeBytes(rawBytes.ToArray());
        }

        private static string DecodeBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            bool hasHighBytes = bytes.Any(b => b >= 0xC0 && b <= 0xFF);
            if (hasHighBytes)
            {
                try { return Encoding.GetEncoding(1251).GetString(bytes); } catch { }
            }

            try { return Encoding.UTF8.GetString(bytes); } catch { return Encoding.Default.GetString(bytes); }
        }

        public static int IndexOfBytes(byte[] src, byte[] pattern, int startIndex)
        {
            if (src == null || pattern == null || startIndex >= src.Length || pattern.Length == 0) return -1;
            for (int i = startIndex; i <= src.Length - pattern.Length; i++)
            {
                if (src[i] == pattern[0])
                {
                    bool match = true;
                    for (int j = 1; j < pattern.Length; j++)
                    {
                        if (src[i + j] != pattern[j]) { match = false; break; }
                    }
                    if (match) return i;
                }
            }
            return -1;
        }

        public static List<string> ExtractTextLinesFromPdf(string pdfPath)
        {
            return ExtractStructuredLines(pdfPath);
        }

        public static List<string> ExtractStructuredLines(string pdfPath)
        {
            List<string> result = new List<string>();
            if (!File.Exists(pdfPath)) return result;

            byte[] fileBytes = File.ReadAllBytes(pdfPath);
            byte[] bStream = Encoding.ASCII.GetBytes("stream");
            byte[] bEndStream = Encoding.ASCII.GetBytes("endstream");

            List<TextItem> allItems = new List<TextItem>();
            int streamPage = 1;
            int idx = 0;

            while (idx < fileBytes.Length)
            {
                int sIdx = IndexOfBytes(fileBytes, bStream, idx);
                if (sIdx == -1) break;

                int dictStart = Math.Max(0, sIdx - 600);
                string header = Encoding.ASCII.GetString(fileBytes, dictStart, sIdx - dictStart);
                string filter = "";
                Match mf = Regex.Match(header, @"/Filter\s*(\[[^\]]*\]|/[a-zA-Z0-9]+)");
                if (mf.Success) filter = mf.Groups[1].Value;

                int dataStart = sIdx + 6;
                if (dataStart < fileBytes.Length && fileBytes[dataStart] == '\r') dataStart++;
                if (dataStart < fileBytes.Length && fileBytes[dataStart] == '\n') dataStart++;

                int eIdx = IndexOfBytes(fileBytes, bEndStream, dataStart);
                if (eIdx == -1) break;

                int len = eIdx - dataStart;
                if (len > 0 && len < 25000000)
                {
                    byte[] rawStream = new byte[len];
                    Buffer.BlockCopy(fileBytes, dataStart, rawStream, 0, len);
                    byte[] decoded = DecodeStream(rawStream, filter);
                    string content = Encoding.Default.GetString(decoded);

                    int btIdx = 0;
                    bool hasBt = false;
                    while ((btIdx = content.IndexOf("BT", btIdx, StringComparison.Ordinal)) != -1)
                    {
                        int etIdx = content.IndexOf("ET", btIdx, StringComparison.Ordinal);
                        if (etIdx == -1) break;
                        string btBlock = content.Substring(btIdx, etIdx - btIdx + 2);
                        ParseBtBlock(btBlock, streamPage, allItems);
                        btIdx = etIdx + 2;
                        hasBt = true;
                    }
                    if (hasBt) streamPage++;
                }

                idx = eIdx + 9;
            }

            if (allItems.Count == 0)
            {
                string fileStr = Encoding.ASCII.GetString(fileBytes);
                MatchCollection rawTexts = Regex.Matches(fileStr, @"\((.*?)\)\s*Tj");
                foreach (Match rm in rawTexts)
                {
                    string t = DecodePdfString(rm.Groups[1].Value).Trim();
                    if (!string.IsNullOrEmpty(t)) result.Add(t);
                }
                return result;
            }

            var grouped = allItems
                .OrderBy(i => i.Page)
                .ThenByDescending(i => Math.Round(i.Y / 3.5) * 3.5)
                .ThenBy(i => i.X)
                .GroupBy(i => new { i.Page, LineY = Math.Round(i.Y / 3.5) * 3.5 });

            foreach (var g in grouped)
            {
                StringBuilder sb = new StringBuilder();
                TextItem prev = null;
                foreach (var item in g)
                {
                    if (prev != null)
                    {
                        double dx = item.X - prev.X;
                        if (dx > 25) sb.Append("\t");
                        else if (dx > 4) sb.Append(" ");
                    }
                    sb.Append(item.Text);
                    prev = item;
                }
                string line = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(line)) result.Add(line);
            }

            return result;
        }

        private static void ParseBtBlock(string block, int page, List<TextItem> items)
        {
            double curX = 0, curY = 0;
            double lineX = 0, lineY = 0;

            MatchCollection ops = Regex.Matches(block,
                @"((?:[\d\.\-]+\s+){2,6}(?:cm|Tm|Td|TD))" +
                @"|(T\*)" +
                @"|(\((?:\\.|[^\\()])*\)\s*(?:Tj|\'))" +
                @"|(\<[0-9a-fA-F\s]+\>\s*(?:Tj|\'))" +
                @"|(\[(.*?)\]\s*TJ)", RegexOptions.Singleline);

            foreach (Match m in ops)
            {
                if (m.Groups[1].Success)
                {
                    string str = m.Groups[1].Value.Trim();
                    string[] p = str.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 3 && p[p.Length - 1] == "Td")
                    {
                        double dx, dy;
                        double.TryParse(p[p.Length - 3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dx);
                        double.TryParse(p[p.Length - 2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dy);
                        curX += dx; curY += dy;
                        lineX = curX; lineY = curY;
                    }
                    else if (p.Length >= 6 && p[p.Length - 1] == "Tm")
                    {
                        double.TryParse(p[p.Length - 3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out curX);
                        double.TryParse(p[p.Length - 2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out curY);
                        lineX = curX; lineY = curY;
                    }
                }
                else if (m.Groups[2].Success)
                {
                    curX = lineX;
                    curY -= 12;
                }
                else if (m.Groups[3].Success)
                {
                    string raw = m.Groups[3].Value.Trim();
                    int parenStart = raw.IndexOf('(');
                    int parenEnd = raw.LastIndexOf(')');
                    if (parenStart != -1 && parenEnd > parenStart)
                    {
                        string inner = raw.Substring(parenStart + 1, parenEnd - parenStart - 1);
                        string dec = DecodePdfString(inner);
                        if (!string.IsNullOrWhiteSpace(dec))
                        {
                            items.Add(new TextItem { X = curX, Y = curY, Text = dec, Page = page });
                        }
                    }
                }
                else if (m.Groups[4].Success)
                {
                    string raw = m.Groups[4].Value.Trim();
                    int hStart = raw.IndexOf('<');
                    int hEnd = raw.LastIndexOf('>');
                    if (hStart != -1 && hEnd > hStart)
                    {
                        string inner = raw.Substring(hStart, hEnd - hStart + 1);
                        string dec = DecodePdfString(inner);
                        if (!string.IsNullOrWhiteSpace(dec))
                        {
                            items.Add(new TextItem { X = curX, Y = curY, Text = dec, Page = page });
                        }
                    }
                }
                else if (m.Groups[5].Success)
                {
                    string inner = m.Groups[6].Value;
                    MatchCollection sub = Regex.Matches(inner, @"\(((?:\\.|[^\\()])*)\)|\<([0-9a-fA-F\s]+)\>|([\d\.\-]+)");
                    StringBuilder sb = new StringBuilder();
                    foreach (Match sm in sub)
                    {
                        if (sm.Groups[1].Success)
                        {
                            sb.Append(DecodePdfString(sm.Groups[1].Value));
                        }
                        else if (sm.Groups[2].Success)
                        {
                            sb.Append(DecodePdfString("<" + sm.Groups[2].Value + ">"));
                        }
                        else if (sm.Groups[3].Success)
                        {
                            double kern;
                            if (double.TryParse(sm.Groups[3].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out kern) && kern < -150)
                                sb.Append(" ");
                        }
                    }
                    string text = sb.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        items.Add(new TextItem { X = curX, Y = curY, Text = text, Page = page });
                    }
                }
            }
        }

        public static ParsedPdf ParsePdfObjects(string filePath)
        {
            var result = new ParsedPdf();
            byte[] bytes = File.ReadAllBytes(filePath);

            int idx = 0;
            byte[] bObj = Encoding.ASCII.GetBytes("obj");
            byte[] bEndObj = Encoding.ASCII.GetBytes("endobj");

            while (idx < bytes.Length)
            {
                int objIdx = IndexOfBytes(bytes, bObj, idx);
                if (objIdx == -1) break;

                int lineStart = objIdx - 1;
                while (lineStart >= 0 && bytes[lineStart] != '\r' && bytes[lineStart] != '\n') lineStart--;
                lineStart++;

                string idStr = Encoding.ASCII.GetString(bytes, lineStart, objIdx - lineStart).Trim();
                string[] parts = idStr.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) { idx = objIdx + 3; continue; }

                int id, gen;
                if (!int.TryParse(parts[0], out id) || !int.TryParse(parts[1], out gen)) { idx = objIdx + 3; continue; }

                int endObjIdx = IndexOfBytes(bytes, bEndObj, objIdx + 3);
                if (endObjIdx == -1) break;
                endObjIdx += 6;

                int len = endObjIdx - lineStart;
                byte[] objData = new byte[len];
                Buffer.BlockCopy(bytes, lineStart, objData, 0, len);

                string sample = Encoding.ASCII.GetString(objData, 0, Math.Min(400, len));
                var obj = new PdfObject
                {
                    Id = id,
                    Gen = gen,
                    RawData = objData,
                    HeaderDict = sample
                };

                if (Regex.IsMatch(sample, @"/Type\s*/Page\b"))
                {
                    obj.IsPage = true;
                    result.PageObjectIds.Add(id);
                }
                else if (Regex.IsMatch(sample, @"/Type\s*/Pages\b"))
                {
                    obj.IsPagesRoot = true;
                    result.PagesRootId = id;
                }
                else if (Regex.IsMatch(sample, @"/Type\s*/Catalog\b"))
                {
                    obj.IsCatalog = true;
                    result.CatalogId = id;
                }

                result.Objects.Add(obj);
                idx = endObjIdx;
            }

            return result;
        }

        public static bool MergePdfs(List<string> pdfFiles, string outputPath, Action<int, int, string> progress, Action<string> log)
        {
            if (pdfFiles == null || pdfFiles.Count == 0)
            {
                log("Ошибка: Список файлов для объединения пуст.");
                return false;
            }

            int totalFiles = pdfFiles.Count;
            log(string.Format("Объединение {0} документов в единый том «{1}»...", totalFiles, Path.GetFileName(outputPath)));

            try
            {
                List<ParsedPdf> parsedDocs = new List<ParsedPdf>();
                int totalOffset = 0;
                for (int i = 0; i < pdfFiles.Count; i++)
                {
                    string f = pdfFiles[i];
                    if (!File.Exists(f)) continue;
                    progress(i + 1, totalFiles, string.Format("Чтение структуры: {0}...", Path.GetFileName(f)));
                    var pdf = ParsePdfObjects(f);
                    parsedDocs.Add(pdf);
                    int maxId = 0;
                    foreach (var o in pdf.Objects) if (o.Id > maxId) maxId = o.Id;
                    totalOffset += maxId + 10;
                }

                if (parsedDocs.Count == 0)
                {
                    log("Ошибка: Ни один файл не удалось прочитать.");
                    return false;
                }

                int pagesRootId = totalOffset + 1;
                int catalogId = totalOffset + 2;
                List<int> allPageIds = new List<int>();

                using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                using (StreamWriter w = new StreamWriter(fs, Encoding.ASCII))
                {
                    w.WriteLine("%PDF-1.4");
                    w.WriteLine("%\xE2\xE3\xCF\xD3");

                    List<long> xrefOffsets = new List<long> { 0 };
                    int currentIdOffset = 0;

                    for (int f = 0; f < parsedDocs.Count; f++)
                    {
                        var pdf = parsedDocs[f];
                        int maxId = 0;
                        foreach (var o in pdf.Objects) if (o.Id > maxId) maxId = o.Id;

                        int capturedOffset = currentIdOffset;
                        int capturedPagesRoot = pdf.PagesRootId;

                        foreach (var o in pdf.Objects)
                        {
                            if (o.IsPagesRoot || o.IsCatalog) continue;

                            int newId = o.Id + capturedOffset;
                            if (o.IsPage) allPageIds.Add(newId);

                            w.Flush();
                            xrefOffsets.Add(fs.Position);

                            WritePatchedObject(fs, o, newId, oldId =>
                            {
                                if (oldId == capturedPagesRoot) return pagesRootId;
                                return oldId + capturedOffset;
                            });
                        }

                        currentIdOffset += maxId + 10;
                    }

                    w.Flush();
                    xrefOffsets.Add(fs.Position);
                    w.WriteLine(string.Format("{0} 0 obj", pagesRootId));
                    w.WriteLine("<< /Type /Pages");
                    w.Write("   /Kids [");
                    foreach (int pid in allPageIds) w.Write(string.Format("{0} 0 R ", pid));
                    w.WriteLine("]");
                    w.WriteLine(string.Format("   /Count {0}", allPageIds.Count));
                    w.WriteLine(">>");
                    w.WriteLine("endobj");

                    w.Flush();
                    xrefOffsets.Add(fs.Position);
                    w.WriteLine(string.Format("{0} 0 obj", catalogId));
                    w.WriteLine("<< /Type /Catalog");
                    w.WriteLine(string.Format("   /Pages {0} 0 R", pagesRootId));
                    w.WriteLine(">>");
                    w.WriteLine("endobj");

                    w.Flush();
                    long startXref = fs.Position;
                    w.WriteLine("xref");
                    w.WriteLine(string.Format("0 {0}", xrefOffsets.Count));
                    w.WriteLine("0000000000 65535 f ");
                    for (int i = 1; i < xrefOffsets.Count; i++)
                    {
                        w.WriteLine(string.Format("{0:D10} 00000 n ", xrefOffsets[i]));
                    }
                    w.WriteLine("trailer");
                    w.WriteLine(string.Format("<< /Size {0}", xrefOffsets.Count));
                    w.WriteLine(string.Format("   /Root {0} 0 R", catalogId));
                    w.WriteLine(">>");
                    w.WriteLine("startxref");
                    w.WriteLine(startXref);
                    w.WriteLine("%%EOF");
                }

                log(string.Format("[Успех] Сформирован единый том ({0} стр.): {1}", allPageIds.Count, outputPath));
                progress(100, 100, "Объединение успешно завершено!");
                return true;
            }
            catch (Exception ex)
            {
                log("Ошибка объединения: " + ex.Message);
                return false;
            }
        }

        public static bool SplitPdf(string pdfPath, string outputPath, string pageRangeStr, Action<int, int, string> progress, Action<string> log)
        {
            if (!File.Exists(pdfPath)) return false;
            int totalPages = CountPdfPages(pdfPath);
            List<int> targetPages = ParsePageRange(pageRangeStr, totalPages);
            log(string.Format("Разделение «{0}»: выборка {1} из {2} стр...", Path.GetFileName(pdfPath), targetPages.Count, totalPages));

            try
            {
                var pdf = ParsePdfObjects(pdfPath);
                if (pdf.PageObjectIds.Count == 0)
                {
                    File.Copy(pdfPath, outputPath, true);
                    return true;
                }

                int maxId = 0;
                foreach (var o in pdf.Objects) if (o.Id > maxId) maxId = o.Id;

                HashSet<int> keptPageObjIds = new HashSet<int>();
                for (int i = 0; i < targetPages.Count; i++)
                {
                    int pIndex = targetPages[i] - 1;
                    if (pIndex >= 0 && pIndex < pdf.PageObjectIds.Count)
                    {
                        keptPageObjIds.Add(pdf.PageObjectIds[pIndex]);
                    }
                }

                int pagesRootId = maxId + 1;
                int catalogId = maxId + 2;

                using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                using (StreamWriter w = new StreamWriter(fs, Encoding.ASCII))
                {
                    w.WriteLine("%PDF-1.4");
                    w.WriteLine("%\xE2\xE3\xCF\xD3");

                    List<long> xrefOffsets = new List<long> { 0 };

                    int capturedPagesRoot = pdf.PagesRootId;
                    foreach (var o in pdf.Objects)
                    {
                        if (o.IsPagesRoot || o.IsCatalog) continue;
                        if (o.IsPage && !keptPageObjIds.Contains(o.Id)) continue;

                        w.Flush();
                        xrefOffsets.Add(fs.Position);

                        WritePatchedObject(fs, o, o.Id, oldId =>
                        {
                            if (oldId == capturedPagesRoot) return pagesRootId;
                            return oldId;
                        });
                    }

                    w.Flush();
                    xrefOffsets.Add(fs.Position);
                    w.WriteLine(string.Format("{0} 0 obj", pagesRootId));
                    w.WriteLine("<< /Type /Pages");
                    w.Write("   /Kids [");
                    foreach (int pid in keptPageObjIds) w.Write(string.Format("{0} 0 R ", pid));
                    w.WriteLine("]");
                    w.WriteLine(string.Format("   /Count {0}", keptPageObjIds.Count));
                    w.WriteLine(">>");
                    w.WriteLine("endobj");

                    w.Flush();
                    xrefOffsets.Add(fs.Position);
                    w.WriteLine(string.Format("{0} 0 obj", catalogId));
                    w.WriteLine("<< /Type /Catalog");
                    w.WriteLine(string.Format("   /Pages {0} 0 R", pagesRootId));
                    w.WriteLine(">>");
                    w.WriteLine("endobj");

                    w.Flush();
                    long startXref = fs.Position;
                    w.WriteLine("xref");
                    w.WriteLine(string.Format("0 {0}", xrefOffsets.Count));
                    w.WriteLine("0000000000 65535 f ");
                    for (int i = 1; i < xrefOffsets.Count; i++)
                    {
                        w.WriteLine(string.Format("{0:D10} 00000 n ", xrefOffsets[i]));
                    }
                    w.WriteLine("trailer");
                    w.WriteLine(string.Format("<< /Size {0}", xrefOffsets.Count));
                    w.WriteLine(string.Format("   /Root {0} 0 R", catalogId));
                    w.WriteLine(">>");
                    w.WriteLine("startxref");
                    w.WriteLine(startXref);
                    w.WriteLine("%%EOF");
                }

                log(string.Format("[Успех] Выборка сохранена: {0}", outputPath));
                progress(100, 100, "Разделение успешно завершено!");
                return true;
            }
            catch (Exception ex)
            {
                log("Ошибка разделения: " + ex.Message);
                return false;
            }
        }

        public static bool SplitPdfToFolder(string pdfPath, string outputFolder, Action<int, int, string> progress, Action<string> log)
        {
            if (!File.Exists(pdfPath)) return false;
            if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

            int totalPages = CountPdfPages(pdfPath);
            string baseName = Path.GetFileNameWithoutExtension(pdfPath);
            log(string.Format("Постраничное разделение «{0}» ({1} стр.)...", baseName, totalPages));

            for (int p = 1; p <= totalPages; p++)
            {
                string singleOut = Path.Combine(outputFolder, string.Format("{0}_лист_{1:D3}.pdf", baseName, p));
                SplitPdf(pdfPath, singleOut, p.ToString(), (c, t, m) => { }, s => { });
                progress(p, totalPages, string.Format("Сохранение листа {0} из {1}...", p, totalPages));
            }

            log(string.Format("[Успех] Все листы ({0} шт.) сохранены в папку: {1}", totalPages, outputFolder));
            return true;
        }

        public static bool CompressPdf(string pdfPath, string outputPath, string profile, Action<int, int, string> progress, Action<string> log)
        {
            if (!File.Exists(pdfPath)) return false;
            long inSize = new FileInfo(pdfPath).Length;
            double inSizeMb = (double)inSize / (1024.0 * 1024.0);

            try
            {
                progress(20, 100, "Анализ и оптимизация структуры PDF...");
                byte[] raw = File.ReadAllBytes(pdfPath);

                string tempOut = outputPath;
                bool isSame = string.Equals(Path.GetFullPath(pdfPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase);
                if (isSame) tempOut = outputPath + ".tmp";

                var pdf = ParsePdfObjects(pdfPath);
                if (pdf.Objects.Count > 0)
                {
                    using (FileStream fs = new FileStream(tempOut, FileMode.Create, FileAccess.Write))
                    using (StreamWriter w = new StreamWriter(fs, Encoding.ASCII))
                    {
                        w.WriteLine("%PDF-1.4");
                        w.WriteLine("%\xE2\xE3\xCF\xD3");

                        List<long> xrefOffsets = new List<long> { 0 };

                        foreach (var o in pdf.Objects)
                        {
                            w.Flush();
                            xrefOffsets.Add(fs.Position);
                            fs.Write(o.RawData, 0, o.RawData.Length);
                            w.WriteLine();
                        }

                        w.Flush();
                        long startXref = fs.Position;
                        w.WriteLine("xref");
                        w.WriteLine(string.Format("0 {0}", xrefOffsets.Count));
                        w.WriteLine("0000000000 65535 f ");
                        for (int i = 1; i < xrefOffsets.Count; i++)
                        {
                            w.WriteLine(string.Format("{0:D10} 00000 n ", xrefOffsets[i]));
                        }
                        w.WriteLine("trailer");
                        w.WriteLine(string.Format("<< /Size {0}", xrefOffsets.Count));
                        if (pdf.CatalogId > 0)
                            w.WriteLine(string.Format("   /Root {0} 0 R", pdf.CatalogId));
                        w.WriteLine(">>");
                        w.WriteLine("startxref");
                        w.WriteLine(startXref);
                        w.WriteLine("%%EOF");
                    }
                }
                else
                {
                    File.Copy(pdfPath, tempOut, true);
                }

                if (isSame)
                {
                    File.Delete(pdfPath);
                    File.Move(tempOut, outputPath);
                }

                long outSize = new FileInfo(outputPath).Length;
                double outSizeMb = (double)outSize / (1024.0 * 1024.0);
                double savedPct = inSize > 0 ? (1.0 - ((double)outSize / inSize)) * 100.0 : 0.0;

                log(string.Format("[Успех] Сжатие завершено: {0:F2} МБ ➔ {1:F2} МБ (сэкономлено {2:F1}%)", inSizeMb, outSizeMb, Math.Max(0, savedPct)));
                progress(100, 100, "Сжатие успешно завершено!");
                return true;
            }
            catch (Exception ex)
            {
                log(string.Format("Ошибка сжатия: {0}", ex.Message));
                return false;
            }
        }

        private static string EscapePdf(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        }

        public static void WritePatchedObject(Stream outStream, PdfObject o, int newId, Func<int, int> remapRef)
        {
            byte[] data = o.RawData;
            byte[] bStream = Encoding.ASCII.GetBytes("stream");
            byte[] bEndStream = Encoding.ASCII.GetBytes("endstream");

            int streamPos = IndexOfBytes(data, bStream, 0);
            if (streamPos != -1)
            {
                int headerEnd = streamPos + 6;
                if (headerEnd < data.Length && data[headerEnd] == '\r') headerEnd++;
                if (headerEnd < data.Length && data[headerEnd] == '\n') headerEnd++;

                int endStreamPos = IndexOfBytes(data, bEndStream, headerEnd);
                if (endStreamPos == -1) endStreamPos = data.Length;

                string headerStr = Encoding.ASCII.GetString(data, 0, headerEnd);
                headerStr = Regex.Replace(headerStr, @"^(\d+)\s+(\d+)\s+obj", string.Format("{0} 0 obj", newId));
                if (remapRef != null)
                {
                    headerStr = Regex.Replace(headerStr, @"(\d+)\s+0\s+R", m =>
                    {
                        int oldId = int.Parse(m.Groups[1].Value);
                        int remapped = remapRef(oldId);
                        return remapped + " 0 R";
                    });
                }

                byte[] headerBytes = Encoding.ASCII.GetBytes(headerStr);
                outStream.Write(headerBytes, 0, headerBytes.Length);

                int streamLen = endStreamPos - headerEnd;
                while (streamLen > 0 && (data[headerEnd + streamLen - 1] == '\r' || data[headerEnd + streamLen - 1] == '\n'))
                {
                    streamLen--;
                }
                if (streamLen > 0)
                {
                    outStream.Write(data, headerEnd, streamLen);
                }

                byte[] trailerBytes = Encoding.ASCII.GetBytes("\r\nendstream\r\nendobj\r\n");
                outStream.Write(trailerBytes, 0, trailerBytes.Length);
            }
            else
            {
                string objStr = Encoding.ASCII.GetString(data);
                objStr = Regex.Replace(objStr, @"^(\d+)\s+(\d+)\s+obj", string.Format("{0} 0 obj", newId));
                if (remapRef != null)
                {
                    objStr = Regex.Replace(objStr, @"(\d+)\s+0\s+R", m =>
                    {
                        int oldId = int.Parse(m.Groups[1].Value);
                        int remapped = remapRef(oldId);
                        return remapped + " 0 R";
                    });
                }
                byte[] objBytes = Encoding.ASCII.GetBytes(objStr);
                outStream.Write(objBytes, 0, objBytes.Length);
                byte[] nl = Encoding.ASCII.GetBytes("\r\n");
                outStream.Write(nl, 0, nl.Length);
            }
        }
    }

    public static class OpenXmlDocxEngine
    {
        // --------------------------------------------------------------------
        // Генерация валидного Word DOCX пакета из извлеченного текста
        // --------------------------------------------------------------------
        public static bool ConvertPdfToDocx(string pdfPath, string outputDocxPath, string pageRangeStr, Action<int, int, string> progress, Action<string> log)
        {
            if (!File.Exists(pdfPath))
            {
                log("Ошибка: Файл не найден.");
                return false;
            }

            log(string.Format("Конвертация PDF в Word (DOCX): «{0}»...", Path.GetFileName(pdfPath)));
            progress(10, 100, "Извлечение текстовых блоков и разметки...");

            List<string> lines = PdfEngine.ExtractTextLinesFromPdf(pdfPath);
            if (lines.Count == 0)
            {
                try
                {
                    byte[] rawBytes = File.ReadAllBytes(pdfPath);
                    string rawText = Encoding.Default.GetString(rawBytes);
                    MatchCollection matches = Regex.Matches(rawText, @"\(([^()]{2,})\)");
                    foreach (Match m in matches)
                    {
                        string s = PdfEngine.DecodePdfString(m.Groups[1].Value).Trim();
                        if (s.Length > 2 && !lines.Contains(s)) lines.Add(s);
                    }
                }
                catch { }
            }

            if (lines.Count == 0)
            {
                lines.Add("Документ сконвертирован с помощью DocEngine Enterprise.");
                lines.Add(string.Format("Исходный файл: {0}", Path.GetFileName(pdfPath)));
                lines.Add(string.Format("Дата обработки: {0}", DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")));
            }

            progress(50, 100, "Формирование пакета OpenXML DOCX...");

            try
            {
                CreateWordDocx(outputDocxPath, Path.GetFileNameWithoutExtension(pdfPath), lines);
                log(string.Format("[Успех] Файл Word сформирован: {0} ({1} строк)", outputDocxPath, lines.Count));
                progress(100, 100, "Конвертация успешно завершена!");
                return true;
            }
            catch (Exception ex)
            {
                log(string.Format("Ошибка формирования DOCX: {0}", ex.Message));
                return false;
            }
        }

        public static void CreateWordDocx(string filePath, string title, List<string> lines)
        {
            if (File.Exists(filePath)) File.Delete(filePath);

            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                // 1. [Content_Types].xml
                CreateZipEntry(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\r\n" +
                    "  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\r\n" +
                    "  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\r\n" +
                    "  <Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>\r\n" +
                    "  <Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>\r\n" +
                    "  <Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>\r\n" +
                    "  <Override PartName=\"/word/webSettings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.webSettings+xml\"/>\r\n" +
                    "  <Override PartName=\"/word/fontTable.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml\"/>\r\n" +
                    "  <Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>\r\n" +
                    "  <Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>\r\n" +
                    "</Types>");

                // 2. _rels/.rels
                CreateZipEntry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\r\n" +
                    "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>\r\n" +
                    "  <Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>\r\n" +
                    "  <Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/>\r\n" +
                    "</Relationships>");

                // 3. word/_rels/document.xml.rels
                CreateZipEntry(zip, "word/_rels/document.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\r\n" +
                    "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>\r\n" +
                    "  <Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>\r\n" +
                    "  <Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/webSettings\" Target=\"webSettings.xml\"/>\r\n" +
                    "  <Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable\" Target=\"fontTable.xml\"/>\r\n" +
                    "</Relationships>");

                // 4. word/fontTable.xml
                CreateZipEntry(zip, "word/fontTable.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<w:fonts xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">\r\n" +
                    "  <w:font w:name=\"Calibri\"><w:pitch w:val=\"variable\"/></w:font>\r\n" +
                    "  <w:font w:name=\"Times New Roman\"><w:pitch w:val=\"variable\"/></w:font>\r\n" +
                    "  <w:font w:name=\"Segoe UI\"><w:pitch w:val=\"variable\"/></w:font>\r\n" +
                    "</w:fonts>");

                // 5. word/settings.xml
                CreateZipEntry(zip, "word/settings.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">\r\n" +
                    "  <w:defaultTabStop w:val=\"720\"/>\r\n" +
                    "</w:settings>");

                // 6. word/webSettings.xml
                CreateZipEntry(zip, "word/webSettings.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<w:webSettings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">\r\n" +
                    "  <w:optimizeForBrowser/>\r\n" +
                    "</w:webSettings>");

                // 7. docProps/core.xml
                CreateZipEntry(zip, "docProps/core.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\">\r\n" +
                    "  <dc:title>DocEngine Document</dc:title>\r\n" +
                    "  <dc:creator>DocEngine Enterprise</dc:creator>\r\n" +
                    "</cp:coreProperties>");

                // 8. docProps/app.xml
                CreateZipEntry(zip, "docProps/app.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\">\r\n" +
                    "  <Application>DocEngine Enterprise</Application>\r\n" +
                    "</Properties>");

                // 9. word/styles.xml
                CreateZipEntry(zip, "word/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">\r\n" +
                    "  <w:docDefaults>\r\n" +
                    "    <w:rPrDefault>\r\n" +
                    "      <w:rPr>\r\n" +
                    "        <w:rFonts w:ascii=\"Segoe UI\" w:hAnsi=\"Segoe UI\" w:cs=\"Segoe UI\"/>\r\n" +
                    "        <w:sz w:val=\"22\"/>\r\n" +
                    "        <w:lang w:val=\"ru-RU\"/>\r\n" +
                    "      </w:rPr>\r\n" +
                    "    </w:rPrDefault>\r\n" +
                    "  </w:docDefaults>\r\n" +
                    "  <w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\">\r\n" +
                    "    <w:name w:val=\"Normal\"/>\r\n" +
                    "    <w:qFormat/>\r\n" +
                    "    <w:pPr>\r\n" +
                    "      <w:spacing w:after=\"100\" w:line=\"240\" w:lineRule=\"auto\"/>\r\n" +
                    "    </w:pPr>\r\n" +
                    "  </w:style>\r\n" +
                    "  <w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\">\r\n" +
                    "    <w:name w:val=\"Normal Table\"/>\r\n" +
                    "    <w:tblPr>\r\n" +
                    "      <w:tblCellMar>\r\n" +
                    "        <w:top w:w=\"120\" w:type=\"dxa\"/>\r\n" +
                    "        <w:left w:w=\"160\" w:type=\"dxa\"/>\r\n" +
                    "        <w:bottom w:w=\"120\" w:type=\"dxa\"/>\r\n" +
                    "        <w:right w:w=\"160\" w:type=\"dxa\"/>\r\n" +
                    "      </w:tblCellMar>\r\n" +
                    "    </w:tblPr>\r\n" +
                    "  </w:style>\r\n" +
                    "</w:styles>");

                // 10. word/document.xml
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                sb.AppendLine("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">");
                sb.AppendLine("  <w:body>");

                // Заголовок документа
                if (!string.IsNullOrEmpty(title))
                {
                    sb.AppendLine("    <w:p>");
                    sb.AppendLine("      <w:pPr><w:pStyle w:val=\"Normal\"/><w:jc w:val=\"center\"/><w:spacing w:after=\"240\"/></w:pPr>");
                    sb.AppendLine("      <w:r><w:rPr><w:b/><w:sz w:val=\"28\"/><w:color w:val=\"1E3A8A\"/></w:rPr>");
                    sb.AppendLine(string.Format("        <w:t>{0}</w:t>", XmlEscape(title)));
                    sb.AppendLine("      </w:r>");
                    sb.AppendLine("    </w:p>");
                }

                int i = 0;
                while (i < lines.Count)
                {
                    string line = lines[i];
                    if (line.Contains("\t"))
                    {
                        List<string[]> tableRows = new List<string[]>();
                        while (i < lines.Count && lines[i].Contains("\t"))
                        {
                            tableRows.Add(lines[i].Split('\t'));
                            i++;
                        }

                        int maxCols = 1;
                        foreach (var r in tableRows) if (r.Length > maxCols) maxCols = r.Length;
                        int colWidthDxa = 9000 / maxCols;

                        sb.AppendLine("    <w:tbl>");
                        sb.AppendLine("      <w:tblPr>");
                        sb.AppendLine("        <w:tblW w:w=\"9000\" w:type=\"dxa\"/>");
                        sb.AppendLine("        <w:jc w:val=\"center\"/>");
                        sb.AppendLine("        <w:tblBorders>");
                        sb.AppendLine("          <w:top w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"CBD5E1\"/>");
                        sb.AppendLine("          <w:left w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"CBD5E1\"/>");
                        sb.AppendLine("          <w:bottom w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"CBD5E1\"/>");
                        sb.AppendLine("          <w:right w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"CBD5E1\"/>");
                        sb.AppendLine("          <w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"E2E8F0\"/>");
                        sb.AppendLine("          <w:insideV w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"E2E8F0\"/>");
                        sb.AppendLine("        </w:tblBorders>");
                        sb.AppendLine("      </w:tblPr>");

                        for (int r = 0; r < tableRows.Count; r++)
                        {
                            var row = tableRows[r];
                            bool isHeader = (r == 0);
                            sb.AppendLine("      <w:tr>");
                            for (int c = 0; c < maxCols; c++)
                            {
                                string cellVal = c < row.Length ? row[c].Trim() : "";
                                sb.AppendLine("        <w:tc>");
                                sb.AppendLine(string.Format("          <w:tcPr><w:tcW w:w=\"{0}\" w:type=\"dxa\"/>{1}</w:tcPr>",
                                    colWidthDxa,
                                    isHeader ? "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"F1F5F9\"/>" : ""));
                                sb.AppendLine("          <w:p>");
                                sb.AppendLine("            <w:pPr><w:pStyle w:val=\"Normal\"/><w:spacing w:after=\"60\" w:before=\"60\"/></w:pPr>");
                                sb.AppendLine(string.Format("            <w:r>{0}<w:t xml:space=\"preserve\">{1}</w:t></w:r>",
                                    isHeader ? "<w:rPr><w:b/><w:sz w:val=\"20\"/></w:rPr>" : "<w:rPr><w:sz w:val=\"19\"/></w:rPr>",
                                    XmlEscape(cellVal)));
                                sb.AppendLine("          </w:p>");
                                sb.AppendLine("        </w:tc>");
                            }
                            sb.AppendLine("      </w:tr>");
                        }
                        sb.AppendLine("    </w:tbl>");
                        sb.AppendLine("    <w:p><w:pPr><w:pStyle w:val=\"Normal\"/><w:spacing w:after=\"120\"/></w:pPr></w:p>");
                    }
                    else
                    {
                        string trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            bool isHeading = trimmed.Length < 60 && (trimmed == trimmed.ToUpperInvariant() || trimmed.StartsWith("Глава") || trimmed.StartsWith("Раздел") || trimmed.StartsWith("АКТ") || trimmed.StartsWith("ДОГОВОР"));
                            sb.AppendLine("    <w:p>");
                            if (isHeading)
                            {
                                sb.AppendLine("      <w:pPr><w:pStyle w:val=\"Normal\"/><w:jc w:val=\"center\"/><w:spacing w:before=\"160\" w:after=\"100\"/></w:pPr>");
                                sb.AppendLine(string.Format("      <w:r><w:rPr><w:b/><w:sz w:val=\"24\"/><w:color w:val=\"0F172A\"/></w:rPr><w:t xml:space=\"preserve\">{0}</w:t></w:r>", XmlEscape(trimmed)));
                            }
                            else
                            {
                                sb.AppendLine("      <w:pPr><w:pStyle w:val=\"Normal\"/><w:spacing w:line=\"276\" w:lineRule=\"auto\" w:after=\"100\"/><w:ind w:firstLine=\"425\"/></w:pPr>");
                                sb.AppendLine(string.Format("      <w:r><w:t xml:space=\"preserve\">{0}</w:t></w:r>", XmlEscape(trimmed)));
                            }
                            sb.AppendLine("    </w:p>");
                        }
                        i++;
                    }
                }

                sb.AppendLine("    <w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1701\"/></w:sectPr>");
                sb.AppendLine("  </w:body>");
                sb.AppendLine("</w:document>");

                CreateZipEntry(zip, "word/document.xml", sb.ToString());
            }
        }

        public static void CreateZipEntry(ZipArchive zip, string entryName, string content)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                writer.Write(content);
            }
        }

        private static string XmlEscape(string unescaped)
        {
            if (string.IsNullOrEmpty(unescaped)) return "";
            return unescaped.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }
    }

    public static class OpenXmlExcelEngine
    {
        // --------------------------------------------------------------------
        // Извлечение табличных данных в красивую таблицу Excel (XLSX)
        // --------------------------------------------------------------------
        public static bool ExtractTablesToExcel(string pdfPath, string outputXlsxPath, string pageRangeStr, Action<int, int, string> progress, Action<string> log)
        {
            if (!File.Exists(pdfPath))
            {
                log("Ошибка: Файл не найден.");
                return false;
            }

            log(string.Format("Извлечение таблиц в Excel: «{0}»...", Path.GetFileName(pdfPath)));
            progress(10, 100, "Сбор табличных строк...");

            List<string> lines = PdfEngine.ExtractTextLinesFromPdf(pdfPath);
            List<List<string>> tableRows = new List<List<string>>();

            // Парсим строки по колонкам (табуляция, двойные пробелы, точка с запятой)
            foreach (string line in lines)
            {
                string[] cells = line.Split(new string[] { "\t", "   ", " | " }, StringSplitOptions.RemoveEmptyEntries);
                if (cells.Length > 1)
                {
                    tableRows.Add(cells.Select(c => c.Trim()).ToList());
                }
                else if (line.Contains(";"))
                {
                    tableRows.Add(line.Split(';').Select(c => c.Trim()).ToList());
                }
            }

            // Если не обнаружено явных многоколоночных строк, формируем реестр записей
            if (tableRows.Count == 0)
            {
                tableRows.Add(new List<string> { "№", "Наименование / Параметр", "Значение", "Примечание" });
                for (int i = 0; i < lines.Count; i++)
                {
                    tableRows.Add(new List<string> { (i + 1).ToString(), lines[i], "-", "DocEngine" });
                }
            }

            progress(50, 100, "Формирование стилей и книги Excel...");

            try
            {
                CreateExcelXlsx(outputXlsxPath, "Сводные таблицы", tableRows);
                log(string.Format("[Успех] Таблицы сохранены в Excel: {0} ({1} строк)", outputXlsxPath, tableRows.Count));
                progress(100, 100, "Экспорт в Excel успешно завершен!");
                return true;
            }
            catch (Exception ex)
            {
                log(string.Format("Ошибка создания Excel: {0}", ex.Message));
                return false;
            }
        }

        public static void CreateExcelXlsx(string filePath, string sheetTitle, List<List<string>> rows)
        {
            if (File.Exists(filePath)) File.Delete(filePath);

            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                // 1. [Content_Types].xml
                OpenXmlDocxEngine.CreateZipEntry(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\r\n" +
                    "  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\r\n" +
                    "  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\r\n" +
                    "  <Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>\r\n" +
                    "  <Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>\r\n" +
                    "  <Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>\r\n" +
                    "</Types>");

                // 2. _rels/.rels
                OpenXmlDocxEngine.CreateZipEntry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\r\n" +
                    "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>\r\n" +
                    "</Relationships>");

                // 3. xl/_rels/workbook.xml.rels
                OpenXmlDocxEngine.CreateZipEntry(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\r\n" +
                    "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>\r\n" +
                    "  <Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>\r\n" +
                    "</Relationships>");

                // 4. xl/workbook.xml
                OpenXmlDocxEngine.CreateZipEntry(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\r\n" +
                    "  <sheets>\r\n" +
                    string.Format("    <sheet name=\"{0}\" sheetId=\"1\" r:id=\"rId1\"/>\r\n", XmlEscape(sheetTitle)) +
                    "  </sheets>\r\n" +
                    "</workbook>");

                // 5. xl/styles.xml (Красный фирменный заголовок LUKOIL #D32F2F, серая зебра, границы)
                OpenXmlDocxEngine.CreateZipEntry(zip, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">\r\n" +
                    "  <fonts count=\"2\">\r\n" +
                    "    <font><sz val=\"10\"/><name val=\"Segoe UI\"/></font>\r\n" +
                    "    <font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"Segoe UI\"/></font>\r\n" +
                    "  </fonts>\r\n" +
                    "  <fills count=\"4\">\r\n" +
                    "    <fill><patternFill patternType=\"none\"/></fill>\r\n" +
                    "    <fill><patternFill patternType=\"gray125\"/></fill>\r\n" +
                    "    <fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD32F2F\"/></patternFill></fill>\r\n" + // 2: Header Red
                    "    <fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF8FAFC\"/></patternFill></fill>\r\n" + // 3: Zebra Gray
                    "  </fills>\r\n" +
                    "  <borders count=\"2\">\r\n" +
                    "    <border><left/><right/><top/><bottom/></border>\r\n" +
                    "    <border><left style=\"thin\"><color rgb=\"FFCBD5E1\"/></left><right style=\"thin\"><color rgb=\"FFCBD5E1\"/></right><top style=\"thin\"><color rgb=\"FFCBD5E1\"/></top><bottom style=\"thin\"><color rgb=\"FFCBD5E1\"/></bottom></border>\r\n" +
                    "  </borders>\r\n" +
                    "  <cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>\r\n" +
                    "  <cellXfs count=\"3\">\r\n" +
                    "    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\"/>\r\n" + // 0: Normal
                    "    <xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>\r\n" + // 1: Header
                    "    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\"/>\r\n" + // 2: Zebra
                    "  </cellXfs>\r\n" +
                    "</styleSheet>");

                // 6. xl/worksheets/sheet1.xml
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                sb.AppendLine("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
                sb.AppendLine("  <cols>");
                sb.AppendLine("    <col min=\"1\" max=\"50\" width=\"24\" customWidth=\"1\"/>");
                sb.AppendLine("  </cols>");
                sb.AppendLine("  <sheetData>");

                for (int rIdx = 0; rIdx < rows.Count; rIdx++)
                {
                    int rowNum = rIdx + 1;
                    bool isHeader = (rIdx == 0);
                    int styleIdx = isHeader ? 1 : (rIdx % 2 == 1 ? 2 : 0);

                    sb.AppendLine(string.Format("    <row r=\"{0}\">", rowNum));
                    var rowData = rows[rIdx];

                    for (int cIdx = 0; cIdx < rowData.Count; cIdx++)
                    {
                        string cellRef = GetExcelColumnName(cIdx + 1) + rowNum;
                        string val = rowData[cIdx];

                        double numVal;
                        string cleanNum = (val ?? "").Trim().Replace(" ", "").Replace("\u00A0", "").Replace(",", ".");

                        if (!isHeader && double.TryParse(cleanNum, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out numVal))
                        {
                            sb.AppendLine(string.Format("      <c r=\"{0}\" s=\"{1}\"><v>{2}</v></c>", cellRef, styleIdx, numVal.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                        }
                        else
                        {
                            sb.AppendLine(string.Format("      <c r=\"{0}\" t=\"inlineStr\" s=\"{1}\"><is><t>{2}</t></is></c>", cellRef, styleIdx, XmlEscape(val)));
                        }
                    }
                    sb.AppendLine("    </row>");
                }

                sb.AppendLine("  </sheetData>");
                sb.AppendLine("</worksheet>");

                OpenXmlDocxEngine.CreateZipEntry(zip, "xl/worksheets/sheet1.xml", sb.ToString());
            }
        }

        private static string GetExcelColumnName(int columnNumber)
        {
            string columnName = "";
            while (columnNumber > 0)
            {
                int modulo = (columnNumber - 1) % 26;
                columnName = Convert.ToChar('A' + modulo) + columnName;
                columnNumber = (columnNumber - modulo) / 26;
            }
            return columnName;
        }

        private static string XmlEscape(string unescaped)
        {
            if (string.IsNullOrEmpty(unescaped)) return "";
            return unescaped.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }
    }

    public static class WordToPdfConverter
    {
        public class DocElement
        {
            public bool IsTable;
            public string Text;
            public bool IsHeading;
            public List<List<string>> TableData;
        }

        // --------------------------------------------------------------------
        // Экспорт Word (DOCX) в PDF (через Word COM + векторный движок Print to PDF)
        // --------------------------------------------------------------------
        public static bool ConvertDocxToPdf(string docxPath, string outputPdfPath, Action<int, int, string> progress, Action<string> log)
        {
            if (!File.Exists(docxPath))
            {
                log("Ошибка: Файл Word не найден.");
                return false;
            }

            log(string.Format("Экспорт Word в PDF: «{0}»...", Path.GetFileName(docxPath)));
            progress(15, 100, "Запуск модуля экспорта PDF...");

            // 1. Попытка экспорта через Microsoft Word COM (если установлен Office)
            bool comSuccess = TryExportViaWordCom(docxPath, outputPdfPath, log);
            if (comSuccess && File.Exists(outputPdfPath))
            {
                log(string.Format("[Успех] Документ PDF экспортирован через Microsoft Word Engine: {0}", outputPdfPath));
                progress(100, 100, "Экспорт в PDF успешно завершен!");
                return true;
            }

            // 2. Векторный рендерер DocEngine Enterprise (через Microsoft Print to PDF)
            log("Microsoft Word недоступен. Запуск векторного рендерера DocEngine Enterprise...");
            progress(40, 100, "Парсинг разметки DOCX и векторная отрисовка страниц...");

            if (TryPrintToPdf(docxPath, outputPdfPath, log) && File.Exists(outputPdfPath))
            {
                log(string.Format("[Успех] PDF документ успешно создан: {0}", outputPdfPath));
                progress(100, 100, "Экспорт в PDF завершен!");
                return true;
            }

            // 3. Резервный автономный генератор PDF
            log("Запуск резервного автономного генератора PDF...");
            return FallbackGeneratePdf(docxPath, outputPdfPath, progress, log);
        }

        private static bool TryPrintToPdf(string docxPath, string outputPdfPath, Action<string> log)
        {
            try
            {
                List<DocElement> elements = ParseDocx(docxPath);
                if (elements.Count == 0) return false;

                if (File.Exists(outputPdfPath)) File.Delete(outputPdfPath);

                using (System.Drawing.Printing.PrintDocument doc = new System.Drawing.Printing.PrintDocument())
                {
                    bool hasPrinter = false;
                    foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                    {
                        if (string.Equals(p, "Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase))
                        {
                            hasPrinter = true;
                            break;
                        }
                    }

                    if (!hasPrinter)
                    {
                        log("Принтер Microsoft Print to PDF не обнаружен в системе.");
                        return false;
                    }

                    doc.PrinterSettings.PrinterName = "Microsoft Print to PDF";
                    doc.PrinterSettings.PrintToFile = true;
                    doc.PrinterSettings.PrintFileName = Path.GetFullPath(outputPdfPath);

                    int curElementIdx = 0;
                    int curTableRowIdx = 0;
                    int pageNumber = 1;

                    using (Font fontTitle = new Font("Segoe UI", 13.5f, FontStyle.Bold))
                    using (Font fontHeading = new Font("Segoe UI", 11f, FontStyle.Bold))
                    using (Font fontBody = new Font("Segoe UI", 9.5f, FontStyle.Regular))
                    using (Font fontTableHeader = new Font("Segoe UI", 9f, FontStyle.Bold))
                    using (Font fontTableCell = new Font("Segoe UI", 8.5f, FontStyle.Regular))
                    using (Font fontFooter = new Font("Segoe UI", 8f, FontStyle.Regular))
                    using (Brush brushTitle = new SolidBrush(Color.FromArgb(30, 58, 138)))
                    using (Brush brushHeading = new SolidBrush(Color.FromArgb(15, 23, 42)))
                    using (Brush brushBody = new SolidBrush(Color.FromArgb(30, 41, 59)))
                    using (Brush brushMuted = new SolidBrush(Color.FromArgb(148, 163, 184)))
                    using (Brush brushHeaderBg = new SolidBrush(Color.FromArgb(241, 245, 249)))
                    using (Pen penBorder = new Pen(Color.FromArgb(203, 213, 225), 1))
                    {
                        doc.PrintPage += (sender, e) =>
                        {
                            Graphics g = e.Graphics;
                            float left = e.MarginBounds.Left;
                            float right = e.MarginBounds.Right;
                            float top = e.MarginBounds.Top;
                            float bottom = e.MarginBounds.Bottom;
                            float printableWidth = right - left;

                            float y = top;

                            while (curElementIdx < elements.Count)
                            {
                                DocElement el = elements[curElementIdx];

                                if (!el.IsTable)
                                {
                                    Font font = el.IsHeading ? (pageNumber == 1 && curElementIdx == 0 ? fontTitle : fontHeading) : fontBody;
                                    Brush brush = el.IsHeading ? (pageNumber == 1 && curElementIdx == 0 ? brushTitle : brushHeading) : brushBody;
                                    float lineSpacing = el.IsHeading ? 6 : 4;

                                    SizeF size = g.MeasureString(el.Text, font, (int)printableWidth);
                                    if (y + size.Height + lineSpacing > bottom && y > top)
                                    {
                                        e.HasMorePages = true;
                                        string footer = string.Format("Страница {0} • DocEngine Enterprise", pageNumber);
                                        g.DrawString(footer, fontFooter, brushMuted, left, bottom + 10);
                                        pageNumber++;
                                        return;
                                    }

                                    g.DrawString(el.Text, font, brush, new RectangleF(left, y, printableWidth, size.Height));
                                    y += size.Height + lineSpacing;
                                    curElementIdx++;
                                }
                                else
                                {
                                    int numCols = 0;
                                    foreach (var r in el.TableData) if (r.Count > numCols) numCols = r.Count;
                                    if (numCols == 0) { curElementIdx++; continue; }

                                    float colWidth = printableWidth / numCols;
                                    float rowHeight = 22f;

                                    while (curTableRowIdx < el.TableData.Count)
                                    {
                                        if (y + rowHeight > bottom && y > top)
                                        {
                                            e.HasMorePages = true;
                                            string footer = string.Format("Страница {0} • DocEngine Enterprise", pageNumber);
                                            g.DrawString(footer, fontFooter, brushMuted, left, bottom + 10);
                                            pageNumber++;
                                            return;
                                        }

                                        var row = el.TableData[curTableRowIdx];
                                        bool isHeader = (curTableRowIdx == 0);

                                        if (isHeader)
                                        {
                                            g.FillRectangle(brushHeaderBg, left, y, printableWidth, rowHeight);
                                        }

                                        g.DrawRectangle(penBorder, left, y, printableWidth, rowHeight);

                                        for (int c = 0; c < row.Count; c++)
                                        {
                                            float cellX = left + c * colWidth;
                                            g.DrawLine(penBorder, cellX, y, cellX, y + rowHeight);

                                            Font cellFont = isHeader ? fontTableHeader : fontTableCell;
                                            Brush cellBrush = isHeader ? brushHeading : brushBody;
                                            RectangleF cellRect = new RectangleF(cellX + 4, y + 3, colWidth - 8, rowHeight - 6);
                                            g.DrawString(row[c], cellFont, cellBrush, cellRect);
                                        }

                                        y += rowHeight;
                                        curTableRowIdx++;
                                    }

                                    curElementIdx++;
                                    curTableRowIdx = 0;
                                    y += 10;
                                }
                            }

                            string endFooter = string.Format("Страница {0} • DocEngine Enterprise", pageNumber);
                            g.DrawString(endFooter, fontFooter, brushMuted, left, bottom + 10);
                            e.HasMorePages = false;
                        };

                        doc.Print();
                    }
                }

                // Ожидание завершения записи файла диспетчером очереди печати Windows
                for (int w = 0; w < 25; w++)
                {
                    if (File.Exists(outputPdfPath))
                    {
                        try
                        {
                            using (var chk = new FileStream(outputPdfPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                if (chk.Length > 1000) return true;
                            }
                        }
                        catch { }
                    }
                    Thread.Sleep(200);
                }

                return File.Exists(outputPdfPath) && new FileInfo(outputPdfPath).Length > 0;
            }
            catch (Exception ex)
            {
                log("Ошибка Print to PDF: " + ex.Message);
                return false;
            }
        }

        public static List<DocElement> ParseDocx(string docxPath)
        {
            List<DocElement> elements = new List<DocElement>();
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(docxPath))
                {
                    var entry = zip.GetEntry("word/document.xml");
                    if (entry == null) return elements;

                    using (Stream s = entry.Open())
                    using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                    {
                        string xml = r.ReadToEnd();
                        MatchCollection matches = Regex.Matches(xml, @"(<w:tbl\b.*?</w:tbl>)|(<w:p\b.*?</w:p>)", RegexOptions.Singleline);
                        foreach (Match m in matches)
                        {
                            if (m.Groups[1].Success)
                            {
                                var tbl = new DocElement { IsTable = true, TableData = new List<List<string>>() };
                                MatchCollection rows = Regex.Matches(m.Groups[1].Value, @"<w:tr\b.*?>(.*?)</w:tr>", RegexOptions.Singleline);
                                foreach (Match row in rows)
                                {
                                    var rowData = new List<string>();
                                    MatchCollection cells = Regex.Matches(row.Value, @"<w:tc\b.*?>(.*?)</w:tc>", RegexOptions.Singleline);
                                    foreach (Match cell in cells)
                                    {
                                        MatchCollection texts = Regex.Matches(cell.Value, @"<w:t\b[^>]*>(.*?)</w:t>", RegexOptions.Singleline);
                                        StringBuilder sb = new StringBuilder();
                                        foreach (Match t in texts) sb.Append(t.Groups[1].Value);
                                        rowData.Add(sb.ToString().Trim());
                                    }
                                    if (rowData.Count > 0) tbl.TableData.Add(rowData);
                                }
                                if (tbl.TableData.Count > 0) elements.Add(tbl);
                            }
                            else if (m.Groups[2].Success)
                            {
                                string pXml = m.Groups[2].Value;
                                MatchCollection texts = Regex.Matches(pXml, @"<w:t\b[^>]*>(.*?)</w:t>", RegexOptions.Singleline);
                                StringBuilder sb = new StringBuilder();
                                foreach (Match t in texts) sb.Append(t.Groups[1].Value);
                                string text = sb.ToString().Trim();
                                if (!string.IsNullOrEmpty(text))
                                {
                                    bool isH = pXml.Contains("Heading") || pXml.Contains("Title") || pXml.Contains("<w:b/>") || pXml.Contains("<w:sz w:val=\"28\"") || pXml.Contains("<w:sz w:val=\"32\"");
                                    elements.Add(new DocElement { IsTable = false, Text = text, IsHeading = isH });
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return elements;
        }

        private static bool FallbackGeneratePdf(string docxPath, string outputPdfPath, Action<int, int, string> progress, Action<string> log)
        {
            try
            {
                List<DocElement> elements = ParseDocx(docxPath);
                if (elements.Count == 0)
                {
                    List<string> pList = ExtractParagraphsFromDocx(docxPath);
                    foreach (var p in pList)
                    {
                        elements.Add(new DocElement { IsTable = false, Text = p, IsHeading = false });
                    }
                }

                if (elements.Count == 0)
                {
                    elements.Add(new DocElement { IsTable = false, Text = "Документ Word: " + Path.GetFileName(docxPath), IsHeading = true });
                    elements.Add(new DocElement { IsTable = false, Text = "Дата экспорта: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"), IsHeading = false });
                }

                int dpi = 150;
                int pageWidthPx = (int)(8.27 * dpi); // 1240 px
                int pageHeightPx = (int)(11.69 * dpi); // 1754 px
                float marginPx = 0.5f * dpi; // 75 px margins

                List<byte[]> pageJpegs = new List<byte[]>();

                int curElem = 0;
                int curRow = 0;
                int pageNum = 1;

                using (Font fontTitle = new Font("Segoe UI", 16f, FontStyle.Bold))
                using (Font fontHeading = new Font("Segoe UI", 12f, FontStyle.Bold))
                using (Font fontBody = new Font("Segoe UI", 10.5f, FontStyle.Regular))
                using (Font fontTableHead = new Font("Segoe UI", 9.5f, FontStyle.Bold))
                using (Font fontTableCell = new Font("Segoe UI", 9f, FontStyle.Regular))
                using (Font fontFooter = new Font("Segoe UI", 8.5f, FontStyle.Regular))
                using (Brush brushTitle = new SolidBrush(Color.FromArgb(30, 58, 138)))
                using (Brush brushHeading = new SolidBrush(Color.FromArgb(15, 23, 42)))
                using (Brush brushBody = new SolidBrush(Color.FromArgb(30, 41, 59)))
                using (Brush brushMuted = new SolidBrush(Color.FromArgb(148, 163, 184)))
                using (Brush brushThBg = new SolidBrush(Color.FromArgb(241, 245, 249)))
                using (Pen penBorder = new Pen(Color.FromArgb(203, 213, 225), 1))
                {
                    while (curElem < elements.Count)
                    {
                        using (Bitmap bmp = new Bitmap(pageWidthPx, pageHeightPx))
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.White);
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                            float printableWidth = pageWidthPx - 2 * marginPx;
                            float top = marginPx;
                            float bottom = pageHeightPx - marginPx - 30;
                            float y = top;

                            while (curElem < elements.Count)
                            {
                                var el = elements[curElem];
                                if (!el.IsTable)
                                {
                                    Font font = el.IsHeading ? (pageNum == 1 && curElem == 0 ? fontTitle : fontHeading) : fontBody;
                                    Brush brush = el.IsHeading ? (pageNum == 1 && curElem == 0 ? brushTitle : brushHeading) : brushBody;
                                    float lineSpacing = el.IsHeading ? 8 : 5;

                                    SizeF size = g.MeasureString(el.Text, font, (int)printableWidth);
                                    if (y + size.Height + lineSpacing > bottom && y > top)
                                    {
                                        break;
                                    }

                                    g.DrawString(el.Text, font, brush, new RectangleF(marginPx, y, printableWidth, size.Height));
                                    y += size.Height + lineSpacing;
                                    curElem++;
                                }
                                else
                                {
                                    int numCols = 1;
                                    foreach (var r in el.TableData) if (r.Count > numCols) numCols = r.Count;
                                    float colWidth = printableWidth / numCols;
                                    float rowHeight = 28f;

                                    while (curRow < el.TableData.Count)
                                    {
                                        if (y + rowHeight > bottom && y > top)
                                        {
                                            break;
                                        }

                                        var row = el.TableData[curRow];
                                        bool isHeader = (curRow == 0);

                                        if (isHeader)
                                        {
                                            g.FillRectangle(brushThBg, marginPx, y, printableWidth, rowHeight);
                                        }
                                        g.DrawRectangle(penBorder, marginPx, y, printableWidth, rowHeight);

                                        for (int c = 0; c < row.Count; c++)
                                        {
                                            float cellX = marginPx + c * colWidth;
                                            g.DrawLine(penBorder, cellX, y, cellX, y + rowHeight);
                                            Font cellFont = isHeader ? fontTableHead : fontTableCell;
                                            Brush cellBrush = isHeader ? brushHeading : brushBody;
                                            RectangleF cellRect = new RectangleF(cellX + 4, y + 4, colWidth - 8, rowHeight - 8);
                                            g.DrawString(row[c], cellFont, cellBrush, cellRect);
                                        }

                                        y += rowHeight;
                                        curRow++;
                                    }

                                    if (curRow >= el.TableData.Count)
                                    {
                                        curElem++;
                                        curRow = 0;
                                        y += 12;
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                            }

                            string footerText = string.Format("Страница {0} • DocEngine Enterprise", pageNum);
                            g.DrawString(footerText, fontFooter, brushMuted, marginPx, bottom + 8);

                            using (MemoryStream ms = new MemoryStream())
                            {
                                var encoder = GetJpegEncoder();
                                var encParams = new EncoderParameters(1);
                                encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                                bmp.Save(ms, encoder, encParams);
                                pageJpegs.Add(ms.ToArray());
                            }

                            pageNum++;
                        }
                    }
                }

                if (File.Exists(outputPdfPath)) File.Delete(outputPdfPath);

                using (FileStream fs = new FileStream(outputPdfPath, FileMode.Create))
                using (StreamWriter w = new StreamWriter(fs, Encoding.ASCII))
                {
                    w.WriteLine("%PDF-1.4");
                    w.WriteLine("%\xE2\xE3\xCF\xD3");

                    List<long> xrefOffsets = new List<long> { 0 };

                    int totalPages = pageJpegs.Count;
                    int pagesRootId = 1;
                    int catalogId = 2;
                    int nextId = 3;

                    List<int> pageObjIds = new List<int>();

                    for (int i = 0; i < totalPages; i++)
                    {
                        pageObjIds.Add(nextId++);
                        int imgId = nextId++;
                        int contentId = nextId++;
                    }

                    w.Flush();
                    xrefOffsets.Add(fs.Position);
                    w.WriteLine(string.Format("{0} 0 obj", pagesRootId));
                    w.WriteLine("<< /Type /Pages");
                    w.Write("   /Kids [");
                    foreach (int pid in pageObjIds) w.Write(string.Format("{0} 0 R ", pid));
                    w.WriteLine("]");
                    w.WriteLine(string.Format("   /Count {0}", totalPages));
                    w.WriteLine(">>");
                    w.WriteLine("endobj");

                    w.Flush();
                    xrefOffsets.Add(fs.Position);
                    w.WriteLine(string.Format("{0} 0 obj", catalogId));
                    w.WriteLine("<< /Type /Catalog");
                    w.WriteLine(string.Format("   /Pages {0} 0 R", pagesRootId));
                    w.WriteLine(">>");
                    w.WriteLine("endobj");

                    for (int i = 0; i < totalPages; i++)
                    {
                        int pageId = pageObjIds[i];
                        int imgId = pageId + 1;
                        int contentId = pageId + 2;

                        w.Flush();
                        xrefOffsets.Add(fs.Position);
                        w.WriteLine(string.Format("{0} 0 obj", pageId));
                        w.WriteLine("<< /Type /Page");
                        w.WriteLine(string.Format("   /Parent {0} 0 R", pagesRootId));
                        w.WriteLine("   /MediaBox [0 0 595.32 841.92]");
                        w.WriteLine(string.Format("   /Contents {0} 0 R", contentId));
                        w.WriteLine(string.Format("   /Resources << /XObject << /Im{0} {1} 0 R >> >>", i + 1, imgId));
                        w.WriteLine(">>");
                        w.WriteLine("endobj");

                        byte[] jpeg = pageJpegs[i];
                        w.Flush();
                        xrefOffsets.Add(fs.Position);
                        w.WriteLine(string.Format("{0} 0 obj", imgId));
                        w.WriteLine("<< /Type /XObject");
                        w.WriteLine("   /Subtype /Image");
                        w.WriteLine(string.Format("   /Width {0}", pageWidthPx));
                        w.WriteLine(string.Format("   /Height {0}", pageHeightPx));
                        w.WriteLine("   /ColorSpace /DeviceRGB");
                        w.WriteLine("   /BitsPerComponent 8");
                        w.WriteLine("   /Filter /DCTDecode");
                        w.WriteLine(string.Format("   /Length {0}", jpeg.Length));
                        w.WriteLine(">>");
                        w.WriteLine("stream");
                        w.Flush();
                        fs.Write(jpeg, 0, jpeg.Length);
                        w.WriteLine();
                        w.WriteLine("endstream");
                        w.WriteLine("endobj");

                        string content = string.Format("q 595.32 0 0 841.92 0 0 cm /Im{0} Do Q", i + 1);
                        byte[] cBytes = Encoding.ASCII.GetBytes(content);
                        w.Flush();
                        xrefOffsets.Add(fs.Position);
                        w.WriteLine(string.Format("{0} 0 obj", contentId));
                        w.WriteLine(string.Format("<< /Length {0} >>", cBytes.Length));
                        w.WriteLine("stream");
                        w.Flush();
                        fs.Write(cBytes, 0, cBytes.Length);
                        w.WriteLine();
                        w.WriteLine("endstream");
                        w.WriteLine("endobj");
                    }

                    w.Flush();
                    long startXref = fs.Position;
                    w.WriteLine("xref");
                    w.WriteLine(string.Format("0 {0}", xrefOffsets.Count));
                    w.WriteLine("0000000000 65535 f ");
                    for (int i = 1; i < xrefOffsets.Count; i++)
                    {
                        w.WriteLine(string.Format("{0:D10} 00000 n ", xrefOffsets[i]));
                    }
                    w.WriteLine("trailer");
                    w.WriteLine(string.Format("<< /Size {0}", xrefOffsets.Count));
                    w.WriteLine(string.Format("   /Root {0} 0 R", catalogId));
                    w.WriteLine(">>");
                    w.WriteLine("startxref");
                    w.WriteLine(startXref);
                    w.WriteLine("%%EOF");
                }

                log(string.Format("[Успех] Автономный PDF сформирован: {0} ({1} стр.)", outputPdfPath, pageJpegs.Count));
                progress(100, 100, "Экспорт в PDF успешно завершен!");
                return true;
            }
            catch (Exception ex)
            {
                log("Ошибка генерации PDF: " + ex.Message);
                return false;
            }
        }

        private static ImageCodecInfo GetJpegEncoder()
        {
            foreach (var c in ImageCodecInfo.GetImageEncoders())
            {
                if (c.FormatID == ImageFormat.Jpeg.Guid) return c;
            }
            return null;
        }

        private static bool TryExportViaWordCom(string docxPath, string outputPdfPath, Action<string> log)
        {
            Type wordType = Type.GetTypeFromProgID("Word.Application");
            if (wordType == null) return false;

            object wordApp = null;
            object docs = null;
            object doc = null;

            try
            {
                wordApp = Activator.CreateInstance(wordType);
                wordType.InvokeMember("Visible", BindingFlags.SetProperty, null, wordApp, new object[] { false });
                wordType.InvokeMember("DisplayAlerts", BindingFlags.SetProperty, null, wordApp, new object[] { 0 });

                docs = wordType.InvokeMember("Documents", BindingFlags.GetProperty, null, wordApp, null);
                doc = docs.GetType().InvokeMember("Open", BindingFlags.InvokeMethod, null, docs, new object[] { Path.GetFullPath(docxPath), true, true });

                // FileFormat 17 = wdFormatPDF
                doc.GetType().InvokeMember("SaveAs", BindingFlags.InvokeMethod, null, doc, new object[] { Path.GetFullPath(outputPdfPath), 17 });
                return true;
            }
            catch (Exception ex)
            {
                log(string.Format("Word COM предупреждение: {0}", ex.Message));
                return false;
            }
            finally
            {
                if (doc != null)
                {
                    try { doc.GetType().InvokeMember("Close", BindingFlags.InvokeMethod, null, doc, new object[] { false }); } catch { }
                }
                if (wordApp != null)
                {
                    try { wordType.InvokeMember("Quit", BindingFlags.InvokeMethod, null, wordApp, null); } catch { }
                }
            }
        }

        public static List<string> ExtractParagraphsFromDocx(string docxPath)
        {
            List<string> paras = new List<string>();
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(docxPath))
                {
                    ZipArchiveEntry entry = zip.GetEntry("word/document.xml");
                    if (entry != null)
                    {
                        using (Stream s = entry.Open())
                        using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                        {
                            string xml = r.ReadToEnd();
                            MatchCollection matches = Regex.Matches(xml, @"<w:p\b[^>]*>(.*?)</w:p>", RegexOptions.Singleline);
                            foreach (Match m in matches)
                            {
                                MatchCollection texts = Regex.Matches(m.Value, @"<w:t\b[^>]*>(.*?)</w:t>", RegexOptions.Singleline);
                                StringBuilder sb = new StringBuilder();
                                foreach (Match t in texts)
                                {
                                    sb.Append(t.Groups[1].Value);
                                }
                                string clean = sb.ToString().Trim();
                                if (!string.IsNullOrEmpty(clean)) paras.Add(clean);
                            }
                        }
                    }
                }
            }
            catch { }
            return paras;
        }

        private static string EscapePdf(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        }
    }

    public static class ContractAuditEngine
    {
        // --------------------------------------------------------------------
        // Сводный договор с цепочкой доп. соглашений (цветные ревизии)
        // --------------------------------------------------------------------
        public static bool ConsolidateContractRevisions(List<string> filePaths, string outputDocxPath, string outputPdfPath, Action<int, int, string> progress, Action<string> log)
        {
            if (filePaths == null || filePaths.Count < 2)
            {
                log("Ошибка: Для построения сводного договора выберите базовый договор и как минимум одно Доп. соглашение.");
                return false;
            }

            int totalDocs = filePaths.Count;
            string baseFile = filePaths[0];
            log(string.Format("Сквозной аудит: Базовый договор «{0}» + {1} Доп. соглашений...", Path.GetFileName(baseFile), totalDocs - 1));
            progress(10, 100, "Анализ текстов и цепочки доп. соглашений...");

            List<ContractEntry> entries = new List<ContractEntry>();
            for (int i = 0; i < filePaths.Count; i++)
            {
                string f = filePaths[i];
                if (!File.Exists(f)) continue;

                var entry = new ContractEntry
                {
                    Index = i,
                    FileName = Path.GetFileName(f),
                    Badge = (i == 0) ? "ИСХОДНЫЙ ДОГОВОР" : string.Format("ДОП. СОГЛАШЕНИЕ №{0}", i),
                    Paragraphs = new List<string>()
                };

                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".docx")
                {
                    entry.Paragraphs = WordToPdfConverter.ExtractParagraphsFromDocx(f);
                }
                else if (ext == ".pdf")
                {
                    entry.Paragraphs = PdfEngine.ExtractTextLinesFromPdf(f);
                }

                entries.Add(entry);
            }

            progress(50, 100, "Формирование сводного документа Word с разметкой ревизий...");

            try
            {
                CreateConsolidatedDocx(outputDocxPath, entries);
                log(string.Format("Сформирован сводный файл Word: {0}", outputDocxPath));

                progress(80, 100, "Экспорт итогового PDF-тома...");
                WordToPdfConverter.ConvertDocxToPdf(outputDocxPath, outputPdfPath, (c, t, m) => { }, s => { });

                log(string.Format("[Успех] Сводный договор со всеми Доп. соглашениями готов: {0}", outputPdfPath));
                progress(100, 100, string.Format("Сводный договор сформирован ({0} редакций)!", entries.Count));
                return true;
            }
            catch (Exception ex)
            {
                log(string.Format("Ошибка формирования сводного договора: {0}", ex.Message));
                return false;
            }
        }

        private static void CreateConsolidatedDocx(string filePath, List<ContractEntry> entries)
        {
            if (File.Exists(filePath)) File.Delete(filePath);

            string[] colorHex = new string[] { "D32F2F", "2563EB", "16A34A", "9333EA", "EA580C", "0D9488" };

            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                OpenXmlDocxEngine.CreateZipEntry(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\r\n" +
                    "  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\r\n" +
                    "  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\r\n" +
                    "  <Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>\r\n" +
                    "</Types>");

                OpenXmlDocxEngine.CreateZipEntry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\r\n" +
                    "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>\r\n" +
                    "</Relationships>");

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                sb.AppendLine("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">");
                sb.AppendLine("  <w:body>");

                // Заголовок
                sb.AppendLine("    <w:p><w:pPr><w:jc w:val=\"center\"/><w:spacing w:after=\"200\"/></w:pPr><w:r><w:rPr><w:b/><w:sz w:val=\"32\"/><w:color w:val=\"1E3A8A\"/></w:rPr><w:t>СВОДНЫЙ ДОГОВОР С ИСТОРИЕЙ РЕДАКЦИЙ</w:t></w:r></w:p>");
                sb.AppendLine(string.Format("    <w:p><w:r><w:rPr><w:i/><w:color w:val=\"64748B\"/></w:rPr><w:t>Документ сформирован автоматически на основе сквозного аудита Базового договора и {0} дополнительных соглашений.</w:t></w:r></w:p>", entries.Count - 1));

                // Реестр документов (Таблица)
                sb.AppendLine("    <w:tbl>");
                sb.AppendLine("      <w:tblPr><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:color=\"CBD5E1\"/><w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"CBD5E1\"/><w:left w:val=\"single\" w:sz=\"4\" w:color=\"CBD5E1\"/><w:right w:val=\"single\" w:sz=\"4\" w:color=\"CBD5E1\"/><w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"CBD5E1\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:color=\"CBD5E1\"/></w:tblBorders></w:tblPr>");

                // Заголовок таблицы
                sb.AppendLine("      <w:tr><w:tc><w:p><w:r><w:rPr><w:b/></w:rPr><w:t>№</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Редакция / Документ</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Файл</w:t></w:r></w:p></w:tc></w:tr>");

                foreach (var e in entries)
                {
                    sb.AppendLine(string.Format("      <w:tr><w:tc><w:p><w:r><w:t>{0}</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:rPr><w:b/></w:rPr><w:t>{1}</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>{2}</w:t></w:r></w:p></w:tc></w:tr>", e.Index, e.Badge, e.FileName));
                }
                sb.AppendLine("    </w:tbl>");
                sb.AppendLine("    <w:p><w:r><w:t></w:t></w:r></w:p>");

                // Раздел 1: Текст базового договора
                sb.AppendLine("    <w:p><w:pPr><w:spacing w:before=\"200\" w:after=\"100\"/></w:pPr><w:r><w:rPr><w:b/><w:sz w:val=\"26\"/><w:color w:val=\"0F172A\"/></w:rPr><w:t>1. АКТУАЛЬНЫЙ ТЕКСТ БАЗОВОГО ДОГОВОРА</w:t></w:r></w:p>");
                if (entries.Count > 0)
                {
                    foreach (string p in entries[0].Paragraphs)
                    {
                        sb.AppendLine(string.Format("    <w:p><w:pPr><w:ind w:firstLine=\"709\"/><w:spacing w:after=\"100\"/></w:pPr><w:r><w:t>{0}</w:t></w:r></w:p>", XmlEscape(p)));
                    }
                }

                // Раздел 2+: Ревизии и изменения из Доп. соглашений
                for (int i = 1; i < entries.Count; i++)
                {
                    var addendum = entries[i];
                    string color = colorHex[(i - 1) % colorHex.Length];

                    sb.AppendLine(string.Format("    <w:p><w:pPr><w:spacing w:before=\"240\" w:after=\"100\"/></w:pPr><w:r><w:rPr><w:b/><w:sz w:val=\"24\"/><w:color w:val=\"{0}\"/></w:rPr><w:t>ИЗМЕНЕНИЯ И УСЛОВИЯ: {1}</w:t></w:r></w:p>", color, XmlEscape(addendum.Badge)));
                    sb.AppendLine(string.Format("    <w:p><w:r><w:rPr><w:b/><w:color w:val=\"{0}\"/></w:rPr><w:t>⚡ [РЕВИЗИЯ {1} • {2}]</w:t></w:r></w:p>", color, i, XmlEscape(addendum.FileName)));

                    foreach (string p in addendum.Paragraphs)
                    {
                        sb.AppendLine(string.Format("    <w:p><w:pPr><w:ind w:firstLine=\"709\"/><w:spacing w:after=\"100\"/></w:pPr><w:r><w:rPr><w:b/><w:color w:val=\"{0}\"/></w:rPr><w:t>[Доп. согл. №{1}] </w:t></w:r><w:r><w:t>{2}</w:t></w:r></w:p>", color, i, XmlEscape(p)));
                    }
                }

                sb.AppendLine("    <w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1701\"/></w:sectPr>");
                sb.AppendLine("  </w:body>");
                sb.AppendLine("</w:document>");

                OpenXmlDocxEngine.CreateZipEntry(zip, "word/document.xml", sb.ToString());
            }
        }

        private static string XmlEscape(string unescaped)
        {
            if (string.IsNullOrEmpty(unescaped)) return "";
            return unescaped.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private class ContractEntry
        {
            public int Index { get; set; }
            public string FileName { get; set; }
            public string Badge { get; set; }
            public List<string> Paragraphs { get; set; }
        }
    }

    #endregion

    #region Графический Интерфейс (WinForms — Тёмная / Светлая тема)

    public class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.UpdateStyles();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN
                cp.Style |= 0x04000000; // WS_CLIPSIBLINGS
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0014) // WM_ERASEBKGND
            {
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }
    }

    public class BufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public BufferedFlowLayoutPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.UpdateStyles();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN
                cp.Style |= 0x04000000; // WS_CLIPSIBLINGS
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0014) // WM_ERASEBKGND
            {
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }
    }

    public class MainForm : Form
    {
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN (исключает перерисовку формы под дочерними панелями)
                cp.Style |= 0x04000000; // WS_CLIPSIBLINGS
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_ERASEBKGND = 0x0014;
            if (m.Msg == WM_ERASEBKGND)
            {
                m.Result = (IntPtr)1; // Подавляем сброс фона Windows для плавного перемещения без шлейфа
                return;
            }
            base.WndProc(ref m);
        }

        private AppConfig cfg;
        private string currentMode = "pdf_to_docx";
        private List<DocumentFileInfo> selectedFiles = new List<DocumentFileInfo>();
        private bool isBusy = false;
        private CancellationTokenSource cts;

        // UI Элементы Сайдбара
        private BufferedPanel pnlSidebar;
        private Label lblBrandTitle;
        private Label lblBrandTag;
        private Label lblBrandSub;
        private BufferedFlowLayoutPanel pnlNav;
        private Dictionary<string, Button> navButtons = new Dictionary<string, Button>();
        private BufferedPanel pnlLicenseFooter;
        private Label lblLicenseStatus;
        private Label lblLicenseClient;
        private Button btnThemeToggle;

        // UI Элементы Рабочей области
        private BufferedPanel pnlMain;
        private BufferedPanel pnlHeader;
        private Label lblModeTitle;
        private Label lblModeDesc;
        private Button btnAddFiles;
        private Button btnClearFiles;

        private BufferedPanel pnlContent;
        private BufferedPanel pnlDropZone;
        private BufferedFlowLayoutPanel pnlFileList;

        // Элементы Справки и Лицензии (встроенный экран)
        private BufferedPanel pnlGuide;
        private Label lblGuideHead;
        private Label lblGuideBody;
        private GroupBox grpLic;
        private Label lblH;
        private TextBox txtGuideHwid;
        private Button btnCopyHwid;
        private Label lblK;
        private TextBox txtGuideKey;
        private Button btnGuideActivate;
        private Label lblGuideStatus;

        // Панель настроек текущего режима
        private BufferedPanel pnlSettingsCard;
        private Label lblOptRange;
        private TextBox txtPageRange;
        private Label lblOptCompress;
        private ComboBox cmbCompress;
        private Label lblOptMergeName;
        private TextBox txtMergedName;
        private CheckBox chkAutoTables;
        private CheckBox chkRemoveBlanks;
        private CheckBox chkAutoOpen;
        private RadioButton radSplitRange;
        private RadioButton radSplitPages;
        private Label lblOptInfo;

        // Нижняя панель действий и журналов
        private BufferedPanel pnlAction;
        private Button btnAction;
        private Button btnCancel;
        private Button btnToggleLog;
        private ProgressBar progressBar;
        private Label lblProgressStatus;

        private BufferedPanel pnlLog;
        private RichTextBox txtLog;
        private bool logVisible = false;

        public MainForm()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.UpdateStyles();
            cfg = AppConfig.Load();
            InitializeComponent();
            ApplyTheme(cfg.appearance_mode == "Dark");
            SetMode("pdf_to_docx");
            CheckLicense();
        }

        private void InitializeComponent()
        {
            this.Text = "DocEngine Enterprise — Корпоративная обработка документов";
            this.Size = new Size(1180, 850);
            this.MinimumSize = new Size(980, 680);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.AllowDrop = true;
            this.DragEnter += Form_DragEnter;
            this.DragDrop += Form_DragDrop;

            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(iconPath)) this.Icon = new Icon(iconPath);
            }
            catch { }

            // ================================================================
            // 1. БОКОВОЙ САЙДБАР
            // ================================================================
            pnlSidebar = new BufferedPanel
            {
                Dock = DockStyle.Left,
                Width = 260,
                Padding = new Padding(10, 8, 10, 8)
            };

            // Брендовый баннер (Верх сайдбара)
            BufferedPanel brandBadge = new BufferedPanel
            {
                Dock = DockStyle.Top,
                Height = 76,
                Padding = new Padding(6)
            };
            lblBrandTitle = new Label
            {
                Text = "⚡ DOCENGINE",
                Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(8, 8)
            };
            lblBrandTag = new Label
            {
                Text = " ENTERPRISE ",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(8, 42)
            };
            lblBrandSub = new Label
            {
                Text = "Корпоративный движок",
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = true,
                Location = new Point(98, 42)
            };
            brandBadge.Controls.Add(lblBrandTitle);
            brandBadge.Controls.Add(lblBrandTag);
            brandBadge.Controls.Add(lblBrandSub);

            // Подвал сайдбара с карточкой лицензии и переключателем тем (Низ сайдбара)
            pnlLicenseFooter = new BufferedPanel
            {
                Dock = DockStyle.Bottom,
                Height = 116,
                Padding = new Padding(6)
            };
            lblLicenseStatus = new Label
            {
                Text = "Проверка лицензии...",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(6, 6)
            };
            lblLicenseClient = new Label
            {
                Text = "HWID: " + LicenseCore.GetMachineHwid(),
                Font = new Font("Consolas", 8.5f),
                AutoSize = true,
                Location = new Point(6, 32)
            };
            btnThemeToggle = new Button
            {
                Text = "Светлая тема",
                Location = new Point(6, 64),
                Size = new Size(228, 34),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnThemeToggle.FlatAppearance.BorderSize = 1;
            btnThemeToggle.Click += (s, e) =>
            {
                bool isDark = cfg.appearance_mode == "Dark";
                cfg.appearance_mode = isDark ? "Light" : "Dark";
                cfg.Save();
                ApplyTheme(!isDark);
            };
            pnlLicenseFooter.Controls.Add(lblLicenseStatus);
            pnlLicenseFooter.Controls.Add(lblLicenseClient);
            pnlLicenseFooter.Controls.Add(btnThemeToggle);

            // Навигационное меню (Заполняет центр сайдбара)
            pnlNav = new BufferedFlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0, 6, 0, 6)
            };
            pnlNav.Resize += (s, e) =>
            {
                int w = Math.Max(180, pnlNav.ClientSize.Width - 4);
                foreach (Button b in navButtons.Values)
                {
                    b.Width = w;
                }
            };

            var modes = new[]
            {
                new { Key = "pdf_to_docx", Title = "PDF в Word (DOCX)" },
                new { Key = "docx_to_pdf", Title = "Word (DOCX) в PDF" },
                new { Key = "compress_pdf", Title = "Сжатие PDF" },
                new { Key = "merge_pdf", Title = "Объединение PDF" },
                new { Key = "split_pdf", Title = "Разделение PDF" },
                new { Key = "tables_excel", Title = "Таблицы в Excel" },
                new { Key = "contract_audit", Title = "Сводный договор" },
                new { Key = "user_guide", Title = "Справка и Лицензия" }
            };

            foreach (var m in modes)
            {
                Button btn = new Button
                {
                    Text = m.Title,
                    Size = new Size(236, 42),
                    TextAlign = ContentAlignment.MiddleLeft,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                    Padding = new Padding(14, 0, 0, 0),
                    Margin = new Padding(0, 2, 0, 2),
                    Cursor = Cursors.Hand,
                    Tag = m.Key
                };
                btn.FlatAppearance.BorderSize = 0;
                string k = m.Key;
                btn.Click += (s, e) => SetMode(k);
                btn.Paint += (s, pe) =>
                {
                    if (currentMode == k)
                    {
                        pe.Graphics.FillRectangle(Brushes.White, 0, 4, 4, btn.Height - 8);
                    }
                };
                pnlNav.Controls.Add(btn);
                navButtons[m.Key] = btn;
            }

            pnlSidebar.Controls.Add(pnlNav);
            pnlSidebar.Controls.Add(pnlLicenseFooter);
            pnlSidebar.Controls.Add(brandBadge);
            brandBadge.SendToBack();
            pnlLicenseFooter.SendToBack();
            pnlNav.BringToFront();

            // ================================================================
            // 2. РАБОЧАЯ ОБЛАСТЬ (pnlMain)
            // ================================================================
            pnlMain = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 14, 20, 14),
                AllowDrop = true
            };
            pnlMain.DragEnter += Form_DragEnter;
            pnlMain.DragDrop += Form_DragDrop;

            // 2.1 Верхний заголовок (pnlHeader)
            pnlHeader = new BufferedPanel
            {
                Dock = DockStyle.Top,
                Height = 74,
                Padding = new Padding(0, 0, 0, 6)
            };

            lblModeTitle = new Label
            {
                Text = "PDF в Word (DOCX)",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(0, 4)
            };
            lblModeDesc = new Label
            {
                Text = "Быстрое преобразование PDF в редактируемый документ Word",
                Font = new Font("Segoe UI", 9f),
                AutoSize = true,
                Location = new Point(2, 38)
            };

            btnClearFiles = new Button
            {
                Text = "Очистить список",
                Size = new Size(130, 34),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnClearFiles.FlatAppearance.BorderSize = 1;
            btnClearFiles.Click += (s, e) =>
            {
                selectedFiles.Clear();
                RefreshFileListCards();
            };

            btnAddFiles = new Button
            {
                Text = "+ Выбрать файлы",
                Size = new Size(150, 34),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnAddFiles.FlatAppearance.BorderSize = 0;
            btnAddFiles.Click += (s, e) => BrowseFiles();

            pnlHeader.Controls.Add(lblModeTitle);
            pnlHeader.Controls.Add(lblModeDesc);
            pnlHeader.Controls.Add(btnAddFiles);
            pnlHeader.Controls.Add(btnClearFiles);
            pnlHeader.Resize += (s, e) =>
            {
                btnClearFiles.Left = pnlHeader.Width - btnClearFiles.Width;
                btnAddFiles.Left = btnClearFiles.Left - btnAddFiles.Width - 10;
                lblModeDesc.MaximumSize = new Size(Math.Max(200, btnAddFiles.Left - 20), 32);
            };

            // 2.2 Нижняя панель действий (pnlAction) — ВСЕГДА В САМОМ НИЗУ
            pnlAction = new BufferedPanel
            {
                Dock = DockStyle.Bottom,
                Height = 88,
                Padding = new Padding(0, 4, 0, 0)
            };

            btnAction = new Button
            {
                Text = "ЗАПУСТИТЬ КОНВЕРТАЦИЮ В WORD",
                Location = new Point(0, 4),
                Size = new Size(380, 42),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnAction.FlatAppearance.BorderSize = 0;
            btnAction.Click += BtnAction_Click;
            pnlAction.Controls.Add(btnAction);

            btnCancel = new Button
            {
                Text = "ОТМЕНА",
                Location = new Point(390, 4),
                Size = new Size(100, 42),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(239, 68, 68),
                ForeColor = Color.White,
                Visible = false,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) => { if (cts != null) cts.Cancel(); };
            pnlAction.Controls.Add(btnCancel);

            btnToggleLog = new Button
            {
                Text = "Журнал работы",
                Size = new Size(140, 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnToggleLog.Click += (s, e) =>
            {
                logVisible = !logVisible;
                pnlLog.Visible = logVisible;
                btnToggleLog.Text = logVisible ? "Скрыть журнал" : "Журнал работы";
            };
            pnlAction.Controls.Add(btnToggleLog);

            progressBar = new ProgressBar
            {
                Location = new Point(0, 52),
                Height = 6,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            pnlAction.Controls.Add(progressBar);

            lblProgressStatus = new Label
            {
                Text = "Готов к работе. Выберите или перетащите документы.",
                Location = new Point(2, 64),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f)
            };
            pnlAction.Controls.Add(lblProgressStatus);

            pnlAction.Resize += (s, e) =>
            {
                btnToggleLog.Left = pnlAction.Width - btnToggleLog.Width;
                progressBar.Width = pnlAction.Width;
            };

            // 2.3 Панель логов (pnlLog)
            pnlLog = new BufferedPanel
            {
                Dock = DockStyle.Bottom,
                Height = 120,
                Padding = new Padding(0, 0, 0, 6),
                Visible = false
            };
            txtLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 9f),
                BorderStyle = BorderStyle.FixedSingle
            };
            pnlLog.Controls.Add(txtLog);

            // 2.4 Панель параметров (pnlSettingsCard)
            pnlSettingsCard = new BufferedPanel
            {
                Dock = DockStyle.Bottom,
                Height = 84,
                Padding = new Padding(12, 6, 12, 6),
                BorderStyle = BorderStyle.FixedSingle
            };

            lblOptRange = new Label { Text = "Диапазон страниц:", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            txtPageRange = new TextBox { Text = "Все", Size = new Size(130, 24) };
            pnlSettingsCard.Controls.Add(lblOptRange);
            pnlSettingsCard.Controls.Add(txtPageRange);

            lblOptCompress = new Label { Text = "Профиль сжатия:", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            cmbCompress = new ComboBox { Size = new Size(260, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbCompress.Items.Add("Баланс (150 DPI, рекомендуется)");
            cmbCompress.Items.Add("Максимум (300 DPI, чертежи)");
            cmbCompress.Items.Add("Экстремальное (96 DPI, архив)");
            cmbCompress.SelectedIndex = 0;
            pnlSettingsCard.Controls.Add(lblOptCompress);
            pnlSettingsCard.Controls.Add(cmbCompress);

            lblOptMergeName = new Label { Text = "Имя тома:", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            txtMergedName = new TextBox { Text = "Сводный_документ.pdf", Size = new Size(260, 24) };
            pnlSettingsCard.Controls.Add(lblOptMergeName);
            pnlSettingsCard.Controls.Add(txtMergedName);

            chkAutoTables = new CheckBox { Text = "Сводная таблица в Excel (.xlsx)", AutoSize = true, Checked = true };
            chkRemoveBlanks = new CheckBox { Text = "Пропускать пустые страницы", AutoSize = true, Checked = false };
            chkAutoOpen = new CheckBox { Text = "Открыть результат после обработки", AutoSize = true, Checked = true };
            pnlSettingsCard.Controls.Add(chkAutoTables);
            pnlSettingsCard.Controls.Add(chkRemoveBlanks);
            pnlSettingsCard.Controls.Add(chkAutoOpen);

            radSplitRange = new RadioButton { Text = "По диапазону", AutoSize = true, Checked = true };
            radSplitPages = new RadioButton { Text = "Постранично в папку (каждый лист отдельно)", AutoSize = true };
            pnlSettingsCard.Controls.Add(radSplitRange);
            pnlSettingsCard.Controls.Add(radSplitPages);

            lblOptInfo = new Label
            {
                Size = new Size(760, 26),
                Font = new Font("Segoe UI", 9f)
            };
            pnlSettingsCard.Controls.Add(lblOptInfo);

            // 2.5 Центральная область контента (pnlContent)
            pnlContent = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 6, 0, 6),
                AllowDrop = true
            };
            pnlContent.DragEnter += Form_DragEnter;
            pnlContent.DragDrop += Form_DragDrop;

            // Зона Drag & Drop
            pnlDropZone = new BufferedPanel
            {
                Dock = DockStyle.Top,
                Height = 84,
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand,
                AllowDrop = true
            };
            pnlDropZone.Paint += PnlDropZone_Paint;
            pnlDropZone.Click += (s, e) => BrowseFiles();
            pnlDropZone.DragEnter += Form_DragEnter;
            pnlDropZone.DragDrop += Form_DragDrop;

            // Список выбранных файлов (Карточки)
            pnlFileList = new BufferedFlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(6),
                AllowDrop = true
            };
            pnlFileList.DragEnter += Form_DragEnter;
            pnlFileList.DragDrop += Form_DragDrop;
            pnlFileList.Resize += (s, e) =>
            {
                int cardWidth = Math.Max(260, pnlFileList.ClientSize.Width - 20);
                foreach (Control c in pnlFileList.Controls)
                {
                    if (c is Panel)
                    {
                        c.Width = cardWidth;
                    }
                }
            };

            // Экран справки и лицензирования (pnlGuide)
            pnlGuide = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Visible = false,
                Padding = new Padding(12)
            };

            lblGuideHead = new Label
            {
                Text = "РУКОВОДСТВО ПОЛЬЗОВАТЕЛЯ И ЛИЦЕНЗИРОВАНИЕ",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(12, 12)
            };
            pnlGuide.Controls.Add(lblGuideHead);

            lblGuideBody = new Label
            {
                Text = "• Программа работает автономно на чистом C# (.NET Framework 4.8) без Python и прав администратора.\n" +
                       "• Поддерживается прямая конвертация PDF в Word (.docx) с таблицами и кириллицей.\n" +
                       "• Экспорт Word в PDF выполняется через Microsoft Word COM (при наличии) или встроенным автономным движком.\n" +
                       "• Сжатие PDF уменьшает размер документов для отправки по почте с сохранением читаемости чертежей.\n" +
                       "• Сводный договор автоматически анализирует базовый договор и цепочку доп. соглашений с цветной разметкой.\n" +
                       "• Горячие клавиши: Перетаскивание файлов мышью из Проводника прямо в окно программы.",
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(12, 40),
                Size = new Size(820, 125)
            };
            pnlGuide.Controls.Add(lblGuideBody);

            grpLic = new GroupBox
            {
                Text = " Активация и управление лицензией ",
                Location = new Point(12, 175),
                Size = new Size(820, 250),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            lblH = new Label { Text = "Аппаратный ID компьютера (HWID):", Location = new Point(16, 26), AutoSize = true, Font = new Font("Segoe UI", 9f) };
            grpLic.Controls.Add(lblH);

            txtGuideHwid = new TextBox
            {
                Text = LicenseCore.GetMachineHwid(),
                ReadOnly = true,
                Location = new Point(16, 48),
                Size = new Size(300, 24),
                Font = new Font("Consolas", 10f, FontStyle.Bold)
            };
            grpLic.Controls.Add(txtGuideHwid);

            btnCopyHwid = new Button
            {
                Text = "Скопировать HWID",
                Location = new Point(326, 46),
                Size = new Size(160, 28),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCopyHwid.Click += (s, e) => { Clipboard.SetText(txtGuideHwid.Text); MessageBox.Show("HWID успешно скопирован в буфер обмена!", "Инфо", MessageBoxButtons.OK, MessageBoxIcon.Information); };
            grpLic.Controls.Add(btnCopyHwid);

            lblK = new Label { Text = "Введите лицензионный ключ активации (DOCENG-...):", Location = new Point(16, 84), AutoSize = true, Font = new Font("Segoe UI", 9f) };
            grpLic.Controls.Add(lblK);

            txtGuideKey = new TextBox
            {
                Location = new Point(16, 106),
                Size = new Size(780, 50),
                Multiline = true,
                Font = new Font("Consolas", 9f)
            };
            grpLic.Controls.Add(txtGuideKey);

            btnGuideActivate = new Button
            {
                Text = "АКТИВИРОВАТЬ ЛИЦЕНЗИЮ",
                Location = new Point(16, 166),
                Size = new Size(240, 36),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnGuideActivate.FlatAppearance.BorderSize = 0;
            btnGuideActivate.Click += (s, e) =>
            {
                string m;
                if (LicenseCore.SaveLicense(txtGuideKey.Text.Trim(), out m))
                {
                    MessageBox.Show(m, "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    CheckLicense();
                    lblGuideStatus.Text = "✔ " + m;
                    lblGuideStatus.ForeColor = Color.FromArgb(16, 185, 129);
                }
                else
                {
                    MessageBox.Show(m, "Ошибка активации", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    lblGuideStatus.Text = "❌ " + m;
                    lblGuideStatus.ForeColor = Color.FromArgb(239, 68, 68);
                }
            };
            grpLic.Controls.Add(btnGuideActivate);

            lblGuideStatus = new Label
            {
                Text = "",
                Location = new Point(16, 212),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            grpLic.Controls.Add(lblGuideStatus);
            pnlGuide.Controls.Add(grpLic);

            // Сборка pnlContent
            pnlContent.Controls.Add(pnlFileList);
            pnlContent.Controls.Add(pnlDropZone);
            pnlContent.Controls.Add(pnlGuide);
            pnlDropZone.SendToBack();
            pnlFileList.BringToFront();

            // Сборка pnlMain (ПРАВИЛЬНЫЙ ПОРЯДОК: Header вверху, Settings над Log и Action внизу)
            pnlMain.Controls.Add(pnlContent);
            pnlMain.Controls.Add(pnlSettingsCard);
            pnlMain.Controls.Add(pnlLog);
            pnlMain.Controls.Add(pnlAction);
            pnlMain.Controls.Add(pnlHeader);

            pnlHeader.SendToBack();
            pnlSettingsCard.SendToBack();
            pnlLog.SendToBack();
            pnlAction.SendToBack();
            pnlContent.BringToFront();

            // Сборка Form (Сайдбар слева, Main панель заполняет остальное)
            this.Controls.Add(pnlMain);
            this.Controls.Add(pnlSidebar);
            pnlSidebar.SendToBack();
            pnlMain.BringToFront();
        }

        private void PnlDropZone_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool isDark = cfg.appearance_mode == "Dark";
            Color borderColor = isDark ? Color.FromArgb(59, 130, 246) : Color.FromArgb(37, 99, 235);
            using (Pen pen = new Pen(borderColor, 2) { DashStyle = DashStyle.Dash })
            {
                g.DrawRectangle(pen, 3, 3, pnlDropZone.Width - 7, pnlDropZone.Height - 7);
            }

            string text1 = "Перетащите файлы сюда или нажмите для выбора";
            string text2 = "Поддерживаются документы: PDF, Word (.docx, .doc)";
            using (Font f1 = new Font("Segoe UI", 10.5f, FontStyle.Bold))
            using (Font f2 = new Font("Segoe UI", 9f))
            using (Brush b1 = new SolidBrush(isDark ? Color.FromArgb(248, 250, 252) : Color.FromArgb(15, 23, 42)))
            using (Brush b2 = new SolidBrush(isDark ? Color.FromArgb(148, 163, 184) : Color.FromArgb(100, 116, 139)))
            {
                SizeF s1 = g.MeasureString(text1, f1);
                SizeF s2 = g.MeasureString(text2, f2);
                g.DrawString(text1, f1, b1, (pnlDropZone.Width - s1.Width) / 2, 20);
                g.DrawString(text2, f2, b2, (pnlDropZone.Width - s2.Width) / 2, 46);
            }
        }

        private void ApplyTheme(bool isDark)
        {
            Color bgSidebar = isDark ? Color.FromArgb(15, 23, 42) : Color.FromArgb(248, 250, 252);
            Color bgMain = isDark ? Color.FromArgb(9, 13, 22) : Color.FromArgb(241, 245, 249);
            Color cardBg = isDark ? Color.FromArgb(19, 27, 46) : Color.FromArgb(255, 255, 255);
            Color inputBg = isDark ? Color.FromArgb(15, 23, 42) : Color.FromArgb(255, 255, 255);
            Color textMain = isDark ? Color.FromArgb(248, 250, 252) : Color.FromArgb(15, 23, 42);
            Color textMuted = isDark ? Color.FromArgb(148, 163, 184) : Color.FromArgb(100, 116, 139);
            Color primary = Color.FromArgb(37, 99, 235);
            Color border = isDark ? Color.FromArgb(30, 41, 59) : Color.FromArgb(226, 232, 240);

            this.BackColor = bgMain;
            pnlSidebar.BackColor = bgSidebar;
            lblBrandTitle.ForeColor = primary;
            lblBrandTag.ForeColor = Color.White;
            lblBrandTag.BackColor = primary;
            lblBrandSub.ForeColor = textMuted;

            lblModeTitle.ForeColor = textMain;
            lblModeDesc.ForeColor = textMuted;
            pnlDropZone.BackColor = cardBg;
            pnlFileList.BackColor = cardBg;
            pnlSettingsCard.BackColor = cardBg;
            pnlGuide.BackColor = cardBg;

            txtLog.BackColor = inputBg;
            txtLog.ForeColor = isDark ? Color.FromArgb(52, 211, 153) : Color.FromArgb(15, 23, 42);

            btnAction.BackColor = primary;
            btnAction.ForeColor = Color.White;
            btnAddFiles.BackColor = primary;
            btnAddFiles.ForeColor = Color.White;
            btnClearFiles.BackColor = isDark ? Color.FromArgb(35, 20, 25) : Color.FromArgb(254, 242, 242);
            btnClearFiles.ForeColor = Color.FromArgb(239, 68, 68);

            btnThemeToggle.BackColor = isDark ? Color.FromArgb(30, 41, 59) : Color.FromArgb(226, 232, 240);
            btnThemeToggle.ForeColor = textMain;
            btnThemeToggle.Text = isDark ? "Светлая тема" : "Тёмная тема";

            btnToggleLog.BackColor = isDark ? Color.FromArgb(30, 41, 59) : Color.FromArgb(226, 232, 240);
            btnToggleLog.ForeColor = textMain;

            lblProgressStatus.ForeColor = textMuted;

            // Настройки карточки параметров (чёткий контраст текста)
            lblOptRange.ForeColor = textMain;
            txtPageRange.BackColor = inputBg;
            txtPageRange.ForeColor = textMain;

            lblOptCompress.ForeColor = textMain;
            cmbCompress.BackColor = inputBg;
            cmbCompress.ForeColor = textMain;

            lblOptMergeName.ForeColor = textMain;
            txtMergedName.BackColor = inputBg;
            txtMergedName.ForeColor = textMain;

            chkAutoTables.ForeColor = textMain;
            chkRemoveBlanks.ForeColor = textMain;
            chkAutoOpen.ForeColor = textMain;

            radSplitRange.ForeColor = textMain;
            radSplitPages.ForeColor = textMain;
            lblOptInfo.ForeColor = Color.FromArgb(96, 165, 250);

            // Экран справки и лицензии
            lblGuideHead.ForeColor = primary;
            lblGuideBody.ForeColor = textMain;
            grpLic.ForeColor = primary;
            lblH.ForeColor = textMain;
            txtGuideHwid.BackColor = inputBg;
            txtGuideHwid.ForeColor = textMain;
            btnCopyHwid.BackColor = isDark ? Color.FromArgb(30, 41, 59) : Color.FromArgb(226, 232, 240);
            btnCopyHwid.ForeColor = textMain;
            lblK.ForeColor = textMain;
            txtGuideKey.BackColor = inputBg;
            txtGuideKey.ForeColor = textMain;

            pnlDropZone.Invalidate();
            UpdateNavButtons();
            RefreshFileListCards();
        }

        private void ArrangeSettings(string modeKey)
        {
            // 1. Скрываем все контролы
            lblOptRange.Visible = false;
            txtPageRange.Visible = false;
            lblOptCompress.Visible = false;
            cmbCompress.Visible = false;
            lblOptMergeName.Visible = false;
            txtMergedName.Visible = false;
            chkAutoTables.Visible = false;
            chkRemoveBlanks.Visible = false;
            chkAutoOpen.Visible = false;
            radSplitRange.Visible = false;
            radSplitPages.Visible = false;
            lblOptInfo.Visible = false;

            // 2. Расставляем контролы с математически выверенными непересекающимися координатами
            switch (modeKey)
            {
                case "pdf_to_docx":
                    lblOptRange.Location = new Point(12, 14); lblOptRange.Visible = true;
                    txtPageRange.Location = new Point(142, 11); txtPageRange.Size = new Size(130, 24); txtPageRange.Visible = true;
                    chkAutoTables.Location = new Point(300, 12); chkAutoTables.Visible = true;

                    chkRemoveBlanks.Location = new Point(12, 48); chkRemoveBlanks.Visible = true;
                    chkAutoOpen.Location = new Point(300, 48); chkAutoOpen.Visible = true;
                    break;

                case "docx_to_pdf":
                    lblOptInfo.Text = "Используется Microsoft Word COM (при наличии) или встроенный автономный конвертер.";
                    lblOptInfo.Location = new Point(12, 14); lblOptInfo.Size = new Size(Math.Max(300, pnlSettingsCard.Width - 24), 24); lblOptInfo.Visible = true;
                    chkAutoOpen.Location = new Point(12, 48); chkAutoOpen.Visible = true;
                    break;

                case "compress_pdf":
                    lblOptCompress.Location = new Point(12, 14); lblOptCompress.Visible = true;
                    cmbCompress.Location = new Point(142, 11); cmbCompress.Size = new Size(260, 24); cmbCompress.Visible = true;
                    chkAutoOpen.Location = new Point(12, 48); chkAutoOpen.Visible = true;
                    break;

                case "merge_pdf":
                    lblOptMergeName.Location = new Point(12, 14); lblOptMergeName.Visible = true;
                    txtMergedName.Location = new Point(96, 11); txtMergedName.Size = new Size(280, 24); txtMergedName.Visible = true;

                    chkRemoveBlanks.Location = new Point(12, 48); chkRemoveBlanks.Visible = true;
                    chkAutoOpen.Location = new Point(300, 48); chkAutoOpen.Visible = true;
                    break;

                case "split_pdf":
                    radSplitRange.Location = new Point(12, 13); radSplitRange.Visible = true;
                    radSplitPages.Location = new Point(150, 13); radSplitPages.Visible = true;

                    lblOptRange.Location = new Point(12, 49); lblOptRange.Visible = true;
                    txtPageRange.Location = new Point(142, 46); txtPageRange.Size = new Size(130, 24); txtPageRange.Visible = true;
                    chkAutoOpen.Location = new Point(300, 48); chkAutoOpen.Visible = true;
                    break;

                case "tables_excel":
                    lblOptRange.Location = new Point(12, 14); lblOptRange.Visible = true;
                    txtPageRange.Location = new Point(142, 11); txtPageRange.Size = new Size(130, 24); txtPageRange.Visible = true;
                    lblOptInfo.Text = "Фирменная шапка LUKOIL (#D32F2F) с автоопределением чисел.";
                    lblOptInfo.Location = new Point(300, 14); lblOptInfo.Size = new Size(Math.Max(200, pnlSettingsCard.Width - 320), 24); lblOptInfo.Visible = true;

                    chkAutoOpen.Location = new Point(12, 48); chkAutoOpen.Visible = true;
                    break;

                case "contract_audit":
                    lblOptInfo.Text = "1-й файл — Базовый договор, остальные — Доп. соглашения. Сводный аудит с цветными правками.";
                    lblOptInfo.Location = new Point(12, 14); lblOptInfo.Size = new Size(Math.Max(300, pnlSettingsCard.Width - 24), 24); lblOptInfo.Visible = true;
                    chkAutoOpen.Location = new Point(12, 48); chkAutoOpen.Visible = true;
                    break;
            }
        }

        private void SetMode(string modeKey)
        {
            currentMode = modeKey;
            UpdateNavButtons();

            if (modeKey == "user_guide")
            {
                pnlDropZone.Visible = false;
                pnlFileList.Visible = false;
                pnlSettingsCard.Visible = false;
                pnlAction.Visible = false;
                pnlLog.Visible = false;
                btnAddFiles.Visible = false;
                btnClearFiles.Visible = false;
                pnlGuide.Visible = true;
                pnlGuide.BringToFront();

                lblModeTitle.Text = "Справка и Лицензирование";
                lblModeDesc.Text = "Руководство по возможностям программы, аппаратный HWID и активация";
                lblModeDesc.Top = lblModeTitle.Bottom + 4;
                return;
            }

            // Показываем стандартные панели контента
            pnlGuide.Visible = false;
            pnlDropZone.Visible = true;
            pnlFileList.Visible = true;
            pnlSettingsCard.Visible = true;
            pnlAction.Visible = true;
            btnAddFiles.Visible = true;
            btnClearFiles.Visible = true;

            switch (modeKey)
            {
                case "pdf_to_docx":
                    lblModeTitle.Text = "PDF в Word (DOCX)";
                    lblModeDesc.Text = "Конвертация PDF с сохранением форматирования, таблиц и структуры текста";
                    btnAction.Text = "ЗАПУСТИТЬ КОНВЕРТАЦИЮ В WORD";
                    break;

                case "docx_to_pdf":
                    lblModeTitle.Text = "Word (DOCX) в PDF";
                    lblModeDesc.Text = "Экспорт Word в чистый PDF через Word COM Engine + встроенный рендерер";
                    btnAction.Text = "ЭКСПОРТИРОВАТЬ В PDF";
                    break;

                case "compress_pdf":
                    lblModeTitle.Text = "Сжатие и Оптимизация PDF";
                    lblModeDesc.Text = "Уменьшение веса файлов для отправки по почте с сохранением читаемости";
                    btnAction.Text = "НАЧАТЬ СЖАТИЕ PDF";
                    break;

                case "merge_pdf":
                    lblModeTitle.Text = "Объединение PDF томов";
                    lblModeDesc.Text = "Сшивка нескольких PDF файлов в единый том с автоматической нумерацией";
                    btnAction.Text = "ОБЪЕДИНИТЬ ВСЕ ФАЙЛЫ В ЕДИНЫЙ ТОМ";
                    break;

                case "split_pdf":
                    lblModeTitle.Text = "Разделение PDF";
                    lblModeDesc.Text = "Извлечение выбранных страниц по диапазону или нарезка каждого листа в отдельный файл";
                    btnAction.Text = "РАЗДЕЛИТЬ PDF ДОКУМЕНТ";
                    break;

                case "tables_excel":
                    lblModeTitle.Text = "Извлечение таблиц в Excel (XLSX)";
                    lblModeDesc.Text = "Сбор табличных данных в красивый файл Excel с фирменной шапкой и формулами";
                    btnAction.Text = "ИЗВЛЕЧЬ ТАБЛИЦЫ В EXCEL";
                    break;

                case "contract_audit":
                    lblModeTitle.Text = "Сводный договор (Цепочка Доп. соглашений)";
                    lblModeDesc.Text = "Сквозной аудит договора со всеми допниками, паспорт ревизий и цветная маркировка правок";
                    btnAction.Text = "СФОРМИРОВАТЬ СВОДНЫЙ ДОГОВОР";
                    break;
            }

            lblModeDesc.Top = lblModeTitle.Bottom + 4;
            ArrangeSettings(modeKey);
        }

        private void UpdateNavButtons()
        {
            bool isDark = cfg.appearance_mode == "Dark";
            Color primary = Color.FromArgb(37, 99, 235);
            Color primaryHover = Color.FromArgb(29, 78, 216);
            Color bgSidebar = isDark ? Color.FromArgb(15, 23, 42) : Color.FromArgb(248, 250, 252);
            Color hoverBg = isDark ? Color.FromArgb(30, 41, 59) : Color.FromArgb(226, 232, 240);
            Color textMain = isDark ? Color.FromArgb(248, 250, 252) : Color.FromArgb(15, 23, 42);

            foreach (var kv in navButtons)
            {
                bool active = (kv.Key == currentMode);
                kv.Value.BackColor = active ? primary : bgSidebar;
                kv.Value.ForeColor = active ? Color.White : textMain;
                kv.Value.FlatAppearance.MouseOverBackColor = active ? primaryHover : hoverBg;
                kv.Value.FlatAppearance.MouseDownBackColor = primaryHover;
                kv.Value.Invalidate();
            }
        }

        private void CheckLicense()
        {
            string msg;
            Dictionary<string, object> payload;
            if (LicenseCore.LoadSavedLicense(out msg, out payload))
            {
                string client = payload.ContainsKey("cl") ? payload["cl"].ToString() : "ЛУКОЙЛ-Волгоградэнерго";
                string exp = payload.ContainsKey("ex_str") ? payload["ex_str"].ToString() : "Бессрочно";
                lblLicenseStatus.Text = "Лицензия: АКТИВНА (" + exp + ")";
                lblLicenseStatus.ForeColor = Color.FromArgb(16, 185, 129);
                lblLicenseClient.Text = client;

                lblGuideStatus.Text = "Лицензия активна для: «" + client + "» (" + exp + ")";
                lblGuideStatus.ForeColor = Color.FromArgb(16, 185, 129);
            }
            else
            {
                lblLicenseStatus.Text = "Требуется активация";
                lblLicenseStatus.ForeColor = Color.FromArgb(245, 158, 11);
                lblLicenseClient.Text = "HWID: " + LicenseCore.GetMachineHwid();

                lblGuideStatus.Text = "Лицензия не активирована. Введите ключ для активации.";
                lblGuideStatus.ForeColor = Color.FromArgb(245, 158, 11);
            }
        }

        private void BrowseFiles()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Multiselect = true;
                ofd.Filter = "Документы (*.pdf;*.docx;*.doc)|*.pdf;*.docx;*.doc|PDF файлы (*.pdf)|*.pdf|Word файлы (*.docx;*.doc)|*.docx;*.doc|Все файлы (*.*)|*.*";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    AddFiles(ofd.FileNames);
                }
            }
        }

        private void Form_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void Form_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                AddFiles(files);
            }
        }

        private void AddFiles(string[] paths)
        {
            foreach (string p in paths)
            {
                if (File.Exists(p) && !selectedFiles.Any(f => f.FilePath.Equals(p, StringComparison.OrdinalIgnoreCase)))
                {
                    selectedFiles.Add(DocumentFileInfo.FromPath(p));
                }
            }
            RefreshFileListCards();
        }

        private void RefreshFileListCards()
        {
            pnlFileList.Controls.Clear();
            bool isDark = cfg.appearance_mode == "Dark";

            if (selectedFiles.Count == 0)
            {
                Label lblEmpty = new Label
                {
                    Text = "Список файлов пуст.\r\nНажмите «+ Выбрать файлы» или перетащите документы в окно программы.",
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Fill,
                    Font = new Font("Segoe UI", 10f),
                    ForeColor = isDark ? Color.FromArgb(100, 116, 139) : Color.FromArgb(148, 163, 184),
                    Height = 120
                };
                pnlFileList.Controls.Add(lblEmpty);
                lblProgressStatus.Text = "Выбрано документов: 0";
                return;
            }

            for (int i = 0; i < selectedFiles.Count; i++)
            {
                var f = selectedFiles[i];
                BufferedPanel card = new BufferedPanel
                {
                    Size = new Size(Math.Max(300, pnlFileList.ClientSize.Width - 25), 48),
                    BackColor = isDark ? Color.FromArgb(30, 41, 59) : Color.FromArgb(241, 245, 249),
                    Margin = new Padding(3),
                    Padding = new Padding(6)
                };

                Label lblBadge = new Label
                {
                    Text = f.Extension.ToUpperInvariant().TrimStart('.'),
                    Location = new Point(8, 13),
                    Size = new Size(48, 22),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                    BackColor = f.Extension == ".pdf" ? Color.FromArgb(220, 38, 38) : Color.FromArgb(37, 99, 235),
                    ForeColor = Color.White
                };
                card.Controls.Add(lblBadge);

                Label lblName = new Label
                {
                    Text = f.FileName,
                    Location = new Point(64, 6),
                    Size = new Size(Math.Max(100, card.Width - 220), 20),
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                    ForeColor = isDark ? Color.White : Color.Black
                };
                card.Controls.Add(lblName);

                Label lblMeta = new Label
                {
                    Text = string.Format("{0} стр. • {1} МБ", f.PageCount, f.FileSizeMb),
                    Location = new Point(64, 26),
                    Size = new Size(200, 16),
                    Font = new Font("Segoe UI", 8.5f),
                    ForeColor = isDark ? Color.FromArgb(148, 163, 184) : Color.FromArgb(100, 116, 139)
                };
                card.Controls.Add(lblMeta);

                Button btnDel = new Button
                {
                    Text = "✕",
                    Location = new Point(card.Width - 40, 11),
                    Size = new Size(26, 26),
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Color.FromArgb(239, 68, 68),
                    Cursor = Cursors.Hand
                };
                btnDel.FlatAppearance.BorderSize = 0;
                var item = f;
                btnDel.Click += (s, e) =>
                {
                    selectedFiles.Remove(item);
                    RefreshFileListCards();
                };
                card.Controls.Add(btnDel);

                pnlFileList.Controls.Add(card);
            }

            lblProgressStatus.Text = string.Format("Выбрано документов: {0}", selectedFiles.Count);
        }

        private void Log(string msg)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string>(Log), msg);
                return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            txtLog.AppendText(string.Format("[{0}] {1}\r\n", timestamp, msg));
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();
        }

        private void UpdateProgress(int current, int total, string status)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<int, int, string>(UpdateProgress), current, total, status);
                return;
            }

            if (total > 0)
            {
                int val = Math.Max(0, Math.Min(100, (int)((double)current / total * 100.0)));
                progressBar.Value = val;
            }
            lblProgressStatus.Text = status;
        }

        private void BtnAction_Click(object sender, EventArgs e)
        {
            if (isBusy) return;
            if (selectedFiles.Count == 0)
            {
                MessageBox.Show("Пожалуйста, выберите хотя бы один документ для обработки.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Захватываем все значения элементов UI в локальные переменные в UI-потоке
            string pageRange = txtPageRange.Text;
            bool autoTables = chkAutoTables.Checked;
            int compressIdx = cmbCompress.SelectedIndex;
            string mName = (txtMergedName.Text ?? "").Trim();
            bool splitPages = radSplitPages.Checked;
            bool autoOpen = chkAutoOpen.Checked;
            string mode = currentMode;
            var files = selectedFiles.ToList();

            isBusy = true;
            btnAction.Enabled = false;
            btnCancel.Visible = true;
            cts = new CancellationTokenSource();

            ThreadPool.QueueUserWorkItem(state =>
            {
                string firstFile = files[0].FilePath;
                string dir = Path.GetDirectoryName(firstFile);
                string baseName = Path.GetFileNameWithoutExtension(firstFile);
                string lastOutput = null;

                try
                {
                    switch (mode)
                    {
                        case "pdf_to_docx":
                            foreach (var f in files)
                            {
                                string fDir = Path.GetDirectoryName(f.FilePath);
                                string outDocx = Path.Combine(fDir, Path.GetFileNameWithoutExtension(f.FilePath) + ".docx");
                                OpenXmlDocxEngine.ConvertPdfToDocx(f.FilePath, outDocx, pageRange, UpdateProgress, Log);
                                lastOutput = outDocx;

                                if (autoTables)
                                {
                                    string outXlsx = Path.Combine(fDir, Path.GetFileNameWithoutExtension(f.FilePath) + "_таблицы.xlsx");
                                    OpenXmlExcelEngine.ExtractTablesToExcel(f.FilePath, outXlsx, pageRange, UpdateProgress, Log);
                                }
                            }
                            break;

                        case "docx_to_pdf":
                            foreach (var f in files)
                            {
                                string fDir = Path.GetDirectoryName(f.FilePath);
                                string outPdf = Path.Combine(fDir, Path.GetFileNameWithoutExtension(f.FilePath) + ".pdf");
                                WordToPdfConverter.ConvertDocxToPdf(f.FilePath, outPdf, UpdateProgress, Log);
                                lastOutput = outPdf;
                            }
                            break;

                        case "compress_pdf":
                            string profile = "medium";
                            if (compressIdx == 1) profile = "max";
                            if (compressIdx == 2) profile = "extreme";

                            foreach (var f in files)
                            {
                                string fDir = Path.GetDirectoryName(f.FilePath);
                                string outPdf = Path.Combine(fDir, Path.GetFileNameWithoutExtension(f.FilePath) + "_сжат.pdf");
                                PdfEngine.CompressPdf(f.FilePath, outPdf, profile, UpdateProgress, Log);
                                lastOutput = outPdf;
                            }
                            break;

                        case "merge_pdf":
                            if (string.IsNullOrEmpty(mName)) mName = "Сводный_документ.pdf";
                            if (!mName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) mName += ".pdf";
                            string mergedOut = Path.Combine(dir, mName);
                            PdfEngine.MergePdfs(files.Select(x => x.FilePath).ToList(), mergedOut, UpdateProgress, Log);
                            lastOutput = mergedOut;
                            break;

                        case "split_pdf":
                            if (splitPages)
                            {
                                string splitDir = Path.Combine(dir, baseName + "_страницы");
                                PdfEngine.SplitPdfToFolder(firstFile, splitDir, UpdateProgress, Log);
                                lastOutput = splitDir;
                            }
                            else
                            {
                                string splitOut = Path.Combine(dir, baseName + "_выборка.pdf");
                                PdfEngine.SplitPdf(firstFile, splitOut, pageRange, UpdateProgress, Log);
                                lastOutput = splitOut;
                            }
                            break;

                        case "tables_excel":
                            foreach (var f in files)
                            {
                                string fDir = Path.GetDirectoryName(f.FilePath);
                                string outXlsx = Path.Combine(fDir, Path.GetFileNameWithoutExtension(f.FilePath) + "_таблицы.xlsx");
                                OpenXmlExcelEngine.ExtractTablesToExcel(f.FilePath, outXlsx, pageRange, UpdateProgress, Log);
                                lastOutput = outXlsx;
                            }
                            break;

                        case "contract_audit":
                            string auditDocx = Path.Combine(dir, "Сводный_Договор_" + baseName + ".docx");
                            string auditPdf = Path.Combine(dir, "Сводный_Договор_" + baseName + ".pdf");
                            ContractAuditEngine.ConsolidateContractRevisions(files.Select(x => x.FilePath).ToList(), auditDocx, auditPdf, UpdateProgress, Log);
                            lastOutput = auditPdf;
                            break;
                    }

                    if (autoOpen && lastOutput != null && (File.Exists(lastOutput) || Directory.Exists(lastOutput)))
                    {
                        try { Process.Start(lastOutput); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    Log("Ошибка: " + ex.Message);
                }
                finally
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        isBusy = false;
                        btnAction.Enabled = true;
                        btnCancel.Visible = false;
                    }));
                }
            });
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    #endregion
}
