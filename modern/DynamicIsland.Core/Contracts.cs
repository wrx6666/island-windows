namespace Island;

public readonly record struct AccentColor(byte R, byte G, byte B)
{
    public static AccentColor FromArgb(int r, int g, int b) => new((byte)r, (byte)g, (byte)b);
    public static AccentColor Hue(double hue, double saturation = .55, double value = .9)
    {
        hue = (hue % 360 + 360) % 360;
        double c = value * saturation, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = value - c;
        (double r, double g, double b) = hue switch { < 60 => (c,x,0d), < 120 => (x,c,0d), < 180 => (0d,c,x), < 240 => (0d,x,c), < 300 => (x,0d,c), _ => (c,0d,x) };
        return FromArgb((int)((r+m)*255),(int)((g+m)*255),(int)((b+m)*255));
    }
    public static AccentColor Fallback(string key) { uint hash=2166136261; foreach(char c in key) hash=unchecked((hash^c)*16777619); return Hue(hash%360); }
}

public sealed record ArtworkData(byte[] Bytes, AccentColor Primary, AccentColor Secondary);
public interface IArtworkDecoder { ArtworkData Decode(Stream stream); }
public interface IMusicBridge : IDisposable
{
    MusicSnapshot Current { get; }
    bool Connected { get; }
    bool LaunchReady { get; }
    string CommandFailure { get; }
    event Action Changed;
    void Start();
    void SetArtworkActive(bool enabled);
    Task<bool> Command(string action, double level=0, string service="", string url="");
    Task<bool> LaunchPlay(string service, string url);
}
public interface IMusicService
{
    MusicSnapshot Current { get; }
    bool Initialized { get; }
    bool HasControlSession { get; }
    bool LaunchBridgeReady { get; }
    string CommandFailure { get; }
    Task<bool> PlayExisting();
    Task<bool> LaunchPlay(string service,string url);
    Task<bool> WaitUntil(Func<bool> ready,int timeout,CancellationToken cancellation);
}
public static class MusicCatalog
{
    public static bool AllowedSite(string site) => site is "music.yandex.ru" or "music.yandex.com" or "vk.com" or "vk.ru" or "music.vk.com";
}
public static class AudioMath
{
    public static float Follow(float current,float target,double seconds,double attack,double release) => current+(target-current)*(float)(1-Math.Exp(-seconds/(target>current?attack:release)));
}

public sealed class MusicSnapshot : IDisposable
{
    public string Title="",Artist="",Source="",AudioProcess="",PageUrl="";
    public int AudioProcessId;
    public bool Playing,CanPrevious,CanNext,CanSeek;
    public float Volume=-1;
    public byte[] Cover;
    public AccentColor Color=AccentColor.FromArgb(174,157,249), SecondaryColor=AccentColor.FromArgb(126,167,238);
    public bool Available=>!string.IsNullOrWhiteSpace(Title);
    public bool CanVolume=>Volume>=0;
    public double DurationSeconds,PreviewPosition=-1;
    private double basePosition,rate=1;
    private long stamp;
    public double PositionSeconds=>PreviewPosition>=0?PreviewPosition:Math.Clamp(basePosition+(Playing?(System.Diagnostics.Stopwatch.GetTimestamp()-stamp)/(double)System.Diagnostics.Stopwatch.Frequency*rate:0),0,DurationSeconds);
    public void SetTimeline(double position,double duration,double speed)
    {
        DurationSeconds=double.IsFinite(duration)&&duration>0&&duration<604800?duration:0;
        basePosition=double.IsFinite(position)?Math.Clamp(position,0,DurationSeconds):0;
        rate=double.IsFinite(speed)&&speed>0&&speed<=16?speed:1;
        stamp=System.Diagnostics.Stopwatch.GetTimestamp();
    }
    public void Dispose(){Cover=null;}
}
