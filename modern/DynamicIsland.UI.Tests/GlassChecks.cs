using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island;
public static class GlassChecks
{
    public static async Task Run(App app,string directory)
    {
        Directory.CreateDirectory(directory);var report=new List<string>();
        void Check(bool condition,string message){if(!condition)throw new Exception(message);report.Add("PASS "+message);File.WriteAllLines(Path.Combine(directory,"glass-checks.txt"),report);}
        var panel=app.Panel;var overlay=app.Overlay;var decoder=new ArtworkDecoder();
        T Get<T>(string name)=>(T)panel.FindName(name);
        using var cover=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","approved-album.png"));
        var artwork=await Task.Run(()=>decoder.Decode(cover));
        var track=new MusicSnapshot{Title="Arrivederci 2",Artist="FRIENDLY THUG 52 NGG",Source="music.yandex.ru",Playing=true,CanPrevious=true,CanNext=true,CanSeek=true,Volume=.57f,Cover=artwork.Bytes,Color=artwork.Primary,SecondaryColor=artwork.Secondary};
        track.SetTimeline(21,121,1);track.PreviewPosition=21;
        app.Config.Visual.Enabled=true;app.Config.Visual.Palette=0;app.Config.Visual.Mode=VisualMode.Combined;
        app.Music.DiagnosticTrack(track);overlay.Open(true,true);
        panel.SetWeather(new(){Available=true,Temperature=2,FeelsLike=-2,Humidity=86,Wind=4,Pressure=980,Code=3,ObservedUtc=DateTime.UtcNow,ReceivedUtc=DateTime.UtcNow});
        Get<TextBlock>("DateText").Text="Суббота, 14 ноября 2026";Get<TextBlock>("ClockText").Text="09:28";
        await Task.Delay(1000);Get<TextBlock>("PositionText").Text="0:21";
        WpfScenarios.Save(panel,Path.Combine(directory,"expanded.png"));
        Check(Get<Image>("Cover").Source is BitmapSource bitmap && bitmap.PixelWidth==384,"Cover decoded at 384 px for large artwork and DPI");
        Check(Get<TextBlock>("ArtistText").ActualHeight<30,"Single-line artist retained");
        Check(Get<Border>("VolumePanel").Visibility==Visibility.Visible,"Inline volume regulator is available");
        // A separate unconnected view tests UI dispatch without controlling the user's player.
        var probe=new IslandWindow{Config=app.Config};new System.Windows.Interop.WindowInteropHelper(probe).EnsureHandle();probe.SetTrack(track);probe.Present(true,true);
        T Control<T>(string name)=>(T)probe.FindName(name);
        int play=0,previous=0,next=0;double? seek=null,volume=null;
        probe.TogglePlay+=()=>play++;probe.Previous+=()=>previous++;probe.Next+=()=>next++;probe.SeekRequested+=v=>seek=v;probe.VolumeRequested+=v=>volume=v;
        Control<Button>("PlayButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Control<Button>("PreviousButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Control<Button>("NextButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(play==1 && previous==1 && next==1,"Transport buttons dispatch existing play/previous/next events once");
        Control<Button>("BackButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(seek==6,"Back control seeks 15 seconds");
        Control<Button>("ForwardButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(seek==36,"Forward control seeks 15 seconds");
        Control<Button>("VolumeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(volume==0,"Speaker icon mutes music");
        Control<Button>("VolumeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(Math.Abs(volume.Value-.57)<.001,"Speaker icon restores music volume");
        Control<Slider>("VolumeSlider").Value=.42;Check(volume.HasValue && Math.Abs(volume.Value-.42)<.001,"Volume slider emits requested volume");
        await Task.Delay(100);WpfScenarios.Save(panel,Path.Combine(directory,"volume.png"));
        probe.Close();
        overlay.Close(true,true);overlay.Open(true,true);
        var old=((SolidColorBrush)Get<Slider>("SeekSlider").Foreground).Color;
        track.Color=AccentColor.FromArgb(230,40,60);track.SecondaryColor=AccentColor.FromArgb(245,150,70);app.Music.DiagnosticTrack(track);
        await Task.Delay(500);var warm=((SolidColorBrush)Get<Slider>("SeekSlider").Foreground).Color;
        Check(warm!=old && warm.R>warm.B,"Album palette change retints controls and glass");
        WpfScenarios.Save(panel,Path.Combine(directory,"warm-palette.png"));
        app.Config.Visual.Mode=VisualMode.Border;panel.SetTrack(track);await Task.Delay(100);WpfScenarios.Save(panel,Path.Combine(directory,"border-mode.png"));
        app.Config.Animations=false;panel.UpdateActivity();await Task.Delay(100);Check(!panel.Rendering && !app.Levels.Sampling,"Reduced motion keeps playing artwork static and stops analysis");app.Config.Animations=true;panel.UpdateActivity();
        track.Title="Длинное название композиции, которое занимает две строки";app.Music.DiagnosticTrack(track);await Task.Delay(150);
        Check(Get<TextBlock>("TitleText").ActualHeight>50 && Canvas.GetTop(Get<TextBlock>("ArtistText"))>=Canvas.GetTop(Get<TextBlock>("TitleText"))+Get<TextBlock>("TitleText").ActualHeight,"Long title wraps to two lines above artist and transport");
        WpfScenarios.Save(panel,Path.Combine(directory,"long-title.png"));
        app.Config.Visual.Mode=VisualMode.Combined;track.Playing=false;app.Music.DiagnosticTrack(track);await Task.Delay(1200);
        Check(!panel.Rendering,"Pause settles to static album glass with no frame subscription");
        overlay.SetEnabled(false);await Task.Delay(200);Check(!panel.Rendering && !app.Levels.Sampling && OneShot.ActiveCount==0,"Hidden panel stops popup, render, analysis and one-shot timers");
        ArtworkData DecodeSolid(byte r,byte g,byte b){byte[] pixels=new byte[32*32*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=b;pixels[i+1]=g;pixels[i+2]=r;pixels[i+3]=255;}var image=BitmapSource.Create(32,32,96,96,PixelFormats.Bgra32,null,pixels,128);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=new MemoryStream();encoder.Save(stream);stream.Position=0;return decoder.Decode(stream);}
        var gray=DecodeSolid(128,128,128);Check(Math.Abs(gray.Primary.R-gray.Primary.B)<2 && Math.Abs(gray.Primary.R-gray.Primary.G)<2,"Neutral cover stays neutral rather than forced purple/blue");
        var red=DecodeSolid(220,30,35);var blue=DecodeSolid(20,45,230);Check(red.Primary.R>red.Primary.B+100 && blue.Primary.B>blue.Primary.R+100,"Distinct artwork clusters produce distinct palettes");
        File.WriteAllText(Path.Combine(directory,"palette.json"),new JsonCodec().Serialize(new{primary=artwork.Primary,secondary=artwork.Secondary}));
        app.Music.DiagnosticTrack(new());overlay.SetEnabled(true);overlay.Open(true,true);await Task.Delay(100);WpfScenarios.Save(panel,Path.Combine(directory,"empty.png"));overlay.SetEnabled(false);
        string source=Path.Combine(directory,"source.png");if(File.Exists(source))Compare(source,Path.Combine(directory,"expanded.png"),Path.Combine(directory,"comparison.png"));
        string failure=Path.Combine(directory,"FAILED.txt");if(File.Exists(failure))File.Delete(failure);
    }
    public static void Compare(string source,string actual,string destination)
    {
        BitmapImage Load(string path){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(Path.GetFullPath(path));image.EndInit();image.Freeze();return image;}
        var reference=Load(source);var implementation=Load(actual);var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()){dc.DrawRectangle(Brushes.Black,null,new Rect(0,0,1028,548));dc.DrawImage(new CroppedBitmap(reference,new Int32Rect(8,13,reference.PixelWidth-17,reference.PixelHeight-29)),new Rect(0,0,504,548));dc.DrawImage(implementation,new Rect(524,0,504,548));}
        var bitmap=new RenderTargetBitmap(1028,548,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(destination);encoder.Save(output);
        var detail=new PngBitmapEncoder();detail.Frames.Add(BitmapFrame.Create(new CroppedBitmap(bitmap,new Int32Rect(0,245,1028,233))));using var details=File.Create(Path.Combine(Path.GetDirectoryName(destination),"comparison-controls-weather.png"));detail.Save(details);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(destination),"capture.json"),new JsonCodec().Serialize(new{sourceWidth=reference.PixelWidth,sourceHeight=reference.PixelHeight,crop=new{x=8,y=13,width=reference.PixelWidth-17,height=reference.PixelHeight-29},logicalWidth=504,logicalHeight=548,dpi=96,implementationWidth=implementation.PixelWidth,implementationHeight=implementation.PixelHeight}));
    }
}
