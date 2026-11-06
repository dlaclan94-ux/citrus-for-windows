using Tangerine.Core;

namespace Tangerine.Core.Tests;

public class FormatCatalogTests
{
    [Theory]
    [InlineData(@"C:\a\photo.JPEG", MediaKind.Image)]
    [InlineData("shot.heic", MediaKind.Image)]
    [InlineData("logo.SVG", MediaKind.Image)]
    [InlineData("clip.MOV", MediaKind.Video)]
    [InlineData("song.flac", MediaKind.Audio)]
    [InlineData("보고서.HWPX", MediaKind.Document)]
    [InlineData("notes.txt", MediaKind.Document)]
    [InlineData("명단.csv", MediaKind.Document)]
    [InlineData("자막.SRT", MediaKind.Subtitle)]
    public void KindOf_recognizes_extensions_case_insensitively(string path, MediaKind expected)
    {
        Assert.Equal(expected, FormatCatalog.KindOf(path));
    }

    [Theory]
    [InlineData("drawing.dwg")]
    [InlineData("no-extension")]
    public void Unsupported_files_can_still_be_zipped_or_have_their_path_copied(string path)
    {
        Assert.Null(FormatCatalog.KindOf(path));
        Assert.Empty(FormatCatalog.TargetsFor(path));
        Assert.Equal([FormatCatalog.Zip, FormatCatalog.CopyPath], FormatCatalog.MenuTargets([path]));
    }

    [Theory]
    [InlineData("a.hwp")]
    [InlineData("a.docx")]
    [InlineData("a.txt")]
    public void Documents_become_pdf_through_office_or_hancom(string path)
    {
        Assert.Equal(["pdf", "zip", "path"], FormatCatalog.MenuTargets([path]).Select(t => t.Id));
        Assert.Equal(ConversionEngine.Document, FormatCatalog.Resolve(path, "pdf")!.Engine);
    }

    [Fact]
    public void Slides_become_pdf_or_one_picture_per_slide()
    {
        Assert.Equal(["pdf", "jpg", "png", "zip", "path"], FormatCatalog.MenuTargets(["발표.pptx"]).Select(t => t.Id));
        Assert.Equal(ConversionEngine.Slides, FormatCatalog.Resolve("발표.ppt", "jpg")!.Engine);
    }

    [Fact]
    public void Slides_and_pdfs_share_pictures_but_each_uses_its_own_engine()
    {
        Assert.Equal(["jpg", "png", "zip", "path"], FormatCatalog.MenuTargets(["발표.pptx", "도면.pdf"]).Select(t => t.Id));
        Assert.Equal(ConversionEngine.PdfPages, FormatCatalog.Resolve("도면.pdf", "jpg")!.Engine);
    }

    [Fact]
    public void Spreadsheets_and_csv_convert_both_ways()
    {
        Assert.Equal(["pdf", "csv", "zip", "path"], FormatCatalog.MenuTargets(["매출.xlsx"]).Select(t => t.Id));
        Assert.Equal(["xlsx", "zip", "path"], FormatCatalog.MenuTargets(["명단.csv"]).Select(t => t.Id));
        Assert.Equal(ConversionEngine.Spreadsheet, FormatCatalog.Resolve("매출.xls", "csv")!.Engine);
        Assert.Equal(ConversionEngine.Spreadsheet, FormatCatalog.Resolve("명단.csv", "xlsx")!.Engine);
    }

    [Theory]
    [InlineData("자막.srt", "vtt,txt")]
    [InlineData("자막.vtt", "srt,txt")]
    public void Subtitles_offer_the_other_format_and_plain_text(string path, string expected)
    {
        Assert.Equal([.. expected.Split(','), "zip", "path"], FormatCatalog.MenuTargets([path]).Select(t => t.Id));
    }

    [Fact]
    public void Svg_becomes_any_picture_format()
    {
        var ids = FormatCatalog.MenuTargets(["logo.svg"]).Select(t => t.Id).ToList();

        Assert.Contains("png", ids);
        Assert.Contains("jpg", ids);
        Assert.Equal(ConversionEngine.Magick, FormatCatalog.Resolve("logo.svg", "png")!.Engine);
    }

