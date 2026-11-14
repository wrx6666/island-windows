using System;
using Color = Island.AccentColor;
using System.Globalization;

namespace Island
{
    public enum VisualMode { Fluid, Border, Combined }
    public sealed class VisualOptions
    {
        public bool Enabled = true;
        public VisualMode Mode = VisualMode.Combined;
        public int Intensity = 75, Sensitivity = 100, Speed = 100, Palette;
        public string Process = "";
        public VisualOptions Copy() { return (VisualOptions)MemberwiseClone(); }
        public bool Parse(string line)
        {
            int split = line.IndexOf('='); if (split < 0) return false;
            string key = line.Substring(0, split), value = line.Substring(split + 1); int n;
            if (key == "VisualEnabled") { Enabled = value != "False"; return true; }
            if (key == "VisualProcess") { Process = value; return true; }
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return false;
            switch (key)
            {
                case "VisualMode": Mode = (VisualMode)Math.Max(0, Math.Min(2, n)); break;
                case "VisualIntensity": Intensity = Math.Max(0, Math.Min(100, n)); break;
                case "VisualSensitivity": Sensitivity = Math.Max(25, Math.Min(200, n)); break;
                case "VisualSpeed": Speed = Math.Max(25, Math.Min(200, n)); break;
                case "VisualPalette": Palette = Math.Max(0, Math.Min(3, n)); break;
                default: return false;
            }
            return true;
        }
        public string[] Lines() { return new[] { "VisualEnabled=" + Enabled, "VisualMode=" + (int)Mode, "VisualIntensity=" + Intensity, "VisualSensitivity=" + Sensitivity, "VisualSpeed=" + Speed, "VisualPalette=" + Palette, "VisualProcess=" + Process }; }
        public void Colors(MusicSnapshot track, out Color primary, out Color secondary)
        {
            primary = Palette == 1 ? Color.FromArgb(125, 103, 255) : Palette == 2 ? Color.FromArgb(255, 133, 68) : Palette == 3 ? Color.FromArgb(81, 194, 244) : track.Color;
            secondary = Palette == 1 ? Color.FromArgb(75, 225, 191) : Palette == 2 ? Color.FromArgb(255, 93, 150) : Palette == 3 ? Color.FromArgb(170, 153, 255) : track.SecondaryColor;
        }
    }
}
