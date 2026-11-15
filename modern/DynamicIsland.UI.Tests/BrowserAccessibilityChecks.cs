using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Island;

public static class BrowserAccessibilityChecks
{
    private sealed class DocumentPanel : StackPanel
    {
        public string Address="https://music.yandex.ru/";
        protected override AutomationPeer OnCreateAutomationPeer()=>new DocumentPeer(this);
    }
    private sealed class DocumentPeer : FrameworkElementAutomationPeer,IValueProvider
    {
        public DocumentPeer(DocumentPanel owner):base(owner){}
        protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Document;
        protected override string GetNameCore()=>"Owned music document fixture";
        public override object GetPattern(PatternInterface patternInterface)=>patternInterface==PatternInterface.Value?this:base.GetPattern(patternInterface);
        public bool IsReadOnly=>true;
        public string Value=>((DocumentPanel)Owner).Address;
        public void SetValue(string value)=>throw new InvalidOperationException();
    }
    public static async Task Run(App app,string directory)
    {
        Directory.CreateDirectory(directory);var report=new List<string>();
        void Check(bool value,string message){if(!value)throw new Exception(message);report.Add("PASS "+message);File.WriteAllLines(Path.Combine(directory,"checks.txt"),report);}
        Check(!new MusicLaunchOptions().UseExtension,"Normal launch does not require an extension");
        foreach(string url in new[]{"https://music.yandex.ru.evil.test/","https://user@music.yandex.ru/","http://music.yandex.ru/","https://vk.com/video"})
            Check(!BrowserAccessibility.AllowedAddress(url,"yandex"),"Reject foreign or unsafe document address");
        Check(BrowserAccessibility.AllowedAddress("music.yandex.ru/album/123","yandex"),"Address bar without a scheme is supported");
        Check(!BrowserAccessibility.AllowedAddress("https://vk.com/video","vk"),"VK video is not mistaken for music");
        using var access=new BrowserAccessibility();Check(!access.WorkerStarted && !access.Busy,"Idle accessibility client has no thread or scans");
        var document=new DocumentPanel();var button=new Button{Content="Play fixture"};AutomationProperties.SetName(button,"Воспроизвести");document.Children.Add(button);int clicked=0;
        button.Click+=(_,_)=>{clicked++;AutomationProperties.SetName(button,"Пауза");};
        var fixture=new Window{Content=document,Width=300,Height=150,Left=-10000,Top=-10000,ShowActivated=false,ShowInTaskbar=false,Title="Owned accessibility fixture"};
        try
        {
            fixture.Show();nint handle=new WindowInteropHelper(fixture).Handle;await Task.Delay(100);
            nint foreground=Native.GetForegroundWindow();
            var result=await access.InspectWindowAsync(handle,"yandex",true,CancellationToken.None);await Task.Delay(100);
            Check(result.Found && result.Invoked && clicked==1,"Verified music document Play is invoked once through Windows, without focus or key input");
            Check(Native.GetForegroundWindow()==foreground,"Windows Invoke leaves foreground window unchanged in owned provider fixture");
            result=await access.InspectWindowAsync(handle,"yandex",true,CancellationToken.None);await Task.Delay(50);
            Check(result.Playing && !result.Invoked && clicked==1,"Already playing page is not toggled back to pause");
            document.Address="https://music.yandex.ru.evil.test/";AutomationProperties.SetName(button,"Воспроизвести");
            result=await access.InspectWindowAsync(handle,"yandex",true,CancellationToken.None);
            Check(!result.Found && clicked==1,"Matching Play label on foreign page is never invoked");
            document.Address="https://music.yandex.ru/";using var cancel=new CancellationTokenSource();cancel.Cancel();bool canceled=false;
            try{await access.InspectWindowAsync(handle,"yandex",true,cancel.Token);}catch(OperationCanceledException){canceled=true;}
            Check(canceled && clicked==1 && !access.Busy,"Canceled command cannot leave a queued browser click");
            BrowserAccessibility.MinimizeWithoutActivation(handle);await Task.Delay(100);
            result=await access.InspectWindowAsync(handle,"yandex",true,CancellationToken.None);await Task.Delay(100);
            Check(result.Invoked && clicked==2,"Owned minimized provider supports Play without activating its window");
        }
        finally{fixture.Close();}
    }
}
