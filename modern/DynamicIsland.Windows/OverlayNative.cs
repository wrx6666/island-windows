using System.Drawing;
using System.Runtime.InteropServices;
namespace Island;
public static class OverlayNative
{
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetStyle(IntPtr window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetStyle(IntPtr window,int index,IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int w,int h,uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")]private static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("user32.dll")]private static extern int GetWindowRgn(IntPtr window,IntPtr region);
    [DllImport("gdi32.dll")]private static extern int GetRgnBox(IntPtr region,out Rect rect);
    public static Rectangle InputBounds(IntPtr window)
    {
        IntPtr region=CreateRectRgn(0,0,0,0);
        try {if(GetWindowRgn(window,region)==0)return Rectangle.Empty;GetRgnBox(region,out var r);return Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);}
        finally {DeleteObject(region);}
    }
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window,IntPtr region,bool redraw);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Native.Point point);
    [StructLayout(LayoutKind.Sequential)]private struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out Rect rect);
    public static Rectangle Bounds(IntPtr window){GetWindowRect(window,out var r);return Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);}
    public static Point Cursor {get {GetCursorPos(out var p);return new(p.X,p.Y);} }
    public static void Configure(IntPtr window) {SetStyle(window,-20,new IntPtr(GetStyle(window,-20).ToInt64()|0x08000080));}
    public static void AllowActivation(IntPtr window,bool allow){if(window==IntPtr.Zero)return;long style=GetStyle(window,-20).ToInt64();SetStyle(window,-20,new IntPtr(allow?style & ~0x08000000L:style | 0x08000000L));}
    public static void Place(IntPtr window,int x,int y) => SetWindowPos(window,new IntPtr(-1),x,y,0,0,0x0011|0x0200);
    public static void Shape(IntPtr window,Rectangle rect,int radius)
    {
        IntPtr region=CreateRoundRectRgn(rect.Left,rect.Top,rect.Right+1,rect.Bottom+1,radius*2,radius*2);
        if(region!=IntPtr.Zero && SetWindowRgn(window,region,false)==0) DeleteObject(region); // Window owns successful region.
    }
}
