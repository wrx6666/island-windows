using System;
using Color = Island.AccentColor;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Island
{
    public sealed class MusicService : IMusicService, IDisposable
    {
        private readonly Action<Action> dispatch;
        private GlobalSystemMediaTransportControlsSessionManager manager;
        private GlobalSystemMediaTransportControlsSession session;
        private bool disposed, artwork = true, toggling, started;
        private int revision;
        private string artKey = "";
        private MusicSnapshot systemCurrent = new MusicSnapshot();
        private readonly IMusicBridge browser;
        private readonly IArtworkDecoder decoder;
        private readonly Func<GlobalSystemMediaTransportControlsSession,bool> sessionFilter;
        public MusicSnapshot Current { get { return browser != null && browser.Current.Available ? browser.Current : systemCurrent; } }
        public string LastError = "";
        public string ArtworkError = "";
        public string CommandFailure { get; private set; } = "";
        public bool CommandPending { get { return toggling; } }
        public event Action Changed;
        public bool Initialized { get; private set; }
        public bool HasControlSession { get { return IsBrowser || session != null; } }
        public bool LaunchBridgeReady { get { return browser != null && browser.LaunchReady; } }
        public MusicService(Action<Action> send, IMusicBridge bridge, IArtworkDecoder artworkDecoder,Func<GlobalSystemMediaTransportControlsSession,bool> filter=null)
        {
            dispatch = send;
            decoder=artworkDecoder;sessionFilter=filter; browser=bridge; if(browser!=null) browser.Changed+=Notify;
        }

        // System winmds are split on Windows 11. A direct completion bridge avoids
        // requiring an SDK's merged Windows.winmd or any downloaded dependency.
        public static async Task<T> Await<T>(IAsyncOperation<T> operation)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            return await operation.AsTask(timeout.Token);
        }
        public async void Start()
        {
            if (started || disposed) return; started = true;
            if (browser != null) browser.Start();
            try
            {
                var result = await Await(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
                if (disposed) return;
                manager = result;
                manager.SessionsChanged += SessionsChanged;
                manager.CurrentSessionChanged += CurrentChanged;
                SelectSession();
            }
            catch (Exception ex) { LastError = ex.Message; }
            finally { Initialized = true; Notify(); }
        }
        private void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) { dispatch(SelectSession); }
        private void CurrentChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) { dispatch(SelectSession); }
        private void SelectSession()
        {
            if (disposed || manager == null) return;
            try
            {
                var selected = manager.GetCurrentSession();
                if(sessionFilter!=null)selected=manager.GetSessions().FirstOrDefault(sessionFilter);
                // Prefer a dedicated music client over an unrelated browser video.
                foreach (var candidate in sessionFilter==null?manager.GetSessions():Array.Empty<GlobalSystemMediaTransportControlsSession>())
                {
                    string id = candidate.SourceAppUserModelId.ToLowerInvariant();
                    if ((id.Contains("yandex") || id.Contains("vk") || id.Contains("music")) && candidate.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    { selected = candidate; break; }
                }
                if (!object.ReferenceEquals(session, selected))
                {
                    Unsubscribe(); session = selected; artKey = "";
                    if (session != null) { session.MediaPropertiesChanged += PropertiesChanged; session.PlaybackInfoChanged += PlaybackChanged; session.TimelinePropertiesChanged += TimelineChanged; }
                }
                Refresh();
            }
            catch (Exception ex) { LastError = ex.Message; }
        }
        private void PropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) { dispatch(delegate { artKey = ""; Refresh(); }); }
        private void PlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) { dispatch(RefreshPlayback); }
        private void TimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) { dispatch(delegate { RefreshTimeline(systemCurrent); Notify(); }); }
        private void RefreshTimeline(MusicSnapshot track)
        {
            if (session == null) return;
            var timeline = session.GetTimelineProperties(); var info = session.GetPlaybackInfo();
            track.SetTimeline((timeline.Position - timeline.StartTime).TotalSeconds, (timeline.EndTime - timeline.StartTime).TotalSeconds, info.PlaybackRate ?? 1);
            track.CanSeek = info.Controls.IsPlaybackPositionEnabled && track.DurationSeconds > 0;
        }
        private void RefreshPlayback()
        {
            if (disposed) return;
            try
            {
                var info = session == null ? null : session.GetPlaybackInfo();
                systemCurrent.Playing = info != null && info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                systemCurrent.CanPrevious = info != null && info.Controls.IsPreviousEnabled;
                systemCurrent.CanNext = info != null && info.Controls.IsNextEnabled; RefreshTimeline(systemCurrent); Notify();
            }
            catch (Exception ex) { LastError = ex.Message; }
        }
        public void SetArtworkActive(bool value)
        {
            if (disposed || artwork == value) return;
            artwork = value; artKey = ""; if (browser != null) browser.SetArtworkActive(value);
            if (!value) { revision++; systemCurrent.Dispose(); }
            else Refresh();
        }
        private async void Refresh()
        {
            if (disposed) return;
            int version = ++revision;
            var source = session;
            if (source == null) { Replace(new MusicSnapshot()); return; }
            try
            {
                var props = await Await(source.TryGetMediaPropertiesAsync());
                if (disposed || version != revision) return;
                string key = source.SourceAppUserModelId + "\n" + props.Title + "\n" + props.Artist;
                var info = source.GetPlaybackInfo();
                bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                if (key == artKey) { RefreshPlayback(); return; }
                var next = new MusicSnapshot { Title = props.Title, Artist = props.Artist, Source = source.SourceAppUserModelId, Playing = playing, CanPrevious = info.Controls.IsPreviousEnabled, CanNext = info.Controls.IsNextEnabled, Color = Fallback(key) };
                RefreshTimeline(next); next.SecondaryColor = SecondaryPalette(null, next.Color);
                ArtworkError = props.Thumbnail == null ? "No thumbnail supplied" : "";
                if (artwork && props.Thumbnail != null)
                {
                    try
                    {
                        using (var stream = await Await(props.Thumbnail.OpenReadAsync()))
                        {
                            if (stream.Size > 0 && stream.Size <= 5 * 1024 * 1024)
                            using (var reader = new DataReader(stream.GetInputStreamAt(0)))
                            {
                                uint count = await Await(reader.LoadAsync((uint)stream.Size));
                                byte[] bytes = new byte[count]; reader.ReadBytes(bytes);
                                if (disposed || version != revision || !artwork) { next.Dispose(); return; }
                                using (var memory = new MemoryStream(bytes))
                                {
                                    var decoded = await Task.Run(() => decoder.Decode(memory));
                                    if (disposed || version != revision || !artwork) { next.Dispose(); return; }
                                    next.Cover = decoded.Bytes; next.Color = decoded.Primary; next.SecondaryColor = decoded.Secondary;
                                }
                            }
                        }
                    }
                    catch (Exception ex) { ArtworkError = ex.ToString(); /* Metadata still works with missing/invalid artwork. */ }
                }
                if (disposed || version != revision) { next.Dispose(); return; }
                artKey = key; LastError = ""; Replace(next);
            }
            catch (Exception ex) { if (!disposed && version == revision) { LastError = ex.Message; Replace(new MusicSnapshot()); } }
        }
        public void DiagnosticTrack(MusicSnapshot track) { Replace(track); }
        private void Replace(MusicSnapshot next) { var old = systemCurrent; systemCurrent = next; if(!ReferenceEquals(old,next)) old.Dispose(); Notify(); }
        private void Notify() { if (!disposed && Changed != null) Changed(); }
        public bool IsBrowser { get { return browser != null && browser.Current.Available; } }
        private bool BrowserCommands
        {
            get
            {
                if (IsBrowser) return true;
                if (!LaunchBridgeReady) return false;
                string source = systemCurrent.Source.ToLowerInvariant();
                return source.Contains("yandex") || source.Contains("browser") || source.Contains("chrome") || source.Contains("msedge") || source.Contains("microsoftedge");
            }
        }
        public async Task<bool> SetVolume(float level)
        {
            if (!IsBrowser) return false;
            bool ok = await browser.Command("volume", level); CommandFailure = browser.CommandFailure; return ok;
        }
        public async Task<bool> Seek(double seconds)
        {
            if (disposed || toggling || !Current.CanSeek) return false;
            toggling = true; CommandFailure = "";
            try
            {
                seconds = Math.Max(0, Math.Min(Current.DurationSeconds, seconds));
                bool ok = IsBrowser ? await browser.Command("seek", seconds) : session != null && await Await(session.TryChangePlaybackPositionAsync((long)((session.GetTimelineProperties().StartTime.TotalSeconds + seconds) * TimeSpan.TicksPerSecond)));
                if (!ok) CommandFailure = IsBrowser ? browser.CommandFailure : "Плеер не подтвердил перемотку.";
                return ok;
            }
            catch (Exception ex) { CommandFailure = "Не удалось перемотать трек: " + ex.Message; return false; }
            finally { toggling = false; }
        }
        public Task<bool> Toggle() { return Command("toggle"); }
        public async Task<bool> PlayExisting()
        {
            if (Current.Playing) return true;
            if (BrowserCommands) { bool ok = await browser.Command("play"); CommandFailure = browser.CommandFailure; if (!ok) return false; if (await WaitUntil(() => Current.Playing, 2500, CancellationToken.None)) return true; CommandFailure = "Плеер не подтвердил начало воспроизведения."; return false; }
            try { bool ok = session != null && await Await(session.TryPlayAsync()); if (ok && await WaitUntil(() => Current.Playing, 2500, CancellationToken.None)) return true; }
            catch (Exception ex) { LastError = ex.Message; }
            CommandFailure = "Плеер не подтвердил начало воспроизведения."; return false;
        }
        public async Task<bool> LaunchPlay(string service, string url)
        {
            bool ok = browser != null && await browser.LaunchPlay(service, url);
            CommandFailure = browser == null ? "Музыкальное расширение недоступно." : browser.CommandFailure;
            if (!ok) return false;
            if (await WaitUntil(() => Current.Playing && MusicLaunchOptions.ServiceFor(Current.Source) == service, 2500, CancellationToken.None)) return true;
            CommandFailure = "Расширение ответило, но начало воспроизведения не подтверждено музыкальной сессией."; return false;
        }
        public async Task<bool> WaitUntil(Func<bool> ready, int timeout, CancellationToken cancellation)
        {
            if (ready()) return true;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action changed = () => { if (disposed || ready()) completion.TrySetResult(!disposed); };
            Changed += changed;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                deadline.CancelAfter(timeout);
                using (deadline.Token.Register(() => completion.TrySetResult(false)))
                try { changed(); return await completion.Task; }
                finally { Changed -= changed; }
            }
        }
        public Task<bool> Skip(bool next) { return Command(next ? "next" : "previous"); }
        private async Task<bool> Command(string action)
        {
            if (disposed || toggling) return false;
            toggling = true; CommandFailure = "";
            try
            {
                if (BrowserCommands)
                {
                    bool ok = await browser.Command(action); CommandFailure = browser.CommandFailure; return ok;
                }
                if (session == null)
                {
                    CommandFailure = browser != null && browser.Connected ? "Island Music Bridge подключён, но трек не обнаружен. Обнови вкладку Яндекс Музыки/VK и включи трек." : "Музыкальный плеер не обнаружен. Для Яндекс Браузера: Island.BrowserSetup.exe → «Подключить», затем открой Island Music Bridge в браузере и проверь соединение.";
                    return false;
                }
                bool accepted = action == "next" ? await Await(session.TrySkipNextAsync()) : action == "previous" ? await Await(session.TrySkipPreviousAsync()) : await Await(session.TryTogglePlayPauseAsync());
                if (!accepted) CommandFailure = "Плеер Windows отклонил команду. Для музыки в браузере проверь соединение Island Music Bridge; если музыку запускаешь в приложении, проверь управление в самом плеере.";
                return accepted;
            }
            catch (Exception ex) { LastError = ex.Message; CommandFailure = "Не удалось выполнить музыкальную команду. Перезапусти плеер и проверь соединение расширения, если музыка играет в браузере."; return false; }
            finally { toggling = false; }
        }
        public static Color Fallback(string key) => Color.Fallback(key);
        public static Color SecondaryPalette(object ignored, Color primary) => Color.FromArgb(primary.B, primary.R, primary.G);
        private void Unsubscribe()
        {
            if (session == null) return;
            session.MediaPropertiesChanged -= PropertiesChanged; session.PlaybackInfoChanged -= PlaybackChanged; session.TimelinePropertiesChanged -= TimelineChanged;
        }
        public void Dispose()
        {
            disposed = true; revision++; if (browser != null) browser.Dispose(); Unsubscribe();
            if (manager != null) { manager.SessionsChanged -= SessionsChanged; manager.CurrentSessionChanged -= CurrentChanged; }
            session = null; manager = null; systemCurrent.Dispose();
        }
    }
}
