using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

using System.Text;
using System.Collections.Generic;

namespace Island
{
    public static class Native
    {
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        public static readonly int ShowMessage = RegisterWindowMessage("Island.Desktop.Show.v1");
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct Size { public int Width, Height; public Size(int w, int h) { Width = w; Height = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct Blend { public byte Op, Flags, Alpha, Format; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, int message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        public delegate bool EnumWindowProc(IntPtr window, IntPtr param);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr param);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmFrameBounds(IntPtr window, uint attribute, out Rect value, int size);
        public static bool IsCloaked(IntPtr window) { int value; return DwmGetWindowAttribute(window, 14, out value, sizeof(int)) == 0 && value != 0; }
        public static bool IsDesktopWindow(IntPtr window) { string name = ClassName(window); return name == "Progman" || name == "WorkerW" || name == "SHELLDLL_DefView" || name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd"; }
        public static Rectangle VisibleWindowBounds(IntPtr window)
        {
            Rect rect;
            if (DwmFrameBounds(window, 9, out rect, Marshal.SizeOf(typeof(Rect))) != 0 && !GetWindowRect(window, out rect)) return Rectangle.Empty;
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
        public static Rectangle ClientBoundsOnScreen(IntPtr window)
        {
            Rect rect; if (!GetClientRect(window, out rect)) return Rectangle.Empty;
            var point = new Point(rect.Left, rect.Top); if (!ClientToScreen(window, ref point)) return Rectangle.Empty;
            return new Rectangle(point.X, point.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        public static bool OwnWindow(IntPtr window) { uint process; GetWindowThreadProcessId(window, out process); return process == GetCurrentProcessId(); }
        public static void OverlayTopmost(IntPtr window) { SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x0013 | 0x0200); }
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        public static IntPtr WindowAt(System.Drawing.Point point) { return WindowFromPoint(new Point(point.X, point.Y)); }
        public static bool NonActivatingTopmost(IntPtr window) { return (ExtendedStyle(window) & 0x08000008) == 0x08000008; }
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint key, out byte alpha, out uint flags);
        private static long ExtendedStyle(IntPtr window) { return IntPtr.Size == 8 ? GetWindowLongPtr64(window, -20).ToInt64() : GetWindowLong32(window, -20); }
        public static bool FullyTransparent(IntPtr window)
        {
            // IsWindowVisible also reports fully transparent overlays as visible.
            // Respect LWA_ALPHA only when the API actually returned that flag;
            // UpdateLayeredWindow/per-pixel windows must remain eligible.
            if ((ExtendedStyle(window) & 0x80000) == 0) return false;
            uint key, flags; byte alpha;
            return GetLayeredWindowAttributes(window, out key, out alpha, out flags) && (flags & 2) != 0 && alpha == 0;
        }
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, int size);
        public static IntPtr DesktopHost()
        {
            IntPtr host = IntPtr.Zero;
            EnumWindows(delegate(IntPtr window, IntPtr param)
            {
                if (FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
                host = window; return false;
            }, IntPtr.Zero);
            return host != IntPtr.Zero ? host : GetShellWindow();
        }
        public static void PlaceOnDesktop(IntPtr window)
        {
            DesktopPlaceCalls++;
            IntPtr host = DesktopHost();
            if (host == IntPtr.Zero) return;
            IntPtr previous = GetWindow(host, 3); // GW_HWNDPREV, the window just above the desktop
            if (previous == window) return;
            DesktopReorders++;
            // Insert immediately ABOVE the desktop host, BELOW application windows.
            // HWND_BOTTOM would put the island behind the wallpaper itself.
            SetWindowPos(window, previous, 0, 0, 0, 0, 0x0013 | 0x0200); // no activate/move/size/owner change
        }
        public static long DesktopPlaceCalls, DesktopReorders;
        public static bool DesktopForeground()
        {
            string name = ClassName(GetForegroundWindow());
            return name == "Progman" || name == "WorkerW" || name == "SHELLDLL_DefView";
        }
        public static string DesktopReport()
        {
            var report = new StringBuilder();
            report.AppendLine("Foreground=" + ClassName(GetForegroundWindow()));
            EnumWindows(delegate(IntPtr window, IntPtr param)
            {
                uint pid; GetWindowThreadProcessId(window, out pid);
                string process = "?";
                try { using (var p = System.Diagnostics.Process.GetProcessById((int)pid)) process = p.ProcessName; } catch (ArgumentException) { }
                Rect rect; GetWindowRect(window, out rect); int cloaked; DwmGetWindowAttribute(window, 14, out cloaked, sizeof(int));
                if (IsWindowVisible(window) && !IsIconic(window) && cloaked == 0)
                {
                    uint key, flags; byte alpha; bool layered = GetLayeredWindowAttributes(window, out key, out alpha, out flags);
                    report.AppendLine(window.ToString("X") + " " + process + " " + ClassName(window) + " style=" + ExtendedStyle(window).ToString("X") + " alpha=" + (layered ? alpha.ToString() + "/" + flags : "per-pixel/none") + " rect=" + rect.Left + "," + rect.Top + "," + rect.Right + "," + rect.Bottom);
                }
                return true;
            }, IntPtr.Zero);
            return report.ToString();
        }
        public static List<Rectangle> OccludingWindows(IntPtr island, bool ownProcessOnly = false)
        {
            var rectangles = new List<Rectangle>();
            uint ownProcess; GetWindowThreadProcessId(island, out ownProcess);
            EnumWindows(delegate(IntPtr window, IntPtr param)
            {
                if (window == island) return false; // only windows ABOVE the island
                if (!IsWindowVisible(window) || IsIconic(window) || GetAncestor(window, 3) == island || FullyTransparent(window)) return true;
                if (ownProcessOnly) { uint process; GetWindowThreadProcessId(window, out process); if (process != ownProcess) return true; }
                string name = ClassName(window);
                if (name == "Progman" || name == "WorkerW" || name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd") return true;
                // Floating utility / overlay windows are not applications hiding
                // the desktop (tooltips and GPU overlays can span the monitor).
                // Keep ordinary layered application windows eligible.
                if ((ExtendedStyle(window) & 0x80) != 0) return true;
                int cloaked;
                if (DwmGetWindowAttribute(window, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                Rect rect;
                if (GetWindowRect(window, out rect) && rect.Right > rect.Left && rect.Bottom > rect.Top)
                    rectangles.Add(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
                return true;
            }, IntPtr.Zero);
            return rectangles;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr window, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr process);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateWaitableTimerEx(IntPtr attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetWaitableTimer(IntPtr timer, ref long due, int period, IntPtr callback, IntPtr arg, bool resume);
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, ImageSize;
            public int XPelsPerMeter, YPelsPerMeter;
            public uint ColorsUsed, ColorsImportant, Color;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            public ushort SpecVersion, DriverVersion, Size, DriverExtra;
            public uint Fields;
            public int X, Y;
            public uint Orientation, FixedOutput;
            public short Color, Duplex, YResolution, TTOption, Collate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
            public ushort LogPixels;
            public uint BitsPerPixel, Width, Height, Flags, Frequency, IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string device, int mode, ref DisplayMode settings);
        public static int RefreshRate(string device)
        {
            var mode = new DisplayMode(); mode.Size = (ushort)Marshal.SizeOf(typeof(DisplayMode));
            return EnumDisplaySettings(device, -1, ref mode) && mode.Frequency > 1 ? (int)mode.Frequency : 60;
        }
        public static string ClassName(IntPtr window)
        {
            var text = new StringBuilder(128); GetClassName(window, text, text.Capacity); return text.ToString();
        }
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDC, ref Point dst, ref Size size, IntPtr srcDC, ref Point src, int key, ref Blend blend, int flags);
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint flags);
        public static bool MotionAllowed()
        {
            bool enabled;
            return !SystemParametersInfo(0x1042, 0, out enabled, 0) || enabled; // SPI_GETCLIENTAREAANIMATION
        }
    }
}
