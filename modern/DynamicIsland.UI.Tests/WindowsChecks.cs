using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace Island;
public static class WindowsChecks
{
    public static async Task Run(App app,string directory)
    {
        Directory.CreateDirectory(directory);var report=new List<string>();
        void Check(bool value,string message){if(!value)throw new Exception(message);report.Add("PASS "+message);File.WriteAllLines(Path.Combine(directory,"windows-ui.txt"),report);}
        bool invalidCover=false;try{new ArtworkDecoder().Decode(new MemoryStream(new byte[]{1,2,3,4}));}catch(InvalidDataException){invalidCover=true;}Check(invalidCover,"Invalid WIC artwork becomes a recoverable provider error");
        var panel=app.Panel;var overlay=app.Overlay;var monitor=Monitors.Select(app.Config.OverlayMonitor);IntPtr foreground=Native.GetForegroundWindow();
        overlay.Open(true,true);await Task.Delay(100);Check(Native.NonActivatingTopmost(panel.Handle),"Panel uses WS_EX_NOACTIVATE / TOPMOST");
        IntPtr after=Native.GetForegroundWindow();
        if(after==foreground)Check(true,"Showing own overlay did not replace foreground window (sampled)");
        else if(Native.OwnWindow(after))Check(false,"Showing overlay activated an own window");
        else report.Add("SKIP foreground stability sample: another external window became active during the sample; no own window activated.");
        overlay.Close(true,true);Check(!panel.Rendering,"Closing a silent panel removes frame callback");
        app.Music.DiagnosticTrack(new(){Title="Input region fixture",Playing=true});await Task.Delay(50);
        var input=OverlayNative.InputBounds(panel.Handle);var bounds=OverlayNative.Bounds(panel.Handle);
        Check(input.Width<=bounds.Width && input.Height<=bounds.Height && input.Height<=66,"Compact HWND and input region contain no invisible expanded area at current DPI");
        app.Music.DiagnosticTrack(new());
        using var host=new TestWindow();host.Window.Show();await Task.Delay(100);
        using(var watch=new ActiveWindowWatcher(app.Dispatch,w=>w==panel.Handle || w==overlay.Icon.Handle,true,()=>host.Handle))
        {
            watch.Refresh();Check(watch.Current.Kind==ForegroundKind.Windowed,"Native inspection recognizes partial own WPF window");
            host.Window.WindowState=WindowState.Maximized;await Task.Delay(450);watch.Refresh();Check(watch.Current.Kind is ForegroundKind.Maximized or ForegroundKind.Fullscreen,"Native maximized-window classification");
            host.Window.WindowState=WindowState.Normal;host.Window.WindowStyle=WindowStyle.None;host.Window.ResizeMode=ResizeMode.NoResize;host.Window.Left=monitor.Bounds.Left;host.Window.Top=monitor.Bounds.Top;host.Window.Width=monitor.Bounds.Width;host.Window.Height=monitor.Bounds.Height;await Task.Delay(450);watch.Refresh();Check(watch.Current.Kind==ForegroundKind.Fullscreen,"Native borderless monitor-covering classification at current DPI");
            Check(watch.Events>0 && watch.Inspections<watch.Events+10,"Own-window WinEvent hooks receive/coalesce geometry events");
        }
        using(var keys=new Hotkeys(host.Handle))
        using(var conflictWindow=new TestWindow())
        using(var other=new Hotkeys(conflictWindow.Handle))
        {
            Check(keys.Configure(0x87,0,0)==null,"RegisterHotKey accepts single F24 key");
            Check(other.Configure(0x87,0,0)!=null,"Conflicting global key rejected");
            Check(keys.Configure(KeyBinding.Control|0x87,KeyBinding.Control|0x87,0)!=null,"Duplicate command bindings rejected");
            Check(keys.Configure(KeyBinding.Control|0x87,0,KeyBinding.Shift|0x87)==null,"Hotkeys can be reassigned to independent chords");
            int delivered=0;HwndSourceHook hook=(IntPtr h,int message,IntPtr w,IntPtr l,ref bool handled)=>{if(message==0x312 && w.ToInt32()==Hotkeys.MusicId)delivered++;return IntPtr.Zero;};var source=HwndSource.FromHwnd(host.Handle);source.AddHook(hook);Native.PostMessage(host.Handle,0x312,new(Hotkeys.MusicId),IntPtr.Zero);await Task.Delay(50);source.RemoveHook(hook);Check(delivered==1,"WM_HOTKEY arrives through WPF HWND hook (synthetic message, not physical keyboard test)");
        }
        overlay.ApplyForeground(new(){Window=host.Handle,Kind=ForegroundKind.Maximized,Monitor=monitor.DeviceName});Check(!panel.IsVisible && !overlay.Icon.IsVisible,"Maximized context hides icon and panel");
        overlay.Toggle();await Task.Delay(100);Check(panel.IsVisible && panel.ExpandedState,"Hotkey summon can show panel while context blocked");
        overlay.ApplyForeground(new(){Window=host.Handle,Kind=ForegroundKind.Fullscreen,Monitor=monitor.DeviceName});Check(panel.IsVisible,"Same blocked foreground does not cycle temporary summon");
        overlay.Toggle();Check(!panel.IsVisible && !panel.Rendering,"Repeated summon hides panel and detaches rendering");
        overlay.ApplyForeground(new(){Window=new(0x1234),Kind=ForegroundKind.Windowed,Monitor=monitor.DeviceName});Check(overlay.Icon.IsVisible,"Windowed context restores static icon without desktop click");
        overlay.Open(false,true);overlay.TogglePin();Check(overlay.Pinned,"Panel can be pinned");overlay.Close(true,true);Check(!overlay.Pinned,"Explicit dismissal clears pin");
        int fired=0;using(var timer=new OneShot(app.Dispatch,()=>fired++){Interval=100}){timer.Start();await Task.Delay(50);timer.Start();await Task.Delay(70);Check(fired==0,"Rearmed one-shot ignores obsolete due time");timer.Stop();await Task.Delay(70);Check(fired==0,"Stopped one-shot invalidates queued callback");}
        // No mouse input is synthesized and no external process is activated by this suite.
        Check(Monitors.All.Length>=1,"Monitor inventory available");report.Add("NOT TESTED: physical keyboard, Word/browser/F11, Exclusive Fullscreen, multi-monitor/mixed DPI hardware and game FPS.");File.WriteAllLines(Path.Combine(directory,"windows-ui.txt"),report);
    }
    private sealed class TestWindow : IDisposable
    {
        public readonly Window Window=new(){Width=700,Height=500,Left=100,Top=100,ShowInTaskbar=false,ShowActivated=false,Title="Island native integration fixture"};
        public IntPtr Handle=>new WindowInteropHelper(Window).EnsureHandle();
        public void Dispose()=>Window.Close();
    }
}
