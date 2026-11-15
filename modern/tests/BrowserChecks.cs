using Island;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading.Channels;

namespace Island.Tests;
public static class BrowserChecks
{
    private sealed class NoArtwork : IArtworkDecoder {public ArtworkData Decode(Stream stream)=>throw new InvalidOperationException("Artwork disabled for protocol fixture");}
    private static readonly JsonCodec json=new();
    private static int checks;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
    private static object Track(bool playing=true)=>new{type="track",site="music.yandex.ru",title="Fixture — Трек",artist="Автор",playing,canPrevious=true,canNext=true,canSeek=true,duration=236,position=84,rate=1,volume=.57,pageUrl="https://music.yandex.ru/album/123",audioProcess="browser",audioProcessId=9010};
    private static async Task Wait(BrowserMusic bridge,Func<bool> ready)
    {
        if(ready())return;var signal=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);Action handler=()=>{if(ready())signal.TrySetResult();};bridge.Changed+=handler;
        try{handler();await signal.Task.WaitAsync(TimeSpan.FromSeconds(8));}finally{bridge.Changed-=handler;}
    }
    public static async Task Run(string directory,string legacyHost,string modernHost)
    {
        Directory.CreateDirectory(directory);
        foreach(string host in new[]{legacyHost,modernHost})await HostRoundtrip(host);
        BrowserProtocol.TestSuffix=".test."+Guid.NewGuid().ToString("N");
        using(var bridge=new BrowserMusic(a=>a(),new NoArtwork()))
        {
            bridge.SetArtworkActive(false);bridge.Start();using var client=new NamedPipeClientStream(".",BrowserProtocol.PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);await client.ConnectAsync(5000);
            await BrowserProtocol.Write(client,json.Serialize(Track()));await Wait(bridge,()=>bridge.Current.Available);
            Check(bridge.Current.AudioProcessId==9010 && Math.Abs(bridge.Current.Volume-.57)<.001 && bridge.Current.CanSeek,"Direct bridge preserves numeric PID, volume and timeline capabilities");
            await BrowserProtocol.Write(client,"{");await Wait(bridge,()=>!bridge.Connected);Check(!string.IsNullOrEmpty(bridge.LastError),"Malformed JSON disconnects cleanly with diagnostic error");
        }
        await LaunchChecks(directory);File.WriteAllText(Path.Combine(directory,"browser-checks.txt"),$"{checks} assertions passed. Framework and .NET 10 NativeHost stdio/pipe roundtrips tested; actual browser page not automated in this suite.");
    }
    private static async Task HostRoundtrip(string host)
    {
        Guid fixture=Guid.NewGuid();BrowserProtocol.TestSuffix=".test."+fixture.ToString("N");using var bridge=new BrowserMusic(a=>a(),new NoArtwork());bridge.SetArtworkActive(false);bridge.Start();
        var start=new ProcessStartInfo(host.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)?Environment.ProcessPath:host){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        if(host.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(host);start.ArgumentList.Add(BrowserIdentity.Origin);start.ArgumentList.Add(fixture.ToString());
        using var process=Process.Start(start);var commands=Channel.CreateUnbounded<Dictionary<string,object>>();
        var reader=Task.Run(async()=>{try{string message;while((message=await BrowserProtocol.Read(process.StandardOutput.BaseStream))!=null){var data=(Dictionary<string,object>)json.DeserializeObject(message);if((string)data["type"]!="status")await commands.Writer.WriteAsync(data);}}catch(IOException){}finally{commands.Writer.TryComplete();}});
        try
        {
            await BrowserProtocol.Write(process.StandardInput.BaseStream,"{\"type\":\"bridge\",\"version\":1}");await BrowserProtocol.Write(process.StandardInput.BaseStream,json.Serialize(Track(false)));await Wait(bridge,()=>bridge.LaunchReady && bridge.Current.Available);
            Check(bridge.Current.Title=="Fixture — Трек" && bridge.Current.CanPrevious && bridge.Current.CanNext,Path.GetFileName(host)+" connects and forwards UTF8 track/capabilities");
            foreach(string action in new[]{"toggle","play","next","previous","volume","seek","launch_play"})
            {
                var pending=bridge.Command(action,action=="seek"?100:.42,"yandex","https://music.yandex.ru/");var command=await commands.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));Check((string)command["type"]==action,"Host routes "+action);await BrowserProtocol.Write(process.StandardInput.BaseStream,json.Serialize(new{type="ack",id=command["id"],ok=true}));Check(await pending,"ACK confirms "+action);
            }
            var rejected=bridge.Command("play");var blocked=await commands.Reader.ReadAsync();await BrowserProtocol.Write(process.StandardInput.BaseStream,json.Serialize(new{type="ack",id=blocked["id"],ok=false,reason="play_blocked"}));Check(!await rejected && bridge.CommandFailure.Contains("Браузер"),"Autoplay restriction reported without bypass");
            var timed=Stopwatch.StartNew();var timeout=bridge.Command("volume",.4);await commands.Reader.ReadAsync();Check(!await timeout && timed.ElapsedMilliseconds<4500,"Unacknowledged command has bounded end-to-end deadline");
            var disconnect=bridge.Command("next");await commands.Reader.ReadAsync();process.Kill();await process.WaitForExitAsync();Check(!await disconnect,"Host loss releases pending command");await Wait(bridge,()=>!bridge.Current.Available);Check(!bridge.LaunchReady,"Host loss clears stale metadata/readiness");
        }
        finally {if(!process.HasExited){process.Kill();await process.WaitForExitAsync();}await reader;}
    }
    private sealed class FakeMusic : IMusicService
    {
        public MusicSnapshot Current {get;}=new();public bool Initialized=>true;public bool HasControlSession {get;set;}public bool LaunchBridgeReady {get;set;}public string CommandFailure=>"fixture failure";
        public int Plays,Launches;
        public Task<bool> PlayExisting(){Plays++;Current.Playing=true;return Task.FromResult(true);}
        public Task<bool> LaunchPlay(string service,string url){Launches++;Current.Playing=true;return Task.FromResult(true);}
        public async Task<bool> WaitUntil(Func<bool> ready,int timeout,CancellationToken token){if(ready())return true;await Task.Delay(timeout,token);return ready();}
    }
    private static async Task LaunchChecks(string directory)
    {
        Settings.FilePath=Path.Combine(directory,"launch.settings");int starts=0;var fake=new FakeMusic();await using(var launch=new MusicLaunch(fake,new Settings(),_=>false,(_,_)=>{starts++;return Task.CompletedTask;}){WaitMilliseconds=120,StartupBridgeWaitMilliseconds=20})
        {
            var a=launch.Play();var b=launch.Play();Check(ReferenceEquals(a,b),"Repeated cold Play shares one task");Check(!await a && starts==2,"Missing extension opens service URL once after bootstrap");Check(!await launch.Play() && starts==2,"Launch backoff avoids duplicate browser invocations");
        }
        starts=0;await using(var launch=new MusicLaunch(new FakeMusic(),new Settings(),_=>true,(_,_)=>{starts++;return Task.CompletedTask;}){WaitMilliseconds=100,ExistingBridgeWaitMilliseconds=20}){Check(!await launch.Play() && starts==1,"Existing background browser opens music URL once without bridge");Check(!await launch.Play() && starts==1,"Repeat Play does not create another fallback tab");}
        fake=new FakeMusic{HasControlSession=true};await using(var launch=new MusicLaunch(fake,new Settings(),_=>throw new Exception("unnecessary process inspection"),(_,_)=>throw new Exception("unnecessary browser"))){Check(await launch.Play() && fake.Plays==1,"Existing session reused without browser launch");}
        fake=new FakeMusic{LaunchBridgeReady=true};await using(var launch=new MusicLaunch(fake,new Settings(),_=>false,(_,_)=>throw new Exception("duplicate browser"))){Check(await launch.Play() && fake.Launches==1,"Connected bridge launches service through existing browser");}
    }
}
