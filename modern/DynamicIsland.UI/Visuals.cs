using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island;

// One retained visual and one frame source, shared with panel transitions/audio.
public readonly record struct VisualFrame(double Seconds,double Delta,AudioFeatures Audio,float Opacity);
public interface IIslandVisual
{
    void Configure(MusicSnapshot track,VisualOptions options);
    void Frame(in VisualFrame frame);
    void Suspend();
}
public sealed class AmbientVisual : Canvas,IIslandVisual
{
    private sealed class Bank
    {
        public readonly Canvas Root=new(){IsHitTestVisible=false};
        public readonly Image First=new(){Width=640,Height=490},Second=new(){Width=610,Height=420};
        public Bank(Transform first,Transform second)
        {
            First.RenderTransform=first;Second.RenderTransform=second;
            SetLeft(First,-65);SetTop(First,-160);SetLeft(Second,-25);SetTop(Second,148);
            Root.Children.Add(First);Root.Children.Add(Second);
        }
        public void Clear(){First.Source=Second.Source=null;}
    }
    private static readonly Lazy<(byte[] pixels,int size)> mask=new(LoadMask);
    private readonly TranslateTransform moveA=new(),moveB=new();
    private readonly ScaleTransform scale=new(1,1,320,245);
    private readonly RectangleGeometry shape=new(){RadiusX=28,RadiusY=28};
    private readonly SolidColorBrush tint=new(),rimColor=new();
    private readonly Border rim=new(){CornerRadius=new(28),BorderThickness=new(1)};
    private Bank current,next;
    private VisualOptions options=new();
    private AccentColor requestedA,requestedB,fromA,fromB,toA,toB;
    private double mix=1;
    private int revision;
    private bool paletteReady,present,animatePalette;
    public event Action AnimationRequested;
    public event Action<Color,Color> PaletteChanged;
    public bool NeedsFrames=>mix<1;
    public AmbientVisual()
    {
        var first=new TransformGroup();first.Children.Add(scale);first.Children.Add(moveA);
        current=new(first,moveB);next=new(first,moveB);
        Children.Add(current.Root);Children.Add(next.Root);Children.Add(rim);
        Background=tint;rim.BorderBrush=rimColor;Clip=shape;Opacity=0;IsHitTestVisible=false;
    }
    private static (byte[],int) LoadMask()
    {
        using var resource=Application.GetResourceStream(new Uri("pack://application:,,,/Island.Wpf;component/Assets/ambient-rays.png")).Stream;
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=384;image.StreamSource=resource;image.EndInit();image.Freeze();
        var rgba=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);rgba.Freeze();
        var pixels=new byte[rgba.PixelWidth*rgba.PixelHeight*4];rgba.CopyPixels(pixels,rgba.PixelWidth*4,0);return(pixels,rgba.PixelWidth);
    }
    private static BitmapSource Texture(AccentColor color)
    {
        var source=mask.Value;var pixels=new byte[source.pixels.Length];var light=Light(color,.32);
        // The neutral raster is prepared once. Tinting is only on a palette worker,
        // never FFT/UI/render-frame work. No blur or per-frame bitmap uploads.
        for(int i=0;i<pixels.Length;i+=4)
        {
            int x=i/4%source.size,y=i/4/source.size;
            double edge=Math.Clamp(Math.Min(Math.Min(x,source.size-1-x),Math.Min(y,source.size-1-y))/28d,0,1);edge=edge*edge*(3-2*edge);
            int a=(int)(source.pixels[i+3]*source.pixels[i+2]/255d*edge);
            pixels[i]=(byte)(light.B*a/255);pixels[i+1]=(byte)(light.G*a/255);pixels[i+2]=(byte)(light.R*a/255);pixels[i+3]=(byte)a;
        }
        var image=BitmapSource.Create(source.size,pixels.Length/(source.size*4),96,96,PixelFormats.Pbgra32,null,pixels,source.size*4);image.Freeze();return image;
    }
    internal static Color Light(AccentColor c,double white=.42)=>Color.FromRgb((byte)(c.R+(255-c.R)*white),(byte)(c.G+(255-c.G)*white),(byte)(c.B+(255-c.B)*white));
    private static AccentColor Blend(AccentColor a,AccentColor b,double t)=>new((byte)Math.Round(a.R+(b.R-a.R)*t),(byte)Math.Round(a.G+(b.G-a.G)*t),(byte)Math.Round(a.B+(b.B-a.B)*t));
    public async void Configure(MusicSnapshot track,VisualOptions value)
    {
        options=value.Copy();value.Colors(track,out var a,out var b);
        current.Root.Visibility=next.Root.Visibility=options.Mode==VisualMode.Border?Visibility.Hidden:Visibility.Visible;
        if(revision>0 && a==requestedA && b==requestedB){if(!track.Playing || !animatePalette)Rest();return;}
        requestedA=a;requestedB=b;int version=++revision;bool animate=animatePalette;
        try
        {
            var textures=await Task.Run(()=>(Texture(b),Texture(a)));
            if(version!=revision || Dispatcher.HasShutdownStarted)return;
            if(paletteReady && mix<1){if(mix>=.5)(current,next)=(next,current);fromA=Blend(fromA,toA,mix);fromB=Blend(fromB,toB,mix);}
            else{fromA=toA;fromB=toB;}
            next.First.Source=textures.Item1;next.Second.Source=textures.Item2;toA=a;toB=b;
            if(!paletteReady || !animate || !present){mix=1;(current,next)=(next,current);next.Clear();}
            else mix=0;
            paletteReady=true;ApplyPalette();Rest();AnimationRequested?.Invoke();
        }
        catch(Exception ex)when(ex is System.IO.IOException or NotSupportedException or ArgumentException){if(version==revision)revision=0;/* Base glass and controls remain usable without the optional texture. */}
    }
    public void Presence(bool visible,bool animate)
    {
        bool changed=present!=visible || animatePalette!=animate;
        present=visible;animatePalette=animate;
        if(!visible || !animate){if(mix<1){mix=1;(current,next)=(next,current);next.Clear();ApplyPalette();}}
        if(changed)Rest();
    }
    private void ApplyPalette()
    {
        var a=Blend(fromA,toA,mix);var b=Blend(fromB,toB,mix);
        tint.Color=Color.FromArgb(100,(byte)(a.R*.25),(byte)(a.G*.36),(byte)(a.B*.42));
        rimColor.Color=Light(b);PaletteChanged?.Invoke(Light(b,.15),Light(a,.6));
        current.Root.Opacity=1-mix;next.Root.Opacity=mix;
        if(mix>=1){current.Root.Opacity=1;next.Root.Opacity=0;}
    }
    public void Size(double width,double height){shape.Rect=new Rect(0,0,width,height);rim.Width=Math.Max(0,width);rim.Height=Math.Max(0,height);}
    public void Frame(in VisualFrame frame)
    {
        if(mix<1){mix=Math.Min(1,mix+frame.Delta/.2);if(mix>=1){(current,next)=(next,current);next.Clear();}ApplyPalette();}
        double bass=frame.Audio.Bass,beat=frame.Audio.Beat*options.Sensitivity/100d,t=frame.Seconds;
        Opacity=present && options.Enabled?(.45+frame.Opacity*.5)*options.Intensity/100d:0;
        bool fluid=options.Mode!=VisualMode.Border;current.Root.Visibility=next.Root.Visibility=fluid?Visibility.Visible:Visibility.Hidden;
        if(fluid){moveA.X=Math.Sin(t*.39)*22;moveA.Y=Math.Cos(t*.31)*12;
        moveB.X=Math.Cos(t*.29)*18;moveB.Y=Math.Sin(t*.37)*18;
        scale.ScaleX=scale.ScaleY=1+bass*.07+beat*.035;
        current.First.Opacity=next.First.Opacity=.8+Math.Min(.2,bass*.14+beat*.12);
        current.Second.Opacity=next.Second.Opacity=.65+frame.Audio.Mid*.15;}
        rim.Opacity=options.Mode==VisualMode.Fluid?0:frame.Opacity*Math.Clamp(.12+bass*.3+beat*.25,0,.65);
    }
    public void Rest(){Opacity=present && options.Enabled ? .45*options.Intensity/100d : 0;rim.Opacity=0;current.Root.Visibility=next.Root.Visibility=options.Mode==VisualMode.Border?Visibility.Hidden:Visibility.Visible;}
    public void Suspend(){Presence(false,false);}
}
public sealed class SpectrumBars : FrameworkElement
{
    private Brush brush=new SolidColorBrush(Color.FromRgb(255,181,196));
    private AccentColor? color;
    private AudioFeatures audio;
    public long Paints {get;private set;}
    public void SetColor(AccentColor value){if(color==value)return;color=value;var next=new SolidColorBrush(Color.FromRgb(value.R,value.G,value.B));next.Freeze();brush=next;InvalidateVisual();}
    public void Update(AudioFeatures value){if(audio.Energy==value.Energy && audio.Bass==value.Bass && audio.Mid==value.Mid && audio.High==value.High && audio.Beat==value.Beat)return;audio=value;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc){Paints++;for(int i=0;i<7;i++){double band=i<2?audio.Bass:i<5?audio.Mid:audio.High;double h=3+Math.Min(15,band*12+audio.Energy*4+audio.Beat*(i%2==0?3:1));dc.DrawRoundedRectangle(brush,null,new Rect(i*4,(18-h)/2,2.2,h),1,1);}}
}
public sealed class WeatherIcon : FrameworkElement
{
    private static readonly Brush Cloud;
    private static readonly Pen Rain=new(new SolidColorBrush(Color.FromRgb(66,195,244)),2.8);
    private static readonly Brush Sun=new SolidColorBrush(Color.FromRgb(255,213,124));
    static WeatherIcon(){var brush=new LinearGradientBrush(Color.FromRgb(212,230,248),Color.FromRgb(126,158,194),90);brush.Freeze();Cloud=brush;Rain.Freeze();Sun.Freeze();}
    public int Code {get;set;}=3;
    public void Set(int code){if(Code!=code){Code=code;InvalidateVisual();}}
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth/64,ActualHeight/56));
        if(Code==0)dc.DrawEllipse(Sun,null,new Point(32,23),16,16);
        else{dc.DrawEllipse(Cloud,null,new Point(29,22),15,15);dc.DrawEllipse(Cloud,null,new Point(46,29),12,11);dc.DrawEllipse(Cloud,null,new Point(16,30),12,10);dc.DrawRoundedRectangle(Cloud,null,new Rect(14,26,35,14),7,7);}
        if(Code is >=51 and <=82)for(int i=0;i<3;i++)dc.DrawLine(Rain,new Point(21+i*13,46),new Point(17+i*13,52));dc.Pop();
    }
}
