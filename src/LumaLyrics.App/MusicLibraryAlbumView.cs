using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

// Rows virtualize as ordinary ListBox items. Only the cards in those rows exist,
// giving a responsive shelf without measuring every album in a WrapPanel.
internal sealed class MusicLibraryAlbumView:ListBox
{
    readonly Action<MusicAlbum> open,play;readonly MusicLibraryView.ThumbnailCache covers;readonly bool canPlay;
    IReadOnlyList<MusicAlbum> albums=[];int columns;bool arranging;
    internal int Columns=>columns;
    internal IReadOnlyList<MusicAlbum> Albums=>albums;
    internal double ScrollOffset=>FindScroll()?.VerticalOffset??0;
    internal MusicLibraryAlbumView(Action<MusicAlbum> open,Action<MusicAlbum> play,MusicLibraryView.ThumbnailCache covers,bool canPlay)
    {
        this.open=open;this.play=play;this.covers=covers;this.canPlay=canPlay;Background=Brushes.Transparent;BorderThickness=new(0);Padding=new(0);HorizontalContentAlignment=HorizontalAlignment.Stretch;
        VirtualizingPanel.SetIsVirtualizing(this,true);VirtualizingPanel.SetVirtualizationMode(this,VirtualizationMode.Recycling);VirtualizingPanel.SetScrollUnit(this,ScrollUnit.Pixel);ScrollViewer.SetCanContentScroll(this,true);ScrollViewer.SetHorizontalScrollBarVisibility(this,ScrollBarVisibility.Disabled);
        ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        var style=new Style(typeof(ListBoxItem));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));style.Setters.Add(new Setter(MarginProperty,new Thickness(0)));style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));style.Setters.Add(new Setter(FocusableProperty,false));style.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
        var template=new ControlTemplate(typeof(ListBoxItem));var presenter=new FrameworkElementFactory(typeof(ContentPresenter));template.VisualTree=presenter;style.Setters.Add(new Setter(Control.TemplateProperty,template));ItemContainerStyle=style;
        ItemTemplate=new DataTemplate{VisualTree=new FrameworkElementFactory(typeof(AlbumRowPresenter))};SizeChanged+=(_,_)=>ArrangeRows();AutomationProperties.SetName(this,"音乐库专辑");
    }
    internal void SetAlbums(IReadOnlyList<MusicAlbum> value,bool resetScroll=true)
    {
        albums=value;columns=0;ArrangeRows();if(resetScroll)FindScroll()?.ScrollToTop();
    }
    void ArrangeRows()
    {
        int next=Math.Clamp((int)(Math.Max(1,ActualWidth-12)/190),2,6);if(arranging || columns==next)return;
        arranging=true;try{columns=next;ItemsSource=albums.Chunk(columns).Select(a=>new AlbumRow(a,columns,this)).ToArray();}finally{arranging=false;}
    }
    internal void RestoreOffset(double offset)=>FindScroll()?.ScrollToVerticalOffset(offset);
    ScrollViewer? FindScroll()=>VisualChildren<ScrollViewer>(this).FirstOrDefault();
    static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T item)yield return item;foreach(var nested in VisualChildren<T>(child))yield return nested;}
    }
    internal sealed record AlbumRow(MusicAlbum[] Albums,int Columns,MusicLibraryAlbumView Owner);
    sealed class AlbumRowPresenter:Grid
    {
        public AlbumRowPresenter(){DataContextChanged+=(_,_)=>Build();}
        void Build()
        {
            if(DataContext is not AlbumRow row)return;var owner=row.Owner;
            Children.Clear();ColumnDefinitions.Clear();for(int i=0;i<row.Columns;i++)ColumnDefinitions.Add(new());
            for(int i=0;i<row.Albums.Length;i++)
            {
                var album=row.Albums[i];var tile=new Grid{Margin=new(0,0,16,20)};var button=new Button{DataContext=album,Margin=new(0)};button.SetResourceReference(StyleProperty,"AlbumCardButton");tile.Children.Add(button);
                AutomationProperties.SetName(button,"打开专辑 · "+album.Title+" · "+album.Artist);button.ToolTip=album.Title+" · "+album.Artist;button.Click+=(_,_)=>owner.open(album);
                var card=new StackPanel();button.Content=card;var cover=new Border{CornerRadius=new(14),BorderThickness=new(1)};ThemeManager.Bind(cover,Border.BackgroundProperty,"ArtworkBrush");ThemeManager.Bind(cover,Border.BorderBrushProperty,"BorderBrush");cover.SizeChanged+=(_,_)=>{cover.Height=cover.ActualWidth;cover.Clip=new RectangleGeometry(new Rect(0,0,cover.ActualWidth,cover.ActualWidth),14,14);};
                var image=album.CoverPaths.Select(p=>owner.covers.Get(p)).FirstOrDefault(p=>p is not null);cover.Child=image is not null?new Image{Source=image,Stretch=Stretch.UniformToFill}:new TextBlock{Text="◉",FontSize=46,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Opacity=.35};card.Children.Add(cover);
                var title=new TextBlock{Text=album.Title,FontSize=14,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,12,0,0)};ThemeManager.Bind(title,TextBlock.ForegroundProperty,"TextBrush");card.Children.Add(title);
                var artist=new TextBlock{Text=album.Artist,FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,5,0,0)};ThemeManager.Bind(artist,TextBlock.ForegroundProperty,"MutedBrush");card.Children.Add(artist);
                var summary=new TextBlock{Text=album.Summary,FontSize=10,Margin=new(0,5,0,0)};ThemeManager.Bind(summary,TextBlock.ForegroundProperty,"MutedBrush");card.Children.Add(summary);
                // Sibling controls keep opening and playing separate for mouse and keyboard.
                var play=new Button{DataContext=album,Content="▷",Width=38,Height=38,FontSize=18,Padding=new(0),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,0,16,0),Cursor=Cursors.Hand,ToolTip="播放整张专辑",IsEnabled=owner.canPlay && album.CanPlay};
                AutomationProperties.SetName(play,"播放专辑 · "+album.Title+" · "+album.Artist);play.SetResourceReference(StyleProperty,"AlbumPlayButton");
                play.Click+=(_,e)=>{e.Handled=true;owner.play(album);};cover.SizeChanged+=(_,_)=>play.Margin=new(0,Math.Max(8,cover.ActualHeight-48),16,0);tile.Children.Add(play);Grid.SetColumn(tile,i);Children.Add(tile);
            }
        }
    }
}
