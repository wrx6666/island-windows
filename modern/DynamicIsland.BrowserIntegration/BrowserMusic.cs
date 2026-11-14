using System;
using System.Collections.Generic;

using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;


namespace Island
{
    public sealed class BrowserMusic : IMusicBridge
    {
        private static readonly HttpClient http = new(new SocketsHttpHandler { AllowAutoRedirect=false, PooledConnectionLifetime=TimeSpan.FromMinutes(10) });
        private readonly Action<Action> dispatch;
        private readonly object gate = new object();
        private readonly SemaphoreSlim writer = new SemaphoreSlim(1, 1);
        private sealed class Reply { public bool Ok; public string Reason = ""; }
        private readonly Dictionary<int, TaskCompletionSource<Reply>> pending = new Dictionary<int, TaskCompletionSource<Reply>>();
        private NamedPipeServerStream pipe;
        private volatile bool disposed;
        private bool artwork = true;
        private int revision, sequence, started;
        private CancellationTokenSource coverRequest;
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private string key = "", coverUrl = "";
        public MusicSnapshot Current { get; private set; } = new MusicSnapshot();
        public string LastError = "";
        public string CommandFailure { get; private set; } = "";
        public string CommandReason { get; private set; } = "";
        public bool Connected { get { lock (gate) return !disposed && pipe != null && pipe.IsConnected; } }
        public bool LaunchReady { get; private set; }
        public event Action Changed;
        private readonly IArtworkDecoder decoder;
        public BrowserMusic(Action<Action> send,IArtworkDecoder artworkDecoder) { dispatch=send; decoder=artworkDecoder; }
        public void Start() { if (Interlocked.Exchange(ref started, 1) == 0) Task.Run(async()=>{try{await Listen();}catch(Exception ex){LastError=ex.Message;dispatch(Notify);}}); }
        private async Task Listen()
        {
            int retry = 250;
            using (var ready = BrowserProtocol.OpenReadyEvent())
            while (!disposed)
            {
                NamedPipeServerStream server = null;
                var updates = new UiLatest<Dictionary<string, object>>(dispatch, Apply);
                try
                {
                    var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
                    security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    server = NamedPipeServerStreamAcl.Create(BrowserProtocol.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 16384, 16384, security);
                    lock (gate) { if (disposed) { server.Dispose(); return; } pipe = server; }
                    ready.Set();
                    await Task.Factory.FromAsync(server.BeginWaitForConnection, server.EndWaitForConnection, null).ConfigureAwait(false);
                    retry = 250; var parser = new JsonCodec();
                    string json;
                    while ((json = await BrowserProtocol.Read(server).ConfigureAwait(false)) != null)
                    {
                        var message = parser.DeserializeObject(json) as Dictionary<string, object>;
                        if (message == null) continue;
                        if (Text(message, "type", 16) == "ack")
                        {
                            object raw; int id;
                            if (!message.TryGetValue("id", out raw) || !int.TryParse(raw.ToString(), out id)) continue;
                            TaskCompletionSource<Reply> completion;
                            lock (gate) if (pending.TryGetValue(id, out completion)) completion.TrySetResult(new Reply { Ok = Flag(message, "ok"), Reason = Text(message, "reason", 48) });
                        }
                        else if (Text(message, "type", 16) == "bridge") dispatch(delegate { if (!disposed) { LaunchReady = Number(message, "version") >= 1; Notify(); } });
                        else if (Text(message, "type", 16) == "track") updates.Post(message);
                    }
                }
                catch (Exception ex)
                {
                    if (!(ex is System.Text.Json.JsonException) && !(ex is IOException) && !(ex is ObjectDisposedException) && !(ex is UnauthorizedAccessException) && !(ex is ArgumentException) && !(ex is InvalidOperationException)) throw;
                    LastError = ex.Message;
                }
                finally
                {
                    updates.Dispose();
                    lock (gate)
                    {
                        if (pipe == server) pipe = null;
                        foreach (var completion in pending.Values) completion.TrySetResult(new Reply { Reason = "disconnected" });
                        pending.Clear();
                    }
                    if (server != null) server.Dispose();
                    dispatch(delegate { LaunchReady = false; Clear(); });
                }
                if (!disposed)
                {
                    try { await Task.Delay(retry, shutdown.Token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                    retry = Math.Min(15000, retry * 2);
                }
            }
        }
        private static string Text(Dictionary<string, object> data, string name, int limit)
        {
            object raw; string value = data.TryGetValue(name, out raw) ? raw as string : "";
            value = value ?? ""; return value.Length > limit ? value.Substring(0, limit) : value;
        }
        private static bool Flag(Dictionary<string, object> data, string name) { object raw; return data.TryGetValue(name, out raw) && raw is bool && (bool)raw; }
        private static double Number(Dictionary<string, object> data, string name)
        {
            object raw; double value;
            return data.TryGetValue(name, out raw) && raw != null && double.TryParse(Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value) ? value : 0;
        }
        private static void Timeline(MusicSnapshot track, Dictionary<string, object> data)
        {
            track.SetTimeline(Number(data, "position"), Number(data, "duration"), Number(data, "rate"));
            track.CanSeek = Flag(data, "canSeek") && track.DurationSeconds > 0;
        }
        private void Apply(Dictionary<string, object> data)
        {
            if (disposed) return;
            string site = Text(data, "site", 128), title = Text(data, "title", 640), artist = Text(data, "artist", 640), url = Text(data, "artwork", 4096);
            if (!AllowedSite(site)) return;
            if (string.IsNullOrWhiteSpace(title)) { Clear(); return; }
            string nextKey = site + "\n" + title + "\n" + artist + "\n" + url;
            string page = MusicLaunchOptions.SafeUrl(Text(data, "pageUrl", 2048), MusicLaunchOptions.ServiceFor(site));
            float volume = -1; object rawVolume;
            if (data.TryGetValue("volume", out rawVolume) && (rawVolume is double || rawVolume is int || rawVolume is decimal)) { double v = Convert.ToDouble(rawVolume); if (v >= 0 && v <= 1) volume = (float)v; }
            string process = Text(data, "audioProcess", 100);
            if (process != "browser" && process != "chrome" && process != "msedge") process = "";
            object rawPid; int pid = 0;
            if (data.TryGetValue("audioProcessId", out rawPid) && rawPid is int && (int)rawPid > 0 && process.Length > 0) pid = (int)rawPid;
            if (key == nextKey)
            {
                double predicted = Current.PositionSeconds, duration = Current.DurationSeconds; bool seek = Current.CanSeek;
                bool changed = Current.PageUrl != page || Current.Volume != volume || Current.AudioProcess != process || Current.AudioProcessId != pid || Current.Playing != Flag(data, "playing") || Current.CanPrevious != Flag(data, "canPrevious") || Current.CanNext != Flag(data, "canNext");
                Current.PageUrl = page;
                Current.AudioProcessId = pid;
                Current.Volume = volume; Current.AudioProcess = process; Current.Playing = Flag(data, "playing"); Current.CanPrevious = Flag(data, "canPrevious"); Current.CanNext = Flag(data, "canNext"); Timeline(Current, data);
                if (changed || duration != Current.DurationSeconds || seek != Current.CanSeek || Math.Abs(predicted - Current.PositionSeconds) > 0.5) Notify(); return;
            }
            CancelCover(); revision++; key = nextKey; coverUrl = AllowedCover(url) ? url : "";
            var old = Current;
            Current = new MusicSnapshot { Title = title, Artist = artist, Source = site, Volume = volume, AudioProcess = process, Playing = Flag(data, "playing"), CanPrevious = Flag(data, "canPrevious"), CanNext = Flag(data, "canNext"), Color = AccentColor.Fallback(nextKey) };
            Timeline(Current, data); Current.SecondaryColor = AccentColor.FromArgb(Current.Color.B,Current.Color.R,Current.Color.G);
            Current.PageUrl = page;
            Current.AudioProcessId = pid;
            old.Dispose(); LastError = ""; Notify(); LoadCover();
        }
        public static bool AllowedSite(string site)
        {
            return site == "music.yandex.ru" || site == "music.yandex.com" || site == "vk.com" || site == "vk.ru" || site == "music.vk.com";
        }
        public static bool AllowedCover(string value)
        {
            Uri uri; if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) return false;
            string host = uri.DnsSafeHost.ToLowerInvariant();
            return host == "avatars.yandex.net" || host.EndsWith(".yandex.net", StringComparison.Ordinal) && host.StartsWith("avatars", StringComparison.Ordinal) || host.EndsWith(".userapi.com", StringComparison.Ordinal) || host.EndsWith(".vkuserphoto.ru", StringComparison.Ordinal) || host.EndsWith(".vkuseraudio.net", StringComparison.Ordinal);
        }
        private void CancelCover() { var request = coverRequest; coverRequest = null; if (request != null) request.Cancel(); }
        private async void LoadCover()
        {
            if (!artwork || disposed || string.IsNullOrEmpty(coverUrl) || !Current.Available) return;
            CancelCover(); var cancellation = new CancellationTokenSource(6000); coverRequest = cancellation;
            int version = ++revision; string url = coverUrl;
            try
            {
                using (var stream = await http.GetStreamAsync(url, cancellation.Token))
                using (var memory = new MemoryStream())
                {
                    var buffer = new byte[16384]; int size;
                    while ((size = await stream.ReadAsync(buffer.AsMemory(), cancellation.Token)) > 0)
                    {
                        if (memory.Length + size > 5 * 1024 * 1024) throw new IOException("Artwork exceeds limit.");
                        memory.Write(buffer,0,size);
                    }
                    if (disposed || !artwork || version != revision) return;
                    memory.Position = 0;
                    var decoded = await Task.Run(() => decoder.Decode(memory), cancellation.Token);
                    {
                        if (disposed || !artwork || version != revision) return;
                        Current.Dispose(); Current.Cover = decoded.Bytes; Current.Color = decoded.Primary; Current.SecondaryColor = decoded.Secondary;
                    }
                    Notify();
                }
            }
            catch (Exception ex)
            {
                if (!(ex is HttpRequestException) && !(ex is IOException) && !(ex is ArgumentException) && !(ex is OperationCanceledException)) throw;
                if (!disposed && version == revision) LastError = ex.Message;
            }
            finally { if (coverRequest == cancellation) coverRequest = null; cancellation.Dispose(); }
        }
        public void SetArtworkActive(bool value)
        {
            if (disposed || artwork == value) return;
            artwork = value; revision++; CancelCover();
            if (!value) Current.Dispose(); else LoadCover();
        }
        public Task<bool> Toggle() { return Command("toggle"); }
        public Task<bool> LaunchPlay(string service, string url) { return Command("launch_play", 0, service, url); }
        public async Task<bool> Command(string action, double level = 0, string service = "", string url = "")
        {
            CommandFailure = "";CommandReason="";
            if (action != "toggle" && action != "play" && action != "launch_play" && action != "next" && action != "previous" && action != "volume" && action != "seek") return false;
            if (action == "launch_play" && MusicLaunchOptions.SafeUrl(url, service).Length == 0) return false;
            if (action == "seek" && (!Current.CanSeek || double.IsNaN(level) || double.IsInfinity(level))) return false;
            if (Current.Available && (action == "next" && !Current.CanNext || action == "previous" && !Current.CanPrevious)) { CommandFailure = FailureText("unsupported"); return false; }
            int id = Interlocked.Increment(ref sequence); NamedPipeServerStream channel;
            var completion = new TaskCompletionSource<Reply>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate)
            {
                channel = pipe; if (disposed || channel == null || !channel.IsConnected || (action == "launch_play" ? !LaunchReady : !Current.Available && !LaunchReady)) { CommandFailure = FailureText("disconnected"); return false; }
                pending[id] = completion;
            }
            try
            {
                // Deadline includes writer contention and the write itself, not only the ACK.
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                deadline.CancelAfter(action=="launch_play"?18000:3500);
                await writer.WaitAsync(deadline.Token);
                try {
                    try {await BrowserProtocol.Write(channel, new JsonCodec().Serialize(new { type = action, id = id, service = service, url = url, volume = Math.Max(0, Math.Min(1, level)), position = Math.Max(0, Math.Min(Current.DurationSeconds, level)) }),deadline.Token);}
                    catch(OperationCanceledException){channel.Dispose();throw;} // Partial frame must not be reused.
                }
                finally { writer.Release(); }
                Reply reply = await completion.Task.WaitAsync(deadline.Token);
                if (!reply.Ok) {CommandReason=reply.Reason;CommandFailure = FailureText(reply.Reason);}
                return reply.Ok;
            }
            catch (IOException) { CommandFailure = FailureText("disconnected"); return false; }
            catch (ObjectDisposedException) { CommandFailure = FailureText("disconnected"); return false; }
            catch (OperationCanceledException) {CommandFailure=FailureText(disposed?"disconnected":"page_timeout");return false;}
            finally { lock (gate) pending.Remove(id); }
        }
        private static string FailureText(string reason)
        {
            if (reason == "offline") return "Нет подключения к интернету. Подключись к сети и повтори Play.";
            if (reason == "player_not_ready") return "Музыкальная вкладка открыта, но плеер не готов. Проверь интернет, вход в аккаунт и доступность сервиса; затем повтори Play.";
            if (reason == "no_player" || reason == "no_tab") return "Расширение подключено, но плеер не найден. Обнови вкладку Яндекс Музыки/VK и включи трек.";
            if (reason == "play_blocked") return "Браузер не разрешил запуск музыки. Один раз нажми «Воспроизвести» на музыкальной вкладке, затем повтори сочетание.";
            if (reason == "disconnected") return "Связь с расширением прервалась. Запусти Island и проверь статус Island Music Bridge в браузере.";
            if (reason == "unsupported") return "Плеер не предоставляет эту команду переключения трека.";
            if (reason == "tab_disconnected" || reason == "page_timeout") return "Музыкальная вкладка не ответила. Обнови её; после обновления расширения перезагрузи и Island Music Bridge.";
            return "Связь с расширением есть, но плеер не подтвердил команду. Проверь кнопку воспроизведения на музыкальной вкладке.";
        }
        private void Notify() { if (!disposed && Changed != null) Changed(); }
        private void Clear()
        {
            if (disposed) return;
            CancelCover(); revision++; key = coverUrl = ""; var old = Current; Current = new MusicSnapshot(); old.Dispose(); Notify();
        }
        public void Dispose()
        {
            disposed = true; revision++; shutdown.Cancel(); CancelCover();
            lock (gate) { if (pipe != null) pipe.Dispose(); foreach (var completion in pending.Values) completion.TrySetResult(new Reply { Reason = "disconnected" }); pending.Clear(); }
            Current.Dispose();
        }
    }
}
