using System.Runtime.InteropServices;
namespace Island;
public sealed class TrayIcon : IDisposable
{
    public const int CallbackMessage=0x8000+21;
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct Data
    {
        public int Size;public IntPtr Window;public uint Id,Flags,Message;public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string Title;
        public uint InfoFlags;public Guid Guid;public IntPtr Balloon;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern bool Shell_NotifyIcon(uint action,ref Data data);
    [DllImport("user32.dll")]private static extern IntPtr CreateIcon(IntPtr instance,int width,int height,byte planes,byte bits,byte[] and,byte[] xor);
    private Data data;
    public TrayIcon(IntPtr window)
    {
        var pixels=new byte[32*32*4];var mask=Enumerable.Repeat((byte)255,128).ToArray();
        foreach(var c in new[]{(11,11),(21,11),(11,21),(21,21)})for(int y=0;y<32;y++)for(int x=0;x<32;x++)if((x-c.Item1)*(x-c.Item1)+(y-c.Item2)*(y-c.Item2)<16){int p=((31-y)*32+x)*4;pixels[p]=196;pixels[p+1]=181;pixels[p+2]=255;pixels[p+3]=255;mask[y*4+x/8]&=(byte)~(128>>(x%8));}
        data=new Data{Size=Marshal.SizeOf<Data>(),Window=window,Id=1,Flags=7,Message=CallbackMessage,Icon=CreateIcon(IntPtr.Zero,32,32,1,32,mask,pixels),Tip="Island · WPF Preview"};Shell_NotifyIcon(0,ref data);
    }
    public void Restore()=>Shell_NotifyIcon(0,ref data);
    public void Notify(string message) {data.Flags=0x10;data.Info=message.Length>255?message[..255]:message;data.Title="Island";data.InfoFlags=1;Shell_NotifyIcon(1,ref data);data.Flags=7;}
    public void Dispose(){Shell_NotifyIcon(2,ref data);if(data.Icon!=IntPtr.Zero)Native.DestroyIcon(data.Icon);data.Icon=IntPtr.Zero;}
}
