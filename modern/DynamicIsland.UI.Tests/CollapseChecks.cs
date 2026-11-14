using System.Diagnostics;
using System.IO;
using System.Windows.Media;

namespace Island;
public static class CollapseChecks
{
    public static async Task Run(App app,string directory)
    {
        Directory.CreateDirectory(directory);var results=new List<object>();var panel=app.Panel;var overlay=app.Overlay;
        var track=new MusicSnapshot{Title="Compact transition",Artist="Diagnostic fixture",Source="music.yandex.ru",Playing=true};track.SetTimeline(10,200,1);
        app.Config.Visual.Enabled=false;app.Music.DiagnosticTrack(track);overlay.SetEnabled(true);
        foreach(var corner in new[]{(0d,0d),(1d,0d),(0d,1d),(1d,1d)}){
            app.Config.OverlayX=corner.Item1;app.Config.OverlayY=corner.Item2;overlay.Reposition();overlay.Open(true,true);await Task.Delay(100);
            var viewport=OverlayNative.Bounds(panel.Handle);int resizes=0;
            System.Windows.SizeChangedEventHandler resized=(_,_)=>resizes++;panel.SizeChanged+=resized;
            double worstDelay=0;var clock=Stopwatch.StartNew();double last=0;overlay.Close(true);
            while(clock.ElapsedMilliseconds<950){
                await Task.Delay(16);double now=clock.Elapsed.TotalMilliseconds;worstDelay=Math.Max(worstDelay,now-last);last=now;
                if(OverlayNative.Bounds(panel.Handle)!=viewport)throw new Exception($"Collapsing resized/moved native viewport at corner {corner}: {viewport} -> {OverlayNative.Bounds(panel.Handle)}");
            }
            panel.SizeChanged-=resized;
            if(resizes!=0 || panel.ExpandedState || overlay.Requested || panel.Rendering)throw new Exception("Compact handoff must settle without a native resize or running animation");
            var dpi=VisualTreeHelper.GetDpi(panel);var body=panel.BodyOnScreen;var input=OverlayNative.InputBounds(panel.Handle);
            if(Math.Abs(body.Width-368*dpi.DpiScaleX)>1 || Math.Abs(body.Height-64*dpi.DpiScaleY)>1)throw new Exception("Collapsed music card has wrong visible dimensions");
            if(Math.Abs(input.Width-body.Width)>1 || Math.Abs(input.Height-body.Height)>1)throw new Exception("Native input region must match compact card");
            var emptyPoint=new System.Drawing.Point(viewport.Left+viewport.Width/2,corner.Item2<.5?viewport.Bottom-4:viewport.Top+4);
            if(Native.WindowAt(emptyPoint)==panel.Handle)throw new Exception("Transparent viewport intercepts clicks outside compact card");
            var settled=body;await Task.Delay(120);panel.UpdateActivity();
            if(panel.BodyOnScreen!=settled)throw new Exception("Settled compact card must remain stationary");
            WpfScenarios.Save(panel,Path.Combine(directory,$"compact-{corner.Item1}-{corner.Item2}.png"));
            results.Add(new{corner,viewport,body,input,resizes,worstDispatcherIntervalMs=worstDelay});
        }
        File.WriteAllText(Path.Combine(directory,"collapse.json"),new JsonCodec().Serialize(results));
        File.WriteAllText(Path.Combine(directory,"checks.txt"),"PASS: four anchors; no native surface resize or movement on collapse; stable 368x64 card; input region excludes transparent area; frame clock stops after settling. Dispatcher intervals describe this diagnostic run only.");
    }
}
