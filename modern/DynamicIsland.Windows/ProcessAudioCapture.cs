using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Island
{
    // Windows process-tree loopback, not endpoint/microphone capture. No PCM
    // leaves this worker or is written to disk. No browser audio graph changes.
    public sealed class ProcessAudioCapture : IDisposable
    {
        private IAudioClient client;
        private IAudioCaptureClient capture;
        private readonly AutoResetEvent ready = new AutoResetEvent(false);
        private short[] pcm;
        public readonly AudioSpectrum Spectrum = new AudioSpectrum();
        public ProcessAudioCapture(int pid, WaitHandle cancel)
        {
            try
            {
                client = Activate(pid, cancel);
                var format = new WaveFormat { Tag = 1, Channels = 2, Rate = AudioSpectrum.Rate, BytesPerSecond = AudioSpectrum.Rate * 4, Align = 4, Bits = 16 };
                Check(client.Initialize(0, 0x80020000 | 0x40000, 0, 0, ref format, IntPtr.Zero));
                uint frames; Check(client.GetBufferSize(out frames));
                if (frames == 0 || frames > 441000) throw new InvalidOperationException("Invalid capture buffer");
                pcm = new short[checked((int)frames * 2)];
                object service; Guid iid = typeof(IAudioCaptureClient).GUID; Check(client.GetService(ref iid, out service)); capture = (IAudioCaptureClient)service;
                Check(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle())); Check(client.Start());
            }
            catch { Dispose(); throw; }
        }
        public WaitHandle Ready { get { return ready; } }
        public bool Drain()
        {
            uint frames; bool analysed = false;
            while (true)
            {
                Check(capture.GetNextPacketSize(out frames)); if (frames == 0) return analysed;
                IntPtr data; uint flags; ulong position, qpc; Check(capture.GetBuffer(out data, out frames, out flags, out position, out qpc));
                try
                {
                    if (frames * 2 > pcm.Length) throw new InvalidOperationException("Audio packet exceeds reserved buffer");
                    bool silent = (flags & 2) != 0;
                    if (!silent) Marshal.Copy(data, pcm, 0, (int)frames * 2);
                    for (int i = 0; i < frames * 2; i += 2) analysed |= Spectrum.Push(silent ? 0 : pcm[i] / 32768f, silent ? 0 : pcm[i + 1] / 32768f);
                }
                finally { Check(capture.ReleaseBuffer(frames)); }
            }
        }
        private static IAudioClient Activate(int pid, WaitHandle cancel)
        {
            var completion = new Completion(pid); IActivateOperation operation = null;
            try
            {
                Guid iid = typeof(IAudioClient).GUID;
                int result = ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, completion.Parameters, completion, out operation);
                if (result < 0) { completion.StartFailed(); Check(result); }
                int which = WaitHandle.WaitAny(new[] { cancel, completion.Done }, 5000);
                if (which == 0) throw new OperationCanceledException(); if (which == WaitHandle.WaitTimeout) throw new TimeoutException("Process loopback activation timeout");
                return completion.Take();
            }
            finally { completion.Abandon(); if (operation != null) Marshal.ReleaseComObject(operation); }
        }
        public static int FindProcess(string name, int preferred)
        {
            if (preferred > 0)
            {
                try { using (var p = Process.GetProcessById(preferred)) if (!p.HasExited && string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase)) return preferred; }
                catch (ArgumentException) { } catch (System.ComponentModel.Win32Exception) { }
                return 0; // Never silently switch an exited exact source to another instance.
            }
            // Choose an oldest root rather than a random Chromium audio child.
            Process[] found = Process.GetProcessesByName(name); int result = 0; DateTime oldest = DateTime.MaxValue;
            foreach (var p in found) using (p) try { if (p.StartTime < oldest) { oldest = p.StartTime; result = p.Id; } } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            return result;
        }
        public static bool BelongsToProcess(uint candidate, int root)
        {
            if (root <= 0) return true;
            try
            {
                for (int depth = 0; depth < 16 && candidate > 0; depth++)
                {
                    if (candidate == root) return true;
                    using (var p = Process.GetProcessById((int)candidate))
                    {
                        BasicInfo info; int returned;
                        if (NtQueryInformationProcess(p.Handle, 0, out info, Marshal.SizeOf(typeof(BasicInfo)), out returned) < 0) return false;
                        uint parent = (uint)info.Parent.ToInt64(); if (parent == candidate) return false; candidate = parent;
                    }
                }
            }
            catch (ArgumentException) { } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            return false;
        }
        [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr process, int kind, out BasicInfo info, int length, out int returned);
        [StructLayout(LayoutKind.Sequential)] private struct BasicInfo { public IntPtr Reserved, Peb, Reserved2, Reserved3, Pid, Parent; }
        private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
        public void Dispose()
        {
            if (client != null) { client.Stop(); if (capture != null) { Marshal.ReleaseComObject(capture); capture = null; } Marshal.ReleaseComObject(client); client = null; }
            ready.Dispose();
        }
        [DllImport("Mmdevapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int ActivateAudioInterfaceAsync(string path, ref Guid iid, IntPtr parameters, ICompletion completion, out IActivateOperation operation);
        [StructLayout(LayoutKind.Sequential, Pack = 2)] public struct WaveFormat { public ushort Tag, Channels; public uint Rate, BytesPerSecond; public ushort Align, Bits, Extra; }
        [ComVisible(true), Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface ICompletion { [PreserveSig] int ActivateCompleted(IActivateOperation operation); }
        [ComVisible(true), Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface IAgile { }
        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface IActivateOperation { [PreserveSig] int GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object activated); }
        [ComVisible(true), ClassInterface(ClassInterfaceType.None)] public sealed class Completion : ICompletion, IAgile
        {
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
            public IntPtr Parameters;
            private IntPtr activation;
            private readonly object gate = new object();
            private object result;
            private int error;
            private bool abandoned, completed;
            public Completion(int pid)
            {
                activation = Marshal.AllocHGlobal(12); Marshal.WriteInt32(activation, 0, 1); Marshal.WriteInt32(activation, 4, pid); Marshal.WriteInt32(activation, 8, 0);
                Parameters = Marshal.AllocHGlobal(24); for (int i = 0; i < 24; i++) Marshal.WriteByte(Parameters, i, 0);
                Marshal.WriteInt16(Parameters, 65); Marshal.WriteInt32(Parameters, 8, 12); Marshal.WriteIntPtr(Parameters, 8 + IntPtr.Size, activation);
            }
            public int ActivateCompleted(IActivateOperation operation)
            {
                lock (gate)
                {
                    try { int hr = operation.GetActivateResult(out error, out result); if (hr < 0) error = hr; }
                    catch (Exception ex) { error = Marshal.GetHRForException(ex); }
                    completed = true; FreeParameters();
                    if (abandoned) { Release(); Done.Dispose(); } else Done.Set();
                }
                return 0;
            }
            public IAudioClient Take() { lock (gate) { Check(error); var value = (IAudioClient)result; result = null; return value; } }
            public void StartFailed() { lock (gate) { completed = true; FreeParameters(); } }
            public void Abandon() { lock (gate) { abandoned = true; if (completed) { Release(); Done.Dispose(); } } }
            private void Release() { if (result != null) { Marshal.ReleaseComObject(result); result = null; } }
            private void FreeParameters() { if (Parameters != IntPtr.Zero) { Marshal.FreeHGlobal(Parameters); Marshal.FreeHGlobal(activation); Parameters = activation = IntPtr.Zero; } }
        }
        [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface IAudioClient
        {
            [PreserveSig] int Initialize(int share, uint flags, long duration, long periodicity, ref WaveFormat format, IntPtr session);
            [PreserveSig] int GetBufferSize(out uint frames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint frames);
            [PreserveSig] int IsFormatSupported(int share, IntPtr format, out IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long normal, out long minimum);
            [PreserveSig] int Start(); [PreserveSig] int Stop(); [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr handle);
            [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }
        [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong device, out ulong qpc);
            [PreserveSig] int ReleaseBuffer(uint frames); [PreserveSig] int GetNextPacketSize(out uint frames);
        }
    }
}
