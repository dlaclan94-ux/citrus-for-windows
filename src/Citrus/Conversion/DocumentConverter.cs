using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using Citrus.Core;

namespace Citrus.Conversion;

/// <summary>
/// 설치된 한글·Word·Excel·PowerPoint 를 보이지 않게 띄워 문서를 PDF 로 저장한다.
/// PowerPoint 슬라이드 → 그림, Excel 시트 ↔ CSV 도 여기서 한다.
/// PDF 결과 파일은 이미 빈 파일로 만들어져 있어 덮어쓴다.
/// </summary>
internal static class DocumentConverter
{
    // ponytail: 한 번에 문서 하나. Office·한글을 여러 개 동시에 띄우면 서로 막히기 쉽다. 느리면 앱별 잠금으로 나눈다.
    static readonly SemaphoreSlim OneAtATime = new(1, 1);

    // 암호 걸린 문서는 암호 창 대신 바로 실패하도록 엉뚱한 암호를 넘긴다 (암호 없는 문서는 무시한다)
    const string NoPassword = "\u0001citrus";
    const int MsoAutomationSecurityForceDisable = 3; // 매크로는 절대 실행하지 않는다

    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    public static Task ConvertAsync(string source, string output, CancellationToken cancel) =>
        RunExclusiveAsync(() => { Convert(source, output); return 0; }, cancel);

    /// <summary>슬라이드마다 그림 한 장 ("발표 1장.jpg" …). 300dpi, 긴 변 10,000px 까지.</summary>
    public static Task<IReadOnlyList<string>> SlidesAsync(string source, string extension, CancellationToken cancel) =>
        RunExclusiveAsync(() => SlideImages(source, extension), cancel);

    /// <summary>Excel → CSV (시트마다, UTF-8) 또는 CSV → XLSX.</summary>
    public static Task<IReadOnlyList<string>> SpreadsheetAsync(string source, string targetId, CancellationToken cancel) =>
        RunExclusiveAsync(() => targetId == "csv" ? SheetsToCsv(source) : CsvToWorkbook(source), cancel);

    /// <summary>
    /// 한 번에 하나씩, 숨은 STA 스레드에서 돌린다. 취소는 차례를 기다리는 동안에만 먹는다
    /// (이미 시작한 Office 작업은 끝까지 한다).
    /// </summary>
    static async Task<T> RunExclusiveAsync<T>(Func<T> job, CancellationToken cancel)
    {
        await OneAtATime.WaitAsync(cancel);
        try
        {
            var work = RunOnSta(job);
            if (await Task.WhenAny(work, Task.Delay(Limit)) != work)
            {
                // ponytail: 숨은 창(복구·암호 대화상자 등)에 걸린 경우. 그 프로그램은 남아 있을 수 있어 작업 관리자에서 끄면 된다.
                throw new TimeoutException("문서 프로그램이 응답하지 않습니다 (3분 초과)");
            }
            return await work;
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    static void Convert(string source, string output)
    {
        switch (FormatCatalog.NormalizeExtension(Path.GetExtension(source)))
        {
            case ".hwp" or ".hwpx":
                Hancom(source, output);
                break;
            case ".doc" or ".docx":
                Word(source, output, text: null);
                break;
            case ".txt":
                // Word 는 문단을 \r 로 나눈다
                Word(source, output, text: TextFiles.Read(source).Replace("\r\n", "\r").Replace('\n', '\r'));
                break;
            case ".xls" or ".xlsx":
                Excel(source, output);
                break;
            case ".ppt" or ".pptx":
                PowerPoint(source, output);
                break;
            default:
                throw new ArgumentException($"PDF 로 만들 수 없는 문서입니다: {source}", nameof(source));
        }
    }

    static void Hancom(string source, string output)
    {
        dynamic hwp = Create("HWPFrame.HwpObject", "한글");
        try
        {
            if (RegisterHancomModule())
                hwp.RegisterModule("FilePathCheckDLL", HancomModuleName);
            if (!(bool)hwp.Open(source, "", "forceopen:true;versionwarning:false;suspendpassword:true"))
                throw new InvalidOperationException("한글이 문서를 열지 못했습니다 (암호가 걸렸거나 손상된 문서일 수 있음)");
            if (!(bool)hwp.SaveAs(output, "PDF", ""))
                throw new InvalidOperationException("한글이 PDF 로 저장하지 못했습니다");
        }
        finally
        {
            hwp.Clear(1); // 1 = 저장하지 않고 닫기
            hwp.Quit();
            Release(hwp);
        }
    }

    const string HancomModuleName = "CitrusFilePathChecker";

    /// <summary>
    /// 한글이 자동화로 파일을 열 때마다 띄우는 "접근 허용" 확인 창을 건너뛰도록 한컴 보안 승인 모듈의 위치를 적어 둔다.
    /// 시트러스 폴더를 옮겨도 맞도록 매번 적는다. 모듈이 없으면 false (확인 창이 뜬다).
    /// </summary>
    static bool RegisterHancomModule()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "hancom", "FilePathCheckerModuleExample.dll");
        if (!File.Exists(dll))
            return false;
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\HNC\HwpAutomation\Modules");
        key.SetValue(HancomModuleName, dll);
        return true;
    }

