using System;
using System.Diagnostics;
using System.Linq;
using Tangerine.Conversion;
using Tangerine.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Tangerine.Ui;

/// <summary>알림 영역 아이콘과 오른쪽 클릭 메뉴. 변환 결과는 Windows 알림으로 알린다.</summary>
internal sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    readonly Drawing.Icon _image;
    readonly Forms.ToolStripMenuItem _enabledItem;
    readonly Forms.ToolStripMenuItem _explorerOnlyItem;
    readonly Forms.ToolStripMenuItem _startupItem;
    string? _lastOutput;
    int _busy;

    public event Action<bool>? EnabledChanged;
    public event Action<bool>? ExplorerOnlyChanged;
    public event Action? ExitRequested;

    public TrayIcon(AppSettings settings)
    {
        // 실행 파일과 같은 귤 단면 아이콘에서 알림 영역 크기(보통 16px, 배율에 따라 더 큼)를 고른다
        using (var ico = typeof(TrayIcon).Assembly.GetManifestResourceStream("Tangerine.ico")!)
            _image = new Drawing.Icon(ico, Forms.SystemInformation.SmallIconSize);

        _enabledItem = new Forms.ToolStripMenuItem("Shift 끌기로 변환 켜기") { CheckOnClick = true, Checked = settings.Enabled };
        _enabledItem.CheckedChanged += (_, _) =>
        {
            EnabledChanged?.Invoke(_enabledItem.Checked);
            UpdateText();
        };

        _explorerOnlyItem = new Forms.ToolStripMenuItem("탐색기·바탕화면에서만 작동") { CheckOnClick = true, Checked = settings.ExplorerOnly };
        _explorerOnlyItem.CheckedChanged += (_, _) => ExplorerOnlyChanged?.Invoke(_explorerOnlyItem.Checked);

        _startupItem = new Forms.ToolStripMenuItem("Windows 시작 시 실행") { CheckOnClick = true, Checked = StartupRegistration.IsEnabled() };
        _startupItem.CheckedChanged += (_, _) =>
        {
            try
            {
                StartupRegistration.Set(_startupItem.Checked);
            }
            catch (Exception ex)
            {
                AppLog.Write($"자동 실행 설정 실패: {ex.Message}");
                Notify("자동 실행 설정 실패", ex.Message, Forms.ToolTipIcon.Warning);
            }
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(_explorerOnlyItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("사용 방법", null, (_, _) => ShowHelp());
        menu.Items.Add("기록 폴더 열기", null, (_, _) => OpenFolder(AppLog.Folder));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => ExitRequested?.Invoke());

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ShowHelp();
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_lastOutput is not null)
                RevealInExplorer(_lastOutput);
        };
        UpdateText();
    }

    public void ShowHelp() => Notify(
        "Tangerine 사용 방법",
        "탐색기에서 파일을 끄는 도중 Shift 를 누르면 원형 메뉴가 뜹니다. 원하는 형식 위에 놓으면 원본 옆에 새 파일이 생깁니다.");

    public void BeginWork(int files)
    {
        _busy += files;
        UpdateText();
    }

    public void EndWork(BatchResult result)
    {
        _busy = Math.Max(0, _busy - result.Files.Count);
        UpdateText();

        if (result.Target.Engine is Core.ConversionEngine.Archive or Core.ConversionEngine.PdfMerge)
        {
            var zip = result.Target.Engine == Core.ConversionEngine.Archive;
            var combined = result.FirstOutput;
            var name = System.IO.Path.GetFileName(combined);
            Notify(
                (zip ? "압축" : "합치기") + (combined is null ? " 실패" : " 완료"),
                combined is null
                    ? $"{(zip ? "압축하지" : "합치지")} 못했습니다: {result.Files[0].Error}"
                    : zip
                        ? $"{result.Files.Count}개 항목을 {name} 로 압축했습니다."
                        : $"PDF {result.Files.Count}개를 {name} 로 합쳤습니다.",
                combined is null ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info,
                reveal: combined);
            return;
        }

        var label = result.Target.Label;
        var text = result.Converted > 0 ? $"{label} 파일 {result.Made}개를 만들었습니다." : $"{label} 파일을 만들지 못했습니다.";
        if (result.Skipped > 0)
            text += $" 이미 {label} 이거나 바꿀 수 없는 {result.Skipped}개는 건너뛰었습니다.";
        if (result.Failed > 0)
        {
            var reason = result.Files.First(f => f.Outcome == FileOutcome.Failed).Error;
            text += result.Failed == 1 && reason is not null
                ? $" 실패: {reason}."
                : $" {result.Failed}개 실패 — 기록 폴더의 tangerine.log 를 확인하세요.";
        }

        Notify(
            result.Failed > 0 ? "일부 변환 실패" : "변환 완료",
            text,
            result.Failed > 0 ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info,
            reveal: result.FirstOutput);
    }

    /// <param name="reveal">알림을 누르면 탐색기에서 선택해 보여 줄 파일</param>
    public void Notify(string title, string text, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info, string? reveal = null)
    {
        _lastOutput = reveal;
        _icon.ShowBalloonTip(4000, title, text, icon);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }

    void UpdateText()
    {
        var text = _busy > 0 ? $"Tangerine — 변환 중 {_busy}개" : _enabledItem.Checked ? "Tangerine — 대기 중" : "Tangerine — 꺼짐";
        _icon.Text = text;
    }

    static void RevealInExplorer(string path) =>
        Start("explorer.exe", $"/select,\"{path}\"");

    static void OpenFolder(string folder)
    {
        System.IO.Directory.CreateDirectory(folder);
        Start("explorer.exe", $"\"{folder}\"");
    }

    static void Start(string file, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write($"{file} 실행 실패: {ex.Message}");
        }
    }

}
