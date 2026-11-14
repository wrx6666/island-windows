using System.Diagnostics;
using System.IO;

namespace Island;
public sealed class PanelDiagnostics
{
    // Allocated only with --diagnostics or by the separate test host.
    private readonly List<double> intervals=new(8192),costs=new(8192),latency=new(128);
    private long input;
    public void Input() {input=Stopwatch.GetTimestamp();}
    public void Frame(long start,double dt,bool transition)
    {
        if(intervals.Count<32768){intervals.Add(dt*1000);costs.Add((Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency);}
        if(input!=0) {if(latency.Count<4096)latency.Add((Stopwatch.GetTimestamp()-input)*1000d/Stopwatch.Frequency);input=0;}
    }
    private static double P95(List<double> values) {if(values.Count==0)return 0;var sorted=values.Order().ToArray();return sorted[(int)Math.Ceiling(sorted.Length*.95)-1];}
    public async Task<object> Measure(string name,int milliseconds,IslandWindow window,Func<object> overlay)
    {
        intervals.Clear();costs.Clear();latency.Clear();input=0;
        using var process=Process.GetCurrentProcess();process.Refresh();var cpu=process.TotalProcessorTime;long frames=window.FrameCount,callbacks=window.CallbackCount,alloc=GC.GetTotalAllocatedBytes(false);int gen0=GC.CollectionCount(0);var watch=Stopwatch.StartNew();
        await Task.Delay(milliseconds);process.Refresh();double wall=watch.Elapsed.TotalSeconds,cpuMs=(process.TotalProcessorTime-cpu).TotalMilliseconds;
        return new {scenario=name,seconds=wall,cpuMilliseconds=cpuMs,cpuPercentOneCore=cpuMs/wall/10,cpuPercentMachine=cpuMs/wall/10/Environment.ProcessorCount,workingSetBytes=process.WorkingSet64,privateBytes=process.PrivateMemorySize64,managedBytes=GC.GetTotalMemory(false),allocatedBytes=GC.GetTotalAllocatedBytes(false)-alloc,gc0=GC.CollectionCount(0)-gen0,handles=process.HandleCount,threads=process.Threads.Count,frames=window.FrameCount-frames,visualUpdatesPerSecond=(window.FrameCount-frames)/wall,compositionCallbacksPerSecond=(window.CallbackCount-callbacks)/wall,displayHz=window.DisplayFrameRate,targetHz=window.TargetFrameRate,updateIntervalP95Ms=intervals.Count>0?(double?)P95(intervals):null,updateCostP95Ms=costs.Count>0?(double?)P95(costs):null,inputToUpdateP95Ms=latency.Count>0?(double?)P95(latency):null,inputSamples=latency.Count,activeTimers=OneShot.ActiveCount,renderSubscribed=window.Rendering,renderTier=System.Windows.Media.RenderCapability.Tier>>16,overlay=overlay(),gpu="Not measured",presentedFps="Not measured; visualUpdatesPerSecond counts WPF composition callbacks, not DWM presents"};
    }
}
