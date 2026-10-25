using Tangerine.Core;

namespace Tangerine.Core.Tests;

public class FfmpegArgumentsTests
{
    const string Input = @"C:\in\clip.mov";
    const string Output = @"C:\in\clip.mp4";

    [Fact]
    public void Input_comes_after_dash_i_and_output_is_last()
    {
        var args = FfmpegArguments.Build(Input, Output, "mp4");

        Assert.Equal(Input, args[args.ToList().IndexOf("-i") + 1]);
        Assert.Equal(Output, args[^1]);
        Assert.Contains("-nostdin", args);
    }

    [Fact]
    public void Mp4_is_h264_with_even_size_and_faststart()
    {
        var args = FfmpegArguments.Build(Input, Output, "mp4");

        Assert.Contains("libx264", args);
        Assert.Contains("yuv420p", args);
        Assert.Contains("+faststart", args);
        Assert.Contains(args, a => a.StartsWith("scale=trunc(iw/2)*2"));
    }

    [Theory]
    [InlineData("mp3")]
    [InlineData("wav")]
    [InlineData("m4a")]
    [InlineData("flac")]
    [InlineData("ogg")]
    [InlineData("opus")]
    [InlineData("aiff")]
    public void Audio_targets_drop_the_video_stream(string targetId)
    {
        Assert.Contains("-vn", FfmpegArguments.Build(Input, "out." + targetId, targetId));
    }

    [Fact]
    public void Every_ffmpeg_target_in_the_catalog_has_arguments()
    {
        var ffmpegIds = FormatCatalog.VideoTargets
            .Concat(FormatCatalog.AudioTargets)
            .Concat(FormatCatalog.TargetsFor("a.gif"))
            .Where(t => t.Engine == ConversionEngine.Ffmpeg)
            .Select(t => t.Id)
            .Distinct();

        foreach (var id in ffmpegIds)
            Assert.NotEmpty(FfmpegArguments.Build(Input, "out." + id, id));
    }

    [Fact]
    public void Unknown_target_throws()
    {
        Assert.Throws<ArgumentException>(() => FfmpegArguments.Build(Input, Output, "png"));
    }
}
