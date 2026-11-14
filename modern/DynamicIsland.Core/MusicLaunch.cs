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
        public string HistoryError = "";
        public readonly LastMusicSession Last = LastMusicSession.Load();
        public string Status = "", Failure = "";
        public int WaitMilliseconds = 20000;
        public Func<string, bool> BrowserRunning ;
        public Func<string, string, Task> StartBrowser ;
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
                if (music.HasControlSession) { bool accepted = await music.PlayExisting(); if (!accepted) Failure = music.CommandFailure; State(accepted ? "Музыка играет" : Failure); return accepted; }
                if (!settings.Launch.OnPlay) return Fail("Запуск сервиса по Play отключён в настройках.");
                string service = settings.Launch.Restore && Last.Available ? Last.Service : settings.Launch.Service;
                string url = settings.Launch.Restore && Last.Available && Last.Url != null && Last.Url.Length > 0 ? Last.Url : MusicLaunchOptions.Home(service);
                if (!music.LaunchBridgeReady)
                {
                    if (await Task.Run(() => BrowserRunning(settings.Launch.Browser))) State("Ждём Island Music Bridge…");
                    else
                    {
                        State("Открываем музыкальный сервис…");
                        if ((DateTime.UtcNow - lastBrowserStart).TotalSeconds < 30) return Fail("Браузер уже запускался. Подожди подключения расширения и повтори Play через несколько секунд.");
                        lastBrowserStart = DateTime.UtcNow;
                        await StartBrowser(settings.Launch.Browser, url);
                    }
                    if (!await music.WaitUntil(() => music.LaunchBridgeReady, WaitMilliseconds, stop.Token) && !stop.IsCancellationRequested)
                        return Fail("Браузер не подключил обновлённый Island Music Bridge. Включи расширение 0.10.0 в выбранном профиле и обнови музыкальную вкладку. Повторная вкладка не создавалась.");
                }
                if (stop.IsCancellationRequested) return false;
                if (music.HasControlSession) { bool accepted = await music.PlayExisting(); if (!accepted) return Fail(music.CommandFailure); State("Музыка играет"); return true; }
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
