using System;
using ImageMagick;

namespace Tangerine.Conversion;

/// <summary>Magick.NET 으로 이미지를 바꾼다. 결과 파일은 이미 빈 파일로 만들어져 있어 덮어쓴다.</summary>
internal static class ImageConverter
{
    public static void Convert(string source, string output, string targetId)
    {
        var format = FormatOf(targetId);

        // 움직이는 그림(GIF·WEBP)과 여러 쪽(TIFF·PDF)은 모든 프레임을 살린다
        if (targetId is "gif" or "webp" or "tiff" or "pdf")
        {
            using var frames = new MagickImageCollection(source);
            if (frames.Count > 1 && targetId is ("gif" or "webp"))
                frames.Coalesce();
            foreach (var frame in frames)
            {
                frame.AutoOrient();
                if (targetId == "webp")
                    frame.Quality = 85;
            }
            frames.Write(output, format);
            return;
        }

        using var image = new MagickImage(source);
        image.AutoOrient();

        switch (targetId)
        {
            case "jpg":
                // JPG 는 투명을 못 담으므로 흰 바탕에 얹는다
                if (image.HasAlpha)
                {
                    image.BackgroundColor = MagickColors.White;
                    image.Alpha(AlphaOption.Remove);
                }
                image.Quality = 90;
                break;

            case "avif":
                image.Quality = 80;
                break;

            case "ico":
                // 아이콘은 256px 까지. 여러 크기를 한 파일에 담는다
                image.Resize(new MagickGeometry(256, 256) { Greater = true });
                image.Settings.SetDefine("icon:auto-resize", "256,128,64,48,32,16");
                break;
        }

        image.Write(output, format);
    }

    static MagickFormat FormatOf(string targetId) => targetId switch
    {
        "jpg" => MagickFormat.Jpeg,
        "png" => MagickFormat.Png,
        "webp" => MagickFormat.WebP,
        "avif" => MagickFormat.Avif,
        "gif" => MagickFormat.Gif,
        "bmp" => MagickFormat.Bmp,
        "tiff" => MagickFormat.Tiff,
        "ico" => MagickFormat.Ico,
        "pdf" => MagickFormat.Pdf,
        _ => throw new ArgumentException($"이미지로 만들 수 없는 형식입니다: {targetId}", nameof(targetId)),
    };
}
