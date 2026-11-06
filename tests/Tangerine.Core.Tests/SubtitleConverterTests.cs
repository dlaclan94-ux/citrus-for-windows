using System.Text;
using Tangerine.Core;

namespace Tangerine.Core.Tests;

public class SubtitleConverterTests
{
    const string Srt = "﻿1\r\n00:00:01,500 --> 00:00:03,000\r\n{\\an8}<i>안녕하세요</i>\r\n둘째 줄 &amp; 끝\r\n\r\n2\r\n01:02:03,004 --> 01:02:05,000\r\n<font color=\"#ff0\">노란 글자</font>\r\n";

    const string Vtt = "WEBVTT\n\nNOTE 메모는 건너뛴다\n\nintro\n00:01.5 --> 00:03.000 align:start position:10%\n<v 철수>안녕</v>\n\n00:00:04.000 --> 00:00:05.000\n안녕\n";

    [Fact]
    public void Srt_becomes_vtt_with_dot_times_and_shared_tags_only()
    {
        var vtt = SubtitleConverter.ToVtt(SubtitleConverter.Parse(Srt));

        Assert.StartsWith("WEBVTT\n\n", vtt);
        Assert.Contains("00:00:01.500 --> 00:00:03.000\n<i>안녕하세요</i>\n", vtt);
        Assert.Contains("01:02:03.004 --> 01:02:05.000\n노란 글자\n", vtt);
        Assert.DoesNotContain("{\\an8}", vtt);
    }

    [Fact]
    public void Vtt_becomes_numbered_srt_skipping_notes_and_settings()
    {
        var srt = SubtitleConverter.ToSrt(SubtitleConverter.Parse(Vtt));

        Assert.Equal("1\r\n00:00:01,500 --> 00:00:03,000\r\n안녕\r\n\r\n2\r\n00:00:04,000 --> 00:00:05,000\r\n안녕\r\n\r\n", srt);
    }

    [Fact]
    public void Text_keeps_only_the_words_and_drops_repeats()
    {
        Assert.Equal("안녕하세요\r\n둘째 줄 & 끝\r\n노란 글자\r\n", SubtitleConverter.ToText(SubtitleConverter.Parse(Srt)));
        Assert.Equal("안녕\r\n", SubtitleConverter.ToText(SubtitleConverter.Parse(Vtt)));
    }

    [Fact]
    public void Korean_ansi_subtitles_are_read_as_cp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Encoding.GetEncoding(949).GetBytes("1\r\n00:00:01,000 --> 00:00:02,000\r\n한글 자막\r\n"));
            Assert.Equal("한글 자막", SubtitleConverter.Parse(TextFiles.Read(path))[0].Lines[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_file_without_cues_is_rejected()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "그냥 메모");
            Assert.Throws<InvalidDataException>(() => SubtitleConverter.ConvertFile(path, path + ".vtt", "vtt"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
