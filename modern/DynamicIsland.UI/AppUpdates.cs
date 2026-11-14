using System.IO;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace Island;

public interface IAppUpdateBackend
{
    bool Installed {get;}
    Task<string> CheckAsync(CancellationToken cancellation);
    Task DownloadAsync(Action<int> progress,CancellationToken cancellation);
    void QueueApply();
}

public sealed class VelopackUpdateBackend : IAppUpdateBackend
{
    private readonly UpdateManager manager;
    private UpdateInfo update;
    public bool Installed=>manager.IsInstalled;
    public VelopackUpdateBackend(string repository)=>manager=new(new GithubSource(repository,null,false,new Downloader()),new(){AllowVersionDowngrade=false});
    public async Task<string> CheckAsync(CancellationToken cancellation)
    {
        if(manager.UpdatePendingRestart is {} ready)return ready.Version.ToString();
        update=await manager.CheckForUpdatesAsync().WaitAsync(cancellation);
        return update?.TargetFullRelease.Version.ToString()??"";
    }
    public async Task DownloadAsync(Action<int> progress,CancellationToken cancellation)
    {
        if(update!=null)await manager.DownloadUpdatesAsync(update,progress,cancellation);
        else if(manager.UpdatePendingRestart==null)throw new IOException("Сначала проверьте обновления");
    }
    public void QueueApply()=>manager.WaitExitThenApplyUpdates(update?.TargetFullRelease??manager.UpdatePendingRestart,restartArgs:new[]{"--open"});
    // Metadata requests are short; package downloads are cancellable and bounded.
    private sealed class Downloader : IFileDownloader
    {
        private readonly HttpClientFileDownloader inner=new();
        public Task<byte[]> DownloadBytes(string url,IDictionary<string,string> headers=null,double timeout=30)=>inner.DownloadBytes(url,headers,Math.Min(timeout,.25));
        public Task<string> DownloadString(string url,IDictionary<string,string> headers=null,double timeout=30)=>inner.DownloadString(url,headers,Math.Min(timeout,.25));
        public Task DownloadFile(string url,string targetFile,Action<int> progress,IDictionary<string,string> headers=null,double timeout=30,CancellationToken cancelToken=default)=>inner.DownloadFile(url,targetFile,progress,headers,Math.Min(timeout,10),cancelToken);
    }
}

public sealed class AppUpdates : IDisposable
{
    private sealed record State(DateTime CheckedUtc,string NotifiedVersion);
    private readonly IAppUpdateBackend backend;
    private readonly Settings config;
    private readonly string stateFile;
    private readonly Func<Task> shutdown;
    private readonly CancellationTokenSource stop=new();
    private CancellationTokenSource schedule;
    private State state=new(DateTime.MinValue,"");
    private bool disposed;
    public event Action Changed;
    public event Action<string> UpdateAvailable;
    public bool Busy {get;private set;}
    public string AvailableVersion {get;private set;}="";
    public string Status {get;private set;}
    public bool CanCheck=>backend?.Installed==true;
    public bool Automatic=>config.CheckUpdates;
    public string CurrentVersion=>typeof(App).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute),false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0]??"1.0.0";
    public AppUpdates(Settings settings,string dataDirectory,Func<Task> stopApp,IAppUpdateBackend updateBackend=null)
    {
        config=settings;shutdown=stopApp;stateFile=Path.Combine(dataDirectory,"updates-state.json");
        string repo=DistributionConfig.NormalizeRepository(DistributionConfig.Load().Repository);
        backend=updateBackend??(repo.Length>0?new VelopackUpdateBackend(repo):null);
        Status=backend==null?"Источник обновлений ещё не настроен":!backend.Installed?"Для обновлений установите Island через Setup.exe":"Можно проверить наличие новой версии";
        try{if(File.Exists(stateFile))state=JsonSerializer.Deserialize<State>(File.ReadAllText(stateFile))??state;}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){}
    }
    public void Start()
    {
        schedule?.Cancel();schedule?.Dispose();schedule=null;
        if(disposed || !Automatic || !CanCheck)return;
        schedule=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);_=ScheduleAsync(schedule.Token);
    }
    private async Task ScheduleAsync(CancellationToken cancellation)
    {
        try{
            await Task.Delay(TimeSpan.FromSeconds(25),cancellation);
            while(!cancellation.IsCancellationRequested){
                if(DateTime.UtcNow-state.CheckedUtc>=TimeSpan.FromHours(6))await CheckAsync(false);
                await Task.Delay(TimeSpan.FromHours(6),cancellation);
            }
        }catch(OperationCanceledException){}
    }
    public async Task SetAutomaticAsync(bool enabled){config.CheckUpdates=enabled;await config.SaveAsync();Start();Signal();}
    public async Task CheckAsync(bool manual=true)
    {
        if(disposed || Busy || !CanCheck)return;
        if(!manual && DateTime.UtcNow-state.CheckedUtc<TimeSpan.FromHours(6))return;
        Busy=true;Status="Проверяем обновления…";Signal();state=state with{CheckedUtc=DateTime.UtcNow};
        try{
            string version=await backend.CheckAsync(stop.Token);if(disposed)return;
            AvailableVersion=version;Status=version.Length==0?"Установлена актуальная версия":"Доступна версия "+version;
            if(version.Length>0 && version!=state.NotifiedVersion){state=state with{NotifiedVersion=version};UpdateAvailable?.Invoke(version);}
        }catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        catch(Exception){if(!disposed)Status="Не удалось проверить обновления. Повторите позже.";}
        finally{Busy=false;if(!disposed){await SaveState();Signal();}}
    }
    public async Task DownloadAndRestartAsync()
    {
        if(disposed || Busy || AvailableVersion.Length==0 || !CanCheck)return;
        Busy=true;Status="Загрузка обновления…";Signal();
        try{
            var progress=new Progress<int>(value=>{if(!disposed){Status="Загрузка обновления: "+Math.Clamp(value,0,100)+"%";Signal();}});
            await backend.DownloadAsync(value=>((IProgress<int>)progress).Report(value),stop.Token);
            if(disposed)return;
            await config.SaveAsync();await Settings.FlushAsync();
            Status="Перезапуск Island…";Signal();backend.QueueApply();await shutdown();
        }catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        catch(Exception){if(!disposed)Status="Не удалось установить обновление. Текущая версия сохранена; можно повторить.";}
        finally{Busy=false;Signal();}
    }
    private async Task SaveState()
    {
        try{Directory.CreateDirectory(Path.GetDirectoryName(stateFile));string temp=stateFile+".tmp";await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(state));File.Move(temp,stateFile,true);}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
    }
    private void Signal(){if(!disposed)Changed?.Invoke();}
    public void Dispose(){if(disposed)return;disposed=true;schedule?.Cancel();schedule?.Dispose();stop.Cancel();stop.Dispose();}
}
