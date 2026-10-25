using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LumaLyrics.Core;

namespace LumaLyrics.App;

internal sealed class LyricsSearchWindow:Window
{
    internal TextBox SongTitle {get;}
    internal TextBox Artist {get;}
    internal TextBox Album {get;}
    internal TextBox Duration {get;}
    public Track? Query {get;private set;}
    public LyricsSearchWindow(Window owner,Track? initial)
    {
        Owner=owner;Title="浮光歌词 · 搜索歌词";
        var panel=DialogUi.Panel(this,"搜索歌词","只填歌名也能搜索；也可以输入歌名和歌手作为关键词。歌手、专辑和时长可留空，用于候选排序。搜索不会改变歌词绑定目标。");
        SongTitle=DialogUi.Field(panel,"歌名或关键词",initial?.Title??"");
        Artist=DialogUi.Field(panel,"歌手（可留空）",initial?.Artist??"");
        Album=DialogUi.Field(panel,"专辑（可留空）",initial?.Album??"");
        Duration=DialogUi.Field(panel,"时长，秒（可留空）",initial?.DurationMs is >0?(initial.DurationMs.Value/1000).ToString("0.###",CultureInfo.CurrentCulture):"");
        var search=DialogUi.Button(panel,"搜索",()=>
        {
            if(string.IsNullOrWhiteSpace(SongTitle.Text)){SongTitle.Focus();return;}
            double? duration=double.TryParse(Duration.Text,out var seconds) && double.IsFinite(seconds*1000) && seconds>0?seconds*1000:null;
            Query=new(SongTitle.Text.Trim(),Artist.Text.Trim(),Album.Text.Trim(),duration);DialogResult=true;
        });
        search.IsDefault=true;
        var cancel=DialogUi.Button(panel,"取消",()=>Close());cancel.IsCancel=true;
        Loaded+=(_,_)=>{SongTitle.Focus();SongTitle.SelectAll();};
    }
    internal static Track? Prompt(Window owner,Track? initial)
    {
        var dialog=new LyricsSearchWindow(owner,initial);return dialog.ShowDialog()==true?dialog.Query:null;
    }
}
