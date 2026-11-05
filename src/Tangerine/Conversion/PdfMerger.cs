using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Tangerine.Core;
using static Tangerine.Interop.NativeMethods;

namespace Tangerine.Conversion;

/// <summary>
/// 끌어 온 PDF 를 파일 이름 순서(탐색기와 같이 2장 → 10장)로 합쳐 첫 파일 옆에 "첫파일 외 N개.pdf" 를 만든다.
/// 쪽을 그림으로 바꾸지 않고 그대로 옮기므로 글자 선택과 벡터 품질이 유지된다.
/// </summary>
internal static class PdfMerger
{
    public static Task<string> MergeAsync(IReadOnlyList<string> files) => Task.Run(() => Merge(files));

    static string Merge(IReadOnlyList<string> files)
    {
        // Windows 끌기 데이터의 파일 순서는 일정하지 않아 이름으로 맞춘다
        var ordered = files.Select(Path.GetFullPath).Order(Comparer<string>.Create(StrCmpLogicalW)).ToList();
        var first = ordered[0];
        var slot = Path.Combine(Path.GetDirectoryName(first)!, $"{Path.GetFileNameWithoutExtension(first)} 외 {ordered.Count - 1}개.pdf");
        var output = OutputPaths.Reserve(slot, ".pdf");
        try
        {
            using var merged = new PdfDocument();
            foreach (var file in ordered)
            {
                using var source = Open(file);
                foreach (var page in source.Pages)
                    merged.AddPage(page);
            }
            merged.Save(output);
        }
        catch
        {
            ConversionService.TryDelete(output);
            throw;
        }
        return output;
    }

    static PdfDocument Open(string file)
    {
        try
        {
            return PdfReader.Open(file, PdfDocumentOpenMode.Import);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new InvalidOperationException($"{Path.GetFileName(file)} 을(를) 열지 못했습니다 (손상됐거나 암호가 걸린 파일일 수 있음)", ex);
        }
    }
}
