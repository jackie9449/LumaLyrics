using System.Windows;
using System.Windows.Controls;
namespace LumaLyrics.App;

internal static class SearchHint
{
    internal static void Attach(TextBox input,TextBlock hint)
    {
        void Refresh()=>hint.Visibility=input.Text.Length==0&&!input.IsKeyboardFocusWithin?Visibility.Visible:Visibility.Collapsed;
        input.TextChanged+=(_,_)=>Refresh();
        input.IsKeyboardFocusWithinChanged+=(_,_)=>Refresh();
        Refresh();
    }
}
