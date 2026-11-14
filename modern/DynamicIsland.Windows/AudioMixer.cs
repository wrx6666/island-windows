using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Island
{
    public sealed class AudioApp
    {
        public string ProcessName;
        public bool Muted;
        public override string ToString() { return Friendly(ProcessName); }
        public static string Friendly(string name) { return name == "DeltaForceClient-Win64-Shipping" ? "Delta Force" : name; }
    }

    // Only per-session mute is changed. No master volume, game injection or hooks.
    public sealed class AudioMixer : IDisposable, IAsyncDisposable
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, bool> originals = new Dictionary<string, bool>();
        private readonly Dictionary<string, IAudioSessionManager2> watches = new Dictionary<string, IAudioSessionManager2>();
        private readonly SessionNotification notification;
        private string target = "";
        private bool muted, disposed;
        public bool Muted { get { lock (gate) return muted; } }
        private static Guid context = new Guid("47642e63-f3db-48c5-9ae1-b19706d006d3");
        public AudioMixer() { notification = new SessionNotification(this); }
        public Task<List<AudioApp>> Apps()
        {
            return Task.Run(() =>
            {
                lock (gate)
                {
                    var apps = new Dictionary<string, AudioApp>(StringComparer.OrdinalIgnoreCase);
                    Visit((key, name, volume) =>
                    {
                        bool state; Check(volume.GetMute(out state));
                        if (!apps.ContainsKey(name)) apps.Add(name, new AudioApp { ProcessName = name, Muted = state });
                    });
                    return new List<AudioApp>(apps.Values);
                }
            });
        }
        public Task<string> Toggle(string processName)
        {
            return Task.Run(() =>
            {
                lock (gate)
                {
                    if (disposed) return "Island закрывается";
                    try
                    {
                        if (muted) { Restore(); return AudioApp.Friendly(target) + ": звук восстановлен"; }
                        target = processName; muted = true;
                        int count = ApplyMute();
                        if (count == 0) { muted = false; originals.Clear(); ClearWatches(); return "У приложения пока нет доступной звуковой сессии. Запусти в нём звук и повтори."; }
                        return AudioApp.Friendly(target) + ": звук выключен";
                    }
                    catch (Exception ex) { try { Restore(); } catch (COMException) { } return "Не удалось переключить звук: " + ex.Message; }
                }
            });
        }
        public Task RestoreBeforeChange()
        {
            return Task.Run(() => { lock (gate) if (muted) Restore(); });
        }
        public Task<float> MusicVolume(string name, float? level)
        {
            return Task.Run(() =>
            {
                lock (gate)
                {
                    float result = -1;
                    if (disposed || string.IsNullOrEmpty(name)) return result;
                    Visit((key, process, volume) =>
                    {
                        if (!string.Equals(process, name, StringComparison.OrdinalIgnoreCase)) return;
                        if (level.HasValue) Check(volume.SetMasterVolume(Math.Max(0, Math.Min(1, level.Value)), ref context));
                        float value; Check(volume.GetMasterVolume(out value)); result = value;
                    });
                    return result;
                }
            });
        }
        private int ApplyMute()
        {
            int count = 0;
            Visit((key, name, volume) =>
            {
                if (!string.Equals(name, target, StringComparison.OrdinalIgnoreCase)) return;
                bool previous; Check(volume.GetMute(out previous));
                if (!originals.ContainsKey(key)) originals.Add(key, previous);
                Check(volume.SetMute(true, ref context)); count++;
            });
            return count;
        }
        private void Restore()
        {
            muted = false; ClearWatches();
            try
            {
                Visit((key, name, volume) =>
                {
                    bool original;
                    if (originals.TryGetValue(key, out original)) Check(volume.SetMute(original, ref context));
                });
            }
            finally { originals.Clear(); }
        }
        private void NewSession()
        {
            Task.Run(() =>
            {
                lock (gate)
                {
                    if (!muted || disposed) return;
                    try { ApplyMute(); } catch (COMException) { }
                }
            });
        }
        public void Visit(Action<string, string, ISimpleAudioVolume> action, Action<string, object> inspect = null)
        {
            IMMDeviceEnumerator enumerator = null; IMMDeviceCollection devices = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                Check(enumerator.EnumAudioEndpoints(0, 1, out devices));
                uint deviceCount; Check(devices.GetCount(out deviceCount));
                for (uint d = 0; d < deviceCount; d++)
                {
                    IMMDevice device = null; IAudioSessionManager2 manager = null; IAudioSessionEnumerator sessions = null;
                    bool retained = false;
                    try
                    {
                        Check(devices.Item(d, out device)); string deviceId; Check(device.GetId(out deviceId));
                        object activated; Guid iid = typeof(IAudioSessionManager2).GUID;
                        Check(device.Activate(ref iid, 23, IntPtr.Zero, out activated)); manager = (IAudioSessionManager2)activated;
                        if (muted && !watches.ContainsKey(deviceId))
                        {
                            Check(manager.RegisterSessionNotification(notification)); watches.Add(deviceId, manager); retained = true;
                        }
                        Check(manager.GetSessionEnumerator(out sessions)); int count; Check(sessions.GetCount(out count));
                        for (int i = 0; i < count; i++)
                        {
                            object session = null;
                            try
                            {
                                Check(sessions.GetSession(i, out session)); var control = (IAudioSessionControl2)session;
                                uint pid; int state; Check(control.GetProcessId(out pid)); Check(control.GetState(out state));
                                if (pid == 0 || state == 2) continue;
                                string name; using (var process = Process.GetProcessById((int)pid)) name = process.ProcessName;
                                string id; Check(control.GetSessionInstanceIdentifier(out id));
                                action(deviceId + "/" + id, name, (ISimpleAudioVolume)session);
                                if (inspect != null) inspect(name, session);
                            }
                            catch (ArgumentException) { /* Process exited during enumeration. */ }
                            catch (System.ComponentModel.Win32Exception) { /* Protected/exited process. */ }
                            catch (COMException) { /* Session/device disappeared. Continue other endpoints. */ }
                            finally { Release(session); }
                        }
                    }
                    catch (COMException) { }
                    finally { Release(sessions); if (!retained) Release(manager); Release(device); }
                }
            }
            finally { Release(devices); Release(enumerator); }
        }
        private void ClearWatches()
        {
            foreach (var manager in watches.Values) { manager.UnregisterSessionNotification(notification); Release(manager); }
            watches.Clear();
        }
        private static void Check(int hr) { Marshal.ThrowExceptionForHR(hr); }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        public void Dispose()
        {
            // Run on MTA too, so outstanding callbacks never depend on the UI loop.
            Task.Run(() => { lock (gate) { if (disposed) return; disposed = true; try { if (muted) Restore(); } catch (COMException) { } ClearWatches(); } }).GetAwaiter().GetResult();
        }
        public async ValueTask DisposeAsync() => await Task.Run(Dispose);
        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        private sealed class SessionNotification : IAudioSessionNotification
        {
            private readonly AudioMixer owner;
            public SessionNotification(AudioMixer mixer) { owner = mixer; }
            public int OnSessionCreated(IntPtr session) { owner.NewSession(); return 0; }
        }
    }

    // Vtable order follows Windows SDK audiopolicy.h / mmdeviceapi.h.
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] public class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr guid, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, uint flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        [PreserveSig] int RegisterSessionNotification(IAudioSessionNotification notification);
        [PreserveSig] int UnregisterSessionNotification(IAudioSessionNotification notification);
        [PreserveSig] int RegisterDuckNotification(IntPtr id, IntPtr notification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr notification);
    }
    [Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionNotification { [PreserveSig] int OnSessionCreated(IntPtr session); }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }
    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr context);
        [PreserveSig] int GetGroupingParam(out Guid value);
        [PreserveSig] int SetGroupingParam(ref Guid value, IntPtr context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
