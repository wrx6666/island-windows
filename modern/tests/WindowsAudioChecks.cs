using Island;
using System.Diagnostics;
namespace Island.Tests;
public static class WindowsAudioChecks
{
    public static Process StartTone(int hz,int seconds)
    {
        // Siblings, so the selected source's process tree does not contain the other tone.
        string executable=Environment.ProcessPath;var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(WindowsAudioChecks).Assembly.Location);
        start.ArgumentList.Add("--tone");start.ArgumentList.Add(hz.ToString());start.ArgumentList.Add(seconds.ToString());return Process.Start(start);
    }
    public static async Task Run(string directory)
    {
        Directory.CreateDirectory(directory);var results=new List<string>();
        void Require(bool value,string message){if(!value)throw new Exception(message);results.Add("PASS "+message);Console.WriteLine(results[^1]);}
        using var source=StartTone(110,10);using var other=StartTone(8000,10);using var mixer=new AudioMixer();using var levels=new MusicLevels();
        await Task.Delay(1000);levels.Configure(true,source.ProcessName,source.Id);float bass=0,high=0;int packets=0;var clock=Stopwatch.StartNew();
        while(clock.Elapsed.TotalSeconds<3){await Task.Delay(20);var f=levels.Read();if(f.Spectrum){packets++;bass=Math.Max(bass,f.Bass);high=Math.Max(high,f.High);}}
        Require(packets>50 && bass>.05 && high<.025,$"Real process capture includes 110 Hz and excludes sibling 8 kHz: {levels.Status}, bass={bass:F4}, high={high:F4}");
        float original=await mixer.MusicVolume(source.ProcessName,null);
        Require(original>=0,"Own test audio session available");
        try {Require(Math.Abs(await mixer.MusicVolume(source.ProcessName,.23f)-.23f)<.01,"Per-application volume write/readback");await mixer.Toggle(source.ProcessName);Require(mixer.Muted,"Mute test application sessions");await mixer.RestoreBeforeChange();Require(!mixer.Muted,"Restore muted sessions before changing target");}
        finally {await mixer.MusicVolume(source.ProcessName,original);await mixer.RestoreBeforeChange();}
        levels.Configure(false,"");await Task.Delay(300);long count=levels.Samples;await Task.Delay(400);Require(levels.Samples==count && !levels.Sampling,"Hidden/disabled stops audio sampling");
        float endpoint=await EndpointVolume.Level();Require(endpoint>=0 && endpoint<=1,"Default endpoint master volume read");Require(Math.Abs(await EndpointVolume.Level(endpoint)-endpoint)<.001,"Master volume writes identical level without audible change");
        File.WriteAllLines(Path.Combine(directory,"windows-audio.txt"),results);File.WriteAllText(Path.Combine(directory,"audio-isolation.json"),new JsonCodec().Serialize(new{bass,excludedHigh=high,packets,status=levels.Status,endpoint,os=Environment.OSVersion.VersionString,runtime=Environment.Version.ToString()}));
        source.Kill();other.Kill();await source.WaitForExitAsync();await other.WaitForExitAsync();
    }
}
