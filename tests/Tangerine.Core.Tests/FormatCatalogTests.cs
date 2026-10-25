using Tangerine.Core;

namespace Tangerine.Core.Tests;

public class FormatCatalogTests
{
    [Theory]
    [InlineData(@"C:\a\photo.JPEG", MediaKind.Image)]
    [InlineData("shot.heic", MediaKind.Image)]
    [InlineData("clip.MOV", MediaKind.Video)]
    [InlineData("song.flac", MediaKind.Audio)]
    public void KindOf_recognizes_extensions_case_insensitively(string path, MediaKind expected)
    {
        Assert.Equal(expected, FormatCatalog.KindOf(path));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("no-extension")]
    public void KindOf_returns_null_for_unsupported_files(string path)
    {
        Assert.Null(FormatCatalog.KindOf(path));
        Assert.Empty(FormatCatalog.TargetsFor(path));
        Assert.Empty(FormatCatalog.MenuTargets([path]));
    }

    [Fact]
    public void Menu_for_a_jpeg_does_not_offer_jpg()
    {
        var ids = FormatCatalog.MenuTargets(["photo.jpeg"]).Select(t => t.Id).ToList();

        Assert.DoesNotContain("jpg", ids);
        Assert.Contains("png", ids);
        Assert.Contains("webp", ids);
    }

    [Fact]
    public void Gif_can_become_video_through_ffmpeg()
    {
        var mp4 = FormatCatalog.Resolve("anim.gif", "mp4");

        Assert.NotNull(mp4);
        Assert.Equal(ConversionEngine.Ffmpeg, mp4.Engine);
        Assert.Equal(ConversionEngine.Magick, FormatCatalog.Resolve("anim.gif", "png")!.Engine);
    }

    [Fact]
    public void Video_to_gif_uses_ffmpeg_and_image_to_gif_uses_magick()
    {
        Assert.Equal(ConversionEngine.Ffmpeg, FormatCatalog.Resolve("clip.mp4", "gif")!.Engine);
        Assert.Equal(ConversionEngine.Magick, FormatCatalog.Resolve("photo.png", "gif")!.Engine);
    }

    [Fact]
    public void Mixed_images_keep_a_format_that_only_some_files_already_have()
    {
        var ids = FormatCatalog.MenuTargets(["a.jpg", "b.png"]).Select(t => t.Id).ToList();

        Assert.Contains("jpg", ids);
        Assert.Contains("png", ids);
    }

    [Fact]
    public void Mixed_kinds_only_offer_formats_every_file_supports()
    {
        var ids = FormatCatalog.MenuTargets(["clip.mp4", "anim.gif"]).Select(t => t.Id).ToList();

        // 첫 파일(영상)의 메뉴 순서를 따른다
        Assert.Equal(["mp4", "webm", "gif"], ids);
    }

    [Fact]
    public void Image_and_audio_have_nothing_in_common()
    {
        Assert.Empty(FormatCatalog.MenuTargets(["photo.png", "song.mp3"]));
    }

    [Theory]
    [InlineData("a.TIF", "tiff", true)]
    [InlineData("a.aif", "aiff", true)]
    [InlineData("a.png", "jpg", false)]
    public void IsSameFormat_treats_aliases_as_the_same(string path, string targetId, bool expected)
    {
        var target = FormatCatalog.Resolve(path, targetId)!;

        Assert.Equal(expected, FormatCatalog.IsSameFormat(path, target));
    }
}
