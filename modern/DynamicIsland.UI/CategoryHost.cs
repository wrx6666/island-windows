using System.Windows;
using System.Windows.Controls;
namespace Island;
internal sealed class CategoryHost : Grid
{
    private FrameworkElement selected;
    private readonly OpacityMixer mixer=new(true);
    public Func<bool> MotionAllowed {get;set;}=()=>true;
    public CategoryHost(){ClipToBounds=true;IsVisibleChanged+=(_,_)=>{if(!IsVisible)mixer.Settle();};}
    public void Select(FrameworkElement content)
    {
        if(ReferenceEquals(selected,content))return;
        selected=content;if(!Children.Contains(content)){content.Opacity=0;Children.Add(content);}
        mixer.Select(Children.OfType<FrameworkElement>(),new[]{content},IsVisible && MotionAllowed());
    }
}
