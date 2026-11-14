using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using DrawingRect=System.Drawing.Rectangle;

namespace Island;
public sealed class IconWindow : Window
{
    public IntPtr Handle {get;private set;}
    public event Action Entered,LeftIcon,MovedPointer,Clicked,PositionChanged;
    private System.Drawing.Point start,origin;
    private bool pending,dragged;
    public bool Dragging=>pending;
    public IconWindow()
    {
        Title="Island · открыть панель";Width=Height=44;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowActivated=false;ShowInTaskbar=false;Topmost=true;Cursor=Cursors.Hand;
        var border=new Border{CornerRadius=new(22),Background=new SolidColorBrush(Color.FromRgb(26,26,33)),BorderBrush=new SolidColorBrush(Color.FromRgb(94,89,106)),BorderThickness=new(1)};
        var canvas=new Canvas{Width=44,Height=44};var ink=new SolidColorBrush(Color.FromRgb(255,181,196));ink.Freeze();
        foreach(var point in new[]{new Point(16,15),new Point(24,15),new Point(16,23),new Point(24,23)}) {var dot=new Ellipse{Width=6,Height=6,Fill=ink};Canvas.SetLeft(dot,point.X-1);Canvas.SetTop(dot,point.Y-1);canvas.Children.Add(dot);}border.Child=canvas;Content=border;
        SourceInitialized+=(_,_)=> {Handle=new WindowInteropHelper(this).Handle;OverlayNative.Configure(Handle);HwndSource.FromHwnd(Handle).AddHook((IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled)=>{if(msg==0x7b){handled=true;return IntPtr.Zero;}if(msg==0x21){handled=true;return new(3);}return IntPtr.Zero;});};
        MouseEnter+=(_,_)=>Entered?.Invoke();MouseLeave+=(_,_)=>LeftIcon?.Invoke();
        MouseLeftButtonDown+=(_,e)=>{pending=true;dragged=false;start=OverlayNative.Cursor;origin=new(Bounds.Left,Bounds.Top);CaptureMouse();e.Handled=true;};
        MouseMove+=(_,_)=>{if(!pending){MovedPointer?.Invoke();return;}var p=OverlayNative.Cursor;int dx=p.X-start.X,dy=p.Y-start.Y;if(!dragged && Math.Abs(dx)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(dy)<SystemParameters.MinimumVerticalDragDistance)return;dragged=true;OverlayNative.Place(Handle,origin.X+dx,origin.Y+dy);};
        MouseLeftButtonUp+=(_,e)=>{if(!pending)return;pending=false;ReleaseMouseCapture();if(dragged)PositionChanged?.Invoke();else Clicked?.Invoke();e.Handled=true;};
        LostMouseCapture+=(_,_)=>{if(pending){pending=false;if(dragged)PositionChanged?.Invoke();}};
        PreviewMouseRightButtonDown+=(_,e)=>e.Handled=true;
        PreviewMouseRightButtonUp+=(_,e)=>e.Handled=true;
        ContextMenuOpening+=(_,e)=>e.Handled=true;
        DpiChanged+=(_,_)=>Shape();
    }
    public DrawingRect Bounds => OverlayNative.Bounds(Handle);
    private void Shape() {if(Handle==IntPtr.Zero)return;var b=Bounds;OverlayNative.Shape(Handle,new(0,0,b.Width,b.Height),b.Width/2);}
    public void Place(Settings settings)
    {
        new WindowInteropHelper(this).EnsureHandle();var work=Monitors.Select(settings.OverlayMonitor).WorkingArea;
        OverlayNative.Place(Handle,work.Left+12,work.Top+12);double scale=Math.Max(.75,Native.GetDpiForWindow(Handle)/96d);int size=(int)Math.Round(44*scale),margin=(int)Math.Round(12*scale);
        int x=work.Left+margin+(int)Math.Round(Math.Max(0,work.Width-size-margin*2)*settings.OverlayX),y=work.Top+margin+(int)Math.Round(Math.Max(0,work.Height-size-margin*2)*settings.OverlayY);
        OverlayNative.Place(Handle,x,y);Shape();
    }
}
