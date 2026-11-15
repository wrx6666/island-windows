using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Island;
public sealed class HotkeyBox : TextBox
{
    public int Binding {get;private set;}
    public HotkeyBox(int value)
    {
        SetResourceReference(StyleProperty,typeof(TextBox));Binding=value;Text=KeyBinding.Label(value);IsReadOnly=true;Padding=new(8);Margin=new(0,4,0,4);MinWidth=220;ToolTip="Нажми одну клавишу или сочетание. Кнопка × очищает бинд.";
        PreviewKeyDown+=(_,e)=>{var key=e.Key==Key.System?e.SystemKey:e.Key;int vk=KeyInterop.VirtualKeyFromKey(key);int modifiers=(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)?KeyBinding.Control:0)|(Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)?KeyBinding.Alt:0)|(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?KeyBinding.Shift:0);if(!Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) && KeyBinding.Valid(vk|modifiers)){Binding=vk|modifiers;Text=KeyBinding.Label(Binding);}e.Handled=true;};
    }
    public void ClearBinding(){Binding=0;Text=KeyBinding.Label(0);}
}
public sealed class SettingsWindow : Window
{
    private readonly App app;
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(255,181,196)),Margin=new(0,8,0,8)};
    private readonly CheckBox floating,hover,hideFull,allowPin,animations,motion,visualEnabled,launchOnPlay,restore,autoPlay,startup;
    private readonly HotkeyBox musicKey,muteKey,overlayKey;
    private readonly ComboBox monitor,mode,palette,browser,service,apps,visualProcess;
    private readonly Slider x,y,delay,intensity,sensitivity,speed,appVolume,masterVolume;
    private readonly bool startupInitial;
    private bool loaded,masterBusy,appVolumeBusy;
    private float? nextMaster,nextApp;
    public SettingsWindow(App application,Action dismiss=null)
    {
        app=application;Title="Island · Настройки WPF";Width=610;Height=730;MinWidth=560;MinHeight=540;Background=new SolidColorBrush(Color.FromRgb(27,27,34));Foreground=Brushes.White;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var outer=new DockPanel{Margin=new(0)};Content=outer;outer.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/Island.Wpf;component/SettingsResources.xaml",UriKind.Relative)});var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);outer.Children.Add(bottom);bottom.Children.Add(status);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};bottom.Children.Add(actions);var save=new Button{Content="Применить",Padding=new(18,8,18,8),Margin=new(5),Background=new SolidColorBrush(Color.FromRgb(70,56,68))};var close=new Button{Content="Закрыть",Padding=new(18,8,18,8),Margin=new(5)};actions.Children.Add(save);actions.Children.Add(close);close.Click+=(_,_)=>{if(dismiss!=null)dismiss();else Close();};save.Click+=Apply;
        var shell=new Grid();shell.ColumnDefinitions.Add(new(){Width=new GridLength(148)});shell.ColumnDefinitions.Add(new());outer.Children.Add(shell);
        var nav=new StackPanel{Margin=new(0,0,12,0)};shell.Children.Add(nav);var host=new CategoryHost{MotionAllowed=()=>app.Config.Animations && Native.MotionAllowed()};Grid.SetColumn(host,1);shell.Children.Add(host);
        StackPanel Tab(string name){var stack=new StackPanel{Margin=new(16)};var scroll=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};var card=new Border{CornerRadius=new(22),Background=new SolidColorBrush(Color.FromRgb(24,24,26)),Child=scroll};var button=new Button{Content=name,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new(12,10,12,10),Margin=new(0,2,0,2),Background=Brushes.Transparent,Foreground=Brushes.White,BorderThickness=new(0),FontSize=12.5};nav.Children.Add(button);button.Click+=(_,_)=>{host.Select(card);foreach(Button item in nav.Children)item.Background=Brushes.Transparent;button.Background=new SolidColorBrush(Color.FromRgb(42,42,44));};if(host.Children.Count==0){host.Select(card);button.Background=new SolidColorBrush(Color.FromRgb(42,42,44));}return stack;}
        var music=Tab("Музыка и звук");Label(music,"Глобальные клавиши · можно назначить одну клавишу");musicKey=Binding(music,"Пауза / продолжить",app.Config.MusicHotkey);muteKey=Binding(music,"Звук выбранного приложения",app.Config.MuteHotkey);overlayKey=Binding(music,"Открыть / закрыть островок",app.Config.OverlayHotkey);
        Label(music,"Приложение для отключения звука");apps=Combo(music,new[]{app.Config.AudioProcess});apps.IsEditable=true;apps.Text=app.Config.AudioProcess;var refresh=new Button{Content="Обновить список звуковых приложений",FontSize=14,Padding=new(8),HorizontalAlignment=HorizontalAlignment.Left};music.Children.Add(refresh);refresh.Click+=async(_,_)=>await RefreshApps();
        Label(music,"Громкость выбранного приложения");appVolume=Range(music,0,100,100);appVolume.ValueChanged+=async(_,_)=>{if(!loaded)return;nextApp=(float)appVolume.Value/100;await SetAppVolume();};
        Label(music,"Общая громкость Windows (текущее устройство вывода)");masterVolume=Range(music,0,100,100);masterVolume.ValueChanged+=async(_,_)=>{if(!loaded)return;nextMaster=(float)masterVolume.Value/100;await SetMaster();};
        var display=Tab("Оверлей");floating=Check(display,"Плавающая иконка",app.Config.FloatingIcon);hover=Check(display,"Открывать по наведению (иначе по нажатию)",app.Config.OverlayHover);hideFull=Check(display,"Скрывать в развёрнутых и полноэкранных приложениях",app.Config.OverlayAutoHide);allowPin=Check(display,"Разрешить закрепление панели нажатием",app.Config.OverlayAllowPin);animations=Check(display,"Плавное раскрытие",app.Config.Animations);motion=Check(display,"Музыкальная подсветка в движении",app.Config.MusicMotion);
        Label(display,"Монитор");var monitors=Monitors.All;monitor=Combo(display,monitors);monitor.SelectedItem=monitors.FirstOrDefault(m=>m.DeviceName==app.Config.OverlayMonitor)??monitors.FirstOrDefault(m=>m.Primary);
        Label(display,"Положение по горизонтали (слева → справа)");x=Range(display,0,1,app.Config.OverlayX);Label(display,"Положение по вертикали (сверху → снизу)");y=Range(display,0,1,app.Config.OverlayY);Label(display,"Задержка сворачивания, мс");delay=Range(display,300,500,app.Config.OverlayDelay);delay.TickFrequency=50;delay.IsSnapToTickEnabled=true;
        Label(display,"Иконку также можно перетащить мышью. Поверх Exclusive Fullscreen Windows может не показать панель; инъекции не используются.");
        var visual=Tab("Подсветка");visualEnabled=Check(visual,"Включить эффект",app.Config.Visual.Enabled);Label(visual,"Режим");mode=Combo(visual,new[]{"Плавные переливы","RGB-обводка","Комбинированный"});mode.SelectedIndex=(int)app.Config.Visual.Mode;Label(visual,"Интенсивность");intensity=Range(visual,0,100,app.Config.Visual.Intensity);Label(visual,"Чувствительность к ударам");sensitivity=Range(visual,25,200,app.Config.Visual.Sensitivity);Label(visual,"Скорость переливов");speed=Range(visual,25,200,app.Config.Visual.Speed);Label(visual,"Палитра");palette=Combo(visual,new[]{"Из обложки","Фиолетовый / мятный","Тёплый / розовый","Голубой / сиреневый"});palette.SelectedIndex=app.Config.Visual.Palette;Label(visual,"Процесс для аудиоанализа (пусто = музыкальный источник)");visualProcess=Combo(visual,new[]{app.Config.Visual.Process});visualProcess.IsEditable=true;visualProcess.Text=app.Config.Visual.Process;Label(visual,app.Levels.Status);
        var launch=Tab("Запуск");Label(launch,"Музыкальный сервис");service=Combo(launch,new[]{"Яндекс Музыка","VK Музыка"});service.SelectedIndex=app.Config.Launch.Service=="vk"?1:0;Label(launch,"Браузер");browser=Combo(launch,new[]{"По умолчанию Windows","Яндекс Браузер","Chrome","Edge"});browser.SelectedIndex=Array.IndexOf(new[]{"default","yandex","chrome","edge"},app.Config.Launch.Browser);launchOnPlay=Check(launch,"Запускать сервис по Play",app.Config.Launch.OnPlay);restore=Check(launch,"Продолжать последнюю доступную сессию",app.Config.Launch.Restore);autoPlay=Check(launch,"Автоматически включать музыку после входа в Windows",app.Config.Launch.AutoPlay);
        startupInitial=Startup.Enabled;startup=Check(launch,"Запускать Island вместе с Windows",startupInitial);Label(launch,"Настройки сохраняются при обновлении Island. Точная очередь и позиция трека зависят от музыкального сервиса.");
        Label(launch,"Расширение: "+(app.Bridge.Connected?"подключено":"нет соединения")+" · "+(app.Music.HasControlSession?"плеер обнаружен":"плеер не обнаружен"));
        Label(launch,"Для запуска музыки в закрытой вкладке нужен включённый Island Music Bridge. Без расширения доступно управление уже существующим плеером Windows. Расширение из режима разработчика браузер может отключать: Island не меняет защитные настройки браузера.");
        var bridgeFolder=new Button{Content="Папка Music Bridge",HorizontalAlignment=HorizontalAlignment.Left};launch.Children.Add(bridgeFolder);
        bridgeFolder.Click+=(_,_)=>OpenExtensionFolder(status);
        var extensions=new Button{Content="Расширения браузера",HorizontalAlignment=HorizontalAlignment.Left};launch.Children.Add(extensions);
        extensions.Click+=async(_,_)=>{try{await BrowserLaunch.OpenExtensions(new[]{"default","yandex","chrome","edge"}[Math.Max(0,browser.SelectedIndex)]);}catch(Exception ex){status.Text=ex.Message;}};
        Label(launch,"Погодные данные: Open-Meteo · open-meteo.com");
        var updates=Tab("Обновления");Label(updates,"Island · "+app.Updates.CurrentVersion);
        var updateStatus=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=13,Margin=new(0,12,0,12)};updates.Children.Add(updateStatus);
        var automatic=Check(updates,"Проверять обновления автоматически",app.Updates.Automatic);
        var checkUpdate=new Button{Content="Проверить обновления",FontSize=13,Padding=new(12,8,12,8),HorizontalAlignment=HorizontalAlignment.Left};updates.Children.Add(checkUpdate);
        var installUpdate=new Button{Content="Загрузить и перезапустить",FontSize=13,Padding=new(12,8,12,8),Margin=new(0,10,0,0),HorizontalAlignment=HorizontalAlignment.Left};updates.Children.Add(installUpdate);
        void UpdateStatus(){updateStatus.Text=app.Updates.Status;checkUpdate.IsEnabled=app.Updates.CanCheck && !app.Updates.Busy;installUpdate.Visibility=app.Updates.AvailableVersion.Length>0?Visibility.Visible:Visibility.Collapsed;installUpdate.IsEnabled=!app.Updates.Busy;}
        updates.IsVisibleChanged+=(_,_)=>UpdateStatus();
        Action changed=UpdateStatus;app.Updates.Changed+=changed;Closed+=(_,_)=>app.Updates.Changed-=changed;
        automatic.Click+=async(_,_)=>await app.Updates.SetAutomaticAsync(automatic.IsChecked==true);checkUpdate.Click+=async(_,_)=>await app.Updates.CheckAsync();installUpdate.Click+=async(_,_)=>await app.Updates.DownloadAndRestartAsync();UpdateStatus();
        Label(updates,"Обновление устанавливается только по нажатию кнопки. Island перезапустится, настройки сохранятся.");
        Label(updates,"Музыка в браузере");Label(updates,"Для Яндекс Музыки и VK включите Island Music Bridge в браузере: Расширения → Режим разработчика → Загрузить распакованное.");
        var extensionFolder=new Button{Content="Открыть папку расширения",FontSize=13,Padding=new(12,8,12,8),HorizontalAlignment=HorizontalAlignment.Left};updates.Children.Add(extensionFolder);
        extensionFolder.Click+=(_,_)=>OpenExtensionFolder(updateStatus);
        outer.Loaded+=async(_,_)=>{try{masterVolume.Value=await EndpointVolume.Level();masterVolume.Value*=100;await RefreshApps();}catch(Exception ex){status.Text=ex.Message;}finally{loaded=true;}};
        apps.SelectionChanged+=async(_,_)=>{if(!loaded)return;try{float level=await app.Mixer.MusicVolume(apps.Text,null);if(level>=0){loaded=false;appVolume.Value=level*100;loaded=true;}}catch(Exception ex){status.Text=ex.Message;}};
    }
    private static void Label(Panel panel,string text)=>panel.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new(0,10,0,4),FontSize=13});
    private void OpenExtensionFolder(TextBlock message)
    {
        try{AppDistribution.PrepareExtension();string folder=AppDistribution.ExtensionDirectory;if(System.IO.Directory.Exists(folder))System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe"){ArgumentList={folder},UseShellExecute=true});else message.Text="Папка расширения поставляется с установщиком Island.";}
        catch(Exception ex)when(ex is System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){message.Text=ex.Message;}
    }
    private static CheckBox Check(Panel panel,string text,bool value){var c=new CheckBox{Content=text,IsChecked=value};panel.Children.Add(c);return c;}
    private static ComboBox Combo(Panel panel,System.Collections.IEnumerable values){var c=new ComboBox{ItemsSource=values,SelectedIndex=0,Padding=new(7),Margin=new(0,4,0,4)};panel.Children.Add(c);return c;}
    private static Slider Range(Panel panel,double min,double max,double value){var s=new Slider{Style=(Style)new ResourceDictionary{Source=new Uri("/Island.Wpf;component/GlassResources.xaml",UriKind.Relative)}["GlassSlider"],Minimum=min,Maximum=max,Value=value,Margin=new(8,0,8,0),SmallChange=(max-min)/100,LargeChange=(max-min)/10};panel.Children.Add(s);return s;}
    private static HotkeyBox Binding(Panel parent,string title,int value){Label(parent,title);var row=new DockPanel();parent.Children.Add(row);var clear=new Button{Content="×",Width=32};DockPanel.SetDock(clear,Dock.Right);row.Children.Add(clear);var box=new HotkeyBox(value);row.Children.Add(box);clear.Click+=(_,_)=>box.ClearBinding();return box;}
    private async Task RefreshApps(){var names=(await app.Mixer.Apps()).Select(a=>a.ProcessName).Append(app.Config.AudioProcess).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();string current=apps.Text;apps.ItemsSource=names;apps.Text=current;}
    private async Task SetMaster(){if(masterBusy)return;masterBusy=true;try{while(nextMaster.HasValue){float value=nextMaster.Value;nextMaster=null;await EndpointVolume.Level(value);}}catch(Exception ex){status.Text=ex.Message;}finally{masterBusy=false;}}
    private async Task SetAppVolume(){if(appVolumeBusy)return;appVolumeBusy=true;try{while(nextApp.HasValue){float value=nextApp.Value;nextApp=null;await app.Mixer.MusicVolume(apps.Text,value);}}catch(Exception ex){status.Text=ex.Message;}finally{appVolumeBusy=false;}}
    private async void Apply(object sender,RoutedEventArgs e)
    {
        try
        {
            string error=app.ConfigureHotkeys(musicKey.Binding,muteKey.Binding,overlayKey.Binding);if(error!=null){status.Text=error;return;}
            if(apps.Text!=app.Config.AudioProcess)await app.Mixer.RestoreBeforeChange();
            var s=app.Config;s.MusicHotkey=musicKey.Binding;s.MuteHotkey=muteKey.Binding;s.OverlayHotkey=overlayKey.Binding;s.AudioProcess=apps.Text.Trim();
            s.FloatingIcon=floating.IsChecked==true;s.OverlayHover=hover.IsChecked==true;s.OverlayAutoHide=hideFull.IsChecked==true;s.OverlayAllowPin=allowPin.IsChecked==true;s.Animations=animations.IsChecked==true;s.MusicMotion=motion.IsChecked==true;s.OverlayMonitor=(monitor.SelectedItem as DisplayMonitor)?.DeviceName??"";s.OverlayX=x.Value;s.OverlayY=y.Value;s.OverlayDelay=(int)delay.Value;
            s.Visual=new(){Enabled=visualEnabled.IsChecked==true,Mode=(VisualMode)mode.SelectedIndex,Intensity=(int)intensity.Value,Sensitivity=(int)sensitivity.Value,Speed=(int)speed.Value,Palette=palette.SelectedIndex,Process=visualProcess.Text.Trim()};
            s.Launch=new(){Service=service.SelectedIndex==1?"vk":"yandex",Browser=new[]{"default","yandex","chrome","edge"}[Math.Max(0,browser.SelectedIndex)],OnPlay=launchOnPlay.IsChecked==true,Restore=restore.IsChecked==true,AutoPlay=autoPlay.IsChecked==true};
            if(startup.IsChecked!=startupInitial)Startup.Set(startup.IsChecked==true);
            await s.SaveAsync();app.SettingsApplied();status.Text="Настройки сохранены";
        }
        catch(Exception ex){status.Text="Не удалось применить: "+ex.Message;}
    }
}
