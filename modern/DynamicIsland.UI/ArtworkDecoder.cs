using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island;
public sealed class ArtworkDecoder : IArtworkDecoder
{
    public ArtworkData Decode(Stream stream)
    {
        try{return DecodeCore(stream);}
        catch(Exception ex)when(ex is System.IO.FileFormatException or NotSupportedException or ArgumentException){throw new InvalidDataException("Unsupported or invalid artwork",ex);}
    }
    private static ArtworkData DecodeCore(Stream stream)
    {
        // WIC downsampling on a worker; no GDI Bitmap or UI Dispatcher ownership.
        var image=new BitmapImage(); image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad; image.DecodePixelWidth=384;image.StreamSource=stream;image.EndInit();image.Freeze();
        if(image.PixelHeight>4096) throw new InvalidDataException("Invalid artwork aspect ratio");
        var small=new TransformedBitmap(image,new ScaleTransform(32d/image.PixelWidth,32d/image.PixelWidth));small.Freeze();
        var pixels=new FormatConvertedBitmap(small,PixelFormats.Bgra32,null,0);pixels.Freeze();
        byte[] data=new byte[pixels.PixelWidth*pixels.PixelHeight*4]; pixels.CopyPixels(data,pixels.PixelWidth*4,0);
        var counts=new double[512];var reds=new double[512];var greens=new double[512];var blues=new double[512];
        for(int i=0;i<data.Length;i+=4)
        {
            int blue=data[i],green=data[i+1],red=data[i+2]; int max=Math.Max(red,Math.Max(green,blue)),min=Math.Min(red,Math.Min(green,blue));
            if(data[i+3]<128)continue;
            // Occupied colour clusters, not the single most saturated pixel.
            // Neutral/black-and-white covers keep their own neutral palette.
            int bin=(red>>5)*64+(green>>5)*8+(blue>>5);double w=.25+(max-min)/255d;
            counts[bin]+=w;reds[bin]+=red*w;greens[bin]+=green*w;blues[bin]+=blue*w;
        }
        AccentColor Cluster(int i)=>counts[i]>0?AccentColor.FromArgb((int)(reds[i]/counts[i]),(int)(greens[i]/counts[i]),(int)(blues[i]/counts[i])):AccentColor.FromArgb(96,104,112);
        int dominant=0;double best=-1;
        for(int i=0;i<512;i++){var c=Cluster(i);double light=(c.R+c.G+c.B)/765d;double score=counts[i]*(.2+Math.Sin(Math.PI*light));if(score>best){best=score;dominant=i;}}
        var primary=Cluster(dominant);var secondary=primary;best=-1;
        for(int i=0;i<512;i++)if(counts[i]>0){var c=Cluster(i);double distance=Math.Sqrt(Math.Pow(c.R-primary.R,2)+Math.Pow(c.G-primary.G,2)+Math.Pow(c.B-primary.B,2));double score=Math.Sqrt(counts[i])*(distance+15)*(.35+(c.R+c.G+c.B)/765d);if(score>best){best=score;secondary=c;}}
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var output=new MemoryStream();encoder.Save(output);
        return new(output.ToArray(),primary,secondary);
    }
    public static BitmapSource Image(byte[] bytes)
    {
        if(bytes==null) return null;
        using var input=new MemoryStream(bytes);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=input;bitmap.EndInit();bitmap.Freeze();return bitmap;
    }
}
