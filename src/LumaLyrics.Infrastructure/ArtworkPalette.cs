namespace LumaLyrics.Infrastructure;

public readonly record struct ArtworkColor(byte R, byte G, byte B, byte A = 255)
{
    internal (double Hue, double Saturation, double Lightness) Hsl
    {
        get
        {
            double r=R/255d,g=G/255d,b=B/255d;
            double max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
            double lightness=(max+min)/2;
            if(delta<.0001)return (0,0,lightness);
            double hue=max==r?((g-b)/delta+6)%6:max==g?(b-r)/delta+2:(r-g)/delta+4;
            return (hue*60,delta/(1-Math.Abs(2*lightness-1)),lightness);
        }
    }
}

/// <summary>A small, deterministic cover palette; it never stores artwork or personal metadata.</summary>
public sealed record ArtworkPalette(ArtworkColor Primary, ArtworkColor Secondary)
{
    struct Bucket
    {
        public double Weight,R,G,B;
        public void Add(ArtworkColor color,double weight)
        { Weight+=weight;R+=color.R*weight;G+=color.G*weight;B+=color.B*weight; }
        public void Add(Bucket other)
        { Weight+=other.Weight;R+=other.R;G+=other.G;B+=other.B; }
        public readonly ArtworkColor Color=>new((byte)Math.Round(R/Weight),(byte)Math.Round(G/Weight),(byte)Math.Round(B/Weight));
    }

    public static ArtworkPalette? Extract(ReadOnlySpan<ArtworkColor> pixels)
    {
        var hues=new Bucket[36];var all=new Bucket();double chromaticWeight=0;
        foreach(var pixel in pixels)
        {
            if(pixel.A<32)continue;
            double alpha=pixel.A/255d;all.Add(pixel,alpha);
            var (hue,saturation,lightness)=pixel.Hsl;
            // Avoid white margins, black borders and small gray details drowning out the cover's colors.
            if(saturation<.12 || lightness<.06 || lightness>.96)continue;
            double weight=alpha*(.3+.7*saturation)*(.4+.6*Math.Sin(Math.PI*lightness));
            hues[Math.Min(35,(int)(hue/10))].Add(pixel,weight);chromaticWeight+=alpha;
        }
        if(all.Weight==0)return null;
        if(chromaticWeight<all.Weight*.04)
        {
            byte gray=(byte)Math.Round((all.R+all.G+all.B)/(3*all.Weight));
            return new(new(gray,gray,gray),new(gray,gray,gray));
        }
        Bucket Neighbors(int index)
        {
            var result=new Bucket();
            for(int offset=-1;offset<=1;offset++)result.Add(hues[(index+offset+36)%36]);
            return result;
        }
        int primaryIndex=0;var primary=new Bucket();
        for(int i=0;i<36;i++){var candidate=Neighbors(i);if(candidate.Weight>primary.Weight){primary=candidate;primaryIndex=i;}}
        var secondary=new Bucket();
        for(int i=0;i<36;i++)
        {
            int distance=Math.Abs(i-primaryIndex);distance=Math.Min(distance,36-distance);
            if(distance<6)continue;
            var candidate=Neighbors(i);if(candidate.Weight>secondary.Weight)secondary=candidate;
        }
        return new(primary.Color,secondary.Weight>=primary.Weight*.18?secondary.Color:primary.Color);
    }

    public AppTheme ApplyTo(AppTheme theme)
    {
        if(theme.Id is not ("liquid" or "frost"))return theme;
        var p=Primary;var s=Secondary;
        if(theme.IsDark)return theme with {
            WindowStart=Tint(p,.09,.38),WindowEnd=Tint(s,.17,.45),
            Panel=Tint(p,.145,.32,218),PanelEnd=Tint(s,.105,.32,236),
            Raised=Tint(p,.19,.28),Control=Tint(p,.22,.30),Hover=Tint(p,.27,.28),
            Muted=Tint(p,.83,.16),Accent=Tint(p,.78,.58),AccentText=Tint(p,.12,.32),
            AccentSoft=Tint(p,.25,.28),Border=Tint(p,.62,.25,100),
            Glow=Tint(p,.40,.50),GlowSecondary=Tint(s,.36,.50)
        };
        return theme with {
            WindowStart=Tint(p,.957,.20),WindowEnd=Tint(s,.924,.22),
            Panel=Tint(p,.978,.28,245),Raised=Tint(p,.988,.20),
            Control=Tint(p,.933,.18),Hover=Tint(p,.885,.20),
            Muted=Tint(p,.285,.16),Accent=Tint(p,.22,.52),AccentText="#FFFFFF",
            AccentSoft=Tint(p,.912,.24),Border=Tint(p,.69,.14,145),
            Glow=Tint(p,.83,.20),GlowSecondary=Tint(s,.87,.20)
        };
    }

    static string Tint(ArtworkColor color,double lightness,double strength,byte alpha=255)
    {
        var (hue,saturation,_)=color.Hsl;saturation*=strength;
        double chroma=(1-Math.Abs(2*lightness-1))*saturation;
        double x=chroma*(1-Math.Abs(hue/60%2-1)),m=lightness-chroma/2;
        var (r,g,b)=hue switch {
            <60=>(chroma,x,0d),<120=>(x,chroma,0d),<180=>(0d,chroma,x),
            <240=>(0d,x,chroma),<300=>(x,0d,chroma),_=>(chroma,0d,x)
        };
        byte Byte(double value)=>(byte)Math.Clamp(Math.Round((value+m)*255),0,255);
        string rgb=$"{Byte(r):X2}{Byte(g):X2}{Byte(b):X2}";
        return alpha==255?"#"+rgb:$"#{alpha:X2}"+rgb;
    }
}
