using System.Windows;
using System.Windows.Media;

namespace LumaLyrics.App;

// A theme describes a visual system, not a replacement set of colors. These
// metrics are shared by playback, browsing, dialogs and settings.
internal sealed record ThemeDesign(
    string Id,string DisplayFont,double Headline,FontWeight Weight,
    CornerRadius Panel,CornerRadius Control,CornerRadius Input,CornerRadius Card,
    double RailWidth,double NavWidth,double NavHeight,bool NavLabels,
    Thickness ArtworkMat,double ArtworkRadius,double LyricScale,
    Thickness CardPadding,Thickness CardBorder)
{
    const string Sans="Segoe UI Variable, Microsoft YaHei UI";
    internal static ThemeDesign For(string id)=>id switch
    {
        "frost"=>new(id,Sans,28,FontWeights.Light,new(28),new(18),new(15),new(22),84,52,56,false,new(8),20,1,new(10),new(0)),
        "material"=>new(id,Sans,30,FontWeights.SemiBold,new(32,10,32,32),new(24),new(20),new(26,8,26,26),96,64,64,true,new(0),28,1,new(12),new(0)),
        "paper"=>new(id,"Georgia, SimSun",31,FontWeights.Normal,new(0),new(0),new(0),new(0),88,56,64,true,new(10),0,1,new(0,0,0,14),new(0,0,0,1)),
        "studio"=>new(id,"Bahnschrift, Microsoft YaHei UI",27,FontWeights.SemiBold,new(4),new(4),new(3),new(4),72,48,48,false,new(3),2,1,new(9),new(1)),
        "bauhaus"=>new(id,"Arial Black, Microsoft YaHei UI",30,FontWeights.Bold,new(2),new(2),new(2),new(2),80,48,52,false,new(0),0,1,new(10),new(1.5)),
        _=>new("liquid",Sans,25,FontWeights.SemiBold,new(26),new(12),new(10),new(18),80,48,48,false,new(0),26,1,new(8),new(1))
    };
}
