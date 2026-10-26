using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tangerine.Core;

namespace Tangerine.Conversion;

/// <summary>
/// 설치된 한글·Word·Excel·PowerPoint 를 보이지 않게 띄워 문서를 PDF 로 저장한다.
/// 결과 파일은 이미 빈 파일로 만들어져 있어 덮어쓴다.
/// </summary>
internal static class DocumentConverter
{
    // ponytail: 한 번에 문서 하나. Office·한글을 여러 개 동시에 띄우면 서로 막히기 쉽다. 느리면 앱별 잠금으로 나눈다.
    static readonly SemaphoreSlim OneAtATime = new(1, 1);

    // 암호 걸린 문서는 암호 창 대신 바로 실패하도록 엉뚱한 암호를 넘긴다 (암호 없는 문서는 무시한다)
    const string NoPassword = "\u0001tangerine";
    const int MsoAutomationSecurityForceDisable = 3; // 매크로는 절대 실행하지 않는다

    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    public static async Task ConvertAsync(string source, string output)
    {
        await OneAtATime.WaitAsync();
        try
        {
            var work = RunOnSta(() => Convert(source, output));
            if (await Task.WhenAny(work, Task.Delay(Limit)) != work)
            {
                // ponytail: 숨은 창(복구·암호 대화상자 등)에 걸린 경우. 그 프로그램은 남아 있을 수 있어 작업 관리자에서 끄면 된다.
                throw new TimeoutException("문서 프로그램이 응답하지 않습니다 (3분 초과)");
            }
            await work;
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
                Word(source, output, text: ReadText(source));
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

    const string HancomModuleName = "TangerineFilePathChecker";

    /// <summary>
    /// 한글이 자동화로 파일을 열 때마다 띄우는 "접근 허용" 확인 창을 건너뛰도록 한컴 보안 승인 모듈의 위치를 적어 둔다.
    /// 탠저린 폴더를 옮겨도 맞도록 매번 적는다. 모듈이 없으면 false (확인 창이 뜬다).
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

    static void Excel(string source, string output)
    {
        dynamic excel = Create("Excel.Application", "Microsoft Excel");
        dynamic? book = null;
        try
        {
            excel.Visible = false;
            excel.DisplayAlerts = false;
            excel.AutomationSecurity = MsoAutomationSecurityForceDisable;
            // Filename, UpdateLinks(0 = 안 함), ReadOnly, Format, Password
            book = excel.Workbooks.Open(source, 0, true, Type.Missing, NoPassword);
            book.ExportAsFixedFormat(0, output); // 0 = PDF, 모든 시트
        }
        finally
        {
            book?.Close(false);
            if ((int)excel.Workbooks.Count == 0)
                excel.Quit();
            Release(excel);
        }
    }

    static void PowerPoint(string source, string output)
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
            deck.SaveAs(output, 32); // 32 = PDF
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

    /// <summary>메모장 파일의 글자. BOM 이 있으면 그대로, 없으면 UTF-8 로 읽어 보고 깨지면 한국어 Windows 기본(CP949)으로.</summary>
    static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string text;
        using (var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true))
        {
            try
            {
                text = reader.ReadToEnd();
            }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                text = Encoding.GetEncoding(949).GetString(bytes);
            }
        }
        // Word 는 문단을 \r 로 나눈다
        return text.Replace("\r\n", "\r").Replace('\n', '\r');
    }

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

    static Task RunOnSta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex is COMException com ? new InvalidOperationException($"문서 프로그램 오류: {com.Message}", com) : ex);
            }
        })
        {
            IsBackground = true,
            Name = "Tangerine 문서 변환",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
