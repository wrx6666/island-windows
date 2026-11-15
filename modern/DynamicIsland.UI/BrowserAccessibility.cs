using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace Island;

public sealed record BrowserPageResult(bool Found=false,bool Playing=false,bool Invoked=false,nint Window=0,bool Minimized=false,long BeforeInvokeRevision=-1);

// On-demand Windows accessibility client. One lazy MTA worker, no idle scans,
// no script injection, remote debugging, profile/cookie reads or keyboard input.
public sealed class BrowserAccessibility : IDisposable
{
    private readonly BlockingCollection<Action> jobs=new(1);
    private readonly object gate=new();
    private Thread worker;
    private bool disposed;
    private int outstanding;
    private readonly Func<long> revision;
    public BrowserAccessibility(Func<long> mediaRevision=null){revision=mediaRevision??(()=>0);}
    public bool WorkerStarted=>worker!=null;
    public bool Busy=>Volatile.Read(ref outstanding)!=0;
    public Task<BrowserPageResult> FindAsync(string executable,string service,bool play,CancellationToken token)=>Run(request=>
    {
        foreach(nint window in WindowsFor(executable))
        {
            request.ThrowIfCancellationRequested();
            var result=Inspect(window,service,play,request);
            if(result.Found)return result;
            var root=AutomationElement.FromHandle(window);
            var tabs=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TabItem));
            for(int i=0;i<Math.Min(tabs.Count,100);i++)
            {
                string name=tabs[i].Current.Name;
                if(!MusicTab(name,service) || !tabs[i].TryGetCurrentPattern(SelectionItemPattern.Pattern,out var pattern))continue;
                request.ThrowIfCancellationRequested();if(!((SelectionItemPattern)pattern).Current.IsSelected)((SelectionItemPattern)pattern).Select();
                return new(Found:true,Window:window,Minimized:IsIconic(window));
            }
            var title=new StringBuilder(512);GetWindowText(window,title,title.Capacity);
            if(IsIconic(window) && MusicTab(title.ToString().Replace(" — Яндекс Браузер",""),service))return new(Found:true,Window:window,Minimized:true);
        }
        return new();
    },token);
    // Also used against an isolated, owned accessibility provider in regression checks.
    public Task<BrowserPageResult> InspectWindowAsync(nint window,string service,bool play,CancellationToken token)=>Run(request=>Inspect(window,service,play,request),token);
    private async Task<BrowserPageResult> Run(Func<CancellationToken,BrowserPageResult> action,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(Interlocked.CompareExchange(ref outstanding,1,0)!=0)throw new InvalidOperationException("Браузер ещё обрабатывает предыдущую команду Windows.");
        var completion=new TaskCompletionSource<BrowserPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(4000);
        // The cancellation check immediately before Invoke is inside Inspect.
        // A timed-out provider never causes a second worker or queued click storm.
        CancellationToken request=deadline.Token;
        lock(gate)
        {
            if(disposed){Interlocked.Exchange(ref outstanding,0);throw new ObjectDisposedException(nameof(BrowserAccessibility));}
            worker??=StartWorker();
            jobs.Add(()=>{try{request.ThrowIfCancellationRequested();var result=action(request);Interlocked.Exchange(ref outstanding,0);completion.TrySetResult(result);}catch(Exception ex){Interlocked.Exchange(ref outstanding,0);completion.TrySetException(ex);}});
        }
        try{return await completion.Task.WaitAsync(request);}
        catch(OperationCanceledException)
        {
            _=completion.Task.ContinueWith(t=>{_=t.Exception;},TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously);
            if(token.IsCancellationRequested)throw;
            throw new TimeoutException("Браузер не ответил на запрос специальных возможностей Windows. Повторные команды не отправлялись.");
        }
    }
    private Thread StartWorker()
    {
        var thread=new Thread(()=>{foreach(var job in jobs.GetConsumingEnumerable())job();}){IsBackground=true,Name="Island browser accessibility"};
        thread.SetApartmentState(ApartmentState.MTA);thread.Start();return thread;
    }
    private BrowserPageResult Inspect(nint window,string service,bool play,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var root=AutomationElement.FromHandle(window);
        var documents=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Document));
        for(int i=0;i<Math.Min(documents.Count,32);i++)
        {
            var document=documents[i];
            string value=document.GetCurrentPropertyValue(ValuePattern.ValueProperty,true) as string??"";
            if(!AllowedAddress(value,service) && !(value.Length==0 && i==0 && AddressBarMatches(root,service)))continue;
            // Only buttons inside a verified musical document are eligible.
            var buttons=document.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
            AutomationElement selected=null;int best=0;
            for(int b=0;b<Math.Min(buttons.Count,300);b++)
            {
                token.ThrowIfCancellationRequested();var button=buttons[b];
                if(!button.Current.IsEnabled)continue;
                string name=button.Current.Name.Trim();
                if(name is "Пауза" or "Приостановить" or "Приостановить воспроизведение" or "Поставить на паузу" or "Pause")return new(true,true,false,window,IsIconic(window));
                int rank=name is "Воспроизвести" or "Воспроизведение" or "Продолжить воспроизведение" or "Play" or "Resume"?3:name is "Моя волна" or "Слушать Мою волну" or "Запустить Мою волну"?2:name is "Слушать" or "Играть"?1:0;
                if(rank>best){selected=button;best=rank;}
            }
            if(play && selected!=null && selected.TryGetCurrentPattern(InvokePattern.Pattern,out var pattern))
            {
                token.ThrowIfCancellationRequested();long before=revision();((InvokePattern)pattern).Invoke();return new(true,false,true,window,IsIconic(window),before);
            }
            return new(true,false,false,window,IsIconic(window));
        }
        return new();
    }
    public static bool AllowedAddress(string value,string service)
    {
        if(!value.Contains("://"))value="https://"+value;
        string safe=MusicLaunchOptions.SafeUrl(value,service);
        return safe.Length>0 && Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.AbsolutePath==new Uri(safe).AbsolutePath;
    }
    private static bool AddressBarMatches(AutomationElement root,string service)
    {
        var edits=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit));
        for(int i=0;i<Math.Min(edits.Count,100);i++)
        {
            var edit=edits[i];string name=edit.Current.Name.ToLowerInvariant(),id=edit.Current.AutomationId.ToLowerInvariant();
            // Never read other edits, password fields or page content.
            if(edit.Current.IsPassword || !(name.Contains("адрес") || name.Contains("address") || id=="omnibox"))continue;
            if(edit.TryGetCurrentPattern(ValuePattern.Pattern,out var pattern) && AllowedAddress(((ValuePattern)pattern).Current.Value,service))return true;
        }
        return false;
    }
    private static bool MusicTab(string name,string service)=>service=="yandex"?(name.EndsWith("Яндекс Музыка",StringComparison.OrdinalIgnoreCase) || name.EndsWith("Yandex Music",StringComparison.OrdinalIgnoreCase)):name.EndsWith("VK Музыка",StringComparison.OrdinalIgnoreCase);
    private static IEnumerable<nint> WindowsFor(string executable)
    {
        var handles=new List<nint>();var processes=new Dictionary<uint,bool>();
        EnumWindows((window,_)=>
        {
            if(!IsWindowVisible(window))return true;GetWindowThreadProcessId(window,out uint pid);
            if(!processes.TryGetValue(pid,out bool matches))
            {
                try{using var process=Process.GetProcessById((int)pid);matches=string.Equals(process.MainModule?.FileName,executable,StringComparison.OrdinalIgnoreCase);}
                catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){matches=false;}
                processes[pid]=matches;
            }
            if(matches)handles.Add(window);return true;
        },0);
        return handles;
    }
    public static void ShowWithoutActivation(nint window)=>ShowWindowAsync(window,4);
    public static void MinimizeWithoutActivation(nint window)=>ShowWindowAsync(window,7);
    public void Dispose(){lock(gate){if(disposed)return;disposed=true;jobs.CompleteAdding();}}
    private delegate bool WindowCallback(nint window,nint parameter);
    [DllImport("user32.dll")]private static extern bool EnumWindows(WindowCallback callback,nint parameter);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [DllImport("user32.dll")]private static extern bool ShowWindowAsync(nint window,int command);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowText(nint window,StringBuilder text,int max);
}
