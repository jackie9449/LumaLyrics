using System.Windows;

namespace LumaLyrics.App;

public partial class MainWindow
{
    bool updatingMainTranslationToggle;

    void MainChineseTranslationToggled(object sender,RoutedEventArgs e)
    {
        if(!updatingMainTranslationToggle && coordinator is not null)
            coordinator.SetTranslationVisibility(MainChineseTranslationToggle.IsChecked==true);
    }

    void RefreshMainTranslationToggle()
    {
        updatingMainTranslationToggle=true;
        try{MainChineseTranslationToggle.IsChecked=settings.ShowChineseTranslation;}
        finally{updatingMainTranslationToggle=false;}
        MainChineseTranslationToggle.ToolTip=settings.ShowChineseTranslation?UiText.T("隐藏中文译文"):UiText.T("显示中文译文");
    }
}
