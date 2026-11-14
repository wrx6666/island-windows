using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Island;

public sealed record LyricLine(double Seconds,string Text);
public sealed record LyricsDocument(LyricLine[] Lines,string PlainText,bool Instrumental=false)
{
    public static readonly LyricsDocument Missing=new([],"");
    private static readonly Regex Stamps=new(@"\[(\d{1,3}):(\d{2}(?:\.\d{1,3})?)\]",RegexOptions.CultureInvariant);
    private static readonly Regex Offset=new(@"\[offset:([+-]?\d+)\]",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    public static LyricsDocument Parse(string synced,string plain,bool instrumental=false)
    {
        if(instrumental)return new([],"",true);
        var lines=new List<LyricLine>();double offset=0;
        var shift=Offset.Match(synced??"");
        if(shift.Success && double.TryParse(shift.Groups[1].Value,CultureInfo.InvariantCulture,out double ms))offset=ms/1000;
        foreach(string row in (synced??"").Split('\n').Take(3000)){
            var stamps=Stamps.Matches(row);if(stamps.Count==0)continue;
            string text=Stamps.Replace(row,"").Trim();
            foreach(Match stamp in stamps){
                double seconds=double.Parse(stamp.Groups[2].Value,CultureInfo.InvariantCulture);
                if(seconds<60)lines.Add(new(Math.Max(0,int.Parse(stamp.Groups[1].Value,CultureInfo.InvariantCulture)*60+seconds+offset),text));
            }
        }
        return new(lines.OrderBy(l=>l.Seconds).ToArray(),(plain??"").Trim());
    }
    public int IndexAt(double seconds)
    {
        int lo=0,hi=Lines.Length;
        while(lo<hi){int mid=(lo+hi)/2;if(Lines[mid].Seconds<=seconds)lo=mid+1;else hi=mid;}
        return lo-1;
    }
}

// One bounded lookup per track, only while the lyrics page is visible. No polling or worker process.
public sealed class LyricsClient : IDisposable
{
    private readonly HttpClient http;
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly Dictionary<string,(LyricsDocument Document,DateTime Expires)> cache=new();
    private DateTime nextRequest;
    public LyricsClient(HttpMessageHandler handler=null)
    {
        http=handler==null?new HttpClient():new HttpClient(handler);
        http.Timeout=TimeSpan.FromSeconds(8);http.MaxResponseContentBufferSize=1024*1024;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("IslandWindows/1.0");
    }
    public static string Key(string title,string artist,double duration)=>title.Trim().ToLowerInvariant()+"\n"+artist.Trim().ToLowerInvariant()+"\n"+Math.Round(duration).ToString(CultureInfo.InvariantCulture);
    public async Task<LyricsDocument> GetAsync(string title,string artist,double duration,CancellationToken cancellation)
    {
        string key=Key(title,artist,duration);
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try{
            if(cache.TryGetValue(key,out var found) && found.Expires>DateTime.UtcNow)return found.Document;
            // Honor server backoff across manual retries and track changes, without keeping a waiting task alive.
            if(nextRequest-DateTime.UtcNow>TimeSpan.FromSeconds(1))throw new HttpRequestException("Lyrics service is temporarily unavailable");
            var delay=nextRequest-DateTime.UtcNow;if(delay>TimeSpan.Zero)await Task.Delay(delay,cancellation).ConfigureAwait(false);
            string url="https://lrclib.net/api/get?track_name="+Uri.EscapeDataString(title)+"&artist_name="+Uri.EscapeDataString(artist);
            if(duration>=1 && duration<=3600)url+="&duration="+Math.Round(duration).ToString(CultureInfo.InvariantCulture);
            using var response=await http.GetAsync(url,cancellation).ConfigureAwait(false);
            nextRequest=DateTime.UtcNow.AddMilliseconds(300);
            if(response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable){
                var retry=response.Headers.RetryAfter;
                nextRequest=(retry?.Date??DateTimeOffset.UtcNow.Add(retry?.Delta??TimeSpan.FromSeconds(60))).UtcDateTime;
                nextRequest=nextRequest.AddMilliseconds(300);
            }
            LyricsDocument result=LyricsDocument.Missing;
            if(response.StatusCode!=HttpStatusCode.NotFound){
                response.EnsureSuccessStatusCode();
                using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
                var root=json.RootElement;
                if(root.ValueKind!=JsonValueKind.Object)throw new JsonException("Invalid lyrics response");
                string Field(string name)=>root.TryGetProperty(name,out var value) && value.ValueKind==JsonValueKind.String?value.GetString():"";
                result=LyricsDocument.Parse(Field("syncedLyrics"),Field("plainLyrics"),root.TryGetProperty("instrumental",out var instrumental) && instrumental.ValueKind==JsonValueKind.True);
            }
            cancellation.ThrowIfCancellationRequested();
            if(cache.Count>=32)cache.Remove(cache.Keys.First());
            cache[key]=(result,DateTime.UtcNow.AddMinutes(result==LyricsDocument.Missing?5:360));return result;
        }finally{gate.Release();}
    }
    public void Dispose()=>http.Dispose();
}
