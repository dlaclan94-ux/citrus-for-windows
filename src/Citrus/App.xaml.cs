using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Citrus.Conversion;
using Citrus.Core;
using Citrus.Interop;
using Citrus.Services;
using Citrus.Ui;
using Forms = System.Windows.Forms;

namespace Citrus;

/// <summary>창 없이 알림 영역에 상주한다. 탐색기에서 파일을 끌어오는 도중 Shift 를 누르면 원형 메뉴를 띄운다.</summary>
public partial class App : Application
{
    Mutex? _singleInstance;
    AppSettings _settings = new();
    DragWatcher? _watcher;
    RadialMenuWindow? _menu;
    TrayIcon? _tray;
    readonly ConversionService _converter = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\Citrus.SingleInstance", out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("시트러스가 이미 실행 중입니다. 알림 영역의 귤 아이콘을 확인하세요.", "Citrus");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        // 변환 스레드 등 화면 밖 스레드의 오류는 앱을 끝내므로 끝나기 전에 이유를 남긴다
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppLog.Write($"처리하지 못한 오류로 종료: {args.ExceptionObject}");
        _settings = AppSettings.Load();

        _menu = new RadialMenuWindow();
        _menu.FormatChosen += OnFormatChosen;

        _tray = new TrayIcon(_settings);
        _tray.EnabledChanged += on =>
        {
            _settings.Enabled = on;
            _settings.Save();
            if (_watcher is not null)
                _watcher.Enabled = on;
        };
        _tray.ExplorerOnlyChanged += on =>
        {
            _settings.ExplorerOnly = on;
            _settings.Save();
            if (_watcher is not null)
                _watcher.ExplorerOnly = on;
        };
        _tray.ExitRequested += () =>
        {
            AppLog.Write("알림 영역 메뉴에서 종료");
            Shutdown();
        };
        _tray.CancelRequested += _converter.CancelAll;
        // 진행률은 변환 스레드에서 오므로 화면 스레드로 넘겨 아이콘을 그린다
        _converter.ProgressChanged += progress => Dispatcher.BeginInvoke(() => _tray?.SetProgress(progress));

        try
        {
            _watcher = new DragWatcher(Dispatcher) { Enabled = _settings.Enabled, ExplorerOnly = _settings.ExplorerOnly };
            _watcher.ShiftDragStarted += (x, y) => _menu.ShowAt(x, y);
            _watcher.DragEnded += () => _menu.HideSoon();
        }
        catch (Exception ex)
        {
            AppLog.Write($"마우스 훅 설치 실패: {ex}");
            _tray.Notify("시트러스를 시작하지 못했습니다", ex.Message, Forms.ToolTipIcon.Error);
        }

        if (!_converter.FfmpegAvailable)
            _tray.Notify("ffmpeg.exe 없음", "이미지 변환만 쓸 수 있습니다. 영상·오디오 변환에는 tools\\ffmpeg.exe 가 필요합니다.", Forms.ToolTipIcon.Warning);
        else if (!_settings.WelcomeShown)
        {
            _tray.ShowHelp();
            _settings.WelcomeShown = true;
            _settings.Save();
        }

        AppLog.Write("시작");
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        AppLog.Write($"Windows {e.ReasonSessionEnding} 로 종료");
        base.OnSessionEnding(e);
    }

    // 로그에 "시작" 다음 "종료" 가 없으면 작업 관리자·다른 프로그램이 강제로 끈 것이다
    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Write($"종료 (코드 {e.ApplicationExitCode})");
        _watcher?.Dispose();
        _tray?.Dispose();
        _menu?.Close();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    async void OnFormatChosen(string[] files, TargetFormat target)
    {
        if (_tray is null)
            return;

        _tray.BeginWork(files.Length);
        try
        {
            var result = await _converter.ConvertAsync(files, target);
            _tray.EndWork(result);
        }
        catch (Exception ex)
        {
            // ConvertAsync 는 파일마다 오류를 잡으므로 여기까지 오면 예상 밖의 문제
            AppLog.Write($"변환 중 오류: {ex}");
            _tray.EndWork(new BatchResult(target, files.Select(f => new FileResult(f, null, FileOutcome.Failed, ex.Message)).ToArray()));
        }
    }

    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write($"처리하지 못한 오류: {e.Exception}");
        _tray?.Notify("Citrus 오류", e.Exception.Message, Forms.ToolTipIcon.Error);
        e.Handled = true;
    }
}
