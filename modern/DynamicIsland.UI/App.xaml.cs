using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Island;
public partial class App : Application
{
    private Mutex instance;
    private bool ownsMutex,stopping,isolated;
    public IslandWindow Panel {get;private set;}
    public Settings Config {get;private set;}
    public OverlayController Overlay {get;private set;}
    public MusicService Music {get;private set;}
    public BrowserMusic Bridge {get;private set;}
    public MusicLaunch Launch {get;private set;}
    public AudioMixer Mixer {get;private set;}
    public MusicLevels Levels {get;private set;}
    public AppUpdates Updates {get;private set;}
    private WeatherService weather;
    private Hotkeys hotkeys;
    private TrayIcon tray;
    private OneShot minute,timeline,volumeDelay;
    private float? requestedVolume;
    private bool volumeBusy;
    private SettingsWindow settingsWindow;
    private string dataDirectory;
    private bool scenario;
    public bool DiagnosticHost {get;set;}
    public Func<App,string[],Task> DiagnosticRunner {get;set;}
    public void Dispatch(Action action) {if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(action,DispatcherPriority.Normal);}
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            scenario=DiagnosticHost && !e.Args.Contains("--live-check");isolated=e.Args.Contains("--isolated") || scenario;
            dataDirectory=Path.GetFullPath(Value(e.Args,"--data")??(!isolated && AppDistribution.Installed?AppDistribution.DataDirectory:Path.Combine(AppContext.BaseDirectory,"data")));Directory.CreateDirectory(dataDirectory);
            Settings.FilePath=Path.Combine(dataDirectory,"island.settings");
            if(!isolated && AppDistribution.Installed){Settings.Import(Path.Combine(AppContext.BaseDirectory,"data"),dataDirectory);Island.Startup.Executable=AppDistribution.StableLauncher;}
            if(!isolated)Settings.Import(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Island"),dataDirectory);
            Config=Settings.Load();Config.OverlayPinned=false;
            Updates=new(Config,dataDirectory,Stop);
            if(isolated)BrowserProtocol.TestSuffix=".test."+Guid.NewGuid().ToString("N");
            instance=new Mutex(false,"Local\\Island.Desktop.wrx666.v1"+(isolated?BrowserProtocol.TestSuffix:""));try{ownsMutex=instance.WaitOne(0);}catch(AbandonedMutexException){ownsMutex=true;}
            if(!ownsMutex){Native.PostMessage(Native.HWND_BROADCAST,Native.ShowMessage,IntPtr.Zero,IntPtr.Zero);Shutdown();return;}
            Panel=new IslandWindow{Config=Config}; MainWindow=Panel;new WindowInteropHelper(Panel).EnsureHandle();
            if(e.Args.Contains("--diagnostics") || DiagnosticHost)Panel.Diagnostics=new();
            Levels=new MusicLevels();Mixer=new AudioMixer();Panel.Levels=Levels;
            var decoder=new ArtworkDecoder();Bridge=new BrowserMusic(Dispatch,decoder);Music=new MusicService(Dispatch,Bridge,decoder);Launch=new MusicLaunch(Music,Config,BrowserLaunch.IsRunning,BrowserLaunch.Start);
            weather=new WeatherService(Dispatch);weather.Changed+=()=>Panel.SetWeather(weather.Current);
            minute=new(Dispatch,ClockTick);timeline=new(Dispatch,TimelineTick);volumeDelay=new(Dispatch,ApplyVolume){Interval=60};
            Overlay=new OverlayController(Panel,Config,Dispatch,!scenario,!scenario);Overlay.PositionChanged+=Save;Overlay.VisibilityChanged+=VisibilityChanged;
            hotkeys=new Hotkeys(Panel.Handle);if(!isolated){string error=hotkeys.Configure(Config.MusicHotkey,Config.MuteHotkey,Config.OverlayHotkey);if(error!=null)Log("Hotkeys: "+error);}
            tray=new TrayIcon(Panel.Handle);
            Updates.UpdateAvailable+=version=>tray.Notify("Доступна Island "+version+". Откройте настройки → Обновления, чтобы загрузить.");
            Panel.NativeMessage+=(_,message,w,l)=>{if(message==0x312){if(w.ToInt32()==Hotkeys.MusicId)Play();else if(w.ToInt32()==Hotkeys.MuteId)Mute();else if(w.ToInt32()==Hotkeys.OverlayId)Overlay.Toggle();}else if(message==Native.ShowMessage)Overlay.Open(true);else if(message==TrayIcon.CallbackMessage){int action=l.ToInt32()&0xffff;if(action==0x203)Overlay.Toggle();else if(action==0x205)ShowMenu();}else if(message==0x7e){Overlay.Reposition();Overlay.Watcher?.Refresh();}};
            Panel.TogglePlay+=Play;Panel.Previous+=()=>Skip(false);Panel.Next+=()=>Skip(true);Panel.SeekRequested+=Seek;Panel.VolumeRequested+=v=>{requestedVolume=(float)v;volumeDelay.Start();};Panel.ExitRequested+=()=>_=Stop();
            Music.Changed+=MusicChanged;Launch.Changed+=()=>Panel.SetLaunch(Launch.Status,Launch.Last.Title);
            Panel.SetLaunch("",Launch.Last.Available?Launch.Last.Title+" · "+Launch.Last.Artist:"Яндекс Музыка / VK");Panel.SetWeather(weather.Current);Panel.SetTrack(Music.Current);Panel.UpdateClock();VisibilityChanged();
            DispatcherUnhandledException+=(_,args)=>{Log(args.Exception.ToString());tray?.Notify("Ошибка: "+args.Exception.Message);args.Handled=true;};
            if(DiagnosticRunner!=null){await DiagnosticRunner(this,e.Args);await Stop();return;}
            Music.Start();Updates.Start();
            if(AppDistribution.FirstRun)ShowSettings();
            if(e.Args.Contains("--open"))Overlay.Open(true);
            if(e.Args.Contains("--settings"))ShowSettings();
            if(e.Args.Contains("--startup") && Config.Launch.AutoPlay)await Launch.Play();
        }
        catch(Exception ex) {Log(ex.ToString());if(!scenario)MessageBox.Show(ex.Message,"Island WPF Preview");await Stop();}
    }
    private static string Value(string[] args,string key){int i=Array.IndexOf(args,key);return i>=0 && i+1<args.Length?args[i+1]:null;}
    private void VisibilityChanged()
    {
        if(Panel.IsVisible){if(!scenario)weather.Resume();ClockTick();if(Panel.ExpandedState && Music.Current.Playing)TimelineTick();else timeline.Stop();}
        else {weather.Pause();minute.Stop();timeline.Stop();}
        if(!scenario)Music.SetArtworkActive(Panel.IsVisible);
    }
    private void ClockTick(){Panel.UpdateClock();minute.Interval=(int)Math.Max(1000,60000-DateTime.Now.TimeOfDay.TotalMilliseconds%60000);minute.Start();}
    private void TimelineTick(){Panel.UpdateTimeline();if(Panel.IsVisible && Panel.ExpandedState && Music.Current.Playing){timeline.Interval=1000;timeline.Start();}}
    private void MusicChanged(){if(stopping)return;Launch.Observe(Music.Current);Panel.SetTrack(Music.Current);Overlay.Playing=Music.Current.Available && Music.Current.Playing;Overlay.Reconcile();VisibilityChanged();}
    private async void Play(){Panel.Diagnostics?.Input();try{bool ok=Music.HasControlSession?await Music.Toggle():await Launch.Play();if(!ok)tray.Notify(string.IsNullOrEmpty(Launch.Failure)?Music.CommandFailure:Launch.Failure);}catch(Exception ex){tray.Notify(ex.Message);}}
    private async void Skip(bool next){Panel.Diagnostics?.Input();if(!await Music.Skip(next))tray.Notify(Music.CommandFailure);}
    private async void Seek(double seconds){if(!await Music.Seek(seconds))tray.Notify(Music.CommandFailure);}
    private async void Mute(){try{tray.Notify(await Mixer.Toggle(Config.AudioProcess));}catch(Exception ex){tray.Notify(ex.Message);}}
    private async void ApplyVolume()
    {
        if(volumeBusy)return;volumeBusy=true;
        try {while(requestedVolume.HasValue){float value=requestedVolume.Value;requestedVolume=null;if(Music.IsBrowser && Music.Current.CanVolume){if(!await Music.SetVolume(value))tray.Notify(Music.CommandFailure);}else await Mixer.MusicVolume(MusicLevels.ProcessFor(Music.Current),value);}}
        catch(Exception ex){tray.Notify(ex.Message);}finally{volumeBusy=false;}
    }
    public string ConfigureHotkeys(int music,int mute,int overlay) => isolated?"В изолированной проверке глобальные клавиши отключены.":hotkeys.Configure(music,mute,overlay);
    public async void Save(){try{await Config.SaveAsync();}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){tray?.Notify("Не удалось сохранить настройки: "+ex.Message);}}
    public void ShowSettings()
    {
        if(scenario){if(settingsWindow!=null)return;settingsWindow=new SettingsWindow(this){ShowActivated=false};settingsWindow.Closed+=(_,_)=>settingsWindow=null;settingsWindow.Show();return;}
        Panel.OpenSettingsPage();Overlay.Open(true,true);
    }
    public void ToggleAppMute()=>Mute();
    public void RefreshWeather(){weather.ForceRefresh();}
    public void SettingsApplied(){Panel.SetTrack(Music.Current);Overlay.RefreshSettings();Overlay.Open(true,true);}
    private void ShowMenu()
    {
        Overlay.Busy=true;var menu=new ContextMenu();void Item(string text,Action action){var item=new MenuItem{Header=text};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Item("Открыть / свернуть",Overlay.Toggle);Item(Overlay.Pinned?"Открепить":"Закрепить",()=>{Overlay.Open(false);Overlay.TogglePin();});Item("Настройки…",ShowSettings);Item("Проверить обновления",()=>{ShowSettings();_=Updates.CheckAsync();});Item("Музыка: пауза / продолжить",Play);Item("Звук "+AudioApp.Friendly(Config.AudioProcess),Mute);Item("Скрыть островок",()=>Overlay.SetEnabled(false));Item("Показать островок",()=>Overlay.SetEnabled(true));menu.Items.Add(new Separator());Item("Выход",()=>_=Stop());
        menu.Closed+=(_,_)=>Overlay.Busy=settingsWindow!=null;menu.IsOpen=true;
    }
    public async Task Stop()
    {
        if(stopping)return;stopping=true;Updates?.Dispose();settingsWindow?.Close();hotkeys?.Dispose();Overlay?.Dispose();minute?.Dispose();timeline?.Dispose();volumeDelay?.Dispose();weather?.Dispose();Music?.Dispose();if(Launch!=null)await Launch.DisposeAsync();if(Levels!=null)await Task.Run(Levels.Dispose);tray?.Dispose();
        if(Mixer!=null)await Mixer.DisposeAsync();try{await Settings.FlushAsync();}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Log(ex.ToString());}Panel?.Close();if(ownsMutex){instance.ReleaseMutex();ownsMutex=false;}instance?.Dispose();Shutdown();
    }
    private void Log(string text){try{File.AppendAllText(Path.Combine(dataDirectory??AppContext.BaseDirectory,"island.log"),DateTime.Now.ToString("O")+" "+text+Environment.NewLine);}catch(IOException){} }
}
