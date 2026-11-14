using System.IO;
using System.Text.Json;
using Velopack;

namespace Island;
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false)
            .OnAfterInstallFastCallback(_=>AppDistribution.Register(true))
            .OnAfterUpdateFastCallback(_=>AppDistribution.Register(false))
            .OnBeforeUninstallFastCallback(_=>AppDistribution.Unregister())
            .OnFirstRun(_=>AppDistribution.FirstRun=true).Run();
        // Isolated installer/update integration checks cannot run in production packages.
        if(DistributionConfig.Load().IntegrationTest){
            if(args.Length<2)return;
            if(args[0]=="--distribution-smoke"){
                File.WriteAllText(args[1],JsonSerializer.Serialize(new{version=Velopack.Locators.VelopackLocator.Current.CurrentlyInstalledVersion?.ToString(),data=AppDistribution.DataDirectory,launcher=AppDistribution.StableLauncher}));return;
            }
            if(args[0]=="--update-smoke" && args.Length==3)try{
                var manager=new UpdateManager(args[1]);var update=manager.CheckForUpdatesAsync().GetAwaiter().GetResult()??throw new IOException("No smoke update available");
                manager.DownloadUpdatesAsync(update).GetAwaiter().GetResult();manager.WaitExitThenApplyUpdates(update.TargetFullRelease,restartArgs:new[]{"--distribution-smoke",args[2]});return;
            }catch(Exception ex){File.WriteAllText(args[2],JsonSerializer.Serialize(new{error=ex.ToString()}));Environment.ExitCode=1;return;}
        }
        if(DistributionConfig.Load().IntegrationTest)return;
        var app=new App();app.InitializeComponent();app.Run();
    }
}
