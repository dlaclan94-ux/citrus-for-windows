using System.Collections.Generic;
using System.Threading.Tasks;
using PdfSharp.Pdf;
using Citrus.Core;

namespace Citrus.Conversion;

/// <summary>
/// PDF 를 쪽마다 따로 된 PDF 로 나눠 원본 옆에 "문서 1쪽.pdf", "문서 2쪽.pdf" … 를 만든다.
/// 쪽을 그림으로 바꾸지 않고 그대로 옮기므로 글자 선택과 벡터 품질이 유지된다.
/// </summary>
internal static class PdfSplitter
{
    public static Task<IReadOnlyList<string>> SplitAsync(string source) => Task.Run(() => Split(source));

    static IReadOnlyList<string> Split(string source)
    {
        var outputs = new List<string>();
        try
        {
            using var pdf = PdfMerger.Open(source);
            if (pdf.PageCount < 2)
                return outputs; // 1쪽짜리는 나눌 것이 없다 (똑같은 복사본을 만들지 않는다)
            for (var i = 0; i < pdf.PageCount; i++)
            {
                var output = OutputPaths.Reserve(PdfPages.PageSlot(source, i + 1, pdf.PageCount), ".pdf");
                outputs.Add(output);
                using var single = new PdfDocument();
                single.AddPage(pdf.Pages[i]);
                single.Save(output);
            }
        }
        catch
        {
            foreach (var output in outputs)
                ConversionService.TryDelete(output);
            throw;
        }
        return outputs;
    }
}
