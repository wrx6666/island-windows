using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Island;
public partial class IslandWindow
{
    private readonly Canvas homePage=new(),weatherPage=new();
    private readonly ContentControl settingsPage=new();
    private readonly TextBlock homeTitle=new(),homeArtist=new(),homeWeather=new(),headerTime=new();
    private readonly Image homeCover=new(){Width=44,Height=44,Stretch=Stretch.UniformToFill};
    private readonly Button homePlay=new();
    private readonly TextBlock[] weatherValues=new TextBlock[8];
    private SettingsWindow settingsEditor;
    private readonly ProgressBar humidityGauge=new(){Height=5,Maximum=100,Margin=new(0,10,0,0),Foreground=Brushes.DeepSkyBlue,Background=new SolidColorBrush(Color.FromRgb(65,65,68))};
    private readonly ProgressBar uvGauge=new(){Height=5,Maximum=11,Margin=new(0,10,0,0),Foreground=Brushes.Gold,Background=new SolidColorBrush(Color.FromRgb(65,65,68))};
    private static Brush Gray=>new SolidColorBrush(Color.FromRgb(164,164,172));
    private Border Tile(UIElement child)=>new(){Background=(Brush)FindResource("GlassFill"),BorderBrush=(Brush)FindResource("GlassEdge"),BorderThickness=new(.6),CornerRadius=new(22),Child=child};
    private static TextBlock Text(string text,double size=13,Brush color=null)=>new(){Text=text,FontSize=size,Foreground=color??Brushes.White,TextWrapping=TextWrapping.Wrap};
    private Button ActionButton(string glyph,string title,Action action,double width,double height)
    {
        var body=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new(height<40?4:12)};
        var icon=Text(glyph,20);icon.FontFamily=new("Segoe MDL2 Assets");icon.Margin=new(0,0,12,0);body.Children.Add(icon);body.Children.Add(Text(title));
        var button=new Button{Width=width,Height=height,Content=new Border{Width=width,Height=height,Background=(Brush)FindResource("GlassFill"),CornerRadius=new(22),Child=body},Style=(Style)FindResource("GlassIconButton"),HorizontalContentAlignment=HorizontalAlignment.Stretch,ToolTip=title};
        button.Click+=(_,_)=>action();return button;
    }
    private static void Place(Canvas canvas,UIElement child,double x,double y){Canvas.SetLeft(child,x);Canvas.SetTop(child,y);canvas.Children.Add(child);}
    private void InitializePages()
    {
        InitializeLyrics();
        Expanded.Width=708;Expanded.Height=580;Expanded.LayoutTransform=new ScaleTransform(PanelScale,PanelScale);Root.Width=ExpandedWidth;Root.Height=580*PanelScale;
        Place(Expanded,homePage,54,140);Place(Expanded,weatherPage,0,62);Place(Expanded,settingsPage,24,62);
        Place(Expanded,headerTime,0,23);headerTime.FontSize=13;headerTime.FontWeight=FontWeights.SemiBold;
        Place(homePage,ActionButton("\uE8D6","Музыка",()=>SetPage("music"),180,58),14,0);
        Place(homePage,ActionButton("\uE706","Погода",()=>SetPage("weather"),180,58),204,0);
        Place(homePage,ActionButton("\uE713","Настройки",OpenSettingsPage,180,58),394,0);
        Place(homePage,ActionButton("\uE74F","Звук игры",()=>((App)Application.Current).ToggleAppMute(),132,54),14,72);
        Place(homePage,ActionButton("\uE718","Закрепить",()=>PinRequested?.Invoke(),132,54),160,72);
        Place(homePage,ActionButton("\uE768","Продолжить",()=>TogglePlay?.Invoke(),132,54),306,72);
        Place(homePage,ActionButton("\uE8A7","Свернуть",()=>CloseRequested?.Invoke(),132,54),452,72);
        var music=new Canvas();Place(music,homeCover,12,14);Place(music,homeTitle,68,13);Place(music,homeArtist,68,35);homeTitle.Width=158;homeTitle.FontSize=13;homeTitle.TextTrimming=TextTrimming.CharacterEllipsis;homeTitle.TextWrapping=TextWrapping.NoWrap;homeArtist.Width=158;homeArtist.Foreground=Gray;homeArtist.FontSize=11.5;homeArtist.TextTrimming=TextTrimming.CharacterEllipsis;
        Button Transport(string glyph,Action action){var b=new Button{Content=new TextBlock{Text=glyph,FontFamily=new("Segoe MDL2 Assets"),FontSize=16},Width=30,Height=36,Style=(Style)FindResource("GlassIconButton"),FontSize=16,FontFamily=new("Segoe MDL2 Assets")};b.Click+=(_,_)=>action();return b;}
        Place(music,Transport("\uE892",()=>Previous?.Invoke()),232,18);homePlay.Style=(Style)FindResource("GlassIconButton");homePlay.Width=30;homePlay.Height=36;homePlay.FontFamily=new("Segoe MDL2 Assets");homePlay.Click+=(_,_)=>TogglePlay?.Invoke();Place(music,homePlay,266,18);Place(music,Transport("\uE893",()=>Next?.Invoke()),300,18);
        var mini=Tile(music);mini.Width=340;mini.Height=72;Place(homePage,mini,14,142);
        var weatherButton=ActionButton("\uE706","",()=>SetPage("weather"),220,72);var weatherStack=new StackPanel{Margin=new(16,10,16,10)};homeWeather.TextTrimming=TextTrimming.CharacterEllipsis;homeWeather.TextWrapping=TextWrapping.NoWrap;homeWeather.Width=188;homeWeather.FontSize=13;weatherStack.Children.Add(homeWeather);weatherStack.Children.Add(Text("Москва",11.5,Gray));var weatherTile=Tile(weatherStack);weatherTile.Width=220;weatherTile.Height=72;weatherButton.Content=weatherTile;Place(homePage,weatherButton,364,142);
        Place(weatherPage,Text("Москва",13,Gray),16,0);var refresh=ActionButton("\uE72C","Обновить",()=>((App)Application.Current).RefreshWeather(),126,34);Place(weatherPage,refresh,560,-8);
        string[] titles={"МОСКВА","ВЕТЕР","ВЛАЖНОСТЬ","ДАВЛЕНИЕ","СОЛНЦЕ","УФ-ИНДЕКС","ОСАДКИ","ПО ЧАСАМ"};
        for(int i=0;i<8;i++){var stack=new StackPanel{Margin=new(14)};stack.Children.Add(Text(titles[i],10.5,Gray));weatherValues[i]=Text("—",i==7?13:25);weatherValues[i].Margin=new(0,20,0,0);stack.Children.Add(weatherValues[i]);if(i==2)stack.Children.Add(humidityGauge);if(i==5)stack.Children.Add(uvGauge);var tile=Tile(stack);tile.Width=160;tile.Height=154;Place(weatherPage,tile,14+i%4*170,40+i/4*164);}
    }
    public void OpenSettingsPage()
    {
        if(settingsEditor==null){settingsEditor=new SettingsWindow((App)Application.Current,()=>SetPage("home"));var content=settingsEditor.Content;settingsEditor.Content=null;settingsPage.Content=content;}
        SetPage("settings");
    }
    private void UpdatePagesWeather(WeatherSnapshot w)
    {
        humidityGauge.Value=double.IsFinite(w.Humidity)?w.Humidity:0;uvGauge.Value=double.IsFinite(w.Uv)?w.Uv:0;
        homeWeather.Text=w.TemperatureText+" · "+w.Description;
        weatherValues[0].FontSize=16;weatherValues[0].Text=w.TemperatureText+"\n"+w.Description+(w.Available?"\nОщущается "+WeatherSnapshot.Degrees(w.FeelsLike):"");
        weatherValues[1].Text=Number(w.Wind)+" м/с";weatherValues[2].Text=Number(w.Humidity)+"%";weatherValues[3].Text=Number(w.Pressure*.750062)+"\nмм рт. ст.";
        weatherValues[4].FontSize=18;weatherValues[4].Text=w.Sunrise==default?"—":"↑ "+w.Sunrise.ToString("HH:mm")+"\n↓ "+w.Sunset.ToString("HH:mm");weatherValues[5].Text=Number(w.Uv);weatherValues[6].Text=Number(w.Rain)+" мм";weatherValues[7].Text=w.Hourly;
    }
    private double ExpandedWidth=>708*PanelScale;
}
