using Island;
using System.Diagnostics;
using System.Drawing;

static class Program
{
    static int checks;
    static void Check(bool condition,string message) { if(!condition) throw new Exception(message); checks++; Console.WriteLine("PASS "+message); }
    static AudioFeatures Tone(int hz,bool inverse=false)
    {
        var fft=new AudioSpectrum();
        for(int i=0;i<AudioSpectrum.Rate*2;i++) { float x=(float)(Math.Sin(i*2*Math.PI*hz/AudioSpectrum.Rate)*.2); fft.Push(x,inverse?-x:x); }
        return fft.Current;
    }
    static async Task<int> Main(string[] args)
    {
        if(args.FirstOrDefault()=="--tone") {await Island.Tests.Tone.Play(int.Parse(args[1]),int.Parse(args[2]));return 0;}
        if(args.FirstOrDefault()=="--audio") {await Island.Tests.WindowsAudioChecks.Run(Path.GetFullPath(args.ElementAtOrDefault(1)??"artifacts/wpf-audio"));return 0;}
        if(args.FirstOrDefault()=="--browser") {await Island.Tests.BrowserChecks.Run(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]),Path.GetFullPath(args[3]));return 0;}
        string directory=Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/wpf-tests"); Directory.CreateDirectory(directory);
        Settings.FilePath=Path.Combine(directory,"island.settings");
        try
        {
            var codec=new JsonCodec(); var json=(Dictionary<string,object>)codec.DeserializeObject("{\"audioProcessId\":123,\"volume\":0.57,\"playing\":true,\"nested\":{\"time\":42}}");
            Check(json["audioProcessId"] is int && json["volume"] is double && json["nested"] is Dictionary<string,object>,"JSON preserves native bridge number/object contracts");
            Check(codec.Serialize(new { type="track",title="Трек",playing=true }).Contains("\"type\":\"track\""),"Wire field names unchanged");
            File.WriteAllLines(Settings.FilePath,new[]{"MusicHotkey=33","MuteHotkey=34","OverlayHotkey=131152","FutureOption=kept","VisualSpeed=125","LaunchAutoPlay=False"});
            var settings=Settings.Load(); Check(settings.MusicHotkey==33 && settings.MuteHotkey==34,"Legacy single-key bindings preserved");
            await settings.SaveAsync(); Check(File.Exists(Settings.FilePath+".bak") && File.ReadAllText(Settings.FilePath).Contains("FutureOption=kept"),"Atomic settings save backs up original and preserves unknown fields");
            settings.OverlayDelay=300;var firstSave=settings.SaveAsync();settings.OverlayDelay=500;var lastSave=settings.SaveAsync();await Task.WhenAll(firstSave,lastSave);Check(Settings.Load().OverlayDelay==500,"Ordered async settings snapshots preserve latest request");
            var imported=Path.Combine(directory,"import"); Settings.Import(directory,imported); File.WriteAllText(Path.Combine(imported,"island.settings"),"KEEP"); Settings.Import(directory,imported);
            Check(File.ReadAllText(Path.Combine(imported,"island.settings"))=="KEEP","Importer never overwrites existing destination");
            Check(!new MusicLaunchOptions().AutoPlay && KeyBinding.Valid(33) && KeyBinding.Valid(0x20050) && !KeyBinding.Valid(16),"Safe launch defaults and key validation");
            Check(MusicLaunchOptions.SafeUrl("https://music.yandex.ru/album/123?token=secret#position","yandex")=="https://music.yandex.ru/album/123" && MusicLaunchOptions.SafeUrl("https://user@music.yandex.ru/","yandex")=="","History excludes tokens and rejects unsafe URLs");
            var history=new LastMusicSession(); history.Update(new MusicSnapshot{Title="Трек",Artist="Автор",Source="music.yandex.ru",PageUrl="https://music.yandex.ru/album/123"}); history.Save(); Check(LastMusicSession.Load().Title=="Трек","Legacy history roundtrip");
            var bass=Tone(110); var mid=Tone(1000); var high=Tone(8000); var anti=Tone(110,true);
            Check(Math.Abs(bass.Rms-.141421)<.004 && bass.Bass>.3 && bass.Mid<.01 && bass.High<.01,"RMS and bass band");
            Check(mid.Mid>.3 && high.High>.3 && Math.Abs(anti.Bass-bass.Bass)<.001 && bass.Beat<.01,"FFT bands, opposite-phase stereo and steady tone is not beat");
            var pulses=new AudioSpectrum(); float onset=0; for(int i=0;i<AudioSpectrum.Rate*4;i++) { double t=i/(double)AudioSpectrum.Rate; float x=(float)(Math.Sin(t*2*Math.PI*110)*(.1+.3*Math.Exp(-(t%.5)*28))); if(pulses.Push(x,x) && t>1) onset=Math.Max(onset,pulses.Current.Beat); }
            Check(onset>.15,"Dense bass attacks remain detectable"); for(int i=0;i<AudioSpectrum.Rate*3;i++) pulses.Push(0,0); Check(pulses.Current.Energy<.002 && pulses.Current.Beat<.002,"Silence settles without synthetic beat");
            var spring=new Spring {Target=1}; for(int i=0;i<120;i++) spring.Step(1d/120); Check(spring.Value==1 && spring.Velocity==0,"Analytic spring settles"); spring.Target=0; spring.Step(.01); double velocity=spring.Velocity; spring.Target=1; Check(spring.Velocity==velocity,"Retarget keeps velocity");
            var monitor=new Rectangle(0,0,1920,1080); var work=new Rectangle(0,0,1920,1040);
            Check(ActiveWindowWatcher.Classify(false,false,monitor,monitor,work,monitor,2)==ForegroundKind.Fullscreen && ActiveWindowWatcher.Classify(false,true,work,work,work,monitor,2)==ForegroundKind.Maximized && ActiveWindowWatcher.Classify(false,false,new(0,0,900,800),new(0,0,900,800),work,monitor,2)==ForegroundKind.Windowed,"Fullscreen, maximized and partial window geometry");
            using(var stream=new MemoryStream()) { await BrowserProtocol.Write(stream,"{\"type\":\"track\",\"title\":\"Трек\"}"); stream.Position=0; Check((await BrowserProtocol.Read(stream)).Contains("Трек"),"Native messaging UTF8 framing roundtrip"); }
            bool rejected=false; try { await BrowserProtocol.Read(new MemoryStream(BitConverter.GetBytes(32769))); } catch(InvalidDataException) {rejected=true;} Check(rejected,"Oversize IPC rejected before allocation");
            Check(BrowserMusic.AllowedCover("https://avatars.yandex.net/a") && !BrowserMusic.AllowedCover("https://avatars.yandex.net.evil.test/a"),"Artwork host boundary retained");
            Console.WriteLine($"{checks} checks passed; .NET {Environment.Version}"); return 0;
        }
        catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
    }
}
