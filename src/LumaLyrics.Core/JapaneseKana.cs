using System.Text;

namespace LumaLyrics.Core;

/// <summary>Compares and suggests readings. It does not translate catalog labels or infer kanji readings.</summary>
public static class JapaneseKana
{
    const int MaximumLength=160;
    const string MarkedVowels="āīūēōâîûêô";
    const string PlainVowels="aiueoaiueo";
    static readonly Dictionary<string,string[]> Readings=BuildReadings();
    static readonly Dictionary<string,string> KanaByRoman=BuildReverse();

    static Dictionary<string,string[]> BuildReadings()
    {
        var result=new Dictionary<string,string[]>(StringComparer.Ordinal);
        const string table="""
            ア:a イ:i ウ:u エ:e オ:o カ:ka キ:ki ク:ku ケ:ke コ:ko
            サ:sa シ:shi/si ス:su セ:se ソ:so タ:ta チ:chi/ti ツ:tsu/tu テ:te ト:to
            ナ:na ニ:ni ヌ:nu ネ:ne ノ:no ハ:ha ヒ:hi フ:fu/hu ヘ:he ホ:ho
            マ:ma ミ:mi ム:mu メ:me モ:mo ヤ:ya ユ:yu ヨ:yo ラ:ra リ:ri ル:ru レ:re ロ:ro
            ワ:wa ヰ:wi ヱ:we ヲ:o/wo ガ:ga ギ:gi グ:gu ゲ:ge ゴ:go
            ザ:za ジ:ji/zi ズ:zu ゼ:ze ゾ:zo ダ:da ヂ:ji/di ヅ:zu/du デ:de ド:do
            バ:ba ビ:bi ブ:bu ベ:be ボ:bo パ:pa ピ:pi プ:pu ペ:pe ポ:po ヴ:vu
            キャ:kya キュ:kyu キョ:kyo ギャ:gya ギュ:gyu ギョ:gyo
            シャ:sha/sya シュ:shu/syu ショ:sho/syo ジャ:ja/jya/zya ジュ:ju/jyu/zyu ジョ:jo/jyo/zyo
            チャ:cha/tya チュ:chu/tyu チョ:cho/tyo ニャ:nya ニュ:nyu ニョ:nyo
            ヒャ:hya ヒュ:hyu ヒョ:hyo ビャ:bya ビュ:byu ビョ:byo ピャ:pya ピュ:pyu ピョ:pyo
            ミャ:mya ミュ:myu ミョ:myo リャ:rya リュ:ryu リョ:ryo
            イェ:ye ウィ:wi ウェ:we ウォ:wo/uo ヴァ:va ヴィ:vi ヴェ:ve ヴォ:vo ヴュ:vyu
            シェ:she/sye ジェ:je/jye チェ:che/tye ティ:ti テュ:tyu トゥ:tu ディ:di デュ:dyu ドゥ:du
            ファ:fa フィ:fi フェ:fe フォ:fo フュ:fyu フョ:fyo スィ:si ズィ:zi
            ツァ:tsa ツィ:tsi ツェ:tse ツォ:tso クァ:kwa クィ:kwi クェ:kwe クォ:kwo グァ:gwa
            キェ:kye ニェ:nye ヒェ:hye ヂャ:ja/dya ヂュ:ju/dyu ヂョ:jo/dyo
            """;
        foreach(var entry in table.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries))
        {
            var fields=entry.Split(':');
            result.Add(fields[0],fields[1].Split('/'));
        }
        return result;
    }

    static Dictionary<string,string> BuildReverse()
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var pair in Readings)result.TryAdd(pair.Value[0],pair.Key);
        foreach(var pair in Readings)
            foreach(var roman in pair.Value.Skip(1))result.TryAdd(roman,pair.Key);
        // These characters are more common than historical kana for contemporary catalog searches.
        result["wi"]="ウィ";
        result["we"]="ウェ";
        return result;
    }

    public static bool HasKana(string value)=>value.Any(ch=>ch is >= '\u3041' and <= '\u3096' or >= '\u30A1' and <= '\u30FA' or >= '\uFF66' and <= '\uFF9D');
    public static bool HasMarkedVowel(string value)=>value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Any(MarkedVowels.Contains);

    static string Katakana(string value)=>new(MatchRules.Normalize(value)
        .Select(ch=>ch is >= '\u3041' and <= '\u3096'?(char)(ch+0x60):ch).ToArray());

    public static bool SameReading(string left,string right)
    {
        if(left.Length==0 || right.Length==0 || left.Length>MaximumLength || right.Length>MaximumLength)return false;
        bool leftKana=HasKana(left),rightKana=HasKana(right);
        if(!leftKana && !rightKana)return false;
        if(leftKana && rightKana)return Katakana(left)==Katakana(right);

        string kana=Katakana(leftKana?left:right);
        string roman=MatchRules.Normalize(leftKana?right:left);
        roman=new(roman.Select(ch=>MarkedVowels.IndexOf(ch) is >=0 and var i?PlainVowels[i]:ch).ToArray());
        var units=ReadingUnits(kana);
        if(units is null)return false;
        var positions=new HashSet<int>{0};
        foreach(var alternatives in units)
        {
            var next=new HashSet<int>();
            foreach(int offset in positions)
                foreach(string form in alternatives)
                    if(roman.AsSpan(offset).StartsWith(form,StringComparison.Ordinal))next.Add(offset+form.Length);
            if(next.Count==0)return false;
            positions=next;
        }
        return positions.Contains(roman.Length);
    }

    static List<string[]>? ReadingUnits(string kana)
    {
        var result=new List<string[]>();
        char vowel='\0';
        bool doubled=false;
        for(int i=0;i<kana.Length;i++)
        {
            char ch=kana[i];
            if(ch=='ッ' && !doubled){doubled=true;vowel='\0';continue;}
            if(ch=='ン' && !doubled)
            {
                result.Add(i+1<kana.Length && "バビブベボパピプペポマミムメモ".Contains(kana[i+1])?["n","m"]:["n"]);
                vowel='\0';
                continue;
            }
            if(!doubled && vowel!='\0' && (ch=='ー' || vowel=='o' && ch is 'ウ' or 'オ' || vowel=='e' && ch is 'イ' or 'エ'))
            {
                result.Add(vowel switch{'o'=>["","o","u"],'e'=>["","e","i"],_=>["",vowel.ToString()]});
                vowel='\0';
                continue;
            }
            string key=i+1<kana.Length && Readings.ContainsKey(kana.Substring(i,2))?kana.Substring(i,2):ch.ToString();
            if(Readings.TryGetValue(key,out var readings))
            {
                if(doubled && "aiueo".Contains(readings[0][0]))return null;
                result.Add(doubled?readings.SelectMany(s=>s.StartsWith("ch",StringComparison.Ordinal)?new[]{"c"+s,"t"+s}:new[]{s[0]+s}).ToArray():readings);
                i+=key.Length-1;
                vowel=readings[0][^1];
                doubled=false;
            }
            else
            {
                if(doubled || ch=='ー' || HasKana(ch.ToString()))return null;
                result.Add([ch.ToString()]);
                vowel='\0';
            }
        }
        return doubled?null:result;
    }

    public static string? SearchKatakana(string value)
    {
        if(value.Length==0 || value.Length>MaximumLength || HasKana(value))return null;
        string source=value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var roman=new StringBuilder();
        foreach(char ch in source)
        {
            int mark=MarkedVowels.IndexOf(ch);
            if(mark>=0)roman.Append(PlainVowels[mark]).Append('ー');
            else if(char.IsAsciiLetterOrDigit(ch) || char.IsWhiteSpace(ch) || ch is '\'' or '’' or '-')roman.Append(ch);
            else return null;
        }
        var katakana=new StringBuilder();
        string reading=roman.ToString();
        int syllables=0;
        foreach(var word in reading.Split([' ','\t','\r','\n','\'','’','-'],StringSplitOptions.RemoveEmptyEntries))
        {
            for(int i=0;i<word.Length;)
            {
                char ch=word[i],next=i+1<word.Length?word[i+1]:'\0';
                if(char.IsAsciiDigit(ch) || ch=='ー'){katakana.Append(ch);i++;continue;}
                if(ch=='n' && !"aiueoy".Contains(next)){katakana.Append('ン');i++;syllables++;continue;}
                if(ch==next && !"aiueo".Contains(ch) || ch=='t' && word.AsSpan(i).StartsWith("tch",StringComparison.Ordinal))
                {katakana.Append('ッ');i++;continue;}
                int length=Math.Min(3,word.Length-i);
                string? kana=null;
                for(;length>0;length--)
                    if(KanaByRoman.TryGetValue(word.Substring(i,length),out kana))break;
                if(kana is null)return null;
                katakana.Append(kana);
                syllables++;
                i+=length;
            }
        }
        return syllables>=2?katakana.ToString():null;
    }
}
