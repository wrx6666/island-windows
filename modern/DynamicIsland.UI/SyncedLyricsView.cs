using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Island;

// Retained text: only its translation and opacity change between lyric timestamps.
// The window's existing frame clock runs while a transition is in progress, then unsubscribes.
public sealed class SyncedLyricsView : Grid
{
    private sealed class Line
    {
        public TextBlock Text;
        public double Center;
        public readonly Spring Opacity=new(){Value=.32,Target=.32};
    }
    private readonly Canvas content=new();
    private readonly TranslateTransform translation=new();
    private readonly Spring scroll=new();
    private readonly List<Line> lines=new(),fading=new();
    private bool positioned;
    public bool Animating {get;private set;}
    public int CurrentIndex {get;private set;}=-1;
    public double Offset=>translation.Y;
    public double TargetOffset=>scroll.Target;
    public string CurrentText=>CurrentIndex>=0 && CurrentIndex<lines.Count?lines[CurrentIndex].Text.Text:"";
    public double CurrentCenter=>CurrentIndex>=0 && CurrentIndex<lines.Count?lines[CurrentIndex].Center+translation.Y:double.NaN;
    public SyncedLyricsView()
    {
        ClipToBounds=true;IsHitTestVisible=false;content.RenderTransform=translation;Children.Add(content);
        var edges=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(0,1)};
        edges.GradientStops.Add(new(Colors.Transparent,0));edges.GradientStops.Add(new(Colors.White,.1));edges.GradientStops.Add(new(Colors.White,.9));edges.GradientStops.Add(new(Colors.Transparent,1));edges.Freeze();OpacityMask=edges;
        SizeChanged+=(_,_)=>ArrangeLines();
    }
    public void SetDocument(LyricsDocument document)
    {
        content.Children.Clear();lines.Clear();fading.Clear();CurrentIndex=-1;positioned=false;Animating=false;
        foreach(var lyric in document.Lines){
            var text=new TextBlock{Text=string.IsNullOrWhiteSpace(lyric.Text)?"♪":lyric.Text,FontSize=18,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,LineHeight=25,Opacity=.32};
            lines.Add(new(){Text=text});content.Children.Add(text);
        }
        ArrangeLines();
    }
    private void ArrangeLines()
    {
        if(ActualWidth<=0)return;
        double top=0;
        foreach(var line in lines){line.Text.Width=ActualWidth;line.Text.Measure(new(ActualWidth,double.PositiveInfinity));double height=line.Text.DesiredSize.Height;Canvas.SetTop(line.Text,top);line.Center=top+height/2;top+=height+26;}
        SetTarget();scroll.Snap();translation.Y=scroll.Value;
    }
    private void SetTarget(){if(lines.Count>0)scroll.Target=ActualHeight/2-lines[Math.Clamp(CurrentIndex,0,lines.Count-1)].Center;}
    public void Select(int index,bool animate)
    {
        CurrentIndex=index;SetTarget();
        for(int i=0;i<lines.Count;i++){
            var line=lines[i];double target=i==index?1:.32;
            if(line.Opacity.Target!=target){line.Opacity.Target=target;if(!fading.Contains(line))fading.Add(line);}
        }
        if(!animate || !positioned)Settle();else Animating=true;
        positioned=true;
    }
    public bool Step(double seconds)
    {
        if(!Animating)return false;
        bool active=scroll.Step(seconds*1.6);translation.Y=scroll.Value;
        for(int i=fading.Count-1;i>=0;i--){var line=fading[i];bool moving=line.Opacity.Step(seconds*1.6);line.Text.Opacity=Math.Clamp(line.Opacity.Value,0,1);active|=moving;if(!moving)fading.RemoveAt(i);}
        return Animating=active;
    }
    public void Settle()
    {
        scroll.Snap();translation.Y=scroll.Value;
        foreach(var line in lines){line.Opacity.Snap();line.Text.Opacity=line.Opacity.Value;}fading.Clear();Animating=false;
    }
    public void Deactivate(){Settle();positioned=false;}
}
