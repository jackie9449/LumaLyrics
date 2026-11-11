using System.Windows.Markup;
namespace LumaLyrics.App;

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(){}
    public LocExtension(string text)=>Text=text;
    [ConstructorArgument("text")]
    public string Text {get;set;}="";
    public override object ProvideValue(IServiceProvider serviceProvider)=>UiText.T(Text);
}
