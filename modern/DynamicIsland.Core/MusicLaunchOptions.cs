using System;
using System.IO;


namespace Island
{
    public sealed class MusicLaunchOptions
    {
        public string Service = "yandex", Browser = "yandex";
        public bool OnPlay = true, Restore = true, AutoPlay;
        public MusicLaunchOptions Copy() { return (MusicLaunchOptions)MemberwiseClone(); }
        public bool Parse(string line)
        {
            if (line.StartsWith("MusicService=")) { Service = line.Substring(13) == "vk" ? "vk" : "yandex"; return true; }
            if (line.StartsWith("MusicBrowser=")) { string b = line.Substring(13); Browser = b == "chrome" || b == "edge" || b == "default" ? b : "yandex"; return true; }
            if (line.StartsWith("MusicLaunchOnPlay=")) { OnPlay = line.Substring(18) != "False"; return true; }
            if (line.StartsWith("MusicRestore=")) { Restore = line.Substring(13) != "False"; return true; }
            if (line.StartsWith("MusicAutoPlay=")) { AutoPlay = line.Substring(14) == "True"; return true; }
            return false;
        }
        public string[] Lines() { return new[] { "MusicService=" + Service, "MusicBrowser=" + Browser, "MusicLaunchOnPlay=" + OnPlay, "MusicRestore=" + Restore, "MusicAutoPlay=" + AutoPlay }; }
        public static string Home(string service) { return service == "vk" ? "https://vk.com/audio" : "https://music.yandex.ru/"; }
        public static string ServiceFor(string site) { return site == "music.yandex.ru" || site == "music.yandex.com" ? "yandex" : MusicCatalog.AllowedSite(site) ? "vk" : ""; }
        public static string SafeUrl(string value, string service)
        {
            if (service != "yandex" && service != "vk") return "";
            Uri url; if (!Uri.TryCreate(value, UriKind.Absolute, out url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length > 0 || ServiceFor(url.DnsSafeHost) != service) return "";
            string path = url.AbsolutePath;
            if (path.Length > 1800 || path.Contains("\\") || path.Contains("\r") || path.Contains("\n")) return "";
            if (service == "vk" && path != "/audio" && path != "/music" && !path.StartsWith("/music/") && url.Host != "music.vk.com") return Home(service);
            return url.GetLeftPart(UriPartial.Path); // Never persist query tokens/fragments.
        }
    }
    public sealed class LastMusicSession
    {
        public string Service { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public bool Available { get { return (Service == "yandex" || Service == "vk") && !string.IsNullOrEmpty(Title); } }
        public static string PathForSettings { get { return Path.ChangeExtension(Settings.FilePath, ".music-session.json"); } }
        public static LastMusicSession Load()
        {
            try
            {
                var path = PathForSettings;
                if (File.Exists(path) && new FileInfo(path).Length < 16384)
                {
                    var value = new JsonCodec().Deserialize<LastMusicSession>(File.ReadAllText(path));
                    if (value != null && value.Available) { value.Url = MusicLaunchOptions.SafeUrl(value.Url, value.Service); value.Title = Limit(value.Title); value.Artist = Limit(value.Artist); return value; }
                }
            }
            catch (System.Text.Json.JsonException) { } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { } catch (InvalidOperationException) { }
            return new LastMusicSession();
        }
        public bool Update(MusicSnapshot track)
        {
            string service = MusicLaunchOptions.ServiceFor(track.Source); if (!track.Available || service.Length == 0) return false;
            string url = MusicLaunchOptions.SafeUrl(track.PageUrl, service); if (url.Length == 0) url = MusicLaunchOptions.Home(service);
            if (Service == service && Url == url && Title == track.Title && Artist == track.Artist) return false;
            Service = service; Url = url; Title = Limit(track.Title); Artist = Limit(track.Artist); return true;
        }
        private static string Limit(string value) { return (value ?? "").Length > 640 ? value.Substring(0, 640) : value ?? ""; }
        public void Save()
        {
            string path = PathForSettings, temporary = path + ".tmp";
            File.WriteAllText(temporary, new JsonCodec().Serialize(this));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }
}
