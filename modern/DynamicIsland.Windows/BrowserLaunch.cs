using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Island
{
    public static class BrowserLaunch
    {
        public static string Resolve(string choice)
        {
            if (choice == "default")
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice"))
                {
                    string id = key == null ? "" : Convert.ToString(key.GetValue("ProgId"));
                    choice = id.StartsWith("Chrome", StringComparison.OrdinalIgnoreCase) ? "chrome" : id.StartsWith("MSEdge", StringComparison.OrdinalIgnoreCase) ? "edge" : id.IndexOf("Yandex", StringComparison.OrdinalIgnoreCase) >= 0 ? "yandex" : "";
                }
                if (choice.Length == 0) throw new IOException("Выбери Яндекс Браузер, Chrome или Edge в настройках: текущий браузер Windows не поддерживает этот bridge.");
            }
            string exe = choice == "chrome" ? "chrome.exe" : choice == "edge" ? "msedge.exe" : "browser.exe";
            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            using (var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
            { string found = key == null ? "" : Convert.ToString(key.GetValue("" )).Trim('"'); if (File.Exists(found)) return found; }
            string[] candidates = choice == "yandex" ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Yandex\YandexBrowser\Application\browser.exe") }
                : choice == "chrome" ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe") }
                : new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe") };
            foreach (string candidate in candidates) if (File.Exists(candidate)) return candidate;
            throw new FileNotFoundException("Выбранный браузер не установлен. Выбери другой в настройках запуска музыки.");
        }
        public static bool IsRunning(string choice)
        {
            string path = Resolve(choice); var found = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)); bool result = false;
            foreach (var p in found) using (p) try { if (string.Equals(p.MainModule.FileName, path, StringComparison.OrdinalIgnoreCase)) result = true; } catch (System.ComponentModel.Win32Exception) { result = true; } catch (InvalidOperationException) { }
            return result;
        }
        public static Task Start(string browser, string url)
        {
            return Task.Run(() =>
            {
                string service = MusicLaunchOptions.ServiceFor(new Uri(url).Host);
                if (MusicLaunchOptions.SafeUrl(url, service).Length == 0) throw new ArgumentException("Недопустимый адрес музыкального сервиса");
                // Existing user's browser profile; no autoplay policy switches,
                // incognito, alternate user-data-dir or forced window activation.
                // The extension owns tab creation after startup, avoiding a CLI URL /
                // tabs.create race and reusing session-restored music tabs.
                using (var process = Process.Start(new ProcessStartInfo(Resolve(browser), "") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized })) { }
            });
        }
    }
}
