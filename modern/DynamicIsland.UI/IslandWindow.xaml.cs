using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DrawingRect=System.Drawing.Rectangle;

namespace Island;

public partial class IslandWindow : Window
{
    public event Action TogglePlay,Previous,Next,OpenRequested,CloseRequested,PinRequested,ExitRequested,PointerEntered,PointerLeft,PointerMoved;
    public event Action<double> SeekRequested,VolumeRequested;
    public event Action<IntPtr,int,IntPtr,IntPtr> NativeMessage;
    public event Action TransitionSettled,NavigationStarted;
    public IntPtr Handle {get;private set;}
    private readonly Spring spring=new();
    private bool iconOrigin;
    private readonly Canvas openingIcon=new(){Width=44,Height=44,IsHitTestVisible=false};
    private readonly RectangleGeometry clip=new();
    private readonly TranslateTransform offset=new();
    private MusicSnapshot track=new();
    private byte[] artwork;
    private bool frameAttached,expanded,updating,transition,closing,systemMotion=true;
    private string page="home";
    private float effectOpacity;
    private double lastFrame,phase,anchorRight,anchorTop;
    private DrawingRect lastRegion;
    private DisplayMonitor placementMonitor;
    private System.Drawing.Point lastLocation=new(int.MinValue,int.MinValue);
    private AudioFeatures smoothed;
    private readonly Stopwatch time=Stopwatch.StartNew();
    public Settings Config {get;set;}=new();
    public MusicLevels Levels {get;set;}
    public PanelDiagnostics Diagnostics {get;set;}
    public bool Rendering => frameAttached;
    public bool ExpandedState => expanded;
    public bool OpeningAnimation=>transition && expanded;
    public bool InteractionBusy => IsMouseCaptureWithin;
    public long FrameCount {get;private set;}
    public long CallbackCount {get;private set;}
    public double TargetFrameRate=>DisplayFrameRate;
    public int DisplayFrameRate {get;private set;}=60;
    public DrawingRect BodyOnScreen {get;private set;}
    private void SetPage(string value)
    {
        if(value==page && pageMorph)return;
        if(value!=page)NavigationStarted?.Invoke();
        bool animatePage=BeginPageChange(value);
        page=value;OverlayNative.AllowActivation(Handle,value=="settings");
        UpdateLyricsActivity();
        WeatherContent.Visibility=WeatherDivider.Visibility=Visibility.Collapsed;
        settingsPage.Width=660;settingsPage.Height=430;
        DateText.Text=value=="settings"?"Настройки":value=="music"?"Музыка":value=="weather"?"Погода":"Главная";
        FinishPageChange(animatePage);NavigationState();
        Canvas.SetLeft(CollapseButton,708-48);Canvas.SetLeft(MenuButton,708-120);Canvas.SetLeft(ExitButton,708-84);Canvas.SetLeft(headerTime,708-180);
        if(IsVisible){SetViewport();ApplyGeometry();if(animatePage)StartFrames();}

    }
    public IslandWindow()
    {
        InitializeComponent();InitializeGlass();InitializePages();
        foreach(var point in new[]{new Point(16,15),new Point(24,15),new Point(16,23),new Point(24,23)}){var dot=new System.Windows.Shapes.Ellipse{Width=6,Height=6,Fill=new SolidColorBrush(Color.FromRgb(255,181,196))};Canvas.SetLeft(dot,point.X-1);Canvas.SetTop(dot,point.Y-1);openingIcon.Children.Add(dot);}
        Root.Children.Add(openingIcon);openingIcon.RenderTransform=offset;SetPage("home"); Root.Clip=clip; Body.RenderTransform=Ambient.RenderTransform=GlassRim.RenderTransform=Expanded.RenderTransform=Compact.RenderTransform=offset;
        SourceInitialized+=(_,_)=>{Handle=new WindowInteropHelper(this).Handle;systemMotion=Native.MotionAllowed();OverlayNative.Configure(Handle);HwndSource.FromHwnd(Handle).AddHook(Hook);};
        MouseEnter+=(_,_)=>PointerEntered?.Invoke();MouseLeave+=(_,_)=>PointerLeft?.Invoke();MouseMove+=(_,_)=>PointerMoved?.Invoke();
        PreviewMouseLeftButtonUp+=(_,e)=> {
            for(DependencyObject node=e.OriginalSource as DependencyObject;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
                if(node is ButtonBase or Slider or ScrollViewer)return;
            if(expanded)PinRequested?.Invoke();else OpenRequested?.Invoke();
        };
        CompactOpen.Click+=(_,_)=>OpenRequested?.Invoke();CollapseButton.Click+=(_,_)=>CloseRequested?.Invoke();MenuButton.Click+=(_,_)=>OpenSettingsPage();
        PreviewMouseRightButtonDown+=(_,e)=>e.Handled=true;
        PreviewMouseRightButtonUp+=(_,e)=>e.Handled=true;
        ContextMenuOpening+=(_,e)=>e.Handled=true;
        ExitButton.Click+=(_,_)=>ExitRequested?.Invoke();
        PlayButton.Click+=(_,_)=>TogglePlay?.Invoke();ResumeButton.Click+=(_,_)=>TogglePlay?.Invoke();PreviousButton.Click+=(_,_)=>Previous?.Invoke();NextButton.Click+=(_,_)=>Next?.Invoke();
        HomeTabButton.Click+=(_,_)=>SetPage("home");MusicTabButton.Click+=(_,_)=>SetPage("music");WeatherTabButton.Click+=(_,_)=>SetPage("weather");
        BackButton.Click+=(_,_)=>SeekRequested?.Invoke(Math.Max(0,track.PositionSeconds-15));ForwardButton.Click+=(_,_)=>SeekRequested?.Invoke(track.PositionSeconds+15);
        SeekSlider.PreviewMouseLeftButtonDown+=(_,_)=>updating=false;
        SeekSlider.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((_,_)=>SeekRequested?.Invoke(SeekSlider.Value)));
        SeekSlider.PreviewMouseLeftButtonUp+=(_,_)=>{if(!updating)SeekRequested?.Invoke(SeekSlider.Value);};
        SeekSlider.PreviewKeyUp+=(_,e)=>{if(e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageDown or Key.PageUp) SeekRequested?.Invoke(SeekSlider.Value);};
        VolumeSlider.ValueChanged+=(_,_)=> {if(!updating) VolumeRequested?.Invoke(VolumeSlider.Value);};
        MouseWheelEventHandler adjustMusicVolume=(_,e)=>{if(page=="music" && expanded && track.Available && e.Delta!=0){VolumeSlider.Value=Math.Clamp(VolumeSlider.Value+Math.Sign(e.Delta)*.05,0,1);e.Handled=true;}};
        VolumeButton.PreviewMouseWheel+=adjustMusicVolume;VolumePanel.PreviewMouseWheel+=adjustMusicVolume;
        IsVisibleChanged+=(_,_)=> {if(!IsVisible){StopFrames();UpdateLyricsActivity();}else UpdateActivity();};
        DpiChanged+=(_,_)=> {lastRegion=DrawingRect.Empty;lastLocation=new(int.MinValue,int.MinValue);SetViewport();ApplyGeometry();};
        Closed+=(_,_)=>{closing=true;CancelLyrics();lyricsClient.Dispose();StopFrames();};
    }
    private IntPtr Hook(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled)
    {
        if(message==0x7b){handled=true;return IntPtr.Zero;}
        if(message==0x21 && page!="settings") {handled=true;return new(3);} // MA_NOACTIVATE, controls still receive click.
        if(message==0x1a) {systemMotion=Native.MotionAllowed();Dispatcher.BeginInvoke((Action)UpdateActivity);}
        if(message==0x7e || message==0x2e0) Dispatcher.BeginInvoke((Action)(()=>{lastRegion=DrawingRect.Empty;ApplyGeometry();}));
        NativeMessage?.Invoke(hwnd,message,w,l);return IntPtr.Zero;
    }
    public void SetAnchor(double rightPhysical,double topPhysical) {placementMonitor=Monitors.Select(Config.OverlayMonitor);DisplayFrameRate=Native.RefreshRate(placementMonitor.DeviceName);anchorRight=rightPhysical;anchorTop=topPhysical;if(Handle!=IntPtr.Zero)ApplyGeometry();}
    public void Present(bool open,bool instant=false,bool fromIcon=false)
    {
        if(!open){StopPageChange();if(!transition)iconOrigin=!track.Playing;}
        bool wasHidden=!IsVisible;
        if(wasHidden){iconOrigin=fromIcon;spring.Value=0;spring.Velocity=0;}
        bool changed=expanded!=open || wasHidden && open;expanded=open;spring.Target=open?1:0;
        if(!changed && IsVisible) {UpdateActivity();return;}
        if(wasHidden) {SetViewport();ApplyGeometry();Show();}
        if(changed && !instant && Config.Animations && systemMotion) {transition=true;SetViewport();StartFrames();}
        else {spring.Snap();transition=false;SetViewport();ApplyGeometry();}
        UpdateActivity();
    }
    public void Suspend() {StopPageChange();transition=false;spring.Snap();Hide(); Ambient.Suspend(); Levels?.Configure(false,"");}
    public void SetTrack(MusicSnapshot value)
    {
        track=value;TitleText.Text=CompactTitle.Text=value.Title;ArtistText.Text=CompactArtist.Text=value.Artist;
        SourceText.Text=(MusicCatalog.AllowedSite(value.Source)?value.Source.StartsWith("music.yandex")?"Яндекс Музыка":"VK Музыка":value.Source)+(value.Playing?" · Играет":" · На паузе");
        if(!ReferenceEquals(artwork,value.Cover)) {artwork=value.Cover;try {Cover.Source=CompactCover.Source=ArtworkDecoder.Image(artwork);}catch(Exception ex) when(ex is System.IO.IOException or NotSupportedException or System.IO.FileFormatException or ArgumentException) {Cover.Source=CompactCover.Source=null;} }
        homeTitle.Text=value.Available?value.Title:"Ничего не играет";homeArtist.Text=value.Artist;homeCover.Source=Cover.Source;homePlay.Content=new TextBlock{Text=value.Playing?"\uE769":"\uE768",FontFamily=new("Segoe MDL2 Assets"),FontSize=16};
        PlayGlyph.Data=(Geometry)FindResource(value.Playing?"Music_pause":"Music_play");
        PreviousButton.IsEnabled=value.CanPrevious;NextButton.IsEnabled=value.CanNext;BackButton.IsEnabled=ForwardButton.IsEnabled=SeekSlider.IsEnabled=value.CanSeek;
        updating=true;if(value.Volume>=0 && !VolumeSlider.IsMouseCaptureWithin)VolumeSlider.Value=value.Volume;updating=false;UpdateVolumeLabel();
        VolumeButton.IsEnabled=VolumeSlider.IsEnabled=value.CanVolume;
        VolumeButton.ToolTip=!MusicCatalog.AllowedSite(value.Source) && value.AudioProcess.Length>0?"Громкость браузера, включая другие его вкладки":"Громкость музыки";

        Canvas.SetTop(WeatherContent,value.Available?248:62);WeatherDivider.Visibility=value.Available?Visibility.Visible:Visibility.Collapsed;SetPage(page);
        UpdateTimeline();if(IsVisible){SetViewport();ApplyGeometry();}UpdateActivity();
    }
    public void SetLaunch(string status,string title) {ResumeText.Text=string.IsNullOrEmpty(status)?title:status;PlayButton.ToolTip=ResumeButton.ToolTip=string.IsNullOrEmpty(status)?"Воспроизвести / пауза":status;}
    public void SetWeather(WeatherSnapshot weather)
    {
        TemperatureText.Text=weather.TemperatureText;DescriptionText.Text=weather.Description;WeatherSymbol.Set(weather.Available?weather.Code:3);
        FeelsText.Text="Ощущается "+(weather.Available?WeatherSnapshot.Degrees(weather.FeelsLike):"—°");HumidityText.Text="Влажность "+Number(weather.Humidity)+"%";
        WindText.Text="Ветер "+Number(weather.Wind)+" м/с";PressureText.Text=Number(weather.Pressure*.750062)+" мм";
        WeatherContent.ToolTip=weather.IsStale?"Погодные данные устарели":null;
        CompactWeather.Text=weather.TemperatureText;UpdatePagesWeather(weather);
    }
    private static string Number(double value)=>double.IsFinite(value)?value.ToString("0",CultureInfo.InvariantCulture):"—";
    public void UpdateClock() {var now=DateTime.Now;var culture=CultureInfo.GetCultureInfo("ru-RU");string date=now.ToString("dddd, d MMMM yyyy",culture);DateText.Text=page=="music"?"Музыка":page=="weather"?"Погода":page=="settings"?"Настройки":"Главная";headerTime.Text=ClockText.Text=CompactClock.Text=now.ToString("HH:mm");}
    public void UpdateTimeline()
    {
        if(!SeekSlider.IsMouseCaptureWithin) {updating=true;SeekSlider.Maximum=Math.Max(1,track.DurationSeconds);SeekSlider.Value=Math.Clamp(track.PositionSeconds,0,SeekSlider.Maximum);updating=false;}
        UpdateLyricPosition();
        PositionText.Text=Duration(track.PositionSeconds);DurationText.Text=track.DurationSeconds>0?Duration(track.DurationSeconds):"—";
    }
    private static string Duration(double value) {int seconds=(int)Math.Max(0,value);return $"{seconds/60}:{seconds%60:00}";}
    private const double PanelScale=.84;
    private double ExpandedHeight=>512*PanelScale;
    // Keep the layered HWND and its backing surface stable across open/compact states.
    // Only the visible clip and native input region shrink; transparent space never accepts input.
    private void SetViewport()
    {
        var dpi=VisualTreeHelper.GetDpi(this);
        Width=Math.Round(ExpandedWidth*dpi.DpiScaleX)/dpi.DpiScaleX;
        Height=Math.Round(580*PanelScale*dpi.DpiScaleY)/dpi.DpiScaleY;
    }
    private void ApplyGeometry()
    {
        if(Handle==IntPtr.Zero || closing)return;
        double p=Math.Clamp(spring.Value,0,1),width=(iconOrigin?44:368)+(ExpandedWidth-(iconOrigin?44:368))*p,height=(iconOrigin?44:64)+(ExpandedHeight-(iconOrigin?44:64))*p;
        var dpi=VisualTreeHelper.GetDpi(this);
        width=Math.Round(width*dpi.DpiScaleX)/dpi.DpiScaleX;height=Math.Round(height*dpi.DpiScaleY)/dpi.DpiScaleY;
        Canvas.SetLeft(CollapseButton,width/PanelScale-48);Canvas.SetLeft(MenuButton,width/PanelScale-120);Canvas.SetLeft(ExitButton,width/PanelScale-84);Canvas.SetLeft(headerTime,width/PanelScale-180);
        double left=Config.OverlayX>.5?Width-width:0;double top=Config.OverlayY>.5?Height-height:0;offset.X=left;offset.Y=top;Body.Width=GlassRim.Width=width;Body.Height=GlassRim.Height=height;
        clip.Rect=new Rect(left,top,width,height);clip.RadiusX=clip.RadiusY=iconOrigin?22+5*p:27;
        Expanded.Opacity=Math.Clamp((p-.25)/.75,0,1);Expanded.IsHitTestVisible=expanded && p>.85;openingIcon.Visibility=iconOrigin && p<.4?Visibility.Visible:Visibility.Collapsed;openingIcon.Opacity=Math.Clamp(1-p/.4,0,1);Compact.Opacity=iconOrigin?0:1-p;Compact.IsHitTestVisible=!expanded && !iconOrigin;
        Expanded.Visibility=p>.001?Visibility.Visible:Visibility.Hidden;Compact.Visibility=!iconOrigin && p<.999?Visibility.Visible:Visibility.Hidden;
        int windowWidth=(int)Math.Round(Width*dpi.DpiScaleX);
        placementMonitor??=Monitors.Select(Config.OverlayMonitor);var screen=placementMonitor.WorkingArea;
        int x=(int)Math.Round(Math.Max(screen.Left+4,Math.Min(screen.Right-windowWidth-4,Config.OverlayX>.5?anchorRight-windowWidth:anchorRight))),y=(int)Math.Round(Math.Max(screen.Top+4,Math.Min(screen.Bottom-Height*dpi.DpiScaleY-4,Config.OverlayY>.5?anchorTop-Height*dpi.DpiScaleY:anchorTop)));
        var location=new System.Drawing.Point(x,y);if(location!=lastLocation){OverlayNative.Place(Handle,x,y);lastLocation=location;}
        var region=new DrawingRect((int)Math.Round(left*dpi.DpiScaleX),(int)Math.Round(top*dpi.DpiScaleY),(int)Math.Round(width*dpi.DpiScaleX),(int)Math.Round(height*dpi.DpiScaleY));
        if(region!=lastRegion) {OverlayNative.Shape(Handle,region,(int)(27*dpi.DpiScaleX));lastRegion=region;}
        BodyOnScreen=new DrawingRect(x+region.X,y+region.Y,region.Width,region.Height);
    }
    private bool EffectWanted=>IsVisible && !expanded && track.Available && track.Playing && Config.Visual.Enabled && Config.MusicMotion && Config.Animations && systemMotion;
    public void UpdateActivity()
    {
        UpdateLyricsActivity();

        string process=string.IsNullOrEmpty(Config.Visual.Process)?MusicLevels.ProcessFor(track):Config.Visual.Process;
        Levels?.Configure(EffectWanted,process,string.IsNullOrEmpty(Config.Visual.Process)?track.AudioProcessId:0);
        if(!Config.Animations || !systemMotion){syncedLyrics.Settle();if(pageMorph){StopPageChange();SetViewport();ApplyGeometry();}if(transition){spring.Snap();transition=false;SetViewport();ApplyGeometry();TransitionSettled?.Invoke();}StopFrames();return;}
        if(IsVisible && (transition || pageMorph || syncedLyrics.Animating || EffectWanted || effectOpacity>.005 || Ambient.NeedsFrames)) StartFrames(); else StopFrames();
    }
    private void StartFrames()
    {
        if(frameAttached || !IsVisible || closing)return;
        frameAttached=true;lastFrame=time.Elapsed.TotalSeconds;
        CompositionTarget.Rendering+=Render;
    }
    private void StopFrames() {syncedLyrics.Settle();if(frameAttached){frameAttached=false;CompositionTarget.Rendering-=Render;}effectOpacity=0;smoothed=default;Ambient.Rest();Bars.Update(default);}
    private void Render(object sender,EventArgs e)
    {
        CallbackCount++;
        long start=Stopwatch.GetTimestamp();double now=time.Elapsed.TotalSeconds,dt=Math.Min(.1,Math.Max(.0001,now-lastFrame));lastFrame=now;FrameCount++;
        if(transition) {transition=spring.Step(dt);ApplyGeometry();if(!transition){SetViewport();ApplyGeometry();TransitionSettled?.Invoke();UpdateActivity();}}
        if(pageMorph)pageMorph=pageFader.Step(dt);
        if(syncedLyrics.Animating)syncedLyrics.Step(dt);
        var audio=Levels?.Read()??default; if((Stopwatch.GetTimestamp()-audio.Stamp)/(double)Stopwatch.Frequency>.3)audio=default;
        effectOpacity=AudioMath.Follow(effectOpacity,EffectWanted?1:0,dt,.16,.16);
        smoothed.Energy=AudioMath.Follow(smoothed.Energy,audio.Energy,dt,.035,.18);smoothed.Bass=AudioMath.Follow(smoothed.Bass,audio.Bass,dt,.04,.18);smoothed.Mid=AudioMath.Follow(smoothed.Mid,audio.Mid,dt,.06,.2);smoothed.High=AudioMath.Follow(smoothed.High,audio.High,dt,.04,.16);smoothed.Beat=AudioMath.Follow(smoothed.Beat,audio.Beat,dt,.035,.14);
        phase+=dt*(.34+smoothed.Mid*.45+smoothed.High*.18)*Config.Visual.Speed/100;
        if(Compact.Visibility==Visibility.Visible)Bars.Update(smoothed);
        Diagnostics?.Frame(start,dt,transition);if(!transition && !pageMorph && !syncedLyrics.Animating && !EffectWanted && effectOpacity<.005 && !Ambient.NeedsFrames)StopFrames();
    }
}
