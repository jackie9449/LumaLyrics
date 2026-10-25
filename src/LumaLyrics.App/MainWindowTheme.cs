using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LumaLyrics.App;

public partial class MainWindow
{
    void ApplyThemeIdentity()
    {
        var d=ThemeDesign.For(settings.UiTheme);
        NavigationColumn.Width=new(d.RailWidth);
        NavigationShell.CornerRadius=d.Id switch{"frost"=>new(24),"material"=>new(32),_=>new(0)};
        NavigationShell.Margin=d.Id switch{"frost"=>new(8,12,0,20),"material"=>new(10,12,2,20),_=>new(0,6,0,20)};
        NavigationShell.BorderThickness=d.Id is "frost" or "material"?new(0):new(0,0,1,0);
        bool labelFirst=d.Id is "paper" or "studio" or "bauhaus";
        Grid.SetRow(PlaybackEyebrow,labelFirst?0:1);Grid.SetRow(PlaybackHeadline,labelFirst?1:0);
        PlaybackEyebrow.Margin=labelFirst?new(1,0,0,9):new(1,7,0,0);
        PlaybackEyebrow.Text=d.Id switch{"paper"=>"L U M A  /  L I N E R  N O T E S","studio"=>"LUMA  /  AFTER HOURS","bauhaus"=>"L U M A   —   S O U N D  &  F O R M",_=>"L I S T E N   C L O S E R"};
        PlaybackHeader.Margin=new(0,0,0,d.Id=="paper"?24:20);
        LyricsPanel.BorderThickness=d.Id switch{"frost" or "material"=>new(0),"paper"=>new(1,0,0,0),"bauhaus"=>new(1.5),_=>new(1)};
        LyricsPanel.Effect=d.Id=="frost"?new DropShadowEffect{BlurRadius=28,ShadowDepth=7,Opacity=.065,Color=Color.FromRgb(35,57,46),RenderingBias=RenderingBias.Performance}:null;
        ApplyPlaybackLayout();ClipArtwork();
    }
}
