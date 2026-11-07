using System.Windows;
using System.Windows.Controls;
namespace LumaLyrics.App;
public sealed class LibraryWorkspacePanel:UserControl,IDisposable
{
    readonly TabControl pages=new(){TabStripPlacement=Dock.Top};
    internal TabControl Navigation=>pages;
    internal LibraryPanel Overview {get;}
    internal ChineseTranslationPanel Display {get;}
    internal TranslationJobListPanel Songs {get;}
    internal LibraryTasksPanel Tasks {get;}
    internal TextBox SourcePath=>Overview.SourcePath;
    internal bool IsSubscribed=>IsLoaded&&IsVisible&&Overview.IsSubscribed;
    public LibraryWorkspacePanel(PlaybackCoordinator coordinator)
    {
        var grid=new Grid();Content=grid;pages.SetResourceReference(StyleProperty,"LibraryWorkspaceTabs");grid.Children.Add(pages);
        Overview=new(coordinator,()=>Select(1));Display=new(coordinator,false);Songs=new(coordinator);Tasks=new(coordinator);
        pages.Items.Add(new TabItem{Header="总览",Content=new ScrollViewer{Content=Overview,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        pages.Items.Add(new TabItem{Header="歌曲",Content=Songs});
        pages.Items.Add(new TabItem{Header="任务",Content=Tasks});
        pages.Items.Add(new TabItem{Header="显示",Content=new ScrollViewer{Content=Display,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
    }
    internal void Select(int page)=>pages.SelectedIndex=page;
    public void Dispose(){Overview.Dispose();Display.Dispose();Songs.Dispose();Tasks.Dispose();}
}
