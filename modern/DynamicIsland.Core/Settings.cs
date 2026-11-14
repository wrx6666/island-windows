using System;
using System.IO;
using System.Globalization;



namespace Island
{
    public sealed class Settings
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "Island.Desktop";
        public static string FilePath = Path.Combine(AppContext.BaseDirectory, "island.settings");
        private readonly System.Collections.Generic.List<string> unknown = new();
        private static readonly object saveGate = new();
        public bool CustomPosition;
        public string ScreenDevice = "";
        public double AnchorX = 0.5, AnchorY;
        public bool Animations = true;
        public bool MusicMotion = true;
        public bool CheckUpdates = true;
        public VisualOptions Visual = new VisualOptions();
        public MusicLaunchOptions Launch = new MusicLaunchOptions();
        public int MusicHotkey = 0, MuteHotkey = 0;
        public int OverlayHotkey = 0;
        public bool FloatingIcon = true, OverlayHover = true, OverlayAutoHide = true, OverlayAllowPin = true, OverlayPinned;
        public string OverlayMonitor = "";
        public double OverlayX = 1, OverlayY;
        public int OverlayDelay = 400;
        public string AudioProcess = "DeltaForceClient-Win64-Shipping";
        public static Settings Load()
        {
            var settings = new Settings();
            try
            {
                if (File.Exists(FilePath)) foreach (string line in File.ReadAllLines(FilePath))
                {
                    if (settings.Visual.Parse(line)) continue;
                    if (settings.Launch.Parse(line)) continue;
                    string name=line.Split('=')[0];
                    if (!Known.Contains(name)) settings.unknown.Add(line);
                    if (line == "CheckUpdates=False") settings.CheckUpdates = false;
                    if (line == "Animations=False") settings.Animations = false;
                    if (line == "MusicMotion=False") settings.MusicMotion = false;
                    int value; double fraction;
                    if (line == "FloatingIcon=False") settings.FloatingIcon = false;
                    if (line == "OverlayHover=False") settings.OverlayHover = false;
                    if (line == "OverlayAutoHide=False") settings.OverlayAutoHide = false;
                    if (line == "OverlayAllowPin=False") settings.OverlayAllowPin = false;
                    if (line == "OverlayPinned=True") settings.OverlayPinned = true;
                    if (line.StartsWith("OverlayMonitor=")) settings.OverlayMonitor = line.Substring(15);
                    if (line.StartsWith("OverlayHotkey=") && int.TryParse(line.Substring(14), out value) && KeyBinding.Valid(value)) settings.OverlayHotkey = (int)value;
                    if (line.StartsWith("OverlayDelay=") && int.TryParse(line.Substring(13), out value)) settings.OverlayDelay = Math.Max(300, Math.Min(500, value));
                    if (line.StartsWith("OverlayX=") && double.TryParse(line.Substring(9), NumberStyles.Float, CultureInfo.InvariantCulture, out fraction) && !double.IsNaN(fraction) && !double.IsInfinity(fraction)) settings.OverlayX = Math.Max(0, Math.Min(1, fraction));
                    if (line.StartsWith("OverlayY=") && double.TryParse(line.Substring(9), NumberStyles.Float, CultureInfo.InvariantCulture, out fraction) && !double.IsNaN(fraction) && !double.IsInfinity(fraction)) settings.OverlayY = Math.Max(0, Math.Min(1, fraction));
                    if (line == "CustomPosition=True") settings.CustomPosition = true;
                    if (line.StartsWith("ScreenDevice=")) settings.ScreenDevice = line.Substring(13);
                    if (line.StartsWith("AnchorX=") && double.TryParse(line.Substring(8), NumberStyles.Float, CultureInfo.InvariantCulture, out fraction) && !double.IsNaN(fraction) && !double.IsInfinity(fraction)) settings.AnchorX = Math.Max(0, Math.Min(1, fraction));
                    if (line.StartsWith("AnchorY=") && double.TryParse(line.Substring(8), NumberStyles.Float, CultureInfo.InvariantCulture, out fraction) && !double.IsNaN(fraction) && !double.IsInfinity(fraction)) settings.AnchorY = Math.Max(0, Math.Min(1, fraction));
                    if (line.StartsWith("MusicHotkey=") && int.TryParse(line.Substring(12), out value) && KeyBinding.Valid(value)) settings.MusicHotkey = (int)value;
                    if (line.StartsWith("MuteHotkey=") && int.TryParse(line.Substring(11), out value) && KeyBinding.Valid(value)) settings.MuteHotkey = (int)value;
                    if (line.StartsWith("AudioProcess=") && line.Length > 13) settings.AudioProcess = line.Substring(13);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return settings;
        }
        private static readonly System.Collections.Generic.HashSet<string> Known = new("CustomPosition ScreenDevice AnchorX AnchorY Animations MusicMotion CheckUpdates MusicHotkey MuteHotkey AudioProcess OverlayHotkey FloatingIcon OverlayHover OverlayAutoHide OverlayAllowPin OverlayPinned OverlayMonitor OverlayX OverlayY OverlayDelay".Split(' '));
        private static Task pendingSave=Task.CompletedTask;
        public Task SaveAsync()
        {
            // Snapshot on the owning thread; serialize writes in request order.
            string[] lines=Lines();string path=FilePath;
            lock(saveGate) return pendingSave=pendingSave.ContinueWith(_=>Write(path,lines),CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default);
        }
        public static Task FlushAsync() {lock(saveGate)return pendingSave;}
        public static bool Import(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            bool imported=false;
            foreach(string file in new[]{"island.settings","island.music-session.json","weather-cache.json"})
            {
                string source=Path.Combine(sourceDirectory,file), target=Path.Combine(destinationDirectory,file);
                if(File.Exists(source) && !File.Exists(target)) { File.Copy(source,target,false); imported=true; }
            }
            return imported;
        }
        private string[] Lines()
        {
            var lines = new System.Collections.Generic.List<string>(new[] { "CustomPosition=" + CustomPosition, "ScreenDevice=" + ScreenDevice, "AnchorX=" + AnchorX.ToString("R", CultureInfo.InvariantCulture), "AnchorY=" + AnchorY.ToString("R", CultureInfo.InvariantCulture), "Animations=" + Animations, "MusicMotion=" + MusicMotion, "CheckUpdates=" + CheckUpdates, "MusicHotkey=" + (int)MusicHotkey, "MuteHotkey=" + (int)MuteHotkey, "AudioProcess=" + AudioProcess,
                "OverlayHotkey=" + (int)OverlayHotkey, "FloatingIcon=" + FloatingIcon, "OverlayHover=" + OverlayHover, "OverlayAutoHide=" + OverlayAutoHide, "OverlayAllowPin=" + OverlayAllowPin, "OverlayPinned=" + OverlayPinned, "OverlayMonitor=" + OverlayMonitor, "OverlayX=" + OverlayX.ToString("R", CultureInfo.InvariantCulture), "OverlayY=" + OverlayY.ToString("R", CultureInfo.InvariantCulture), "OverlayDelay=" + OverlayDelay });
            lines.AddRange(Visual.Lines()); lines.AddRange(Launch.Lines()); lines.AddRange(unknown);
            return lines.ToArray();
        }
        public void Save()=>Write(FilePath,Lines());
        private static void Write(string path,string[] lines)
        {
            lock(saveGate) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); string temp=path+".tmp"; File.WriteAllLines(temp,lines); if(File.Exists(path)) File.Replace(temp,path,path+".bak"); else File.Move(temp,path); }
        }
    }
}
