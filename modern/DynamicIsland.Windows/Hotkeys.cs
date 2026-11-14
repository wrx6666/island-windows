using System;
using System.Runtime.InteropServices;


namespace Island
{
    public sealed class Hotkeys : IDisposable
    {
        public const int MusicId = 0x5141, MuteId = 0x5142, OverlayId = 0x5143;
        private IntPtr handle;
        private int music, mute, overlay;
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        public Hotkeys(IntPtr hwnd) { handle = hwnd; }
        public string Rebind(IntPtr next)
        {
            if (handle == next) return null;
            UnregisterHotKey(handle, MusicId); UnregisterHotKey(handle, MuteId); UnregisterHotKey(handle, OverlayId);
            handle = next; return Configure(music, mute, overlay);
        }
        public static bool Valid(int keys) => KeyBinding.Valid(keys);
        public static string Label(int keys) => KeyBinding.Label(keys);
        private bool Register(int id, int keys)
        {
            if (keys == 0) return true;
            uint mods = 0x4000; // MOD_NOREPEAT: holding a key never toggles repeatedly.
            if ((keys & KeyBinding.Control) != 0) mods |= 2;
            if ((keys & KeyBinding.Alt) != 0) mods |= 1;
            if ((keys & KeyBinding.Shift) != 0) mods |= 4;
            return RegisterHotKey(handle, id, mods, (uint)(keys & KeyBinding.KeyCode));
        }
        public string Configure(int nextMusic, int nextMute)
        { return Configure(nextMusic, nextMute, overlay); }
        public string Configure(int nextMusic, int nextMute, int nextOverlay)
        {
            if (!Valid(nextMusic) || !Valid(nextMute) || !Valid(nextOverlay)) return "Нажми одну клавишу или сочетание с Ctrl, Alt, Shift.";
            if (nextMusic != 0 && nextMusic == nextMute || nextOverlay != 0 && (nextOverlay == nextMusic || nextOverlay == nextMute)) return "Для музыки, звука приложения и панели нужны разные клавиши.";
            UnregisterHotKey(handle, MusicId); UnregisterHotKey(handle, MuteId); UnregisterHotKey(handle, OverlayId);
            if (!Register(MusicId, nextMusic) || !Register(MuteId, nextMute) || !Register(OverlayId, nextOverlay))
            {
                UnregisterHotKey(handle, MusicId); UnregisterHotKey(handle, MuteId); UnregisterHotKey(handle, OverlayId);
                Register(MusicId, music); Register(MuteId, mute); Register(OverlayId, overlay);
                return "Это сочетание занято Windows или другим приложением. Выбери другое.";
            }
            music = nextMusic; mute = nextMute; overlay = nextOverlay; return null;
        }
        public void Dispose() { UnregisterHotKey(handle, MusicId); UnregisterHotKey(handle, MuteId); UnregisterHotKey(handle, OverlayId); }
    }
}
