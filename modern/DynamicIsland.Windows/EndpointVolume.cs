using System.Runtime.InteropServices;
namespace Island;
public static class EndpointVolume
{
    // Separate from per-application volume: changes only the current default render endpoint.
    public static Task<float> Level(float? requested=null)=>Task.Run(()=>
    {
        IMMDeviceEnumerator enumerator=null;IMMDevice device=null;object endpoint=null;
        try
        {
            enumerator=(IMMDeviceEnumerator)new MMDeviceEnumerator();Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0,1,out device));Guid iid=typeof(IAudioEndpointVolume).GUID;Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out endpoint));var volume=(IAudioEndpointVolume)endpoint;
            if(requested.HasValue){Guid context=Guid.Empty;Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(Math.Clamp(requested.Value,0,1),ref context));}
            Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out float value));return value;
        }
        finally {foreach(object value in new[]{endpoint,(object)device,enumerator})if(value!=null && Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
    });
    [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IAudioEndpointVolume
    {
        [PreserveSig]int RegisterControlChangeNotify(IntPtr callback);[PreserveSig]int UnregisterControlChangeNotify(IntPtr callback);[PreserveSig]int GetChannelCount(out uint count);
        [PreserveSig]int SetMasterVolumeLevel(float db,ref Guid context);[PreserveSig]int SetMasterVolumeLevelScalar(float level,ref Guid context);
        [PreserveSig]int GetMasterVolumeLevel(out float db);[PreserveSig]int GetMasterVolumeLevelScalar(out float level);
    }
}
