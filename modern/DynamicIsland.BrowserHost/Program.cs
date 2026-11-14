using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

using System.Diagnostics;
using System.Runtime.InteropServices;


namespace Island
{
    public static class BrowserHost
    {
        private static readonly object gate = new object();
        private static readonly SemaphoreSlim pipeWriter = new SemaphoreSlim(1, 1), browserWriter = new SemaphoreSlim(1, 1);
        private static NamedPipeClientStream pipe;
        private static string latestTrack, latestBridge;
        private static bool stopping;
        private static Stream output;
        private static readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr process, int kind, out BasicInfo info, int length, out int returned);
        [StructLayout(LayoutKind.Sequential)] private struct BasicInfo { public IntPtr Reserved, Peb, Reserved2, Reserved3, Pid, Parent; }
        private static string ParentBrowser(out int browserPid)
        {
            browserPid = 0;
            try
            {
                int pid; using (var current = Process.GetCurrentProcess()) pid = current.Id;
                // Some Chromium versions insert cmd.exe between browser and host.
                for (int depth = 0; depth < 6; depth++)
                {
                    BasicInfo info; int returned;
                    using (var current = Process.GetProcessById(pid))
                    {
                        string name = current.ProcessName.ToLowerInvariant();
                        if (name == "browser" || name == "chrome" || name == "msedge") { browserPid = pid; return name; }
                        if (NtQueryInformationProcess(current.Handle, 0, out info, Marshal.SizeOf(typeof(BasicInfo)), out returned) != 0) return "";
                    }
                    if (info.Parent.ToInt64() <= 0 || info.Parent.ToInt64() == pid) return "";
                    pid = info.Parent.ToInt32();
                }
                return "";
            }
            catch (ArgumentException) { return ""; } catch (System.ComponentModel.Win32Exception) { return ""; }
        }
        private static void Main(string[] args)
        {
            if (args.Length == 0 || args[0] != BrowserIdentity.Origin) return;
            Guid fixture; if (args.Length == 2 && Guid.TryParse(args[1], out fixture)) BrowserProtocol.TestSuffix = ".test." + fixture.ToString("N");
            try { Run().GetAwaiter().GetResult(); } catch (System.Text.Json.JsonException) { } catch (IOException) { } catch (ObjectDisposedException) { } catch(OperationCanceledException) { }
        }
        private static async Task Run()
        {
            output = Console.OpenStandardOutput();
            int audioPid; string audioProcess = ParentBrowser(out audioPid);
            Task connection = Task.Run((Func<Task>)ConnectLoop);
            try
            {
                using (Stream input = Console.OpenStandardInput())
                {
                    var parser = new JsonCodec();
                    string json;
                    while ((json = await BrowserProtocol.Read(input).ConfigureAwait(false)) != null)
                    {
                        var message = parser.DeserializeObject(json) as System.Collections.Generic.Dictionary<string, object>;
                        if (message == null || !message.ContainsKey("type")) continue;
                        string kind = message["type"] as string;
                        if (kind != "track" && kind != "ack" && kind != "bridge") continue;
                        if (kind == "track") { message["audioProcess"] = audioProcess; message["audioProcessId"] = audioPid; json = parser.Serialize(message); }
                        lock (gate) if (kind == "track") latestTrack = json;
                        lock (gate) if (kind == "bridge") latestBridge = json;
                        await SendToIsland(json).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                shutdown.Cancel();
                lock (gate) { stopping = true; if (pipe != null) pipe.Dispose(); }
            }
            await connection.ConfigureAwait(false);
        }
        private static async Task ConnectLoop()
        {
            int retry = 1500;
            using (var ready = BrowserProtocol.OpenReadyEvent())
            while (true)
            {
                lock (gate) if (stopping) return;
                using (var client = new NamedPipeClientStream(".", BrowserProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    try
                    {
                        // Connect runs only while Island is absent; the connected
                        // pipe sleeps in asynchronous reads without polling.
                        await client.ConnectAsync(1200, shutdown.Token).ConfigureAwait(false);
                        retry = 1500;
                        string cached, bridge;
                        lock (gate) { if (stopping) return; pipe = client; cached = latestTrack; bridge = latestBridge; }
                        await SendToBrowser("{\"type\":\"status\",\"connected\":true}").ConfigureAwait(false);
                        if (bridge != null) await SendToIsland(bridge).ConfigureAwait(false);
                        if (cached != null) await SendToIsland(cached).ConfigureAwait(false);
                        string command;
                        while ((command = await BrowserProtocol.Read(client).ConfigureAwait(false)) != null) await SendToBrowser(command).ConfigureAwait(false);
                    }
                    catch (IOException) { }
                    catch (TimeoutException) { }
                    catch (ObjectDisposedException) { }
                    catch (OperationCanceledException) { return; }
                    finally { lock (gate) if (pipe == client) pipe = null; }
                }
                lock (gate) if (stopping) return;
                await SendToBrowser("{\"type\":\"status\",\"connected\":false}").ConfigureAwait(false);
                try { await BrowserProtocol.WaitReady(ready, retry, shutdown.Token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                retry = Math.Min(30000, retry * 2);
            }
        }
        private static async Task SendToIsland(string json)
        {
            await pipeWriter.WaitAsync().ConfigureAwait(false);
            try
            {
                NamedPipeClientStream current; lock (gate) current = pipe;
                if (current != null && current.IsConnected) await BrowserProtocol.Write(current, json).ConfigureAwait(false);
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally { pipeWriter.Release(); }
        }
        private static async Task SendToBrowser(string json)
        {
            await browserWriter.WaitAsync().ConfigureAwait(false);
            try { await BrowserProtocol.Write(output, json).ConfigureAwait(false); }
            finally { browserWriter.Release(); }
        }
    }
}