    /// <param name="text">null 이면 Word 문서를 연다. 아니면 새 문서에 이 글을 넣는다 (메모장 파일).</param>
    static void Word(string source, string output, string? text)
    {
        dynamic word = Create("Word.Application", "Microsoft Word");
        dynamic? doc = null;
        try
        {
            word.Visible = false;
            word.DisplayAlerts = 0;
            word.AutomationSecurity = MsoAutomationSecurityForceDisable;
            if (text is null)
            {
                // FileName, ConfirmConversions, ReadOnly, AddToRecentFiles, PasswordDocument
                doc = word.Documents.Open(source, false, true, false, NoPassword);
            }
            else
            {
                doc = word.Documents.Add();
                doc.Content.Text = text;
                doc.Content.Font.Name = "맑은 고딕";
                doc.Content.Font.Size = 10.5;
            }
            doc.ExportAsFixedFormat(output, 17); // 17 = PDF
        }
        finally
        {
            doc?.Close(0); // 0 = 저장하지 않음
            if ((int)word.Documents.Count == 0)
                word.Quit(0);
            Release(word);
        }
    }

    static void Excel(string source, string output) =>
        // Filename, UpdateLinks(0 = 안 함), ReadOnly, Format, Password
        WithExcel(excel => excel.Workbooks.Open(source, 0, true, Type.Missing, NoPassword),
            (_, book) => book.ExportAsFixedFormat(0, output)); // 0 = PDF, 모든 시트

