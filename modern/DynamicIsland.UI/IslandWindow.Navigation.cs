using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island;
public partial class IslandWindow
{
    private bool pageMorph;
    private readonly OpacityMixer pageFader=new();
    private static readonly IEasingFunction PageEase=new CubicEase{EasingMode=EasingMode.EaseOut};
    private bool BeginPageChange(string target)=>target!=page && expanded && IsVisible && !transition && Config.Animations && systemMotion;
    private void NavigationState()
    {
        foreach(var pair in new[]{(HomeTabButton,"home"),(MusicTabButton,"music"),(WeatherTabButton,"weather")}){
            var dot=pair.Item1.Content as Border;double target=pair.Item2==page?18:6;
            if(dot==null){dot=new Border{Height=6,Width=target,CornerRadius=new(3)};pair.Item1.Content=dot;}
            double current=dot.Width;dot.BeginAnimation(WidthProperty,null);dot.Width=target;dot.Background=pair.Item2==page?Brushes.White:new SolidColorBrush(Color.FromRgb(126,126,135));
            if(IsVisible && Config.Animations && systemMotion && Math.Abs(current-target)>.1)dot.BeginAnimation(WidthProperty,new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(160)){EasingFunction=PageEase,FillBehavior=FillBehavior.Stop});
            pair.Item1.Width=28;
        }
    }
    private FrameworkElement[] PageElements(string value)=>value switch{
        "home"=>new FrameworkElement[]{homePage},"weather"=>new FrameworkElement[]{weatherPage},"settings"=>new FrameworkElement[]{settingsPage},
        _=>track.Available?new FrameworkElement[]{MusicCard,MusicContent,lyricsCard}:new FrameworkElement[]{ResumeContent,lyricsCard}};
    private FrameworkElement[] AllPageElements()=>new FrameworkElement[]{homePage,weatherPage,settingsPage,MusicCard,MusicContent,ResumeContent,lyricsCard};
    private void FinishPageChange(bool animated)
    {
        pageFader.Select(AllPageElements(),PageElements(page),animated);pageMorph=pageFader.Active;
    }
    private void StopPageChange(){pageFader.Settle();pageMorph=false;}
}
