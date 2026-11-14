namespace Island;

// A bounded, cancellable deadline. Callback always runs on the supplied owner context.
// Generation check also invalidates callbacks already queued when Stop is called.
public sealed class OneShot : IDisposable
{
    private readonly Action<Action> dispatch;
    private readonly Action callback;
    private readonly Timer timer;
    private long generation;
    private long due;
    private bool disposed;
    private static int count;
    public static int ActiveCount => Volatile.Read(ref count);
    public bool Enabled { get; private set; }
    public int Interval { get; set; } = 1000;
    public OneShot(Action<Action> send, Action tick)
    {
        dispatch=send; callback=tick;
        timer=new Timer(_ => { long version=Volatile.Read(ref generation); dispatch(() => {
            if(disposed || !Enabled || version!=generation) return;
            double remaining=(due-System.Diagnostics.Stopwatch.GetTimestamp())*1000d/System.Diagnostics.Stopwatch.Frequency;
            if(remaining>0) { timer.Change((int)Math.Ceiling(remaining),Timeout.Infinite); return; }
            Stop(); callback();
        }); });
    }
    public void Start() { if(disposed) return; generation++; due=System.Diagnostics.Stopwatch.GetTimestamp()+(long)(Math.Max(1,Interval)*System.Diagnostics.Stopwatch.Frequency/1000d); if(!Enabled) { Enabled=true; Interlocked.Increment(ref count); } timer.Change(Math.Max(1,Interval),Timeout.Infinite); }
    public void Stop() { generation++; if(Enabled) { Enabled=false; Interlocked.Decrement(ref count); } timer.Change(Timeout.Infinite,Timeout.Infinite); }
    public void Dispose() { if(disposed) return; Stop(); disposed=true; timer.Dispose(); }
}
