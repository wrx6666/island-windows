using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Security.AccessControl;

namespace Island
{
    public static class BrowserProtocol
    {
        public const string HostName = "com.desktopisland.music";
        public static string TestSuffix = "";
        public static string PipeName { get { return "Island.Music." + WindowsIdentity.GetCurrent().User.Value + TestSuffix; } }
        public static EventWaitHandle OpenReadyEvent()
        {
            var security = new EventWaitHandleSecurity(); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new EventWaitHandleAccessRule(WindowsIdentity.GetCurrent().User, EventWaitHandleRights.FullControl, AccessControlType.Allow));
            bool created; return EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, "Local\\" + PipeName + ".Ready", out created, security);
        }
        public static async Task WaitReady(EventWaitHandle ready, int timeout, CancellationToken token)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var wait = ThreadPool.RegisterWaitForSingleObject(ready, (state, expired) => completion.TrySetResult(!expired), null, timeout, true);
            try { using (token.Register(() => completion.TrySetCanceled())) await completion.Task.ConfigureAwait(false); }
            finally { wait.Unregister(null); }
        }
        public static async Task<string> Read(Stream stream, CancellationToken token=default)
        {
            byte[] prefix = new byte[4];
            int read = await stream.ReadAsync(prefix.AsMemory(),token).ConfigureAwait(false);
            if (read == 0) return null;
            await ReadRest(stream, prefix, read,token).ConfigureAwait(false);
            uint size = BitConverter.ToUInt32(prefix, 0);
            if (size == 0 || size > 32768) throw new InvalidDataException("Music message exceeds limit.");
            byte[] data = new byte[size]; await ReadRest(stream, data, 0,token).ConfigureAwait(false);
            return new UTF8Encoding(false, true).GetString(data);
        }
        private static async Task ReadRest(Stream stream, byte[] data, int offset,CancellationToken token)
        {
            while (offset < data.Length)
            {
                int read = await stream.ReadAsync(data.AsMemory(offset),token).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException(); offset += read;
            }
        }
        public static async Task Write(Stream stream, string json,CancellationToken token=default)
        {
            byte[] data = Encoding.UTF8.GetBytes(json);
            if (data.Length == 0 || data.Length > 32768) throw new InvalidDataException("Music message exceeds limit.");
            byte[] prefix = BitConverter.GetBytes(data.Length);
            await stream.WriteAsync(prefix.AsMemory(),token).ConfigureAwait(false);
            await stream.WriteAsync(data.AsMemory(),token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
    }
}
