using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;


namespace Island
{
    public enum ForegroundKind { Desktop, Windowed, Maximized, Fullscreen }
    public sealed class ForegroundState
    {
        public IntPtr Window;
        public ForegroundKind Kind;
        public string Monitor = "";
        public bool Blocks(DisplayMonitor screen) { return Monitor == screen.DeviceName && (Kind == ForegroundKind.Maximized || Kind == ForegroundKind.Fullscreen); }
    }
    // O(1) foreground inspection. No EnumWindows, mouse hook, or idle polling.
    public sealed class ActiveWindowWatcher : IDisposable
    {
        private readonly Native.WinEventProc callback;
        private readonly List<IntPtr> hooks = new List<IntPtr>();
        private readonly OneShot coalesce, settled;
        private readonly Func<IntPtr, bool> ignore;
        private readonly Func<IntPtr> foreground;
        private bool disposed;
        public ForegroundState Current = new ForegroundState();
        public long Events, Inspections;
        public bool Coalescing { get { return coalesce.Enabled; } }
        public bool Settling { get { return settled.Enabled; } }
        public event Action Changed;
        public ActiveWindowWatcher(Action<Action> dispatch, Func<IntPtr, bool> ignored, bool testOwnOnly = false, Func<IntPtr> getForeground = null)
        {
            ignore = ignored; foreground = getForeground ?? Native.GetForegroundWindow; callback = OnEvent;
            coalesce = new OneShot(dispatch, Refresh) { Interval=50 };
            settled = new OneShot(dispatch, Refresh) { Interval=350 };
            uint process = testOwnOnly ? (uint)System.Diagnostics.Process.GetCurrentProcess().Id : 0;
            try
            {
                Hook(3, 3, process); Hook(0x16, 0x17, process);
                Hook(0x8001, 0x8003, process); Hook(0x800b, 0x800b, process);
            }
            catch { Dispose(); throw; }
        }
        private void Hook(uint first, uint last, uint process)
        {
            IntPtr hook = Native.SetWinEventHook(first, last, IntPtr.Zero, callback, process, 0, process == 0 ? 2u : 0u); // SKIPOWNPROCESS in production
            if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            hooks.Add(hook);
        }
        public static bool Covers(Rectangle window, Rectangle target, int tolerance)
        {
            return window.Width > 0 && window.Height > 0 && window.Left <= target.Left + tolerance && window.Top <= target.Top + tolerance && window.Right >= target.Right - tolerance && window.Bottom >= target.Bottom - tolerance;
        }
        public static ForegroundKind Classify(bool desktop, bool maximized, Rectangle bounds, Rectangle client, Rectangle work, Rectangle monitor, int tolerance)
        {
            if (desktop) return ForegroundKind.Desktop;
            if (Covers(client, monitor, tolerance) || Covers(bounds, monitor, tolerance)) return ForegroundKind.Fullscreen;
            if (maximized || Covers(bounds, work, tolerance)) return ForegroundKind.Maximized;
            return ForegroundKind.Windowed;
        }
        public void Refresh()
        {
            if (disposed) return;
            IntPtr window = foreground();
            if (window == IntPtr.Zero || ignore(window)) return; // retain external context while our settings/menu has focus
            IntPtr root = Native.GetAncestor(window, 3); if (root != IntPtr.Zero) window = root;
            if (ignore(window)) return;
            Inspections++;
            DisplayMonitor screen = Monitors.FromWindow(window);
            bool desktop = Native.IsDesktopWindow(window);
            Rectangle bounds = Native.VisibleWindowBounds(window), client = Native.ClientBoundsOnScreen(window);
            bool valid = !Native.IsIconic(window) && Native.IsWindowVisible(window) && !Native.IsCloaked(window);
            var next = new ForegroundState { Window = window, Monitor = screen.DeviceName,
                Kind = valid ? Classify(desktop, Native.IsZoomed(window), bounds, client, screen.WorkingArea, screen.Bounds, 2) : ForegroundKind.Desktop };
            Current = next;
            Action changed = Changed; if (changed != null) changed();
        }
        private void OnEvent(IntPtr hook, uint ev, IntPtr window, int obj, int child, uint thread, uint time)
        {
            if (disposed || window == IntPtr.Zero || ignore(window)) return;
            if (ev >= 0x8000 && (obj != 0 || child != 0)) return;
            if (ev != 3 && window != Current.Window && window != foreground()) return;
            Events++;
            if (ev == 3) Refresh();
            else if (!coalesce.Enabled) coalesce.Start();
            settled.Stop(); settled.Start();
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            foreach (IntPtr hook in hooks) Native.UnhookWinEvent(hook);
            hooks.Clear(); coalesce.Dispose(); settled.Dispose();
        }
    }
}
