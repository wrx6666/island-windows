using System.IO;

namespace Island;
public static class UpdateChecks
{
    private sealed class Fake : IAppUpdateBackend
    {
        public bool Installed=>true;
        public int Checks,Downloads,Applied;
        public string Version="0.11.1";
        public bool Corrupt;
        public TaskCompletionSource<string> Pending;
        public Task<string> CheckAsync(CancellationToken cancellation){Checks++;return Pending?.Task.WaitAsync(cancellation)??Task.FromResult(Version);}
        public Task DownloadAsync(Action<int> progress,CancellationToken cancellation){Downloads++;if(Corrupt)throw new IOException("Checksum mismatch");progress(100);return Task.CompletedTask;}
        public void QueueApply()=>Applied++;
    }
    public static async Task Run(App app,string directory)
    {
        Directory.CreateDirectory(directory);var report=new List<string>();
        void Check(bool value,string message){if(!value)throw new Exception(message);report.Add("PASS "+message);}
        Check(DistributionConfig.NormalizeRepository("https://github.com/owner/repo.git")=="https://github.com/owner/repo","Normalizes GitHub repository");
        foreach(var url in new[]{"http://github.com/owner/repo","https://github.com.evil.test/owner/repo","https://github.com/owner/repo/releases","https://github.com/owner/repo?token=secret","file:///C:/update","https://user:secret@github.com/owner/repo"})Check(DistributionConfig.NormalizeRepository(url)=="","Reject unsupported update source");
        string original=Settings.FilePath;Settings.FilePath=Path.Combine(directory,"island.settings");
        try{
            var config=new Settings();var backend=new Fake{Pending=new()};int stopped=0,notices=0;
            using var service=new AppUpdates(config,directory,()=>{stopped++;return Task.CompletedTask;},backend);service.UpdateAvailable+=_=>notices++;
            var pending=service.CheckAsync();await service.CheckAsync();Check(backend.Checks==1 && service.Busy,"Concurrent checks coalesce without duplicate requests");
            backend.Pending.SetResult("0.11.1");await pending;backend.Pending=null;
            await service.CheckAsync();Check(notices==1 && service.AvailableVersion=="0.11.1","Update notification is emitted once per version");
            await service.CheckAsync(false);Check(backend.Checks==2,"Automatic check respects six-hour cooldown");
            await service.SetAutomaticAsync(false);Check(!Settings.Load().CheckUpdates,"Automatic-check preference persists");
            using(var restored=new AppUpdates(config,directory,()=>Task.CompletedTask,backend)){restored.UpdateAvailable+=_=>notices++;await restored.CheckAsync();Check(notices==1,"Restart does not repeat the same version notification");}
            backend.Corrupt=true;await service.DownloadAndRestartAsync();Check(backend.Applied==0 && stopped==0 && !service.Busy,"Invalid download never applies or stops the running app");
            backend.Corrupt=false;await service.DownloadAndRestartAsync();Check(backend.Applied==1 && stopped==1,"Verified download schedules apply, then graceful shutdown");
            backend.Version="";await service.CheckAsync();Check(service.AvailableVersion=="","Up-to-date check clears available version");
            using(var unconfigured=new AppUpdates(config,directory,()=>Task.CompletedTask)){await unconfigured.CheckAsync();Check(!unconfigured.CanCheck,"Unconfigured repository performs no network work");}
            var canceled=new Fake{Pending=new()};using var closing=new AppUpdates(config,directory,()=>Task.CompletedTask,canceled);var wait=closing.CheckAsync();closing.Dispose();await wait;Check(!closing.Busy,"App shutdown cancels pending update check");
            File.WriteAllLines(Path.Combine(directory,"checks.txt"),report);
        }finally{Settings.FilePath=original;}
    }
}
