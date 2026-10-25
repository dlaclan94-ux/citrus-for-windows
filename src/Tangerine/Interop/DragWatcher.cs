using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using static Tangerine.Interop.NativeMethods;

namespace Tangerine.Interop;

/// <summary>
/// 전역 저수준 마우스 훅으로 "왼쪽 버튼을 누른 채 끄는 중 + Shift" 를 알아챈다.
/// 탐색기의 끌어서 놓기(OLE) 중에도 훅은 마우스 입력을 그대로 받는다.
/// 훅 콜백은 빨리 돌아가야 하므로 (늦으면 Windows 가 훅을 떼어 냄) 화면 작업은 BeginInvoke 로 미룬다.
/// </summary>
internal sealed class DragWatcher : IDisposable
{
    // 파일을 끌어 올 수 있는 탐색기 계열 최상위 창: 탐색기, 바탕화면, 열기/저장 대화상자
    static readonly HashSet<string> ExplorerClasses = new(StringComparer.Ordinal)
    {
        "CabinetWClass", "ExploreWClass", "Progman", "WorkerW", "#32770",
    };

    readonly Dispatcher _dispatcher;
    readonly LowLevelMouseProc _proc; // GC 가 콜백을 치우지 않도록 붙잡아 둔다
    readonly DispatcherTimer _shiftPoll;
    readonly uint _ownProcessId = (uint)Environment.ProcessId;
    IntPtr _hook;

    bool _leftDown;
    bool _dragging;
    bool _fired;
    bool _eligible;
    POINT _downAt;

    public bool Enabled { get; set; } = true;

    /// <summary>true 면 탐색기·바탕화면에서 시작한 끌기에만 반응한다 (글자 선택 등 다른 Shift+끌기와 섞이지 않게).</summary>
    public bool ExplorerOnly { get; set; } = true;

    /// <summary>원형 메뉴를 띄울 때. 인자는 실제 화면 픽셀 좌표.</summary>
    public event Action<int, int>? ShiftDragStarted;

    /// <summary>메뉴를 띄운 끌기가 끝났을 때 (마우스 버튼을 뗌).</summary>
    public event Action? DragEnded;

    public DragWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _proc = HookProc;

        // 끄는 도중 마우스를 멈춘 채 Shift 만 눌러도 메뉴가 뜨도록 잠깐씩 키 상태를 본다
        _shiftPoll = new DispatcherTimer(DispatcherPriority.Input, dispatcher) { Interval = TimeSpan.FromMilliseconds(40) };
        _shiftPoll.Tick += (_, _) => Poll();

        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "마우스 훅을 설치하지 못했습니다.");
    }

    public void Dispose()
    {
        _shiftPoll.Stop();
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            switch ((int)wParam)
            {
                case WM_LBUTTONDOWN:
                    _leftDown = true;
                    _dragging = false;
                    _fired = false;
                    _downAt = info.pt;
                    _eligible = IsEligibleSource(info.pt);
                    break;

                case WM_MOUSEMOVE when _leftDown:
                    if (!_dragging && MovedPastDragThreshold(info.pt))
                    {
                        _dragging = true;
                        _shiftPoll.Start();
                    }
                    if (_dragging)
                        TryFire();
                    break;

                case WM_LBUTTONUP:
                    EndDrag();
                    break;
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    void Poll()
    {
        // 버튼 뗌 메시지를 놓쳤을 때를 대비
        if (!IsKeyDown(VK_LBUTTON))
        {
            EndDrag();
            return;
        }
        TryFire();
    }

    void TryFire()
    {
        if (_fired || !_dragging || !_eligible || !Enabled || !IsKeyDown(VK_SHIFT))
            return;

        _fired = true;
        GetCursorPos(out var p);
        _dispatcher.BeginInvoke(() => ShiftDragStarted?.Invoke(p.X, p.Y));
    }

    void EndDrag()
    {
        var wasFired = _fired;
        _leftDown = false;
        _dragging = false;
        _fired = false;
        _shiftPoll.Stop();
        if (wasFired)
            _dispatcher.BeginInvoke(() => DragEnded?.Invoke());
    }

    bool MovedPastDragThreshold(POINT p) =>
        Math.Abs(p.X - _downAt.X) >= GetSystemMetrics(SM_CXDRAG) ||
        Math.Abs(p.Y - _downAt.Y) >= GetSystemMetrics(SM_CYDRAG);

    bool IsEligibleSource(POINT p)
    {
        var root = GetAncestor(WindowFromPoint(p), GA_ROOT);
        if (root == IntPtr.Zero)
            return false;

        GetWindowThreadProcessId(root, out var pid);
        if (pid == _ownProcessId)
            return false;

        if (!ExplorerOnly)
            return true;

        var name = new StringBuilder(64);
        GetClassName(root, name, name.Capacity);
        return ExplorerClasses.Contains(name.ToString());
    }
}
