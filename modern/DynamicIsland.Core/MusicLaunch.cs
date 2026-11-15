using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;


namespace Island
{
    public sealed class MusicLaunch : IDisposable, IAsyncDisposable
    {
        private readonly IMusicService music;
        private readonly Settings settings;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private Task<bool> pending;
        private readonly object historyGate = new object();
        private LastMusicSession nextHistory;
        private Task historyWrite;
        private DateTime lastBrowserStart = DateTime.MinValue;
        private DateTime lastNavigation = DateTime.MinValue;
        public string HistoryError = "";
        public readonly LastMusicSession Last = LastMusicSession.Load();
        public string Status = "", Failure = "";
        public int WaitMilliseconds = 20000;
        public int StartupBridgeWaitMilliseconds = 5000, ExistingBridgeWaitMilliseconds = 1500;
        public Func<string, bool> BrowserRunning ;
        public Func<string, string, Task> StartBrowser ;
        public IMusicLaunchProvider NativePlayer {get;set;}
        public event Action Changed;
        public bool Busy { get { return pending != null && !pending.IsCompleted; } }
        public MusicLaunch(IMusicService service, Settings configuration, Func<string,bool> running, Func<string,string,Task> start) { music = service; settings = configuration; BrowserRunning=running; StartBrowser=start; }
        public Task<bool> Play()
        {
            if (Busy) return pending;
            pending = Run(); return pending;
        }
        private void State(string message) { Status = message; if (Changed != null) Changed(); }
        public void Observe(MusicSnapshot track)
        {
            if (!Last.Update(track)) return;
            var snapshot = new LastMusicSession { Service = Last.Service, Url = Last.Url, Title = Last.Title, Artist = Last.Artist };
            lock (historyGate)
            {
                nextHistory = snapshot;
                if (historyWrite == null) historyWrite = Task.Run((Action)WriteHistory);
            }
        }
        private void WriteHistory()
        {
            while (true)
            {
                LastMusicSession value;
                lock (historyGate) { value = nextHistory; nextHistory = null; if (value == null) { historyWrite = null; return; } }
                try { value.Save(); HistoryError = ""; }
                catch (IOException ex) { HistoryError = ex.Message; }
                catch (UnauthorizedAccessException ex) { HistoryError = ex.Message; }
            }
        }
        private async Task<bool> Run()
        {
            Failure = "";
            try
            {
                State("Проверяем музыкальную сессию…");
                if (!music.Initialized) await music.WaitUntil(() => music.Initialized, 2000, stop.Token);
                string service = settings.Launch.Restore && Last.Available ? Last.Service : settings.Launch.Service;
                string url = settings.Launch.Restore && Last.Available && Last.Url != null && Last.Url.Length > 0 ? Last.Url : MusicLaunchOptions.Home(service);
                if(!settings.Launch.UseExtension)
                {
                    if(!settings.Launch.OnPlay)
                    {
                        if(music.HasControlSession && await music.PlayExisting()){State("Музыка играет");return true;}
                        return Fail("Запуск сервиса по Play отключён в настройках.");
                    }
                    if(NativePlayer==null)return Fail("Подключение браузера через Windows недоступно.");
                    State("Подключаем музыкальный браузер через Windows…");
                    bool playing=await NativePlayer.Play(settings.Launch.Browser,service,url,stop.Token);
                    if(!playing)return Fail(NativePlayer.Failure);
                    State("Музыка играет");return true;
                }
                if (music.HasControlSession)
                {
                    if (await music.PlayExisting()) { State("Музыка играет"); return true; }
                    // A stale Windows media session must not prevent opening the service.
                    // A connected browser already supplies an actionable player error.
                    if (music.LaunchBridgeReady || !settings.Launch.OnPlay) return Fail(music.CommandFailure);
                }
                if (!settings.Launch.OnPlay) return Fail("Запуск сервиса по Play отключён в настройках.");
                if (!music.LaunchBridgeReady)
                {
                    bool running = await Task.Run(() => BrowserRunning(settings.Launch.Browser));
                    if (!running)
                    {
                        State("Запускаем браузер для музыки…");
                        if ((DateTime.UtcNow - lastBrowserStart).TotalSeconds < 30) return Fail("Браузер уже запускался. Подожди подключения расширения и повтори Play через несколько секунд.");
                        lastBrowserStart = DateTime.UtcNow;
                        // Let the bridge reuse restored tabs before opening a URL ourselves.
                        await StartBrowser(settings.Launch.Browser, "");
                    }
                    State("Ждём подключения музыкального управления…");
                    await music.WaitUntil(() => music.LaunchBridgeReady, running ? ExistingBridgeWaitMilliseconds : StartupBridgeWaitMilliseconds, stop.Token);
                    if (stop.IsCancellationRequested) return false;
                    if (!music.LaunchBridgeReady)
                    {
                        // A background browser process is not proof of an open window or
                        // enabled extension. Open the service once, rather than waiting forever.
                        if ((DateTime.UtcNow - lastNavigation).TotalSeconds < 30)
                            return Fail("Музыкальный сервис уже открыт. Включи Island Music Bridge в браузере и повтори Play; новая вкладка не создавалась.");
                        lastNavigation = DateTime.UtcNow;
                        State("Открываем музыкальный сервис…");
                        await StartBrowser(settings.Launch.Browser, url);
                        if (!await music.WaitUntil(() => music.LaunchBridgeReady, WaitMilliseconds, stop.Token))
                            return stop.IsCancellationRequested ? false : Fail("Сервис открыт, но Island Music Bridge не подключён. Включи расширение в выбранном профиле браузера. Без него Island может управлять только уже доступным плеером Windows и не может гарантировать автоматическое начало музыки.");
                    }
                }
                if (stop.IsCancellationRequested) return false;
                // Route through the selected service, not an unrelated/stale Windows session.
                State("Ждём плеер и запускаем музыку…");
                bool ok = await music.LaunchPlay(service, url);
                if (!ok) return Fail(music.CommandFailure);
                State("Музыка играет"); return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex) { return Fail("Не удалось запустить музыку: " + ex.Message); }
        }
        private bool Fail(string text) { Failure = text; State(text); return false; }
        public void Dispose() { stop.Cancel(); }
        public async ValueTask DisposeAsync() {stop.Cancel();Task writing;lock(historyGate)writing=historyWrite;if(writing!=null)await writing.ConfigureAwait(false);stop.Dispose();}
    }
}