    /// <summary>시트마다 CSV (UTF-8). 시트가 하나면 "매출.csv", 여럿이면 "매출 시트이름.csv".</summary>
    static IReadOnlyList<string> SheetsToCsv(string source)
    {
        var outputs = new List<string>();
        try
        {
            WithExcel(excel => excel.Workbooks.Open(source, 0, true, Type.Missing, NoPassword), (excel, book) =>
            {
                int count = book.Worksheets.Count;
                var dir = Path.GetDirectoryName(Path.GetFullPath(source))!;
                for (var i = 1; i <= count; i++)
                {
                    dynamic sheet = book.Worksheets[i];
                    var slot = count == 1 ? source : Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(source)} {SafeFileName((string)sheet.Name)}.csv");
                    var output = OutputPaths.Reserve(slot, ".csv");
                    outputs.Add(output);
                    sheet.Copy(); // 이 시트만 든 새 통합 문서를 만든다
                    dynamic single = excel.ActiveWorkbook;
                    single.SaveAs(output, 62); // 62 = CSV UTF-8
                    single.Close(false);
                }
            });
        }
        catch
        {
            foreach (var output in outputs)
                ConversionService.TryDelete(output);
            throw;
        }
        return outputs;
    }

    /// <summary>CSV → XLSX. 글자 깨짐을 막으려고 인코딩(UTF-8 / CP949)을 직접 알려 주고 연다.</summary>
    static IReadOnlyList<string> CsvToWorkbook(string source)
    {
        // Excel 은 확장자가 .csv 면 OpenText 의 인코딩 지정을 무시하므로 .txt 사본으로 연다
        var bytes = File.ReadAllBytes(source);
        var temp = Path.Combine(Path.GetTempPath(), $"citrus-{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(temp, bytes);
        var output = OutputPaths.Reserve(source, ".xlsx");
        try
        {
            var origin = IsUtf8(bytes) ? 65001 : 949;
            // Filename, Origin, StartRow, DataType(1 = 구분 기호), TextQualifier(1 = "), ConsecutiveDelimiter, Tab, Semicolon, Comma
            WithExcel(excel =>
            {
                excel.Workbooks.OpenText(temp, origin, 1, 1, 1, false, false, false, true);
                return excel.ActiveWorkbook;
            }, (_, book) => book.SaveAs(output, 51)); // 51 = XLSX
        }
        catch
        {
            ConversionService.TryDelete(output);
            throw;
        }
        finally
        {
            ConversionService.TryDelete(temp);
        }
        return [output];
    }

    static void WithExcel(Func<dynamic, dynamic> open, Action<dynamic, dynamic> work)
    {
        dynamic excel = Create("Excel.Application", "Microsoft Excel");
        dynamic? book = null;
        try
        {
            excel.Visible = false;
            excel.DisplayAlerts = false;
            excel.AutomationSecurity = MsoAutomationSecurityForceDisable;
            book = open(excel);
            work(excel, book);
        }
        finally
        {
            book?.Close(false);
            while ((int)excel.Workbooks.Count > 0) // 시트 복사로 만든 통합 문서가 남았으면 닫는다
                excel.Workbooks[1].Close(false);
            excel.Quit();
            Release(excel);
        }
    }

    static void PowerPoint(string source, string output) =>
        WithPresentation(source, deck => deck.SaveAs(output, 32)); // 32 = PDF

    static IReadOnlyList<string> SlideImages(string source, string extension)
    {
        var outputs = new List<string>();
        try
        {
            WithPresentation(source, deck =>
            {
                // 슬라이드 크기는 포인트(1/72 인치) 단위
                double width = deck.PageSetup.SlideWidth, height = deck.PageSetup.SlideHeight;
                var scale = Math.Min(300 / 72.0, 10000 / Math.Max(width, height));
                int count = deck.Slides.Count;
                for (var i = 1; i <= count; i++)
                {
                    var output = OutputPaths.Reserve(PdfPages.PageSlot(source, i, count, "장"), extension);
                    outputs.Add(output);
                    var png = extension == ".png" ? output : Path.Combine(Path.GetTempPath(), $"citrus-{Guid.NewGuid():N}.png");
                    deck.Slides[i].Export(png, "PNG", (int)Math.Round(width * scale), (int)Math.Round(height * scale));
                    if (png != output)
                    {
                        using (var image = new MagickImage(png))
                        {
                            image.BackgroundColor = MagickColors.White;
                            image.Alpha(AlphaOption.Remove);
                            image.Quality = 90;
                            image.Write(output, MagickFormat.Jpeg);
                        }
                        ConversionService.TryDelete(png);
                    }
                }
            });
        }
        catch
        {
            foreach (var output in outputs)
                ConversionService.TryDelete(output);
            throw;
        }
        return outputs;
    }

    static void WithPresentation(string source, Action<dynamic> work)
    {
        // PowerPoint 는 프로세스 하나를 모두 같이 쓴다. 사람이 열어 둔 발표 자료가 있으면 PowerPoint 를 끄지 않는다.
        var wasRunning = Process.GetProcessesByName("POWERPNT").Length > 0;
        dynamic ppt = Create("PowerPoint.Application", "Microsoft PowerPoint");
        dynamic? deck = null;
        var security = ppt.AutomationSecurity;
        try
        {
            ppt.AutomationSecurity = MsoAutomationSecurityForceDisable;
            // FileName, ReadOnly(-1 = 예), Untitled(0), WithWindow(0 = 창 없이)
            deck = ppt.Presentations.Open(source, -1, 0, 0);
            work(deck);
        }
        finally
        {
            deck?.Close();
            ppt.AutomationSecurity = security;
            if (!wasRunning && (int)ppt.Presentations.Count == 0)
                ppt.Quit();
            Release(ppt);
        }
    }

    static bool IsUtf8(byte[] bytes)
    {
        try
        {
            new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    static dynamic Create(string progId, string appName)
    {
        var type = Type.GetTypeFromProgID(progId) ?? throw new InvalidOperationException($"{appName} 이(가) 설치되어 있지 않습니다");
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException($"{appName} 을(를) 실행하지 못했습니다");
    }

    /// <summary>COM 개체를 바로 놓아 주어야 Excel 같은 프로그램이 뒤에 남지 않는다.</summary>
    static void Release(object app)
    {
        Marshal.FinalReleaseComObject(app);
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    static Task<T> RunOnSta<T>(Func<T> job)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(job());
            }
            catch (Exception ex)
            {
                done.SetException(ex is COMException com ? new InvalidOperationException($"문서 프로그램 오류: {com.Message}", com) : ex);
            }
        })
        {
            IsBackground = true,
            Name = "Citrus 문서 변환",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
