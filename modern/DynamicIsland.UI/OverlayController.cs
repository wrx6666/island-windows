using System.Windows;

namespace Island;
public sealed class OverlayController : IDisposable
{
    private readonly Settings settings;
    private readonly IslandWindow panel;
    public readonly IconWindow Icon=new();
    public readonly ActiveWindowWatcher Watcher;
    private readonly OutsideClickWatcher outside;
    private readonly OneShot enter,leave;
    private bool requested,latched,blocked,closing,disposed,suppress,enabled=true;
    private System.Drawing.Point suppressed;
    private IntPtr context;
    public Func<System.Drawing.Point> Pointer {get;set;}=()=>OverlayNative.Cursor;
    private readonly bool interactive;
    public bool Pinned {get;private set;}
    public bool Busy {get;set;}
    public bool Playing {get;set;}
    public bool Requested=>requested;
    public event Action PositionChanged,VisibilityChanged;
    public OverlayController(IslandWindow window,Settings config,Action<Action> dispatch,bool watch=true,bool acceptInput=true)
    {
        panel=window;settings=config;interactive=acceptInput;new System.Windows.Interop.WindowInteropHelper(panel).EnsureHandle();
        enter=new(dispatch,()=>{if(!blocked && !Icon.Dragging && Inside())Open(false);}){Interval=120};
        leave=new(dispatch,()=>{if(!Pinned && !latched && !Busy && !panel.InteractionBusy && !Inside())Close(false);}){Interval=config.OverlayDelay};
        outside=new(()=>panel.Handle,p=>Native.OwnWindow(Native.WindowAt(p)),()=>{if(requested && !Busy && !panel.InteractionBusy)Close(true);});
        Watcher=watch?new ActiveWindowWatcher(dispatch,Native.OwnWindow):null;if(Watcher!=null)Watcher.Changed+=ForegroundChanged;
        Icon.Entered+=Enter;Icon.MovedPointer+=Move;Icon.LeftIcon+=Leave;Icon.Clicked+=()=>{Pinned=settings.OverlayAllowPin;Open(true);};
        Icon.PositionChanged+=()=>{var monitor=Monitors.FromWindow(Icon.Handle);var b=Icon.Bounds;var work=monitor.WorkingArea;double margin=b.Width*12d/44;settings.OverlayMonitor=monitor.DeviceName;settings.OverlayX=Math.Clamp((b.X-work.Left-margin)/Math.Max(1,work.Width-b.Width-margin*2),0,1);settings.OverlayY=Math.Clamp((b.Y-work.Top-margin)/Math.Max(1,work.Height-b.Height-margin*2),0,1);PositionChanged?.Invoke();Reposition();};
        panel.NavigationStarted+=()=>{Cancel();if(closing)Open(false);};
        panel.OpenRequested+=()=>Open(false);panel.CloseRequested+=()=>Close(true);panel.PinRequested+=TogglePin;
        panel.PointerEntered+=Enter;panel.PointerMoved+=Move;panel.PointerLeft+=Leave;
        panel.TransitionSettled+=()=>{if(closing){closing=false;requested=false;latched=false;Reconcile();}};
        panel.NativeMessage+=(_,message,__,___)=>{if(message==OutsideClickWatcher.ClickMessage)outside.DispatchClicks();};
        Reposition();Watcher?.Refresh();Reconcile();
    }
    public void Reposition() {Icon.Place(settings);var b=Icon.Bounds;panel.SetAnchor(settings.OverlayX>.5?b.Right:b.Left,settings.OverlayY>.5?b.Bottom:b.Top);}
    private void ForegroundChanged()
    {
        ApplyForeground(Watcher.Current);
    }
    public void ApplyForeground(ForegroundState state)
    {
        bool next=settings.OverlayAutoHide && state.Blocks(Monitors.Select(settings.OverlayMonitor));
        if(context!=state.Window || next!=blocked) {context=state.Window;if(next || blocked){latched=requested=closing=false;Pinned=false;Cancel();panel.Suspend();}}
        blocked=next;Reconcile();
    }
    public void RefreshSettings() {if(!settings.OverlayAllowPin)Pinned=false;Cancel();requested=latched=closing=false;Reposition();Watcher?.Refresh();Reconcile();}
    public void Toggle() {panel.Diagnostics?.Input();if(requested)Close(true,true);else Open(true,true);}
    public void Open(bool latch,bool instant=false) {if(!enabled)return;Cancel();panel.Diagnostics?.Input();requested=true;closing=false;latched=latch;bool fromIcon=Icon.IsVisible;Icon.Hide();panel.Present(true,instant,fromIcon);outside.SetActive(interactive);VisibilityChanged?.Invoke();}
    public void TogglePin() {if(!settings.OverlayAllowPin)return;Pinned=!Pinned;latched=false;if(!Pinned)Leave();}
    public void Close(bool explicitClose,bool instant=false)
    {
        Cancel();if(explicitClose){suppress=true;suppressed=Pointer();Pinned=latched=false;}if(!requested)return;
        outside.SetActive(false);closing=true;panel.Present(false,instant);if(instant || !panel.Rendering){requested=closing=latched=false;Reconcile();}
    }
    public void Reconcile()
    {
        if(disposed)return;
        if(enabled && requested) {Icon.Hide();panel.Present(!closing);}
        else if(enabled && settings.FloatingIcon && !blocked && Playing) {Icon.Hide();panel.Present(false);}
        else {panel.Suspend();if(enabled && settings.FloatingIcon && !blocked){if(!Icon.IsVisible)Icon.Show();}else Icon.Hide();}
        outside.SetActive(interactive && enabled && requested && !closing);VisibilityChanged?.Invoke();
    }
    public void SetEnabled(bool value){enabled=value;if(!value){requested=latched=closing=false;Cancel();}Reconcile();}
    private bool Inside() {var p=Pointer();return panel.IsVisible && panel.BodyOnScreen.Contains(p) || Icon.IsVisible && Icon.Bounds.Contains(p);}
    private void Enter() {leave.Stop();QueueHover();}
    private void Move(){leave.Stop();QueueHover();}
    private void QueueHover(){if(!interactive || disposed || blocked || !settings.OverlayHover || requested && !closing || Icon.Dragging || Busy)return;if(suppress){if(Pointer()==suppressed)return;suppress=false;}if(closing && Inside()){Open(false);return;}if(!enter.Enabled)enter.Start();}
    private void Leave(){enter.Stop();if(Inside())return;if(!Inside())suppress=false;if(!requested || Pinned || latched || Busy || panel.InteractionBusy)return;if(panel.OpeningAnimation && !Inside()){Close(false);return;}leave.Interval=settings.OverlayDelay;leave.Start();}
    private void Cancel(){enter.Stop();leave.Stop();}
    public object State()=>new {state=requested?closing?"closing":"panel":panel.IsVisible?"compact-music":Icon.IsVisible?"icon":"hidden",blocked,pinned=Pinned,outsideHook=outside.Active,foreground=Watcher?.Current.Kind.ToString(),events=Watcher?.Events,inspections=Watcher?.Inspections,hoverTimer=enter.Enabled,leaveTimer=leave.Enabled};
    public void Dispose(){if(disposed)return;disposed=true;outside.Dispose();Watcher?.Dispose();enter.Dispose();leave.Dispose();Icon.Close();}
}
