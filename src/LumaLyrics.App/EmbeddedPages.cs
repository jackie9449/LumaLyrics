using System.Windows;
using System.Windows.Controls;

namespace LumaLyrics.App;

public partial class MainWindow
{
    readonly ContentControl embeddedPageHost=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    Window? embeddedPage;

    void ShowEmbeddedPage(Window page,Button navigation)
    {
        if(!MainPageHost.Children.Contains(embeddedPageHost))MainPageHost.Children.Add(embeddedPageHost);
        var content=page.Content;
        page.Content=null;
        embeddedPage=page;
        embeddedPageHost.Content=content;
        embeddedPageHost.Visibility=Visibility.Visible;
        LyricsPage.Visibility=Visibility.Collapsed;
        if(musicLibrary is not null)musicLibrary.Visibility=Visibility.Collapsed;
        foreach(var button in new[]{LyricsNav,MusicNav,SettingsNav,DiagnosticsNav})
        {
            bool active=ReferenceEquals(button,navigation);
            AppMotion.SetIsSelected(button,active);
            button.SetResourceReference(Control.ForegroundProperty,active?"AccentBrush":"MutedBrush");
            if(active)button.SetResourceReference(Control.BackgroundProperty,"AccentSoftBrush");
            else button.ClearValue(Control.BackgroundProperty);
        }
        page.Closed+=(_,_)=>{if(ReferenceEquals(embeddedPage,page)){embeddedPage=null;embeddedPageHost.Content=null;ShowLyricsPage();}};
        AppMotion.Reveal(embeddedPageHost);
    }

    void CloseEmbeddedPage()
    {
        var page=embeddedPage;
        embeddedPage=null;
        embeddedPageHost.Content=null;
        embeddedPageHost.Visibility=Visibility.Collapsed;
        page?.Close();
    }
}
