using System.Globalization;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class ArtworkPaletteTests
{
    static readonly ArtworkColor Blue=new(32,95,210),Orange=new(220,105,35);
    static ArtworkColor[] Repeat(ArtworkColor color,int count)=>Enumerable.Repeat(color,count).ToArray();

    [Fact] public void DominantColorSurvivesWhiteMarginsAndBlackBorders()
    {
        ArtworkColor[] samples=[..Repeat(new(255,255,255),500),..Repeat(new(0,0,0),100),..Repeat(Blue,300),..Repeat(Orange,100)];
        var palette=ArtworkPalette.Extract(samples)!;
        Assert.Equal(Blue,palette.Primary);Assert.Equal(Orange,palette.Secondary);
    }

    [Fact] public void TransparentPixelsDoNotAffectVisiblePalette()
    {
        ArtworkColor[] samples=[..Repeat(new(255,0,0,0),2000),..Repeat(Blue,60),..Repeat(Orange with{A=40},10)];
        var palette=ArtworkPalette.Extract(samples)!;
        Assert.Equal(Blue,palette.Primary);Assert.Equal(Blue,palette.Secondary);
        Assert.Null(ArtworkPalette.Extract(Repeat(new(255,0,0,0),100)));
        Assert.Null(ArtworkPalette.Extract([]));
    }

    [Theory] [InlineData(0)] [InlineData(128)] [InlineData(255)]
    public void GrayCoversProduceNeutralGlassInsteadOfThePreviousHue(byte gray)
    {
        var palette=ArtworkPalette.Extract(Repeat(new(gray,gray,gray),100))!;
        foreach(var id in new[]{"liquid","frost"})
        {
            var theme=palette.ApplyTo(AppThemes.Find(id));
            foreach(var value in new[]{theme.WindowStart,theme.WindowEnd,theme.Glow,theme.Accent})
            {var color=Parse(value);Assert.Equal(color.R,color.G);Assert.Equal(color.G,color.B);}
        }
    }

    [Fact] public void TinyColoredSpeckDoesNotTintAMonochromeCover()
    {
        var palette=ArtworkPalette.Extract([..Repeat(new(170,170,170),99),Orange])!;
        Assert.Equal(palette.Primary.R,palette.Primary.G);Assert.Equal(palette.Primary.G,palette.Primary.B);
    }

    [Fact] public void HueWrapAroundKeepsBothRedShades()
    {
        var palette=ArtworkPalette.Extract([..Repeat(new(240,25,15),50),..Repeat(new(240,15,25),50)])!;
        Assert.Equal(new ArtworkColor(240,20,20),palette.Primary);
        Assert.Equal(palette.Primary,palette.Secondary);
    }

    [Fact] public void MaterialIsUnchangedAndBaseGlassPalettesAreNotMutated()
    {
        var palette=new ArtworkPalette(Orange,Blue);var material=AppThemes.Find("material");
        Assert.Same(material,palette.ApplyTo(material));
        foreach(var id in new[]{"liquid","frost"})
        {
            var original=AppThemes.Find(id);var warm=palette.ApplyTo(original);var cool=new ArtworkPalette(Blue,Blue).ApplyTo(original);
            Assert.NotEqual(warm.WindowStart,cool.WindowStart);Assert.NotEqual(warm.Accent,cool.Accent);
            Assert.Equal(original.Text,warm.Text);Assert.Same(original,AppThemes.Find(id));
            Assert.Equal(original.Id,warm.Id);Assert.Equal(original.Radius,warm.Radius);
        }
    }

    [Theory] [InlineData("liquid")] [InlineData("frost")]
    public void TextAndControlsStayReadableAcrossSaturatedAndExtremeCovers(string id)
    {
        var colors=new List<ArtworkColor>{new(0,0,0),new(255,255,255),new(128,128,128)};
        // Walk the RGB cube edges, including yellow/green which have the highest luminance.
        for(int step=0;step<=255;step+=17)
        {byte n=(byte)step;colors.AddRange([new(255,n,0),new(n,255,0),new(0,255,n),new(0,n,255),new(n,0,255),new(255,0,n)]);}
        foreach(var primary in colors)
        foreach(var secondary in new[]{primary,new ArtworkColor(255,255,0),new ArtworkColor(0,0,255),new ArtworkColor(255,0,255)})
        {
            var theme=new ArtworkPalette(primary,secondary).ApplyTo(AppThemes.Find(id));
            var background=Composite(theme.Glow,Parse(theme.WindowEnd),id=="liquid"?.3:.38);
            var panel=Composite(theme.Panel,background);
            var sheen=Composite("#FFFFFF",panel,id=="liquid"?.16:.09);
            foreach(var surface in new[]{Parse(theme.WindowStart),background,panel,sheen,Parse(theme.Control),Parse(theme.Hover)})
            {
                Assert.True(Contrast(Parse(theme.Text),surface)>=4.5,$"{id} body text: {primary}");
                Assert.True(Contrast(Parse(theme.Muted),surface)>=4.5,$"{id} muted text: {primary}, {theme.Muted} on {surface}");
            }
            Assert.True(Contrast(Parse(theme.AccentText),Parse(theme.Accent))>=4.5,$"{id} button label: {primary}");
            Assert.True(Contrast(Parse(theme.Accent),background)>=4.5,$"{id} accent: {primary}");
        }
    }

    [Theory] [InlineData("material")] [InlineData("paper")] [InlineData("studio")] [InlineData("bauhaus")]
    public void FixedThemesKeepTheirOwnPaletteWhenTheRecordingChanges(string id)
    {
        var theme=AppThemes.Find(id);
        Assert.Same(theme,new ArtworkPalette(Orange,Blue).ApplyTo(theme));
        Assert.Same(theme,new ArtworkPalette(new(0,0,0),new(255,255,255)).ApplyTo(theme));
    }

    [Theory] [InlineData("frost")] [InlineData("material")] [InlineData("paper")] [InlineData("studio")] [InlineData("bauhaus")]
    public void RedesignedThemeTextAndControlsHaveReadableContrast(string id)
    {
        var theme=AppThemes.Find(id);
        foreach(var surface in new[]{theme.WindowStart,theme.Raised,theme.Control,theme.Hover,theme.AccentSoft})
        {
            Assert.True(Contrast(Parse(theme.Text),Parse(surface))>=4.5,id+" text on "+surface);
            Assert.True(Contrast(Parse(theme.Muted),Parse(surface))>=4.5,id+" secondary text on "+surface);
        }
        Assert.True(Contrast(Parse(theme.AccentText),Parse(theme.Accent))>=4.5,id+" primary action");
    }

    static ArtworkColor Parse(string hex)
    {
        uint value=uint.Parse(hex.AsSpan(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        return new((byte)(value>>16),(byte)(value>>8),(byte)value,hex.Length==9?(byte)(value>>24):(byte)255);
    }
    static ArtworkColor Composite(string foreground,ArtworkColor background,double opacity=1)
    {
        var color=Parse(foreground);double a=color.A/255d*opacity;
        byte Channel(byte first,byte second)=>(byte)Math.Round(first*a+second*(1-a));
        return new(Channel(color.R,background.R),Channel(color.G,background.G),Channel(color.B,background.B));
    }
    static double Contrast(ArtworkColor first,ArtworkColor second)
    {
        static double Luminance(ArtworkColor c)
        {static double Linear(byte n){double v=n/255d;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}return .2126*Linear(c.R)+.7152*Linear(c.G)+.0722*Linear(c.B);}
        double a=Luminance(first),b=Luminance(second);return (Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);
    }
}
