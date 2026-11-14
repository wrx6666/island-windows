using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using System.Net.Http;

namespace Island
{
    public sealed class WeatherService : IDisposable
    {
        // Moscow city centre default. No GPS/IP lookup.
        public const string Endpoint = "https://api.open-meteo.com/v1/forecast?latitude=55.7558&longitude=37.6173&current=temperature_2m,apparent_temperature,weather_code,is_day,relative_humidity_2m,wind_speed_10m,surface_pressure&wind_speed_unit=ms&timeformat=unixtime&hourly=temperature_2m,weather_code&daily=sunrise,sunset,uv_index_max,precipitation_sum&forecast_days=1";
        private readonly string cachePath = Path.Combine(Path.GetDirectoryName(Settings.FilePath), "weather-cache.json");
        private readonly OneShot refresh;
        private static readonly HttpClient http = new() { Timeout=TimeSpan.FromSeconds(8) };
        private CancellationTokenSource request;
        private bool active, disposed;
        private DateTime nextAttempt = DateTime.MinValue;
        public WeatherSnapshot Current { get; private set; }
        public event Action Changed;
        public string LastError = "";
        public bool Active { get { return active; } }
        public bool TimerEnabled { get { return refresh.Enabled; } }
        public WeatherService(Action<Action> dispatch)
        {
            Current = new WeatherSnapshot();
            LoadCache();
            refresh = new OneShot(dispatch, RefreshIfDue);
        }
        public void ForceRefresh(){nextAttempt=DateTime.MinValue;if(active)RefreshIfDue();else Resume();}
        public void Resume()
        {
            if (disposed || active) return;
            active = true;
            RefreshIfDue();
        }
        public void Pause()
        {
            active = false; refresh.Stop();
            if (request != null) { request.Cancel(); request = null; }
        }
        private async void RefreshIfDue()
        {
            refresh.Stop();
            if (!active || disposed || request != null) return;
            if (DateTime.UtcNow < nextAttempt) { Schedule(); return; }
            var cancellation = new CancellationTokenSource();
            request = cancellation;
            cancellation.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                string json = await Download(cancellation.Token);
                WeatherSnapshot snapshot = WeatherSnapshot.Parse(json, DateTime.UtcNow);
                if (!active || request != cancellation) return;
                Current = snapshot;
                LastError = "";
                nextAttempt = DateTime.UtcNow.AddMinutes(15);
                SaveCache(json, snapshot.ReceivedUtc);
                Action changed = Changed; if (changed != null) changed();
            }
            catch (Exception ex)
            {
                if (!(ex is HttpRequestException) && !(ex is System.Text.Json.JsonException) && !(ex is IOException) && !(ex is OperationCanceledException) && !(ex is FormatException) && !(ex is InvalidCastException) && !(ex is KeyNotFoundException) && !(ex is ArgumentException) && !(ex is System.Security.SecurityException)) throw;
                if (!active || request != cancellation) return;
                nextAttempt = DateTime.UtcNow.AddMinutes(5);
                LastError = ex.Message;
                if (!Current.Available) Current = new WeatherSnapshot { Status = "Погода временно недоступна" };
                Action changed = Changed; if (changed != null) changed();
            }
            finally
            {
                if (request == cancellation) { request = null; Schedule(); }
                cancellation.Dispose();
            }
        }
        private void Schedule()
        {
            if (!active || disposed) return;
            refresh.Interval = (int)Math.Max(1000, Math.Min(int.MaxValue, (nextAttempt - DateTime.UtcNow).TotalMilliseconds));
            refresh.Start();
        }
        private static async Task<string> Download(CancellationToken token)
        {
            using var response = await http.GetAsync(Endpoint,HttpCompletionOption.ResponseHeadersRead,token);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream);
            var chars = new char[16384]; int count=await reader.ReadBlockAsync(chars.AsMemory(),token);
            if(count==chars.Length) throw new FormatException("Weather response is too large");
            return new string(chars,0,count);
        }
        private void SaveCache(string json, DateTime received)
        {
            try { File.WriteAllText(cachePath, new JsonCodec().Serialize(new { receivedUtc = received.ToString("o"), body = json })); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        private void LoadCache()
        {
            try
            {
                if (!File.Exists(cachePath) || new FileInfo(cachePath).Length > 32768) return;
                var cache = new JsonCodec().Deserialize<Dictionary<string, object>>(File.ReadAllText(cachePath));
                DateTime received = DateTime.Parse((string)cache["receivedUtc"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
                Current = WeatherSnapshot.Parse((string)cache["body"], received);
                nextAttempt = double.IsNaN(Current.Humidity) || double.IsNaN(Current.Wind) || double.IsNaN(Current.Pressure) ? DateTime.MinValue : received.AddMinutes(15);
            }
            catch (Exception ex)
            {
                if (!(ex is System.Text.Json.JsonException) && !(ex is IOException) && !(ex is UnauthorizedAccessException) && !(ex is FormatException) && !(ex is ArgumentException) && !(ex is KeyNotFoundException) && !(ex is InvalidCastException)) throw;
            }
        }
        public void Dispose() { disposed = true; Pause(); refresh.Dispose(); }
    }
}
