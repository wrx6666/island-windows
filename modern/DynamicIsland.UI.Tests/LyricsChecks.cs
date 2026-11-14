using System.Net;
using System.Net.Http;

namespace Island;
public static class LyricsChecks
{
    private sealed class Reply(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)=>send(request,cancellation);
    }
    public static async Task Run()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        var timed=LyricsDocument.Parse("[ar:Test]\n[00:01.25][00:05.500]Повтор\n[01:02]Вторая строка\n[01:61]Некорректно\n[01:05]","");
        Check(timed.Lines.Length==4 && timed.IndexAt(0)==-1 && timed.IndexAt(5.5)==1 && timed.IndexAt(62)==2,"LRC timestamps, repeated lines and invalid seconds");
        Check(timed.IndexAt(3)==0 && timed.IndexAt(100)==3,"LRC must support backward/forward seeking and empty instrumental breaks");
        Check(LyricsDocument.Parse("","plain text").PlainText=="plain text" && LyricsDocument.Parse("[00:01]Text","",true).Instrumental,"Plain and instrumental lyrics");
        int requests=0;
        using var client=new LyricsClient(new Reply((request,_)=>{
            requests++;Check(request.RequestUri.Host=="lrclib.net" && request.RequestUri.Query.Contains("duration=123"),"Lyrics lookup uses track signature");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"syncedLyrics\":\"[00:01]Test\",\"plainLyrics\":\"Test\",\"instrumental\":false}")});
        }));
        var document=await client.GetAsync("Название & трек","Исполнитель",123,CancellationToken.None);
        Check(document.Lines.Single().Text=="Test","Parse real provider response shape");
        await client.GetAsync("Название & трек","Исполнитель",123,CancellationToken.None);Check(requests==1,"Cached lyrics must avoid repeat requests");
        requests=0;
        using var missing=new LyricsClient(new Reply((_,_)=>{requests++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));}));
        Check((await missing.GetAsync("Missing","Artist",123,CancellationToken.None)).Lines.Length==0,"404 must display unavailable lyrics");
        await missing.GetAsync("Missing","Artist",123,CancellationToken.None);Check(requests==1,"Negative result is cached");
        using var limited=new LyricsClient(new Reply((_,_)=>{requests++;var response=new HttpResponseMessage(HttpStatusCode.TooManyRequests);response.Headers.RetryAfter=new(TimeSpan.FromMinutes(1));return Task.FromResult(response);}));
        requests=0;
        for(int i=0;i<2;i++)try{await limited.GetAsync("Limited","Artist",123,CancellationToken.None);throw new Exception("Expected rate-limit error");}catch(HttpRequestException){}
        Check(requests==1,"Server retry-after must block subsequent network requests");
        var started=new TaskCompletionSource();
        using var cancelled=new LyricsClient(new Reply(async (_,token)=>{started.SetResult();await Task.Delay(Timeout.Infinite,token);return new HttpResponseMessage(HttpStatusCode.OK);}));
        using var cancellation=new CancellationTokenSource();var pending=cancelled.GetAsync("Slow","Artist",123,cancellation.Token);await started.Task;cancellation.Cancel();
        try{await pending;throw new Exception("Expected canceled lookup");}catch(OperationCanceledException){}
    }
}
