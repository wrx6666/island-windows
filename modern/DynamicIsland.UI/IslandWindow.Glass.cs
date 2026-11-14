using System.Windows.Media;

namespace Island;
public partial class IslandWindow
{
    private double lastAudibleVolume=.5;
    private void InitializeGlass()
    {
        PlayButton.Background=SeekSlider.Foreground=VolumeSlider.Foreground=Brushes.White;
        Bars.SetColor(new(255,255,255));
        VolumeButton.Click+=(_,_)=>{
            if(VolumeSlider.Value>.001){lastAudibleVolume=VolumeSlider.Value;VolumeSlider.Value=0;}
            else VolumeSlider.Value=lastAudibleVolume;
        };
        VolumeSlider.ValueChanged+=(_,_)=>UpdateVolumeLabel();
    }
    private void UpdateVolumeLabel()
    {
        VolumeValue.Text=track.CanVolume?Math.Round(VolumeSlider.Value*100)+"%":"—";
        VolumeGlyph.Text=VolumeSlider.Value<=.001?"\uE74F":"\uE767";
        VolumeButton.ToolTip="Выключить / включить звук · "+VolumeValue.Text;
    }
}
