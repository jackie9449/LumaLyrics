using System.Windows;

namespace LumaLyrics.App;

public partial class MainWindow
{
    bool updatingMainTranslationToggle;

    void MainChineseTranslationToggled(object sender,RoutedEventArgs e)
    {
        if(!updatingMainTranslationToggle && coordinator is not null)
            coordinator.SetTranslationVisibility(MainChineseTranslationToggle.IsChecked==true,settings.OverlayShowChineseTranslation);
    }

    void RefreshMainTranslationToggle()
    {
        updatingMainTranslationToggle=true;
        try{MainChineseTranslationToggle.IsChecked=settings.ShowChineseTranslation;}
        finally{updatingMainTranslationToggle=false;}
        MainChineseTranslationToggle.ToolTip=settings.ShowChineseTranslation?"隐藏主窗口中文译文":"显示主窗口中文译文";
    }
}
