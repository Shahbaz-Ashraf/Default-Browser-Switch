using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace FastBrowserSwitch
{
    public class BrowserInfo
    {
        public string KeyName { get; set; }
        public string DisplayName { get; set; }
        public string HttpProgId { get; set; }
        public string HttpsProgId { get; set; }
        public string HtmlProgId { get; set; }
        public string HtmProgId { get; set; }
        public string ExePath { get; set; }

        public override string ToString()
        {
            return DisplayName ?? KeyName;
        }
    }

    class Program
    {
        [DllImport("Shell32.dll")]
        private static extern int SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const int SHCNF_FLUSH = 0x1000;

        static void Main(string[] args)
        {
            try
            {
                if (args.Length > 0)
                {
                    string command = args[0].ToLowerInvariant();
                    if (command == "switch" || command == "set" || command == "-s")
                    {
                        if (args.Length < 2)
                        {
                            Console.WriteLine("Usage: FastBrowserSwitch.exe switch <BrowserName or ProgId>");
                            return;
                        }
                        string target = args[1];
                        bool success = DoSwitch(target, false);
                        Environment.ExitCode = success ? 0 : 1;
                        return;
                    }
                    else if (command == "list")
                    {
                        ListBrowsers();
                        return;
                    }
                    else if (command == "current")
                    {
                        var curr = GetCurrentDefaultBrowser();
                        Console.WriteLine(curr != null ? curr.DisplayName : "Unknown");
                        return;
                    }
                    else if (command == "unlock" || command == "status" || command == "check")
                    {
                        CheckAndExplainUcpd();
                        return;
                    }
                    else if (command == "settings" || command == "gui")
                    {
                        Console.WriteLine("[*] Opening Windows Default Apps settings dialog...");
                        try { Process.Start("ms-settings:defaultapps"); } catch { }
                        return;
                    }
                    else if (command == "generate" || command == "generate-shortcuts")
                    {
                        GenerateClickableShortcuts();
                        return;
                    }
                }

                // If run without args, check if shortcuts folder exists; if not, generate them once
                GenerateClickableShortcuts(silent: true);

                // Default: Run Interactive BIOS-style selector
                RunBiosTui();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[Error] " + ex.Message);
                Console.ResetColor();
            }
        }

        public static List<BrowserInfo> GetInstalledBrowsers()
        {
            var list = new List<BrowserInfo>();
            RegistryKey[] rootHives = { Registry.LocalMachine, Registry.CurrentUser };

            foreach (var hive in rootHives)
            {
                using (var baseKey = hive.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet"))
                {
                    if (baseKey == null) continue;
                    foreach (var subKeyName in baseKey.GetSubKeyNames())
                    {
                        if (subKeyName.Equals("IEXPLORE.EXE", StringComparison.OrdinalIgnoreCase))
                            continue; // Skip obsolete Internet Explorer

                        if (list.Exists(b => b.KeyName.Equals(subKeyName, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        using (var bKey = baseKey.OpenSubKey(subKeyName))
                        {
                            if (bKey == null) continue;
                            string name = bKey.GetValue("") as string;
                            if (string.IsNullOrEmpty(name)) name = subKeyName;

                            string exePath = "";
                            using (var cmdKey = bKey.OpenSubKey(@"shell\open\command"))
                            {
                                if (cmdKey != null)
                                {
                                    string raw = cmdKey.GetValue("") as string;
                                    if (!string.IsNullOrEmpty(raw))
                                    {
                                        exePath = raw.Trim('"').Split('"')[0];
                                    }
                                }
                            }

                            string httpProgId = "";
                            string httpsProgId = "";
                            using (var urlKey = bKey.OpenSubKey(@"Capabilities\URLAssociations"))
                            {
                                if (urlKey != null)
                                {
                                    httpProgId = urlKey.GetValue("http") as string;
                                    httpsProgId = urlKey.GetValue("https") as string;
                                }
                            }

                            string htmlProgId = "";
                            string htmProgId = "";
                            using (var fileKey = bKey.OpenSubKey(@"Capabilities\FileAssociations"))
                            {
                                if (fileKey != null)
                                {
                                    htmlProgId = fileKey.GetValue(".html") as string;
                                    htmProgId = fileKey.GetValue(".htm") as string;
                                }
                            }

                            if (string.IsNullOrEmpty(httpProgId))
                                continue;

                            if (string.IsNullOrEmpty(httpsProgId)) httpsProgId = httpProgId;
                            if (string.IsNullOrEmpty(htmlProgId)) htmlProgId = httpProgId;
                            if (string.IsNullOrEmpty(htmProgId)) htmProgId = htmlProgId;

                            list.Add(new BrowserInfo
                            {
                                KeyName = subKeyName,
                                DisplayName = name,
                                HttpProgId = httpProgId,
                                HttpsProgId = httpsProgId,
                                HtmlProgId = htmlProgId,
                                HtmProgId = htmProgId,
                                ExePath = exePath
                            });
                        }
                    }
                }
            }

            return list;
        }

        public static BrowserInfo GetCurrentDefaultBrowser()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice"))
                {
                    if (key != null)
                    {
                        string progId = key.GetValue("ProgId") as string;
                        if (!string.IsNullOrEmpty(progId))
                        {
                            var browsers = GetInstalledBrowsers();
                            var found = browsers.Find(b => b.HttpProgId.Equals(progId, StringComparison.OrdinalIgnoreCase));
                            if (found != null) return found;

                            return new BrowserInfo
                            {
                                KeyName = progId,
                                DisplayName = progId,
                                HttpProgId = progId,
                                HttpsProgId = progId
                            };
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static void ListBrowsers()
        {
            var browsers = GetInstalledBrowsers();
            var curr = GetCurrentDefaultBrowser();
            Console.WriteLine("Installed Browsers:");
            for (int i = 0; i < browsers.Count; i++)
            {
                bool isDef = (curr != null && curr.HttpProgId.Equals(browsers[i].HttpProgId, StringComparison.OrdinalIgnoreCase));
                Console.WriteLine(string.Format("  [{0}] {1} ({2}) {3}", i + 1, browsers[i].DisplayName, browsers[i].HttpProgId, isDef ? "<- CURRENT DEFAULT" : ""));
            }
        }

        public static bool DoSwitch(string target, bool interactive = false)
        {
            var browsers = GetInstalledBrowsers();
            BrowserInfo match = null;

            int idx;
            if (int.TryParse(target, out idx) && idx >= 1 && idx <= browsers.Count)
            {
                match = browsers[idx - 1];
            }
            else
            {
                match = browsers.Find(b => b.DisplayName.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                                           b.KeyName.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                                           b.HttpProgId.Equals(target, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    match = browsers.Find(b => b.DisplayName.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                               b.KeyName.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0);
                }
            }

            if (match == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[!] Browser not found: " + target);
                Console.ResetColor();
                return false;
            }

            var curr = GetCurrentDefaultBrowser();
            if (curr != null && curr.HttpProgId.Equals(match.HttpProgId, StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[i] " + match.DisplayName + " is ALREADY your default browser.");
                Console.WriteLine("    No changes needed.");
                Console.ResetColor();
                return true;
            }

            Console.WriteLine("\n[*] Switching default browser to: " + match.DisplayName + "...");
            var sw = Stopwatch.StartNew();

            string userSid = GetCurrentUserSid();
            string userExp = GetUserExperienceString();
            string hexTime = GetHexDateTime();

            try
            {
                // Write http
                SetAssociation("http", match.HttpProgId, false, userSid, userExp, hexTime);
                // Write https
                SetAssociation("https", match.HttpsProgId, false, userSid, userExp, hexTime);
                // Write .html
                if (!string.IsNullOrEmpty(match.HtmlProgId))
                    SetAssociation(".html", match.HtmlProgId, true, userSid, userExp, hexTime);
                // Write .htm
                if (!string.IsNullOrEmpty(match.HtmProgId))
                    SetAssociation(".htm", match.HtmProgId, true, userSid, userExp, hexTime);

                // Notify Windows Shell to refresh associations immediately
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
                sw.Stop();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(string.Format("[+] Successfully set {0} as default browser in {1}ms!", match.DisplayName, sw.ElapsedMilliseconds));
                Console.ResetColor();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                sw.Stop();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[!] Notice: Windows User Choice Protection Driver (UCPD) is active in RAM.");
                
                int startVal = -1;
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\UCPD"))
                    {
                        if (key != null)
                        {
                            object obj = key.GetValue("Start");
                            if (obj is int) startVal = (int)obj;
                        }
                    }
                }
                catch { }

                if (startVal == 4)
                {
                    Console.WriteLine("    [OK] Service is already DISABLED in registry (Unlock script succeeded).");
                    Console.WriteLine("    [!] A single PC REBOOT is required to unload UCPD from active memory.");
                    Console.WriteLine("    -> Once you restart, all 1-click batch files and the BIOS selector");
                    Console.WriteLine("       will switch default browsers in under 15ms permanently!");
                }
                else
                {
                    Console.WriteLine("    UCPD blocks programmatic registry writes to browser defaults.");
                    Console.WriteLine("    -> Fix: Right-click 'Unlock-FastSwitch.cmd' -> Run as Administrator, then restart PC.");
                }

                Console.WriteLine("    -> Opening Windows Default Apps settings dialog as temporary fallback...");
                Console.ResetColor();

                try
                {
                    Process.Start("ms-settings:defaultapps");
                }
                catch { }

                return false;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[!] Error setting default browser: " + ex.Message);
                Console.ResetColor();
                return false;
            }
        }

        private static void SetAssociation(string assoc, string progId, bool isExt, string sid, string userExp, string hexTime)
        {
            string baseInfo = (assoc + sid + progId + hexTime + userExp).ToLowerInvariant();
            string hash = ComputeHash(baseInfo);

            try
            {
                using (var tKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ApplicationAssociationToasts"))
                {
                    if (tKey != null)
                    {
                        tKey.SetValue(progId + "_" + assoc, 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }

            string regPath = isExt
                ? @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + assoc + @"\UserChoice"
                : @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\" + assoc + @"\UserChoice";

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(regPath);
            }
            catch { }

            using (var key = Registry.CurrentUser.CreateSubKey(regPath))
            {
                if (key == null)
                    throw new UnauthorizedAccessException("Cannot create or open UserChoice key: " + regPath);

                key.SetValue("Hash", hash, RegistryValueKind.String);
                key.SetValue("ProgId", progId, RegistryValueKind.String);
            }
        }

        public static void GenerateClickableShortcuts(bool silent = false)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string targetDir = Path.Combine(appDir, "Click to Switch Browsers");
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var browsers = GetInstalledBrowsers();
            foreach (var b in browsers)
            {
                string cleanName = b.DisplayName;
                foreach (char c in Path.GetInvalidFileNameChars())
                {
                    cleanName = cleanName.Replace(c, '_');
                }

                string scriptPath = Path.Combine(targetDir, "Switch to " + cleanName + ".cmd");
                string content = string.Format(
@"@echo off
title Switch Default Browser to {0}
cls
""{1}\FastBrowserSwitch.exe"" switch ""{0}""
if %errorlevel% neq 0 (
    echo.
    echo Press any key to continue...
    pause >nul
) else (
    timeout /t 2 >nul
)
", b.DisplayName, appDir.TrimEnd('\\'));

                File.WriteAllText(scriptPath, content);
            }

            if (!silent)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[+] Generated clickable switch scripts in: " + targetDir);
                Console.ResetColor();
            }
        }

        #region UserChoice Hash Math
        private static int ShiftRight(long val, int count)
        {
            if ((val & 0x80000000L) != 0)
                return (int)((val >> count) ^ 0xFFFF0000L);
            return (int)(val >> count);
        }

        private static int GetLong(byte[] bytes, int offset = 0)
        {
            return BitConverter.ToInt32(bytes, offset);
        }

        private static int ToInt32(long val)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(val), 0);
        }

        public static string ComputeHash(string baseInfo)
        {
            byte[] bytesBaseInfo = Encoding.Unicode.GetBytes(baseInfo);
            byte[] withNull = new byte[bytesBaseInfo.Length + 2];
            Buffer.BlockCopy(bytesBaseInfo, 0, withNull, 0, bytesBaseInfo.Length);

            byte[] bytesMD5;
            using (MD5 md5 = MD5.Create())
            {
                bytesMD5 = md5.ComputeHash(withNull);
            }

            int lengthBase = (baseInfo.Length * 2) + 2;
            int length = (((lengthBase & 4) <= 1 ? 1 : 0) + ShiftRight(lengthBase, 2)) - 1;

            if (length <= 1) return "";

            int cache = 0;
            int outHash1 = 0;
            int outHash2 = 0;
            int pData = 0;
            long md51 = (GetLong(bytesMD5, 0) | 1) + 0x69FB0000L;
            long md52 = (GetLong(bytesMD5, 4) | 1) + 0x13DB0000L;
            int index = ShiftRight(length - 2, 1);
            int counter = index + 1;

            int[] r1 = new int[2];
            int[] r2 = new int[2];
            int[] r4 = new int[2];
            int[] r5 = new int[2];
            int[] r6 = new int[2];

            while (counter > 0)
            {
                int r0 = ToInt32(GetLong(withNull, pData) + (long)outHash1);
                r1[0] = ToInt32(GetLong(withNull, pData + 4));
                pData += 8;
                r2[0] = ToInt32((r0 * md51) - (0x10FA9605L * ShiftRight(r0, 16)));
                r2[1] = ToInt32((0x79F8A395L * (long)r2[0]) + (0x689B6B9FL * ShiftRight(r2[0], 16)));
                int r3 = ToInt32((0xEA970001L * r2[1]) - (0x3C101569L * ShiftRight(r2[1], 16)));
                r4[0] = ToInt32(r3 + r1[0]);
                r5[0] = ToInt32(cache + r3);
                r6[0] = ToInt32((r4[0] * md52) - (0x3CE8EC25L * ShiftRight(r4[0], 16)));
                r6[1] = ToInt32((0x59C3AF2DL * r6[0]) - (0x2232E0F1L * ShiftRight(r6[0], 16)));
                outHash1 = ToInt32((0x1EC90001L * r6[1]) + (0x35BD1EC9L * ShiftRight(r6[1], 16)));
                outHash2 = ToInt32((long)r5[0] + (long)outHash1);
                cache = outHash2;
                counter--;
            }

            byte[] outHash = new byte[16];
            Buffer.BlockCopy(BitConverter.GetBytes(outHash1), 0, outHash, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(outHash2), 0, outHash, 4, 4);

            cache = 0;
            outHash1 = 0;
            pData = 0;
            md51 = GetLong(bytesMD5, 0) | 1;
            md52 = GetLong(bytesMD5, 4) | 1;
            index = ShiftRight(length - 2, 1);
            counter = index + 1;

            while (counter > 0)
            {
                int r0 = ToInt32(GetLong(withNull, pData) + (long)outHash1);
                pData += 8;
                r1[0] = ToInt32(r0 * md51);
                r1[1] = ToInt32((0xB1110000L * r1[0]) - (0x30674EEFL * ShiftRight(r1[0], 16)));
                r2[0] = ToInt32((0x5B9F0000L * r1[1]) - (0x78F7A461L * ShiftRight(r1[1], 16)));
                r2[1] = ToInt32((0x12CEB96DL * ShiftRight(r2[0], 16)) - (0x46930000L * r2[0]));
                int r3 = ToInt32((0x1D830000L * r2[1]) + (0x257E1D83L * ShiftRight(r2[1], 16)));
                r4[0] = ToInt32(md52 * ((long)r3 + GetLong(withNull, pData - 4)));
                r4[1] = ToInt32((0x16F50000L * r4[0]) - (0x5D8BE90BL * ShiftRight(r4[0], 16)));
                r5[0] = ToInt32((0x96FF0000L * r4[1]) - (0x2C7C6901L * ShiftRight(r4[1], 16)));
                r5[1] = ToInt32((0x2B890000L * r5[0]) + (0x7C932B89L * ShiftRight(r5[0], 16)));
                outHash1 = ToInt32((0x9F690000L * r5[1]) - (0x405B6097L * ShiftRight(r5[1], 16)));
                outHash2 = ToInt32((long)outHash1 + cache + r3);
                cache = outHash2;
                counter--;
            }

            Buffer.BlockCopy(BitConverter.GetBytes(outHash1), 0, outHash, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(outHash2), 0, outHash, 12, 4);

            int hashVal1 = GetLong(outHash, 8) ^ GetLong(outHash, 0);
            int hashVal2 = GetLong(outHash, 12) ^ GetLong(outHash, 4);

            byte[] outHashBase = new byte[8];
            Buffer.BlockCopy(BitConverter.GetBytes(hashVal1), 0, outHashBase, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(hashVal2), 0, outHashBase, 4, 4);

            return Convert.ToBase64String(outHashBase);
        }

        private static string GetCurrentUserSid()
        {
            var user = WindowsIdentity.GetCurrent();
            return user.User.Value.ToLowerInvariant();
        }

        private static string GetHexDateTime()
        {
            DateTime now = DateTime.Now;
            DateTime truncated = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            long fileTime = truncated.ToFileTime();
            long hi = (fileTime >> 32) & 0xFFFFFFFFL;
            long low = fileTime & 0xFFFFFFFFL;
            return (hi.ToString("X8") + low.ToString("X8")).ToLowerInvariant();
        }

        private static string GetUserExperienceString()
        {
            string fallback = "User Choice set via Windows User Experience {D18B6DD5-6124-4341-9318-804003BAFA0B}";
            try
            {
                string shell32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "Shell32.dll");
                if (!File.Exists(shell32))
                    shell32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Shell32.dll");

                using (var fs = new FileStream(shell32, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var br = new BinaryReader(fs))
                {
                    byte[] data = br.ReadBytes(5 * 1024 * 1024);
                    string s = Encoding.Unicode.GetString(data);
                    string target = "User Choice set via Windows User Experience";
                    int idx = s.IndexOf(target);
                    if (idx >= 0)
                    {
                        int end = s.IndexOf("}", idx);
                        if (end > idx)
                        {
                            return s.Substring(idx, end - idx + 1);
                        }
                    }
                }
            }
            catch { }
            return fallback;
        }
        #endregion

        #region Interactive BIOS-style TUI
        public static void RunBiosTui()
        {
            var browsers = GetInstalledBrowsers();
            if (browsers.Count == 0)
            {
                Console.WriteLine("No installed browsers found.");
                return;
            }

            int selectedIndex = 0;
            var curr = GetCurrentDefaultBrowser();
            if (curr != null)
            {
                int found = browsers.FindIndex(b => b.HttpProgId.Equals(curr.HttpProgId, StringComparison.OrdinalIgnoreCase));
                if (found >= 0) selectedIndex = found;
            }

            Console.CursorVisible = false;
            ConsoleColor origBg = Console.BackgroundColor;
            ConsoleColor origFg = Console.ForegroundColor;

            string statusMsg = "Ready. Use Arrow Keys to navigate, Enter to select, Esc to exit.";
            ConsoleColor statusColor = ConsoleColor.White;

            try
            {
                bool running = true;
                while (running)
                {
                    curr = GetCurrentDefaultBrowser();
                    DrawBiosScreen(browsers, selectedIndex, curr, statusMsg, statusColor);

                    ConsoleKeyInfo key = Console.ReadKey(true);
                    switch (key.Key)
                    {
                        case ConsoleKey.UpArrow:
                            selectedIndex = (selectedIndex - 1 + browsers.Count) % browsers.Count;
                            statusMsg = "Select a browser and press [ENTER] to set as default.";
                            statusColor = ConsoleColor.Cyan;
                            break;

                        case ConsoleKey.DownArrow:
                            selectedIndex = (selectedIndex + 1) % browsers.Count;
                            statusMsg = "Select a browser and press [ENTER] to set as default.";
                            statusColor = ConsoleColor.Cyan;
                            break;

                        case ConsoleKey.Enter:
                            var targetBrowser = browsers[selectedIndex];
                            if (curr != null && curr.HttpProgId.Equals(targetBrowser.HttpProgId, StringComparison.OrdinalIgnoreCase))
                            {
                                statusMsg = targetBrowser.DisplayName + " is ALREADY your default browser.";
                                statusColor = ConsoleColor.Yellow;
                            }
                            else
                            {
                                statusMsg = "Applying switch to " + targetBrowser.DisplayName + "...";
                                DrawBiosScreen(browsers, selectedIndex, curr, statusMsg, ConsoleColor.Yellow);

                                bool ok = DoSwitch(targetBrowser.KeyName, true);
                                if (ok)
                                {
                                    try { Console.Beep(950, 70); } catch { }
                                    statusMsg = "[OK] Default browser changed to: " + targetBrowser.DisplayName;
                                    statusColor = ConsoleColor.Green;
                                }
                                else
                                {
                                    try { Console.Beep(400, 150); } catch { }
                                    statusMsg = "[!] UCPD active in RAM. Reboot required or use Settings!";
                                    statusColor = ConsoleColor.Yellow;
                                }
                            }
                            break;

                        case ConsoleKey.Escape:
                        case ConsoleKey.Q:
                            running = false;
                            break;
                    }
                }
            }
            finally
            {
                Console.BackgroundColor = origBg;
                Console.ForegroundColor = origFg;
                Console.Clear();
                Console.CursorVisible = true;
            }
        }

        private static void DrawBiosScreen(List<BrowserInfo> browsers, int selectedIndex, BrowserInfo curr, string statusMsg, ConsoleColor statusColor)
        {
            int width = 78;
            Console.SetCursorPosition(0, 0);

            // BIOS Header
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.ForegroundColor = ConsoleColor.White;

            PrintCentered("==============================================================================", width);
            PrintCentered("                      BIOS DEFAULT BROWSER SETUP UTILITY                      ", width);
            PrintCentered("                      (C) 2026 ANTIGRAVITY ENGINE v2.0                        ", width);
            PrintCentered("==============================================================================", width);

            Console.WriteLine(PadLine("", width));

            // Section Header
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(PadLine("  INSTALLED BROWSER DEVICES DETECTED IN SYSTEM BUS:", width));
            Console.WriteLine(PadLine("  -------------------------------------------------", width));

            // Items
            for (int i = 0; i < browsers.Count; i++)
            {
                bool isSelected = (i == selectedIndex);
                bool isDefault = (curr != null && curr.HttpProgId.Equals(browsers[i].HttpProgId, StringComparison.OrdinalIgnoreCase));

                string defaultTag = isDefault ? " [* CURRENT DEFAULT *] " : "                       ";
                string line = string.Format("   [{0}]  {1,-28} {2}", i + 1, browsers[i].DisplayName, defaultTag);

                if (isSelected)
                {
                    Console.BackgroundColor = ConsoleColor.White;
                    Console.ForegroundColor = ConsoleColor.DarkBlue;
                    Console.WriteLine(PadLine(line, width));
                    Console.BackgroundColor = ConsoleColor.DarkBlue;
                }
                else
                {
                    Console.ForegroundColor = isDefault ? ConsoleColor.Green : ConsoleColor.Gray;
                    Console.WriteLine(PadLine(line, width));
                }
            }

            // Fill empty rows for stable BIOS box layout
            int remaining = Math.Max(0, 8 - browsers.Count);
            for (int i = 0; i < remaining; i++)
            {
                Console.WriteLine(PadLine("", width));
            }

            // Status Box
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(PadLine("  +------------------------------------------------------------------------+", width));
            Console.ForegroundColor = statusColor;
            Console.WriteLine(PadLine("  | MSG: " + PadRightExact(statusMsg, 66) + " |", width));
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(PadLine("  +------------------------------------------------------------------------+", width));

            Console.WriteLine(PadLine("", width));

            // Footer / Legend
            Console.BackgroundColor = ConsoleColor.DarkCyan;
            Console.ForegroundColor = ConsoleColor.Black;
            PrintCentered("[Up/Down] Select Browser   [Enter] Switch Default   [Esc/Q] Exit", width);

            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.ForegroundColor = ConsoleColor.White;
        }

        private static void PrintCentered(string text, int width)
        {
            int pad = Math.Max(0, (width - text.Length) / 2);
            string line = new string(' ', pad) + text;
            if (line.Length < width) line += new string(' ', width - line.Length);
            Console.WriteLine(line.Substring(0, Math.Min(width, line.Length)));
        }

        private static string PadLine(string text, int width)
        {
            if (text.Length >= width) return text.Substring(0, width);
            return text + new string(' ', width - text.Length);
        }

        private static string PadRightExact(string text, int length)
        {
            if (text == null) text = "";
            if (text.Length > length) return text.Substring(0, length);
            return text + new string(' ', length - text.Length);
        }
        #endregion

        private static void CheckAndExplainUcpd()
        {
            Console.WriteLine("[*] Checking User Choice Protection Driver (UCPD) status...\n");
            try
            {
                int startVal = -1;
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\UCPD"))
                {
                    if (key != null)
                    {
                        object obj = key.GetValue("Start");
                        if (obj is int) startVal = (int)obj;
                    }
                }

                var psi = new ProcessStartInfo("sc.exe", "query ucpd")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                bool isRunning = output.Contains("RUNNING");

                if (startVal == 4 && isRunning)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[!] STATUS: Reboot Pending to Finalize Unlock!");
                    Console.WriteLine("    1. UCPD startup is DISABLED in registry (Success).");
                    Console.WriteLine("    2. The driver is still in RAM because Windows cannot unload kernel filters live.");
                    Console.WriteLine("    -> Simply RESTART your PC once to unload it.");
                    Console.WriteLine("    -> After restart, 10ms switching will be permanently active!");
                    Console.ResetColor();
                }
                else if (isRunning)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[!] STATUS: UCPD is ACTIVE.");
                    Console.WriteLine("    It blocks programmatic registry writes to browser defaults.");
                    Console.WriteLine("    -> Run 'Unlock-FastSwitch.cmd' as Administrator to disable it.");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[+] STATUS: UCPD is UNLOADED / NOT RUNNING!");
                    Console.WriteLine("    Instant <15ms default browser switching is ENABLED and ACTIVE!");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not query UCPD: " + ex.Message);
            }
        }
    }
}
