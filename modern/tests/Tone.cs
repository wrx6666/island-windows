using System.Runtime.InteropServices;
using System.Text;
namespace Island.Tests;
public static class Tone
{
    [DllImport("winmm.dll",EntryPoint="PlaySoundW",SetLastError=true)]private static extern bool PlaySound(IntPtr memory,IntPtr module,uint flags);
    public static async Task Play(int hz,int seconds)
    {
        const int rate=44100;using var memory=new MemoryStream();
        using(var w=new BinaryWriter(memory,Encoding.UTF8,true))
        {
            w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write(36+rate*2);w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(rate);w.Write(rate*2);w.Write((short)2);w.Write((short)16);w.Write(Encoding.ASCII.GetBytes("data"));w.Write(rate*2);
            for(int i=0;i<rate;i++)w.Write((short)(Math.Sin(i*2*Math.PI*hz/rate)*.06*32767));
        }
        var pin=GCHandle.Alloc(memory.ToArray(),GCHandleType.Pinned);
        try {if(!PlaySound(pin.AddrOfPinnedObject(),IntPtr.Zero,0x0001|0x0004|0x0008|0x0002))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());await Task.Delay(seconds*1000);}
        finally {PlaySound(IntPtr.Zero,IntPtr.Zero,0);pin.Free();}
    }
}
