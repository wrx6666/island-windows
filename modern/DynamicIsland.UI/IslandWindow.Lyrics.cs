using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Island;
public partial class IslandWindow
{
    private readonly LyricsClient lyricsClient=new();
    private Border lyricsCard;
    private readonly TextBlock lyricStatus=new(),lyricCredit=new(),plainLyrics=new();
    private readonly SyncedLyricsView syncedLyrics=new();
    private readonly ScrollViewer plainLyricsScroll=new();
    private readonly Button lyricsRetry=new();
    private CancellationTokenSource lyricsCancellation;
    private LyricsDocument lyricsDocument;
    private string lyricsKey="",loadingLyricsKey="";
    private int lyricIndex=int.MinValue;
    private void InitializeLyrics()
    {
        var layout=new Grid{Margin=new(20,24,20,20)};
        layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new());layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=Text("ТЕКСТ ПЕСНИ",10.5,Gray);layout.Children.Add(heading);
        var content=new Grid{Margin=new(0,18,0,18),ClipToBounds=true};Grid.SetRow(content,1);layout.Children.Add(content);
        content.Children.Add(syncedLyrics);
        plainLyrics.FontSize=14;plainLyrics.LineHeight=23;plainLyrics.TextWrapping=TextWrapping.Wrap;
        plainLyricsScroll.Content=plainLyrics;plainLyricsScroll.VerticalScrollBarVisibility=ScrollBarVisibility.Hidden;plainLyricsScroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;content.Children.Add(plainLyricsScroll);
        var status=new StackPanel{VerticalAlignment=VerticalAlignment.Center};lyricStatus.FontSize=14;lyricStatus.Foreground=Gray;lyricStatus.TextWrapping=TextWrapping.Wrap;lyricStatus.LineHeight=22;status.Children.Add(lyricStatus);
        lyricsRetry.Content="Повторить";lyricsRetry.Style=(Style)FindResource("GlassIconButton");lyricsRetry.HorizontalAlignment=HorizontalAlignment.Left;lyricsRetry.Margin=new(0,14,0,0);lyricsRetry.Padding=new(0,8,0,8);lyricsRetry.Click+=(_,_)=>{lyricsKey="";UpdateLyricsActivity();};status.Children.Add(lyricsRetry);content.Children.Add(status);
        lyricCredit.FontSize=10.5;lyricCredit.Foreground=Gray;Grid.SetRow(lyricCredit,2);layout.Children.Add(lyricCredit);
        lyricsCard=Tile(layout);lyricsCard.Width=236;lyricsCard.Height=430;lyricsCard.ClipToBounds=true;Place(Expanded,lyricsCard,458,62);
        ShowLyricsStatus("Включите музыку — здесь появится текст");
    }
    private void CancelLyrics()
    {
        var cancellation=lyricsCancellation;lyricsCancellation=null;loadingLyricsKey="";
        cancellation?.Cancel();cancellation?.Dispose();
    }
    private void UpdateLyricsActivity()
    {
        if(lyricsCard==null)return;
        if(closing || !IsVisible || !expanded || page!="music"){CancelLyrics();syncedLyrics.Deactivate();return;}
        if(!track.Available){CancelLyrics();lyricsKey="";lyricsDocument=null;ShowLyricsStatus("Включите музыку — здесь появится текст");return;}
        string key=LyricsClient.Key(track.Title,track.Artist,track.DurationSeconds);
        if(key==lyricsKey){UpdateLyricPosition();return;}
        if(key==loadingLyricsKey)return;
        CancelLyrics();lyricsKey="";lyricsDocument=null;
        if(string.IsNullOrWhiteSpace(track.Artist)){lyricsKey=key;ShowLyricsStatus("Для поиска текста нужен исполнитель");return;}
        if((Application.Current as App)?.DiagnosticHost==true){lyricsKey=key;ApplyLyrics(LyricsDocument.Missing);return;}
        loadingLyricsKey=key;lyricsCancellation=new();ShowLyricsStatus("Ищем текст песни…");
        _=LoadLyricsAsync(key,track.Title,track.Artist,track.DurationSeconds,lyricsCancellation.Token);
    }
    private async Task LoadLyricsAsync(string key,string title,string artist,double duration,CancellationToken cancellation)
    {
        try{
            var document=await lyricsClient.GetAsync(title,artist,duration,cancellation);
            if(cancellation.IsCancellationRequested || closing || loadingLyricsKey!=key)return;
            lyricsKey=key;loadingLyricsKey="";ApplyLyrics(document);
        }catch(OperationCanceledException) when(cancellation.IsCancellationRequested){}
        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or JsonException){
            if(cancellation.IsCancellationRequested || closing || loadingLyricsKey!=key)return;
            lyricsKey=key;loadingLyricsKey="";ShowLyricsStatus("Не удалось загрузить текст. Проверьте подключение к интернету.",true);
        }
    }
    private void ShowLyricsStatus(string text,bool retry=false)
    {
        syncedLyrics.Deactivate();syncedLyrics.Visibility=plainLyricsScroll.Visibility=Visibility.Collapsed;
        lyricStatus.Text=text;lyricStatus.Visibility=Visibility.Visible;lyricsRetry.Visibility=retry?Visibility.Visible:Visibility.Collapsed;lyricCredit.Text="";
    }
    private void ApplyLyrics(LyricsDocument document)
    {
        lyricsDocument=document;lyricIndex=int.MinValue;
        if(document.Instrumental){ShowLyricsStatus("Инструментальная композиция\nБез текста");return;}
        if(document.Lines.Length==0 && string.IsNullOrWhiteSpace(document.PlainText)){ShowLyricsStatus("Текст для этого трека пока не найден");return;}
        lyricStatus.Visibility=lyricsRetry.Visibility=Visibility.Collapsed;
        bool synced=document.Lines.Length>0;
        syncedLyrics.SetDocument(document);
        syncedLyrics.Visibility=synced?Visibility.Visible:Visibility.Collapsed;plainLyricsScroll.Visibility=synced?Visibility.Collapsed:Visibility.Visible;
        plainLyrics.Text=synced?"":document.PlainText;plainLyricsScroll.ScrollToTop();
        lyricCredit.Text=synced?"LRCLIB · Синхронизировано":"LRCLIB · Без синхронизации";
        UpdateLyricPosition();
    }
    private void UpdateLyricPosition()
    {
        if(!IsVisible || !expanded || page!="music" || lyricsDocument==null || lyricsDocument.Lines.Length==0)return;
        int index=lyricsDocument.IndexAt(track.PositionSeconds);if(index==lyricIndex)return;lyricIndex=index;
        syncedLyrics.Select(index,Config.Animations && systemMotion);
        if(syncedLyrics.Animating)StartFrames();
    }
}
