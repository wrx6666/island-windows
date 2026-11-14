using System.IO;
using Windows.Media;
using Windows.Media.Control;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Island;
public static class MediaChecks
{
    public static async Task Run(App app,string directory)
    {
        Directory.CreateDirectory(directory);var report=new List<string>();
        void Check(bool value,string message){if(!value)throw new Exception(message);report.Add("PASS "+message);File.WriteAllLines(Path.Combine(directory,"winrt-media.txt"),report);}
        string wave=Path.Combine(directory,"silent-fixture.wav");using(var w=new BinaryWriter(File.Create(wave))){w.Write("RIFF"u8);w.Write(36+44100*4);w.Write("WAVEfmt "u8);w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(44100);w.Write(88200);w.Write((short)2);w.Write((short)16);w.Write("data"u8);w.Write(44100*4);w.Write(new byte[44100*4]);}
        using var player=new MediaPlayer();player.CommandManager.IsEnabled=false;player.IsLoopingEnabled=true;player.Volume=0;player.Source=MediaSource.CreateFromUri(new Uri(wave));var controls=player.SystemMediaTransportControls;
        controls.IsEnabled=controls.IsPlayEnabled=controls.IsPauseEnabled=controls.IsNextEnabled=controls.IsPreviousEnabled=true;
        string title="Island WinRT fixture "+Guid.NewGuid().ToString("N");
        void Publish(string name,MediaPlaybackStatus state){controls.DisplayUpdater.Type=MediaPlaybackType.Music;controls.DisplayUpdater.MusicProperties.Title=name;controls.DisplayUpdater.MusicProperties.Artist="Own silent test player";controls.PlaybackStatus=state;controls.DisplayUpdater.Update();}
        controls.ButtonPressed+=(_,args)=>app.Dispatch(()=>{if(args.Button==SystemMediaTransportControlsButton.Pause){player.Pause();Publish(title,MediaPlaybackStatus.Paused);}else if(args.Button==SystemMediaTransportControlsButton.Play){player.Play();Publish(title,MediaPlaybackStatus.Playing);}else if(args.Button==SystemMediaTransportControlsButton.Next)Publish(title+" next",MediaPlaybackStatus.Playing);else if(args.Button==SystemMediaTransportControlsButton.Previous)Publish(title+" previous",MediaPlaybackStatus.Playing);});
        player.Play();Publish(title,MediaPlaybackStatus.Playing);
        var manager=await MusicService.Await(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());string ownId=null;
        // Bounded discovery in diagnostic host only; the production service subscribes to events.
        for(int i=0;i<30 && ownId==null;i++){foreach(var session in manager.GetSessions()){var props=await MusicService.Await(session.TryGetMediaPropertiesAsync());if(props.Title==title){ownId=session.SourceAppUserModelId;break;}}if(ownId==null)await Task.Delay(100);}
        Check(ownId!=null,"Own GSMTC media session published by .NET 10 MediaPlayer");
        using var music=new MusicService(app.Dispatch,null,new ArtworkDecoder(),s=>s.SourceAppUserModelId==ownId);music.Start();
        Check(await music.WaitUntil(()=>music.Initialized && music.Current.Title==title,4000,CancellationToken.None),"C#/WinRT request, metadata and playback subscription work");
        Check(music.Current.Playing && music.Current.CanNext && music.Current.CanPrevious,"Native playback/capability projection preserved");
        Check(await music.Skip(true) && await music.WaitUntil(()=>music.Current.Title==title+" next",2500,CancellationToken.None),"GSMTC next command delivered to own session");
        Check(await music.Skip(false) && await music.WaitUntil(()=>music.Current.Title==title+" previous",2500,CancellationToken.None),"GSMTC previous command delivered to own session");
        // Restore a known title and check play/pause with the same provider used in production.
        Publish(title,MediaPlaybackStatus.Playing);await music.WaitUntil(()=>music.Current.Title==title,2000,CancellationToken.None);
        Check(await music.Toggle() && await music.WaitUntil(()=>!music.Current.Playing,2500,CancellationToken.None),"GSMTC pause confirmed through playback event");
        Check(await music.PlayExisting() && music.Current.Playing,"GSMTC resume confirmed");player.Pause();controls.IsEnabled=false;
    }
}
