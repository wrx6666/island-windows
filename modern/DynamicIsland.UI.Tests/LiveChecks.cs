using System.IO;
namespace Island;
public static class LiveChecks
{
    public static async Task Run(App app,string directory)
    {
        Directory.CreateDirectory(directory);var report=new List<string>();
        app.Music.Start();app.Overlay.Open(true,true);
        bool ready=await app.Music.WaitUntil(()=>app.Bridge.Connected && app.Music.Current.Available,8000,CancellationToken.None);
        report.Add("Existing browser bridge: "+app.Bridge.Connected+"; music available: "+app.Music.Current.Available+"; initialized: "+app.Music.Initialized);
        if(ready)
        {
            var track=app.Music.Current;bool playing=track.Playing;report.Add("Source: "+track.Source+"; playing: "+playing+"; volume available: "+track.CanVolume);
            if(track.CanVolume){bool volume=await app.Music.SetVolume(track.Volume);report.Add("Same-volume command acknowledged: "+volume+"; reason="+app.Bridge.CommandReason+"; "+app.Music.CommandFailure);}
            if(playing)
            {
                try{bool pause=await app.Music.Toggle();bool confirmed=pause && await app.Music.WaitUntil(()=>!app.Music.Current.Playing,2500,CancellationToken.None);report.Add("Actual pause confirmed: "+confirmed);}
                finally{bool resume=await app.Music.PlayExisting();report.Add("Original playback restored: "+resume);}
            }
            await app.Music.WaitUntil(()=>app.Music.Current.Cover!=null,6000,CancellationToken.None);report.Add("Artwork decoded: "+(app.Music.Current.Cover!=null));
            WpfScenarios.Save(app.Panel,Path.Combine(directory,"live-panel.png"));
        }
        else report.Add("No connected current browser track; real page commands were not exercised and no browser/tab was launched.");
        File.WriteAllLines(Path.Combine(directory,"live-check.txt"),report);
    }
}
