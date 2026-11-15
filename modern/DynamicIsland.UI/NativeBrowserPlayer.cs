using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Island;

public sealed class NativeBrowserPlayer : IMusicLaunchProvider,IDisposable
{
    private readonly MusicService music;
    private readonly AudioMixer mixer;
    private readonly BrowserAccessibility access;
    private DateTime lastOpen=DateTime.MinValue;
    private string lastTarget="";
    public string Failure {get;private set;}="";
    public event Action<string> StatusChanged;
    public NativeBrowserPlayer(MusicService service,AudioMixer audio)
    {music=service;mixer=audio;access=new(()=>music.StateRevision);}
    public async Task<bool> Play(string browser,string service,string url,CancellationToken cancellation)
    {
        Failure="";nint shownWindow=0;
        try
        {
            string executable=await Task.Run(()=>BrowserLaunch.Resolve(browser),cancellation);
            string process=Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
            music.PreferBrowser(process);
            if(music.HasNativeBrowserSession && await music.PlayExisting() && music.NativeBrowserPlaying)
            {
                music.NativeVolume(await mixer.MusicVolume(process,null));
                return true;
            }
            cancellation.ThrowIfCancellationRequested();
            var timer=Stopwatch.StartNew();bool opened=false;int delay=200;
            while(timer.Elapsed<TimeSpan.FromSeconds(25))
            {
                cancellation.ThrowIfCancellationRequested();
                BrowserPageResult page;
                try{page=await access.FindAsync(executable,service,true,cancellation);}
                catch(Exception ex)when(ex is ElementNotAvailableException or COMException){page=new();}
                if(page.Playing || page.Invoked)
                {
                    StatusChanged?.Invoke("Ждём подтверждения воспроизведения от Windows…");
                    bool confirmed=await music.WaitUntil(()=>music.NativeBrowserPlaying && (page.Playing || music.StateRevision>page.BeforeInvokeRevision),8000,cancellation);
                    if(!confirmed)return Fail("Play отправлен странице, но Windows не подтвердила музыку. Проверь вход в Яндекс Музыку и доступность воспроизведения; расширение не требуется.");
                    float volume=await mixer.MusicVolume(process,null);music.NativeVolume(volume);
                    return true;
                }
                if(page.Found && page.Minimized && shownWindow==0)
                {
                    // Some Chromium providers omit a minimized page. Restore behind
                    // the foreground window; never use SetFocus/SetForegroundWindow.
                    BrowserAccessibility.ShowWithoutActivation(page.Window);shownWindow=page.Window;
                }
                if(!page.Found && !opened)
                {
                    string target=executable+"\n"+service;
                    if(target!=lastTarget || DateTime.UtcNow-lastOpen>TimeSpan.FromSeconds(30))
                    {
                        StatusChanged?.Invoke("Открываем музыку в браузере без расширения…");
                        await BrowserLaunch.StartMusicWindow(browser,url);lastTarget=target;lastOpen=DateTime.UtcNow;
                    }
                    opened=true;
                }
                StatusChanged?.Invoke("Ждём кнопку воспроизведения на музыкальной странице…");
                await Task.Delay(delay,cancellation);delay=Math.Min(1500,delay*2);
            }
            return Fail("Музыкальная страница не предоставила кнопку Play через Windows. Проверь интернет и вход в аккаунт. Возможно, браузеру нужно один раз показать окно; команда не имитировала нажатия клавиш и не меняла защиту браузера.");
        }
        catch(OperationCanceledException){return false;}
        catch(Exception ex)when(ex is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or COMException)
        {return Fail("Не удалось запустить музыку через Windows: "+ex.Message);}
        finally{if(shownWindow!=0)BrowserAccessibility.MinimizeWithoutActivation(shownWindow);}
    }
    private bool Fail(string message){Failure=message;return false;}
    public void Dispose()=>access.Dispose();
}
