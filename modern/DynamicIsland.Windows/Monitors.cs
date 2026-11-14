using System.Drawing;
using System.Runtime.InteropServices;

namespace Island;

public sealed record DisplayMonitor(string DeviceName, Rectangle Bounds, Rectangle WorkingArea, bool Primary)
{
    public override string ToString() => DeviceName + " · " + Bounds.Width + "×" + Bounds.Height;
}
public static class Monitors
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left,Top,Right,Bottom; public Rectangle Value => Rectangle.FromLTRB(Left,Top,Right,Bottom); }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct Info { public int Size; public Rect Monitor,Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Device; }
    private delegate bool EnumProc(IntPtr monitor,IntPtr dc,IntPtr rect,IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,EnumProc proc,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor,ref Info info);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    private static DisplayMonitor Read(IntPtr handle) { var info=new Info {Size=Marshal.SizeOf<Info>()}; if(!GetMonitorInfo(handle,ref info)) throw new System.ComponentModel.Win32Exception(); return new(info.Device,info.Monitor.Value,info.Work.Value,(info.Flags&1)!=0); }
    public static DisplayMonitor[] All { get { var result=new List<DisplayMonitor>(); EnumProc callback=(m,_,__,___)=>{result.Add(Read(m));return true;}; EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,callback,IntPtr.Zero); return result.ToArray(); } }
    public static DisplayMonitor FromWindow(IntPtr window) => Read(MonitorFromWindow(window,2));
    public static DisplayMonitor Select(string name) { var all=All; return all.FirstOrDefault(m=>m.DeviceName==name) ?? all.FirstOrDefault(m=>m.Primary) ?? all[0]; }
}
