using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Island
{
    // One worker owns WASAPI and FFT. Hidden/paused/off sleeps on an event.
    // Peak-only fallback is explicitly identified and never claims bass/beats.
    public sealed class MusicLevels : IDisposable
    {
        private readonly object gate = new object();
        private readonly AutoResetEvent changed = new AutoResetEvent(false);
        private Thread worker;
        private string process = "";
        private bool active, disposed;
        private float energy, beat;
        private AudioFeatures features;
        private int preferredPid, revision;
        private string status = "Анализ остановлен";
        public AudioFeatures Read() { lock (gate) return features; }
        public string Status { get { lock (gate) return status; } }
        public long Samples;
        public float Energy { get { lock (gate) return energy; } }
        public float Beat { get { lock (gate) return beat; } }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ReadPeak(IntPtr instance, out float peak);
        private sealed class Meter
        {
            public IntPtr Pointer;
            public ReadPeak Read;
        }
        public MusicLevels() { }
        public bool WorkerAlive { get { lock (gate) return worker != null && worker.IsAlive; } }
        public bool Sampling { get { lock (gate) return active && !string.IsNullOrEmpty(process); } }
        public void Configure(bool enabled, string name) { Configure(enabled, name, 0); }
        public void Configure(bool enabled, string name, int pid)
        {
            name = name ?? "";
            lock (gate)
            {
                if (disposed) return;
                if (active == enabled && process == name && preferredPid == pid) return;
                energy = beat = 0; features = new AudioFeatures(); revision++;
                status = enabled ? (name.Length > 0 ? "Подключение к аудио процесса…" : "Источник аудио неизвестен; выбери приложение в подсветке") : "Анализ остановлен";
                active = enabled; process = name; preferredPid = pid;
                if (enabled && name.Length > 0 && worker == null)
                {
                    worker = new Thread(Run) { IsBackground = true, Name = "Island music levels" };
                    worker.SetApartmentState(ApartmentState.MTA); worker.Start();
                }
            }
            changed.Set();
        }
        public static string ProcessFor(MusicSnapshot track)
        {
            if (!string.IsNullOrEmpty(track.AudioProcess)) return track.AudioProcess;
            // A browser native host supplies its actual parent executable name.
            // Never fall back to the endpoint meter (which would include games).
            if (MusicCatalog.AllowedSite(track.Source)) return "";
            string source = track.Source.ToLowerInvariant();
            if (source.EndsWith(".exe", StringComparison.Ordinal)) return System.IO.Path.GetFileNameWithoutExtension(source);
            if (source.Contains("yandex")) return "YandexMusic";
            if (source.Contains("spotify")) return "Spotify";
            if (source.Contains("vk")) return "VK";
            return "";
        }
        public static float Follow(float current, float target, double seconds, double attack, double release)
        {
            return current + (target - current) * (float)(1 - Math.Exp(-seconds / (target > current ? attack : release)));
        }
        public sealed class Envelope
        {
            private float average, deviation, previous;
            public float Energy, Beat;
            public void Step(float peak, double seconds)
            {
                if (float.IsNaN(peak) || float.IsInfinity(peak)) peak = 0;
                peak = Math.Max(0, Math.Min(1, peak));
                seconds = Math.Min(0.1, Math.Max(0.001, seconds));
                average = Follow(average, peak, seconds, 0.65, 0.65);
                deviation = Follow(deviation, Math.Abs(peak - average), seconds, 0.4, 0.8);
                // Normalize local contrast instead of requiring a fixed 12%
                // increase over a loud baseline. Compressed passages still have
                // useful transients; a steady tone need not invent beat events.
                float contrast = Math.Max(0, peak - average), flux = Math.Max(0, peak - previous);
                float onset = peak > 0.003f ? Math.Min(1, (flux * 1.5f + contrast * 0.35f) / Math.Max(0.018f, deviation * 1.8f)) : 0;
                float level = (float)(Math.Log(1 + 40 * peak) / Math.Log(41));
                Energy = Follow(Energy, level, seconds, 0.025, 0.18);
                Beat = Follow(Beat, onset, seconds, 0.018, 0.20); previous = peak;
            }
        }
        private void Run()
        {
            int initialized = CoInitializeEx(IntPtr.Zero, 0);
            try
            {
                while (true)
                {
                    string target; int pid, version; bool enabled;
                    lock (gate) { if (disposed) return; target = process; pid = preferredPid; version = revision; enabled = active; }
                    if (!enabled || string.IsNullOrEmpty(target)) { changed.WaitOne(); continue; }
                    try
                    {
                        int resolved = ProcessAudioCapture.FindProcess(target, pid);
                        if (resolved == 0) throw new InvalidOperationException("Процесс источника не найден");
                        pid = resolved;
                        using (var capture = new ProcessAudioCapture(pid, changed))
                        {
                            lock (gate) if (revision == version) status = "FFT · изолированное дерево процесса " + target + " (PID " + pid + ")";
                            var waits = new[] { changed, capture.Ready };
                            while (Current(version))
                            {
                                if (WaitHandle.WaitAny(waits, 500) == 0) break;
                                if (capture.Drain()) Publish(capture.Spectrum.Current, version);
                                else if ((Stopwatch.GetTimestamp() - capture.Spectrum.Current.Stamp) / (double)Stopwatch.Frequency > 0.3)
                                    Publish(new AudioFeatures { Spectrum = true, Stamp = Stopwatch.GetTimestamp() }, version);
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        if (!Current(version)) continue;
                        lock (gate) { features = new AudioFeatures(); energy = beat = 0; status = "Только пики громкости · FFT недоступен: " + ex.Message + " (0x" + Marshal.GetHRForException(ex).ToString("X8") + ")"; }
                        RunFallback(target, version, pid);
                    }
                }
            }
            finally { if (initialized >= 0) CoUninitialize(); changed.Dispose(); }
        }
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint apartment);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
        private bool Current(int version) { lock (gate) return !disposed && active && revision == version; }
        private void Publish(AudioFeatures value, int version)
        {
            lock (gate) if (!disposed && active && revision == version) { features = value; energy = value.Energy; beat = value.Beat; Samples++; }
        }
        private void RunFallback(string target, int version, int root)
        {
            var meters = new List<Meter>(); string selected = "";
            var clock = Stopwatch.StartNew(); double scanned = -10, previous = 0;
            var envelope = new Envelope();
            using (var mixer = new AudioMixer())
            try
            {
                // Retry activation with a 30-second backoff only while visible.
                // Recover a changed audio service/process without reconnect storms.
                while (Current(version) && clock.Elapsed.TotalSeconds < 30)
                {
                    double now = clock.Elapsed.TotalSeconds, dt = Math.Min(0.1, Math.Max(0.001, now - previous)); previous = now;
                    if (selected != target || now - scanned > 3)
                    {
                        if (selected != target) envelope = new Envelope();
                        Clear(meters); selected = target; scanned = now;
                        try
                        {
                            mixer.Visit((key, name, volume) => { }, (name, session) =>
                            {
                                if (!string.Equals(name, target, StringComparison.OrdinalIgnoreCase)) return;
                                uint sessionPid; if (((IAudioSessionControl2)session).GetProcessId(out sessionPid) < 0 || !ProcessAudioCapture.BelongsToProcess(sessionPid, root)) return;
                                IntPtr unknown = Marshal.GetIUnknownForObject(session), pointer;
                                Guid iid = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
                                try
                                {
                                    if (Marshal.QueryInterface(unknown, in iid, out pointer) != 0) return;
                                    try
                                    {
                                        IntPtr table = Marshal.ReadIntPtr(pointer), method = Marshal.ReadIntPtr(table, 3 * IntPtr.Size);
                                        meters.Add(new Meter { Pointer = pointer, Read = (ReadPeak)Marshal.GetDelegateForFunctionPointer(method, typeof(ReadPeak)) });
                                    }
                                    catch { Marshal.Release(pointer); throw; }
                                }
                                finally { Marshal.Release(unknown); }
                            });
                        }
                        catch (COMException) { }
                    }
                    float peak = 0;
                    foreach (var meter in meters) { float value; if (meter.Read(meter.Pointer, out value) >= 0) peak = Math.Max(peak, value); }
                    peak = Math.Max(0, Math.Min(1, peak));
                    envelope.Step(peak, dt);
                    Publish(new AudioFeatures { Energy = envelope.Energy, Rms = peak, Stamp = Stopwatch.GetTimestamp() }, version);
                    if (changed.WaitOne(40)) return; // Explicit 25 Hz fallback, only visible/playing.
                }
            }
            finally { Clear(meters); }
        }
        private static void Clear(List<Meter> meters) { foreach (var meter in meters) Marshal.Release(meter.Pointer); meters.Clear(); }
        public void Dispose()
        {
            lock (gate) { if (disposed) return; disposed = true; }
            changed.Set(); if (worker == null) changed.Dispose();
            // No UI-thread Join on an audio driver/COM callback. Background
            // worker owns and releases its event and native capture when woken.
        }
    }
}
