using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Tangerine.Core;

/// <param name="Start">시작 시각</param>
/// <param name="End">끝 시각</param>
/// <param name="Lines">대사 줄 (서식 꼬리표 포함)</param>
public sealed record Cue(TimeSpan Start, TimeSpan End, IReadOnlyList<string> Lines);

/// <summary>자막 SRT ↔ VTT, 그리고 대사만 모은 TXT.</summary>
public static partial class SubtitleConverter
{
    // 00:01:02,345 / 00:01:02.345 / 01:02.345 (VTT 는 시를 생략할 수 있다)
    [GeneratedRegex(@"(?:(\d+):)?(\d{1,2}):(\d{2})[,.](\d{1,3})")]
    private static partial Regex Time();

    // SRT·VTT 둘 다 쓰는 서식만 남긴다
    [GeneratedRegex(@"<(?!/?(?:i|b|u)>)[^>]*>")]
    private static partial Regex UnsharedTag();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex AnyTag();

    // SRT 에 섞여 오는 ASS 위치 지정 ({\an8} 등)
    [GeneratedRegex(@"\{\\[^}]*\}")]
    private static partial Regex AssTag();

    public static IReadOnlyList<Cue> Parse(string text)
    {
        var cues = new List<Cue>();
        var blocks = text.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').Split("\n\n");
        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            var timing = Array.FindIndex(lines, l => l.Contains("-->"));
            if (timing < 0)
                continue; // WEBVTT 머리말, NOTE, STYLE 등

            var times = Time().Matches(lines[timing]);
            if (times.Count < 2)
                continue;
            var body = lines.Skip(timing + 1).Where(l => l.Length > 0).ToList();
            cues.Add(new Cue(ToTime(times[0]), ToTime(times[1]), body));
        }
        return cues;
    }

    public static string ToSrt(IReadOnlyList<Cue> cues)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < cues.Count; i++)
        {
            sb.Append(i + 1).Append("\r\n");
            sb.Append(Stamp(cues[i].Start, ',')).Append(" --> ").Append(Stamp(cues[i].End, ',')).Append("\r\n");
            foreach (var line in cues[i].Lines)
                sb.Append(WebUtility.HtmlDecode(Clean(line))).Append("\r\n");
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    public static string ToVtt(IReadOnlyList<Cue> cues)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        foreach (var cue in cues)
        {
            sb.Append(Stamp(cue.Start, '.')).Append(" --> ").Append(Stamp(cue.End, '.')).Append('\n');
            foreach (var line in cue.Lines)
                sb.Append(Clean(line)).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>대사 글자만. 꼬리표를 지우고, 바로 앞과 같은 줄은 한 번만.</summary>
    public static string ToText(IReadOnlyList<Cue> cues)
    {
        var lines = new List<string>();
        foreach (var line in cues.SelectMany(c => c.Lines))
        {
            var plain = WebUtility.HtmlDecode(AnyTag().Replace(AssTag().Replace(line, ""), "")).Trim();
            if (plain.Length > 0 && (lines.Count == 0 || lines[^1] != plain))
                lines.Add(plain);
        }
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>자막 파일을 targetId(srt·vtt·txt) 로 바꿔 output 에 쓴다. 대사가 하나도 없으면 실패.</summary>
    public static void ConvertFile(string source, string output, string targetId)
    {
        var cues = Parse(TextFiles.Read(source));
        if (cues.Count == 0)
            throw new InvalidDataException("자막 시각(00:00:01,000 --> …)을 찾지 못했습니다");

        var (text, bom) = targetId switch
        {
            "srt" => (ToSrt(cues), true),   // Windows 플레이어가 한글을 바로 읽도록 BOM 을 붙인다
            "vtt" => (ToVtt(cues), false),  // 웹 표준(VTT)은 BOM 없는 UTF-8
            "txt" => (ToText(cues), true),
            _ => throw new ArgumentException($"자막으로 만들 수 없는 형식입니다: {targetId}", nameof(targetId)),
        };
        File.WriteAllText(output, text, new UTF8Encoding(bom));
    }

    static string Clean(string line) => UnsharedTag().Replace(AssTag().Replace(line, ""), "");

    static TimeSpan ToTime(Match m) => new(0,
        m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0,
        int.Parse(m.Groups[2].Value),
        int.Parse(m.Groups[3].Value),
        int.Parse(m.Groups[4].Value.PadRight(3, '0')));

    static string Stamp(TimeSpan t, char separator) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{separator}{t.Milliseconds:000}";
}
