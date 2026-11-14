using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
namespace Island;
// Retargeting keeps both opacity and velocity, including layers fading out.
internal sealed class OpacityMixer
{
    private sealed class Layer{public FrameworkElement Element;public Spring Motion=new();}
    private readonly List<Layer> layers=new();
    private readonly bool ownClock;
    private bool attached;
    private long last;
    public bool Active {get;private set;}
    public OpacityMixer(bool clock=false){ownClock=clock;}
    public void Select(IEnumerable<FrameworkElement> all,IEnumerable<FrameworkElement> selected,bool animate)
    {
        var chosen=selected.ToHashSet();
        foreach(var element in all){var layer=layers.FirstOrDefault(l=>l.Element==element);if(layer==null){layer=new(){Element=element};layer.Motion.Value=element.Visibility==Visibility.Visible?element.Opacity:0;layers.Add(layer);}layer.Motion.Target=chosen.Contains(element)?1:0;element.IsHitTestVisible=chosen.Contains(element);if(!animate)layer.Motion.Snap();Apply(layer);}
        Active=animate && layers.Any(l=>Math.Abs(l.Motion.Value-l.Motion.Target)>.001 || Math.Abs(l.Motion.Velocity)>.015);
        if(ownClock && Active && !attached){attached=true;last=Stopwatch.GetTimestamp();CompositionTarget.Rendering+=Frame;}if(!Active)Detach();
    }
    private static void Apply(Layer layer){layer.Element.Opacity=Math.Clamp(layer.Motion.Value,0,1);layer.Element.Visibility=layer.Motion.Target>0 || layer.Motion.Value>.001?Visibility.Visible:Visibility.Collapsed;}
    public bool Step(double dt){bool active=false;foreach(var layer in layers){active|=layer.Motion.Step(Math.Min(.05,dt)*1.6);Apply(layer);}Active=active;if(!active)Detach();return active;}
    private void Frame(object sender,EventArgs args){long now=Stopwatch.GetTimestamp();double dt=(now-last)/(double)Stopwatch.Frequency;last=now;Step(dt);}
    public void Settle(){foreach(var layer in layers){layer.Motion.Snap();Apply(layer);}Active=false;Detach();}
    private void Detach(){if(attached){attached=false;CompositionTarget.Rendering-=Frame;}}
}