    [Fact]
    public void Images_and_documents_share_pdf_but_each_uses_its_own_engine()
    {
        Assert.Equal(["pdf", "zip", "path"], FormatCatalog.MenuTargets(["scan.jpg", "report.docx"]).Select(t => t.Id));
        Assert.Equal(ConversionEngine.Magick, FormatCatalog.Resolve("scan.jpg", "pdf")!.Engine);
        Assert.Equal(ConversionEngine.Document, FormatCatalog.Resolve("report.docx", "pdf")!.Engine);
    }

    [Fact]
    public void Pdf_becomes_jpg_or_png_page_by_page()
    {
        Assert.Equal(["split", "jpg", "png", "zip", "path"], FormatCatalog.MenuTargets(["도면.PDF"]).Select(t => t.Id));
        Assert.Equal(ConversionEngine.PdfPages, FormatCatalog.Resolve("도면.pdf", "png")!.Engine);
        // 이미지 → PDF 는 그대로 Magick
        Assert.Equal(ConversionEngine.Magick, FormatCatalog.Resolve("scan.png", "pdf")!.Engine);
    }

    [Fact]
    public void Pdf_and_images_share_jpg_and_png()
    {
        Assert.Equal(["jpg", "png", "zip", "path"], FormatCatalog.MenuTargets(["도면.pdf", "scan.webp"]).Select(t => t.Id));
        Assert.Equal(ConversionEngine.Magick, FormatCatalog.Resolve("scan.webp", "jpg")!.Engine);
    }

    [Fact]
    public void Several_pdfs_can_be_merged()
    {
        Assert.Equal(["merge", "split", "jpg", "png", "zip", "path"], FormatCatalog.MenuTargets(["1장.pdf", "2장.PDF"]).Select(t => t.Id));
    }

    [Fact]
    public void Splitting_a_pdf_is_not_skipped_as_already_pdf()
    {
        var split = FormatCatalog.Resolve("묶음.pdf", "split")!;

        Assert.Equal(ConversionEngine.PdfSplit, split.Engine);
        Assert.False(FormatCatalog.IsSameFormat("묶음.pdf", split));
    }

    [Theory]
    [InlineData("한 개만.pdf")]
    [InlineData("문서.pdf", "사진.png")]
    [InlineData("사진1.png", "사진2.png")]
    public void Merge_is_only_for_two_or_more_pdfs(params string[] files)
    {
        Assert.DoesNotContain(FormatCatalog.PdfMerge, FormatCatalog.MenuTargets(files));
    }

    [Theory]
    [InlineData("photo.png")]
    [InlineData("clip.mp4", "song.mp3")]
    [InlineData("자막.srt")]
    public void The_last_two_slices_are_always_zip_then_copy_path(params string[] files)
    {
        var menu = FormatCatalog.MenuTargets(files);

        Assert.Equal(FormatCatalog.Zip, menu[^2]);
        Assert.Equal(FormatCatalog.CopyPath, menu[^1]);
    }

    [Fact]
    public void A_folder_can_only_be_zipped_or_have_its_path_copied()
    {
        Assert.Equal([FormatCatalog.Zip, FormatCatalog.CopyPath], FormatCatalog.MenuTargets([Path.GetTempPath(), "photo.png"]));
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
        Assert.Equal(["mp4", "webm", "gif", "zip", "path"], ids);
    }

    [Fact]
    public void Image_and_audio_can_only_be_zipped_or_have_their_paths_copied()
    {
        Assert.Equal([FormatCatalog.Zip, FormatCatalog.CopyPath], FormatCatalog.MenuTargets(["photo.png", "song.mp3"]));
    }

    [Theory]
    [InlineData("a.TIF", "tiff", true)]
    [InlineData("a.aif", "aiff", true)]
    [InlineData("a.png", "jpg", false)]
    [InlineData("a.srt", "srt", true)]
    public void IsSameFormat_treats_aliases_as_the_same(string path, string targetId, bool expected)
    {
        var target = FormatCatalog.Resolve(path, targetId)!;

        Assert.Equal(expected, FormatCatalog.IsSameFormat(path, target));
    }
}
