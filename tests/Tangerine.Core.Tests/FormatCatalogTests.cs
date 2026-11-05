using Tangerine.Core;

namespace Tangerine.Core.Tests;

public class FormatCatalogTests
{
    [Theory]
    [InlineData(@"C:\a\photo.JPEG", MediaKind.Image)]
    [InlineData("shot.heic", MediaKind.Image)]
    [InlineData("clip.MOV", MediaKind.Video)]
    [InlineData("song.flac", MediaKind.Audio)]
    [InlineData("보고서.HWPX", MediaKind.Document)]
    [InlineData("notes.txt", MediaKind.Document)]
    public void KindOf_recognizes_extensions_case_insensitively(string path, MediaKind expected)
    {
        Assert.Equal(expected, FormatCatalog.KindOf(path));
    }

    [Theory]
    [InlineData("drawing.dwg")]
    [InlineData("no-extension")]
    public void Unsupported_files_can_still_be_zipped(string path)
    {
        Assert.Null(FormatCatalog.KindOf(path));
        Assert.Empty(FormatCatalog.TargetsFor(path));
        Assert.Equal([FormatCatalog.Zip], FormatCatalog.MenuTargets([path]));
    }

    [Theory]
    [InlineData("a.hwp")]
    [InlineData("a.docx")]
    [InlineData("a.xls")]
    [InlineData("a.pptx")]
    [InlineData("a.txt")]
    public void Documents_become_pdf_through_office_or_hancom(string path)
    {
        Assert.Equal(["pdf", "zip"], FormatCatalog.MenuTargets([path]).Select(t => t.Id));
        Assert.Equal(ConversionEngine.Document, FormatCatalog.Resolve(path, "pdf")!.Engine);
    }

    [Fact]
    public void Images_and_documents_share_pdf_but_each_uses_its_own_engine()
    {
        Assert.Equal(["pdf", "zip"], FormatCatalog.MenuTargets(["scan.jpg", "report.docx"]).Select(t => t.Id));
        Assert.Equal(ConversionEngine.Magick, FormatCatalog.Resolve("scan.jpg", "pdf")!.Engine);
        Assert.Equal(ConversionEngine.Document, FormatCatalog.Resolve("report.docx", "pdf")!.Engine);
    }

    [Fact]
    public void Pdf_becomes_jpg_or_png_page_by_page()
    {
        Assert.Equal(["jpg", "png", "zip"], FormatCatalog.MenuTargets(["도면.PDF"]).Select(t => t.Id));
        Assert.Equal(ConversionEngine.PdfPages, FormatCatalog.Resolve("도면.pdf", "png")!.Engine);
        // 이미지 → PDF 는 그대로 Magick
        Assert.Equal(ConversionEngine.Magick, FormatCatalog.Resolve("scan.png", "pdf")!.Engine);
    }

    [Fact]
    public void Pdf_and_images_share_jpg_and_png()
    {
        Assert.Equal(["jpg", "png", "zip"], FormatCatalog.MenuTargets(["도면.pdf", "scan.webp"]).Select(t => t.Id));
        Assert.Equal(ConversionEngine.Magick, FormatCatalog.Resolve("scan.webp", "jpg")!.Engine);
    }

    [Fact]
    public void Several_pdfs_can_be_merged()
    {
        Assert.Equal(["merge", "jpg", "png", "zip"], FormatCatalog.MenuTargets(["1장.pdf", "2장.PDF"]).Select(t => t.Id));
    }

    [Theory]
    [InlineData("한 개만.pdf")]
    [InlineData("문서.pdf", "사진.png")]
    [InlineData("사진1.png", "사진2.png")]
    public void Merge_is_only_for_two_or_more_pdfs(params string[] files)
    {
        Assert.DoesNotContain(FormatCatalog.PdfMerge, FormatCatalog.MenuTargets(files));
    }

    [Fact]
    public void Zip_is_always_the_last_slice()
    {
        Assert.Equal(FormatCatalog.Zip, FormatCatalog.MenuTargets(["photo.png"])[^1]);
        Assert.Equal(FormatCatalog.Zip, FormatCatalog.MenuTargets(["clip.mp4", "song.mp3"])[^1]);
    }

    [Fact]
    public void A_folder_can_only_be_zipped()
    {
        Assert.Equal([FormatCatalog.Zip], FormatCatalog.MenuTargets([Path.GetTempPath(), "photo.png"]));
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
        Assert.Equal(["mp4", "webm", "gif", "zip"], ids);
    }

    [Fact]
    public void Image_and_audio_can_only_be_zipped_together()
    {
        Assert.Equal([FormatCatalog.Zip], FormatCatalog.MenuTargets(["photo.png", "song.mp3"]));
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
