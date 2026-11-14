using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;


namespace Island
{
    // The low-level hook must never wait for the UI thread. It only copies
    // button coordinates and posts a native message; hit testing runs in UI.
    public sealed class OutsideClickWatcher : IDisposable
    {
        private delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int kind, MouseHook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern int GetMessage(out MessageData message, IntPtr window, uint first, uint last);
        [DllImport("user32.dll")] private static extern bool PeekMessage(out MessageData message, IntPtr window, uint first, uint last, uint flags);
        [StructLayout(LayoutKind.Sequential)] private struct MessageData
        {
            public IntPtr Window;
            public uint Message;
            public UIntPtr W;
            public IntPtr L;
            public uint Time;
            public Point Position;
            public uint Private;
        }
        private sealed class RunState
        {
            public readonly ConcurrentQueue<Point> Clicks = new ConcurrentQueue<Point>();
            public volatile bool Stopped;
            public volatile int ThreadId;
            public IntPtr Window, Hook;
            public MouseHook Callback;
        }
        public static readonly int ClickMessage = RegisterWindowMessage("Island.OutsideClick.v2");
        private readonly Func<IntPtr> owner;
        private readonly Func<Point, bool> inside;
        private readonly Action dismiss;
        private volatile RunState current;
        private bool disposed;
        private int lastError;
        public bool Active { get { var run = current; return run != null && !run.Stopped && Volatile.Read(ref run.Hook) != IntPtr.Zero; } }
        public int LastError { get { return Volatile.Read(ref lastError); } }
        public OutsideClickWatcher(Func<IntPtr> window, Func<Point, bool> contains, Action close)
        { owner = window; inside = contains; dismiss = close; }
        public void SetActive(bool value)
        {
            if (disposed) return;
            var previous = current;
            if (value && previous != null && previous.Window == owner()) return;
            if (previous != null)
            {
                current = null; previous.Stopped = true;
                uint thread = (uint)previous.ThreadId;
                if (thread != 0) PostThreadMessage(thread, 0x12, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
            }
            if (!value) return;
            var run = new RunState { Window = owner() };
            run.Callback = (code, message, data) => OnMouse(run, code, message, data);
            current = run; Volatile.Write(ref lastError, 0);
            new Thread(delegate() { Listen(run); }) { IsBackground = true, Name = "Island outside clicks" }.Start();
        }
        private void Listen(RunState run)
        {
            try
            {
                MessageData message;
                PeekMessage(out message, IntPtr.Zero, 0, 0, 0); // create this thread's message queue before publishing its ID
                run.ThreadId = unchecked((int)Native.GetCurrentThreadId());
                if (run.Stopped) return;
                IntPtr hook = SetWindowsHookEx(14, run.Callback, GetModuleHandle(null), 0);
                Volatile.Write(ref run.Hook, hook);
                if (hook == IntPtr.Zero) { Volatile.Write(ref lastError, Marshal.GetLastWin32Error()); return; }
                while (!run.Stopped && GetMessage(out message, IntPtr.Zero, 0, 0) > 0) { }
            }
            finally
            {
                IntPtr hook = Interlocked.Exchange(ref run.Hook, IntPtr.Zero);
                if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
                run.Stopped = true; GC.KeepAlive(run.Callback);
            }
        }
        private static IntPtr OnMouse(RunState run, int code, IntPtr message, IntPtr data)
        {
            int kind = message.ToInt32();
            if (code >= 0 && !run.Stopped && (kind == 0x201 || kind == 0x204 || kind == 0x207 || kind == 0x20b))
            {
                run.Clicks.Enqueue(new Point(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4)));
                Native.PostMessage(run.Window, ClickMessage, IntPtr.Zero, IntPtr.Zero);
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }
        // Called only by the panel's WndProc. No synchronous UI calls or window
        // queries are allowed in OnMouse or on the native hook thread.
        public void DispatchClicks()
        {
            var run = current; Point point;
            if (disposed || run == null) return;
            while (!run.Stopped && run.Clicks.TryDequeue(out point))
            {
                if (!inside(point)) dismiss();
                if (current != run) break;
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            SetActive(false); disposed = true;
        }
    }
}
