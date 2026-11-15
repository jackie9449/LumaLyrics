using System.Globalization;
using System.Runtime.InteropServices;

namespace LumaLyrics.Infrastructure;

public sealed record LyricFontLanguage(string Tag, string Name, string Script, string Sample, string Alphabet, string Fallback);

public static class LyricFontLanguages
{
    const string Latin = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    public static IReadOnlyList<LyricFontLanguage> All { get; } = Array.AsReadOnly(new LyricFontLanguage[]
    {
        new("zh-Hans", "简体中文", "Hans", "浮光歌词 · 让音乐留在心间", "音乐歌词爱梦想轻风云龙国华语", "Noto Sans CJK SC"),
        new("zh-Hant", "繁体中文", "Hant", "浮光歌詞 · 讓音樂留在心間", "音樂歌詞愛夢想輕風雲龍國華語", "Noto Sans CJK TC"),
        new("en", "英文", "Latn", "Music stays with you · Aa Bb 123", Latin, "Noto Sans"),
        new("ja", "日文", "Jpan", "音楽が心に響く · あいうえお カタカナ", "音楽歌詞あいうえおかきくけこアイウエオカキクケコ", "Noto Sans CJK JP"),
        new("fr", "法文", "Latn", "La musique éclaire nos rêves", Latin+"ÀÂÆÇÉÈÊËÎÏÔŒÙÛÜŸàâæçéèêëîïôœùûüÿ", "Noto Sans"),
        new("es", "西班牙文", "Latn", "La música vive en el corazón", Latin+"ÁÉÍÓÚÜÑáéíóúüñ¿¡", "Noto Sans"),
        new("de", "德文", "Latn", "Musik berührt unsere Herzen", Latin+"ÄÖÜäöüß", "Noto Sans"),
        new("ko", "韩文", "Kore", "음악이 마음에 머물러요", "가나다라마바사아자차카타파하음악마음노래", "Noto Sans CJK KR"),
        new("it", "意大利文", "Latn", "La musica illumina il cuore", Latin+"ÀÈÉÌÒÙàèéìòù", "Noto Sans"),
        new("pt", "葡萄牙文", "Latn", "A música fica no coração", Latin+"ÁÂÃÀÇÉÊÍÓÔÕÚáâãàçéêíóôõú", "Noto Sans"),
        new("nl", "荷兰文", "Latn", "De muziek blijft in je hart", Latin+"ëïé", "Noto Sans"),
        new("sv", "瑞典文", "Latn", "Musiken stannar i våra hjärtan", Latin+"ÅÄÖåäö", "Noto Sans"),
        new("da", "丹麦文", "Latn", "Musikken bliver i vores hjerter", Latin+"ÆØÅæøå", "Noto Sans"),
        new("no", "挪威文", "Latn", "Musikken blir i hjertet", Latin+"ÆØÅæøå", "Noto Sans"),
        new("fi", "芬兰文", "Latn", "Musiikki jää sydämeen", Latin+"ÅÄÖåäö", "Noto Sans"),
        new("pl", "波兰文", "Latn", "Muzyka pozostaje w naszych sercach", Latin+"ĄĆĘŁŃÓŚŹŻąćęłńóśźż", "Noto Sans"),
        new("cs", "捷克文", "Latn", "Hudba zůstává v našich srdcích", Latin+"ÁČĎÉĚÍŇÓŘŠŤÚŮÝŽáčďéěíňóřšťúůýž", "Noto Sans"),
        new("tr", "土耳其文", "Latn", "Müzik kalbimizde yaşar", Latin+"ÇĞİÖŞÜçğıöşü", "Noto Sans"),
        new("vi", "越南文", "Latn", "Âm nhạc ở lại trong trái tim", Latin+"ĂÂĐÊÔƠƯăâđêôơưạảấầẩẫậắằẳẵặẹẻẽếềểễệỉịọỏốồổỗộớờởỡợụủứừửữựỳỵỷỹ", "Noto Sans"),
        new("id", "印度尼西亚文", "Latn", "Musik tinggal di dalam hati", Latin, "Noto Sans"),
        new("ms", "马来文", "Latn", "Muzik kekal dalam hati", Latin, "Noto Sans"),
        new("ru", "俄文", "Cyrl", "Музыка остаётся в сердце", "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя", "Noto Sans"),
        new("uk", "乌克兰文", "Cyrl", "Музика залишається в серці", "АБВГҐДЕЄЖЗИІЇЙКЛМНОПРСТУФХЦЧШЩЬЮЯабвгґдеєжзиіїйклмнопрстуфхцчшщьюя", "Noto Sans"),
        new("el", "希腊文", "Grek", "Η μουσική μένει στην καρδιά", "ΑΒΓΔΕΖΗΘΙΚΛΜΝΞΟΠΡΣΤΥΦΧΨΩαβγδεζηθικλμνξοπρστυφχψωςάέήίόύώ", "Noto Sans"),
        new("ar", "阿拉伯文", "Arab", "الموسيقى تبقى في القلب", "ابتثجحخدذرزسشصضطظعغفقكلمنهويءأإآةى", "Noto Sans Arabic"),
        new("he", "希伯来文", "Hebr", "המוזיקה נשארת בלב", "אבגדהוזחטיךכלםמןנסעףפץצקרשת", "Noto Sans Hebrew"),
        new("hi", "印地文", "Deva", "संगीत दिल में रहता है", "अआइईउऊएऐओऔकखगघचछजझटठडढणतथदधनपफबभमयरलवशषसहािीुूेैोौंः्", "Noto Sans Devanagari"),
        new("th", "泰文", "Thai", "เสียงเพลงอยู่ในหัวใจ", "กขคงจฉชซญฎฏฐฑฒณดตถทธนบปผฝพฟภมยรลวศษสหฬอฮะาำิีึืุูเแโใไ่้๊๋์", "Noto Sans Thai"),
        new("bn", "孟加拉文", "Beng", "সঙ্গীত হৃদয়ে থাকে", "অআইঈউঊএঐওঔকখগঘঙচছজঝঞটঠডঢণতথদধনপফবভমযরলশষসহািীুূেৈোৌ্", "Noto Sans Bengali"),
        new("ta", "泰米尔文", "Taml", "இசை இதயத்தில் நிலைக்கும்", "அஆஇஈஉஊஎஏஐஒஓஔகஙசஞடணதநபமயரலவழளறனாிீுூெேைொோௌ்", "Noto Sans Tamil")
    });

    public static string? NormalizeTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        tag = tag.Replace('_','-');
        if (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return tag.Contains("Hant", StringComparison.OrdinalIgnoreCase) || tag.EndsWith("-TW", StringComparison.OrdinalIgnoreCase) || tag.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) || tag.EndsWith("-MO", StringComparison.OrdinalIgnoreCase) ? "zh-Hant" : "zh-Hans";
        string neutral = tag.Split('-')[0].ToLowerInvariant();
        if (neutral is "nb" or "nn") neutral = "no";
        return All.FirstOrDefault(l => l.Tag == neutral)?.Tag;
    }
    public static LyricFontLanguage Get(string? tag) => All.First(l => l.Tag == (NormalizeTag(tag) ?? "en"));
    public static string SystemLanguage { get; } = ReadSystemLanguage();
    static string ReadSystemLanguage()
    {
        try { return NormalizeTag(OperatingSystem.IsWindows() ? CultureInfo.GetCultureInfo(GetUserDefaultUILanguage()).Name : CultureInfo.InstalledUICulture.Name) ?? "en"; }
        catch (CultureNotFoundException) { return "en"; }
    }
    [DllImport("kernel32.dll")] static extern ushort GetUserDefaultUILanguage();
}
