using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island;
public static class WpfScenarios
{
    public static async Task Run(App app,string directory,bool quick=false)
    {
        await LyricsChecks.Run();
        CheckWindowActions(app);
        Directory.CreateDirectory(directory);var results=new List<object>();var panel=app.Panel;var overlay=app.Overlay;var diag=panel.Diagnostics;
        async Task Measure(string name,int ms){var result=await diag.Measure(name,ms,panel,overlay.State);results.Add(result);File.WriteAllText(Path.Combine(directory,"metrics.json"),new JsonCodec().Serialize(results));}
        int phase=quick?2000:8000;
        var cover=new DrawingVisual();using(var dc=cover.RenderOpen()){dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(24,132,176)),null,new Rect(0,0,96,96));dc.DrawEllipse(Brushes.MidnightBlue,null,new Point(48,60),25,40);}
        var bitmap=new RenderTargetBitmap(96,96,96,96,PixelFormats.Pbgra32);bitmap.Render(cover);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var memory=new MemoryStream();encoder.Save(memory);
        var track=new MusicSnapshot {Title="de_survivor",Artist="onda andar",Source="music.yandex.ru",Playing=true,CanPrevious=true,CanNext=true,CanSeek=true,Volume=.57f,Cover=memory.ToArray(),Color=AccentColor.FromArgb(74,179,224),SecondaryColor=AccentColor.FromArgb(130,119,220)};track.SetTimeline(84,236,1);
        await Task.Delay(1800);await Measure("idle-icon",phase);
        overlay.Open(false);await Task.Delay(80);Save(panel,Path.Combine(directory,"opening-icon.png"));if(!panel.Rendering)throw new Exception("Icon opening must animate");await Task.Delay(600);overlay.Close(true);await Task.Delay(650);
        app.Music.DiagnosticTrack(track);await Task.Delay(700);Save(panel,Path.Combine(directory,"compact.png"));await Measure("compact-playing-synthetic-no-audio",phase);
        var motion=(Spring)typeof(IslandWindow).GetField("spring",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(panel);
        overlay.Open(false);await Task.Delay(65);
        for(int reverse=0;reverse<8;reverse++){
            double position=motion.Value,velocity=motion.Velocity;var shape=panel.BodyOnScreen;
            if(reverse%2==0)overlay.Close(false);else overlay.Open(false);
            if(Math.Abs(motion.Value-position)>1e-9 || Math.Abs(motion.Velocity-velocity)>1e-9)throw new Exception($"Reversal {reverse} reset spring: {position}/{velocity} -> {motion.Value}/{motion.Velocity}; visible={panel.IsVisible}, expanded={panel.ExpandedState}, anim={app.Config.Animations}");
            typeof(IslandWindow).GetMethod("ApplyGeometry",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(panel,null);
            if(panel.BodyOnScreen!=shape)throw new Exception($"Reversal {reverse} changed geometry before advancing time: {shape} -> {panel.BodyOnScreen}; motion={position}/{velocity} -> {motion.Value}/{motion.Velocity}");
            await Task.Delay(65);
        }
        overlay.Open(true);await Task.Delay(800);Save(panel,Path.Combine(directory,"expanded.png"));await Measure("expanded-playing-synthetic-no-audio",phase);
        panel.SetWeather(new WeatherSnapshot{Available=true,Temperature=3,FeelsLike=-2,Humidity=92,Wind=4,Pressure=996,Code=61,Day=true,Uv=1,Rain=2,Sunrise=DateTime.Today.AddHours(7),Sunset=DateTime.Today.AddHours(16),Hourly="12:00  3°\n13:00  4°\n14:00  3°\n15:00  2°"});
        var navigationBounds=OverlayNative.Bounds(panel.Handle);var fixedBody=panel.BodyOnScreen;
        if(panel.ActualWidth>600 || fixedBody.Height>435)throw new Exception("Panel should use reduced dimensions at diagnostic 96 DPI");
        foreach(var page in new[]{"weather","music","settings","home"}){
            if(page=="settings")panel.OpenSettingsPage();else typeof(IslandWindow).GetMethod("SetPage",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(panel,new object[]{page});
            if(OverlayNative.Bounds(panel.Handle)!=navigationBounds)throw new Exception("Navigation resized or moved the native viewport");
            if(page=="music"){
                var lyrics=LyricsDocument.Parse("[00:00]Новый день начинается\n[00:45]Город просыпается\n[01:23]Музыка рядом с тобой\n[02:00]И мы продолжаем путь","");
                typeof(IslandWindow).GetMethod("ApplyLyrics",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(panel,new object[]{lyrics});
                track.PreviewPosition=84;panel.UpdateTimeline();
                var current=(SyncedLyricsView)typeof(IslandWindow).GetField("syncedLyrics",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(panel);
                if(current.CurrentText!="Музыка рядом с тобой")throw new Exception("Lyrics must follow current playback position");
                await Task.Delay(500);
                double lyricOffset=current.Offset;track.PreviewPosition=123;panel.UpdateTimeline();
                if(Math.Abs(current.Offset-lyricOffset)>.000001 || !current.Animating)throw new Exception("Lyrics transition must start smoothly from current position");
                await Task.Delay(45);lyricOffset=current.Offset;track.PreviewPosition=84;panel.UpdateTimeline();
                if(Math.Abs(current.Offset-lyricOffset)>.000001)throw new Exception("Interrupted lyrics transition jumped");
                track.PreviewPosition=123;panel.UpdateTimeline();if(current.CurrentText!="И мы продолжаем путь")throw new Exception("Lyrics did not follow seek");
                track.PreviewPosition=84;panel.UpdateTimeline();
            }
            await Task.Delay(650);
            if(page=="music"){var lyricsView=(SyncedLyricsView)typeof(IslandWindow).GetField("syncedLyrics",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(panel);if(Math.Abs(lyricsView.CurrentCenter-lyricsView.ActualHeight/2)>.1 || lyricsView.Animating)throw new Exception("Lyrics must settle with current line centered");}
            Save(panel,Path.Combine(directory,page+"-page.png"));
            if(panel.BodyOnScreen!=fixedBody || !overlay.Requested)throw new Exception("Page navigation changed the visible panel size or collapsed it");
            if(panel.ActualHeight<200 || panel.ActualWidth<500)throw new Exception("Page viewport did not expand");
        }
        panel.OpenSettingsPage();await Task.Delay(300);
        foreach(var category in new[]{"Оверлей","Подсветка","Запуск","Музыка и звук","Оверлей"}){
            var button=Descendants(panel).OfType<System.Windows.Controls.Button>().Single(b=>Equals(b.Content,category));button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(45);
        }
        await Task.Delay(450);var categoryHost=(System.Windows.Controls.Grid)Descendants(panel).Single(e=>e.GetType().Name=="CategoryHost");
        if(categoryHost.Children.OfType<FrameworkElement>().Count(e=>e.Visibility==Visibility.Visible)!=1 || categoryHost.Children.OfType<FrameworkElement>().Any(e=>e.HasAnimatedProperties))throw new Exception("Settings category transition did not settle to one retained card");
        Save(panel,Path.Combine(directory,"settings-category-settled.png"));
        for(int n=0;n<9;n++){var layers=(FrameworkElement[])typeof(IslandWindow).GetMethod("AllPageElements",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(panel,null);var opacity=layers.Select(e=>e.Opacity).ToArray();typeof(IslandWindow).GetMethod("SetPage",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(panel,new object[]{n%2==0?"weather":"home"});for(int layer=0;layer<layers.Length;layer++)if(Math.Abs(layers[layer].Opacity-opacity[layer])>.000001)throw new Exception("Rapid navigation reset a layer opacity");await Task.Delay(45);}
        await Task.Delay(400);if(panel.Rendering)throw new Exception("Page navigation must stop rendering after settling");Save(panel,Path.Combine(directory,"navigation-settled.png"));
        track.Playing=false;app.Music.DiagnosticTrack(track);await Task.Delay(1200);await Measure("expanded-paused-settled",phase);
        if(panel.Rendering)throw new Exception("Pause fade must stop frame clock");
        overlay.SetEnabled(false);await Task.Delay(500);await Measure("hidden",quick?3000:15000);
        if(panel.Rendering || OneShot.ActiveCount!=0 || app.Levels.Sampling)throw new Exception("Hidden panel must stop clocks and analysis");
        overlay.SetEnabled(true);track.Playing=true;app.Music.DiagnosticTrack(track);await Task.Delay(500);
        var transitionMeasure=diag.Measure("eight-transitions",5200,panel,overlay.State);
        for(int i=0;i<8;i++){diag.Input();if(i%2==0)overlay.Open(true);else overlay.Close(true);await Task.Delay(650);}results.Add(await transitionMeasure);
        // Retained visual tree and HWND lifetime check. Collection is for diagnostics only.
        overlay.SetEnabled(false);await Task.Delay(500);GC.Collect();GC.WaitForPendingFinalizers();using var process=Process.GetCurrentProcess();process.Refresh();long before=GC.GetTotalMemory(true);int handles=process.HandleCount;long privateBefore=process.PrivateMemorySize64;
        bool animated=app.Config.Animations;app.Config.Animations=false;overlay.SetEnabled(true);
        for(int i=0;i<200;i++){overlay.Open(true,true);overlay.Close(true,true);await Task.Delay(5);}overlay.SetEnabled(false);await Task.Delay(1000);GC.Collect();GC.WaitForPendingFinalizers();process.Refresh();
        results.Add(new{scenario="200-open-close-retained",managedBefore=before,managedAfter=GC.GetTotalMemory(true),handlesBefore=handles,handlesAfter=process.HandleCount,privateBefore,privateAfter=process.PrivateMemorySize64,frames=panel.FrameCount,activeTimers=OneShot.ActiveCount,renderSubscribed=panel.Rendering,interpretation="Bounded scenario, not proof of multi-hour leak absence"});app.Config.Animations=animated;
        app.ShowSettings();await Task.Delay(500);var settings=Application.Current.Windows.OfType<SettingsWindow>().Single(w=>w.IsVisible);Save(settings,Path.Combine(directory,"settings.png"));settings.Close();
        File.WriteAllText(Path.Combine(directory,"metrics.json"),new JsonCodec().Serialize(results));
        File.WriteAllText(Path.Combine(directory,"checks.txt"),"WPF startup, icon/compact/expanded, pause fade, hidden unsubscription, repeated retained-window transitions, settings window, PNG visual export completed. Real music/bridge commands, input, DPI and game scenarios require separate checks.");
    }
    private static void CheckWindowActions(App app)
    {
        var probe=new IslandWindow();new System.Windows.Interop.WindowInteropHelper(probe).EnsureHandle();int exits=0;probe.ExitRequested+=()=>exits++;
        ((Button)probe.FindName("ExitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(exits!=1)throw new Exception("Exit button must dispatch exactly once");
        foreach(var window in new Window[]{probe,app.Overlay.Icon}){
            foreach(var routed in new[]{UIElement.PreviewMouseRightButtonDownEvent,UIElement.PreviewMouseRightButtonUpEvent}){
                var click=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Right){RoutedEvent=routed};
                window.RaiseEvent(click);if(!click.Handled)throw new Exception("Right click must be consumed without opening a menu");
            }
        }
        probe.Close();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    public static void Save(FrameworkElement element,string path)
    {
        element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
}
