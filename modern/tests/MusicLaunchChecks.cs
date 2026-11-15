using Island;

namespace Island.Tests;

public static class MusicLaunchChecks
{
    private sealed class Fake : IMusicService
    {
        public MusicSnapshot Current {get;}=new();
        public bool Initialized=>true;
        public bool HasControlSession {get;set;}
        public bool LaunchBridgeReady {get;set;}
        public string CommandFailure=>"stale session";
        public bool ExistingWorks=true;
        public int Plays,Launches;
        public string Service,Url;
        public Task<bool> PlayExisting(){Plays++;return Task.FromResult(ExistingWorks);}
        public Task<bool> LaunchPlay(string service,string url){Launches++;Service=service;Url=url;return Task.FromResult(true);}
        public async Task<bool> WaitUntil(Func<bool> ready,int timeout,CancellationToken cancellation)
        {
            if(ready())return true;
            await Task.Delay(timeout,cancellation);
            return ready();
        }
    }
    private static MusicLaunch Create(Fake fake,Settings settings,bool running,Func<string,string,Task> start)=>new(fake,settings,_=>running,start)
    {WaitMilliseconds=10,StartupBridgeWaitMilliseconds=10,ExistingBridgeWaitMilliseconds=10};
    public static async Task Run(string directory)
    {
        string original=Settings.FilePath;Settings.FilePath=Path.Combine(directory,"cold-start.settings");
        int count=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);count++;Console.WriteLine("PASS "+message);}
        try
        {
            var settings=new Settings();settings.Launch.Restore=false;settings.Launch.UseExtension=true;
            var fake=new Fake();var calls=new List<string>();
            await using(var launch=Create(fake,settings,false,(_,url)=>{calls.Add(url);return Task.CompletedTask;}))
            {
                var a=launch.Play();var b=launch.Play();
                Check(ReferenceEquals(a,b),"Cold Play coalesces repeated requests");
                Check(!await a && calls.SequenceEqual(new[]{"",MusicLaunchOptions.Home("yandex")}),"Disabled bridge opens the music URL after bounded bootstrap");
                Check(launch.Failure.Contains("не подключён"),"Missing bridge reports limitation instead of claiming playback");
                Check(!await launch.Play() && calls.Count==2,"Repeated failed cold Play does not launch duplicate windows/tabs");
            }
            fake=new Fake();calls.Clear();
            await using(var launch=Create(fake,settings,true,(_,url)=>{calls.Add(url);fake.LaunchBridgeReady=true;return Task.CompletedTask;}))
            {
                Check(await launch.Play() && calls.SequenceEqual(new[]{MusicLaunchOptions.Home("yandex")}) && fake.Launches==1,"Background browser without a bridge receives the service URL and resumes after connection");
            }
            fake=new Fake();calls.Clear();
            await using(var launch=Create(fake,settings,false,(_,url)=>{calls.Add(url);fake.LaunchBridgeReady=true;return Task.CompletedTask;}))
            {
                Check(await launch.Play() && calls.SequenceEqual(new[]{""}) && fake.Launches==1,"Healthy cold bridge owns tab creation without duplicate CLI navigation");
            }
            fake=new Fake{LaunchBridgeReady=true};
            await using(var launch=Create(fake,settings,true,(_,_)=>throw new Exception("unnecessary browser launch")))
            {
                Check(await launch.Play() && fake.Service=="yandex" && fake.Url==MusicLaunchOptions.Home("yandex"),"Connected bridge opens selected service without launching another browser");
            }
            fake=new Fake{HasControlSession=true};
            await using(var launch=Create(fake,settings,true,(_,_)=>throw new Exception("unnecessary browser launch")))
            {
                Check(await launch.Play() && fake.Plays==1 && fake.Launches==0,"Working Windows session remains usable without an extension");
            }
            fake=new Fake{HasControlSession=true,ExistingWorks=false};
            await using(var launch=Create(fake,settings,true,(_,_)=>{fake.LaunchBridgeReady=true;return Task.CompletedTask;}))
            {
                Check(await launch.Play() && fake.Plays==1 && fake.Launches==1,"Stale Windows session falls back to service launch");
            }
            settings.Launch.OnPlay=false;fake=new Fake();
            await using(var launch=Create(fake,settings,true,(_,_)=>throw new Exception("launch disabled")))
            {
                Check(!await launch.Play(),"Disabled launch preference never opens browser");
            }
            settings.Launch.OnPlay=true;fake=new Fake();calls.Clear();
            var canceled=Create(fake,settings,true,(_,url)=>{calls.Add(url);return Task.CompletedTask;});
            var task=canceled.Play();await canceled.DisposeAsync();
            Check(!await task && calls.Count==0,"Shutdown cancels launch wait before opening a tab");
            settings.Launch.UseExtension=false;
            fake=new Fake{LaunchBridgeReady=true};var provider=new NativeFake();
            await using(var native=Create(fake,settings,true,(_,_)=>throw new Exception("Native mode must not bootstrap extension")))
            {
                native.NativePlayer=provider;
                Check(await native.Play() && provider.Calls==1 && fake.Launches==0 && fake.Plays==0,"Extension-free mode routes directly to Windows provider even when a bridge exists");
                provider.Ok=false;Check(!await native.Play() && native.Failure==provider.Failure,"Windows provider failure remains explicit without requesting an extension");
            }
            var options=new MusicLaunchOptions();Check(!options.UseExtension && options.Parse("MusicUseExtension=True") && options.UseExtension,"Extension-free is default and optional bridge preference parses");
            Check(MusicService.MatchesBrowser("browser.exe","browser") && !MusicService.MatchesBrowser("YandexMusic.exe","browser") && MusicService.MatchesBrowser("Google.Chrome.UserData.Profile","chrome"),"Browser session matching does not confuse desktop music client with browser");
            Console.WriteLine($"{count} music launch checks passed");
        }
        finally{Settings.FilePath=original;}
    }
    private sealed class NativeFake : IMusicLaunchProvider
    {
        public int Calls;public bool Ok=true;public string Failure=>"Windows provider unavailable";
        public Task<bool> Play(string browser,string service,string url,CancellationToken cancellation){Calls++;return Task.FromResult(Ok);}
    }
}
