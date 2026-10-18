using System.Globalization;
using System.Text.RegularExpressions;
namespace LumaLyrics.Core;

public sealed partial class LrcParser : ILyricsParser
{
    [GeneratedRegex(@"\[(\d{1,4}):(\d{2})(?:\.(\d{1,3}))?\]")] private static partial Regex Stamp();
    [GeneratedRegex(@"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase)] private static partial Regex Offset();
    [GeneratedRegex(@"^\[(ar|ti|al|by|re|ve|length|au):.*\]$", RegexOptions.IgnoreCase)] private static partial Regex Metadata();
    public LyricsDocument Parse(string raw, bool forcePlain = false)
    {
        if (raw.Length > 2 * 1024 * 1024) throw new InvalidDataException("歌词超过 2 MB。");
        if (forcePlain) return new(raw, [], 0, []);
        var lines = new List<(double Time, string Text, int Order)>(); var warnings = new List<string>();
        double offset = 0; int number = 0;
        foreach (var line in raw.TrimStart('\uFEFF').Replace("\r", "").Split('\n'))
        {
            number++; var om = Offset().Match(line);
            if (om.Success) { if (double.TryParse(om.Groups[1].Value, CultureInfo.InvariantCulture, out var v) && Math.Abs(v) <= 3600000) offset = -v; else warnings.Add($"第 {number} 行：无效 offset"); continue; }
            if (Metadata().IsMatch(line.Trim()) || string.IsNullOrWhiteSpace(line)) continue;
            var stamps = Stamp().Matches(line);
            if (stamps.Count == 0) { if (line.StartsWith('[')) warnings.Add($"第 {number} 行：跳过无效时间标签"); continue; }
            string text = Stamp().Replace(line, "").Trim();
            foreach (Match m in stamps)
            {
                int sec = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                double ms = (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60d + sec) * 1000;
                if (m.Groups[3].Success) ms += int.Parse(m.Groups[3].Value.PadRight(3, '0'), CultureInfo.InvariantCulture);
                if (sec >= 60 || ms > 86400000) { warnings.Add($"第 {number} 行：秒数或时间范围无效"); continue; }
                lines.Add((ms, text, lines.Count));
            }
        }
        var groups = lines.OrderBy(x => x.Time).ThenBy(x => x.Order).GroupBy(x => x.Time).Select(g => new TimedLineGroup(g.Key, g.Select(x => x.Text).ToArray())).ToArray();
        return new(raw, groups, offset, warnings);
    }
}
