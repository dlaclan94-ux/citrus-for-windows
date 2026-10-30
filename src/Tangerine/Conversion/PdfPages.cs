using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ImageMagick;
using Tangerine.Core;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Tangerine.Conversion;

/// <summary>
/// Windows 기본 PDF 렌더러로 PDF 를 쪽마다 그림 한 장씩 만든다 (Ghostscript 없이).
/// 한 쪽이면 "문서.jpg", 여러 쪽이면 "문서 1쪽.jpg", "문서 2쪽.jpg" …
/// </summary>
internal static class PdfPages
{
    const double Dpi = 300;        // 인쇄 표준. A4 면 2481×3509px (시험: 1200dpi 까지는 그려지나 1.4GB 를 써 무겁다)
    const double Scale = Dpi / 96; // PDF 쪽 크기는 1/96 인치 단위
    const double MaxSide = 10000;  // A0 같은 큰 도면도 긴 변이 이 픽셀을 넘지 않게

    /// <param name="extension">".jpg" 또는 ".png"</param>
    public static async Task<IReadOnlyList<string>> ConvertAsync(string source, string extension)
    {
        PdfDocument pdf;
        try
        {
            pdf = await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(source)));
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            // Windows PDF 렌더러는 이유를 비워 두는 경우가 많아 알기 쉬운 말로 바꾼다
            throw new InvalidOperationException(ex.HResult == unchecked((int)0x8007052B)
                ? "암호가 걸린 PDF 입니다"
                : "PDF 를 열지 못했습니다 (손상됐거나 암호가 걸린 파일일 수 있음)", ex);
        }
        if (pdf.PageCount == 0)
            throw new InvalidOperationException("쪽이 없는 PDF 입니다");

        var outputs = new List<string>();
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(source))!;
            var name = Path.GetFileNameWithoutExtension(source);
            for (uint i = 0; i < pdf.PageCount; i++)
            {
                // 쪽 이름으로 자리를 잡는다 (같은 이름이 있으면 OutputPaths 가 (1) 을 붙인다)
                var slot = pdf.PageCount == 1 ? source : Path.Combine(dir, $"{name} {i + 1}쪽.pdf");
                var output = OutputPaths.Reserve(slot, extension);
                outputs.Add(output);

                using var page = pdf.GetPage(i);
                var scale = Math.Min(Scale, MaxSide / Math.Max(page.Size.Width, page.Size.Height));
                using var png = new InMemoryRandomAccessStream();
                // 기본은 흰 바탕의 PNG
                await page.RenderToStreamAsync(png, new PdfPageRenderOptions { DestinationWidth = (uint)Math.Round(page.Size.Width * scale) });

                using var rendered = png.GetInputStreamAt(0).AsStreamForRead();
                if (extension == ".png")
                {
                    using var file = File.Create(output);
                    await rendered.CopyToAsync(file);
                }
                else
                {
                    using var image = new MagickImage(rendered);
                    image.Quality = 90;
                    image.Write(output, MagickFormat.Jpeg);
                }
            }
        }
        catch
        {
            foreach (var output in outputs)
                TryDelete(output);
            throw;
        }
        return outputs;
    }

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
