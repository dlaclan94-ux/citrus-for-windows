using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Tangerine.Core;
using static Tangerine.Interop.NativeMethods;

namespace Tangerine.Ui;

/// <summary>
/// 끌고 있는 파일 아래에 뜨는 원형 변환 메뉴. 이 창 자체가 드롭 대상이다.
/// 형식 칸 위에 놓으면 FormatChosen 이 불리고, 가운데나 바깥에 놓으면 아무 일도 없다.
/// </summary>
public partial class RadialMenuWindow : Window
{
    const double Side = 440;
    const double Center = Side / 2;
    const double OuterRadius = 196;
    const double InnerRadius = 64;
    const double GapRadians = 0.02;

    static readonly Brush SegmentFill = Frozen(Color.FromRgb(0xFF, 0xF4, 0xE6));
    static readonly Brush SegmentHoverFill = Frozen(Color.FromRgb(0xFF, 0x8A, 0x00));
    static readonly Brush SegmentStroke = Frozen(Color.FromRgb(0xFF, 0xB8, 0x6B));
    static readonly Brush Ink = Frozen(Color.FromRgb(0x5A, 0x2E, 0x00));
    static readonly Brush SubInk = Frozen(Color.FromRgb(0xA0, 0x62, 0x20));
    static readonly Brush HoverInk = Brushes.White;
    // 투명 창에서 알파 0 인 곳은 마우스·드롭이 통과하므로 눈에 안 보일 만큼만 칠한다
    static readonly Brush HitArea = Frozen(Color.FromArgb(1, 255, 255, 255));

    readonly DispatcherTimer _hideTimer;
    readonly List<Segment> _segments = [];
    readonly Canvas _ring = new();
    readonly TextBlock _centerTitle = CenterText(17, FontWeights.Bold, Ink);
    readonly TextBlock _centerSub = CenterText(11, FontWeights.Normal, SubInk);

    string[] _files = [];
    IReadOnlyList<TargetFormat> _targets = [];
    int _hover = -1;

    /// <summary>파일들과 고른 형식. 드롭 처리가 끝난 뒤에 불린다.</summary>
    public event Action<string[], TargetFormat>? FormatChosen;

    sealed record Segment(Path Shape, TextBlock Label, TextBlock? Hint);

    public RadialMenuWindow()
    {
        InitializeComponent();

        // 놓은 뒤 Drop 이 먼저 처리되도록, 버튼을 떼고 조금 있다가 숨긴다.
        // (바로 숨기면 놓는 자리가 아래 창으로 넘어가 탐색기가 파일을 옮겨 버릴 수 있다)
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _hideTimer.Tick += (_, _) => HideMenu();

        Root.Children.Add(Circle(OuterRadius + 24, HitArea, null));
        _ring.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 3, Opacity = 0.28, Color = Colors.Black };
        Root.Children.Add(_ring);

        var hub = Circle(InnerRadius - 6, Brushes.White, SegmentHoverFill);
        hub.StrokeThickness = 2.5;
        hub.Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 1, Opacity = 0.22, Color = Colors.Black };
        Root.Children.Add(hub);
        Root.Children.Add(_centerTitle);
        Root.Children.Add(_centerSub);

        SetFiles([]);
    }

    /// <summary>실제 화면 픽셀 좌표 (x, y) 를 가운데로 메뉴를 띄운다.</summary>
    public void ShowAt(int x, int y)
    {
        _hideTimer.Stop();
        SetFiles([]);

        if (!IsVisible)
            Show();

        // WPF 좌표(DIP)를 거치지 않고 픽셀로 옮긴다. 다른 DPI 모니터로 옮기면 창 크기가 바뀌므로 한 번 더 맞춘다.
        var hwnd = new WindowInteropHelper(this).Handle;
        for (var i = 0; i < 2; i++)
        {
            GetWindowRect(hwnd, out var r);
            var w = r.Right - r.Left;
            var h = r.Bottom - r.Top;
            SetWindowPos(hwnd, HWND_TOPMOST, x - w / 2, y - h / 2, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        var pop = new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut },
        };
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    /// <summary>끌기가 끝났을 때. 드롭이 처리될 틈을 두고 숨긴다.</summary>
    public void HideSoon()
    {
        if (IsVisible)
            _hideTimer.Start();
    }

    public void HideMenu()
    {
        _hideTimer.Stop();
        if (IsVisible)
            Hide();
        SetFiles([]);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        _hideTimer.Stop();
        var files = ReadFiles(e.Data);
        if (!files.SequenceEqual(_files))
            SetFiles(files);
        UpdateHover(e);
        e.Handled = true;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        UpdateHover(e);
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        SetHover(-1);
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        UpdateHover(e);
        e.Handled = true;

        var index = _hover;
        var files = _files;
        var targets = _targets;
        HideMenu();

        if (index >= 0 && index < targets.Count && files.Length > 0)
        {
            var target = targets[index];
            // 끌어 온 프로그램(탐색기)은 Drop 이 끝나기를 기다리므로 변환은 나중에 시작한다
            Dispatcher.BeginInvoke(() => FormatChosen?.Invoke(files, target));
        }
    }

    void UpdateHover(DragEventArgs e)
    {
        SetHover(HitTest(e.GetPosition(Root)));

        // 반드시 "복사" 로 답한다. "이동" 으로 답하면 탐색기가 원본을 지운다.
        e.Effects = _hover >= 0 && e.AllowedEffects.HasFlag(DragDropEffects.Copy)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    int HitTest(Point p)
    {
        var n = _targets.Count;
        if (n == 0)
            return -1;

        var dx = p.X - Center;
        var dy = p.Y - Center;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < InnerRadius || distance > OuterRadius + 24)
            return -1;

        // 0 = 위쪽, 시계 방향으로 커진다. 칸 0 은 위쪽 가운데에 걸쳐 있다.
        var angle = Math.Atan2(dx, -dy);
        if (angle < 0)
            angle += 2 * Math.PI;
        var step = 2 * Math.PI / n;
        return (int)Math.Floor((angle + step / 2) % (2 * Math.PI) / step) % n;
    }

    void SetFiles(string[] files)
    {
        _files = files;
        _targets = files.Length == 0 ? [] : FormatCatalog.MenuTargets(files);
        _hover = -1;
        BuildSegments();
        UpdateCenter();
    }

    void SetHover(int index)
    {
        if (index == _hover)
            return;

        _hover = index;
        for (var i = 0; i < _segments.Count; i++)
        {
            var on = i == index;
            var s = _segments[i];
            s.Shape.Fill = on ? SegmentHoverFill : SegmentFill;
            s.Label.Foreground = on ? HoverInk : Ink;
            if (s.Hint is not null)
                s.Hint.Foreground = on ? HoverInk : SubInk;
        }
        UpdateCenter();
    }

    void BuildSegments()
    {
        _ring.Children.Clear();
        _segments.Clear();

        var n = _targets.Count;
        if (n == 0)
        {
            // 아직 무엇을 끌고 있는지 모르거나, 바꿀 수 없는 파일일 때: 빈 고리만
            var placeholder = new Path
            {
                Data = Ring(InnerRadius, OuterRadius),
                Fill = SegmentFill,
                Stroke = SegmentStroke,
                StrokeThickness = 1.5,
                StrokeDashArray = [4, 3],
                Opacity = 0.85,
            };
            _ring.Children.Add(placeholder);
            return;
        }

        var step = 2 * Math.PI / n;
        for (var i = 0; i < n; i++)
        {
            var target = _targets[i];
            var mid = i * step;
            var shape = new Path
            {
                Data = n == 1 ? Ring(InnerRadius, OuterRadius) : Sector(mid - step / 2 + GapRadians, mid + step / 2 - GapRadians, InnerRadius, OuterRadius),
                Fill = SegmentFill,
                Stroke = SegmentStroke,
                StrokeThickness = 1.2,
            };
            _ring.Children.Add(shape);

            var labelRadius = (InnerRadius + OuterRadius) / 2 + 6;
            var at = Polar(labelRadius, mid);
            var label = new TextBlock
            {
                Text = target.Label,
                FontSize = n > 8 ? 17 : 20,
                FontWeight = FontWeights.Bold,
                Foreground = Ink,
                TextAlignment = TextAlignment.Center,
                Width = 92,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, at.X - 46);
            Canvas.SetTop(label, at.Y - (target.Hint is null ? 13 : 20));
            _ring.Children.Add(label);

            TextBlock? hint = null;
            if (target.Hint is not null)
            {
                hint = new TextBlock
                {
                    Text = target.Hint,
                    FontSize = 11,
                    Foreground = SubInk,
                    TextAlignment = TextAlignment.Center,
                    Width = 92,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(hint, at.X - 46);
                Canvas.SetTop(hint, at.Y + 4);
                _ring.Children.Add(hint);
            }

            _segments.Add(new Segment(shape, label, hint));
        }
    }

    void UpdateCenter()
    {
        if (_hover >= 0 && _hover < _targets.Count)
        {
            _centerTitle.Text = _targets[_hover].Label;
            _centerSub.Text = "놓으면 변환";
        }
        else if (_files.Length == 0)
        {
            _centerTitle.Text = "Tangerine";
            _centerSub.Text = "형식 위에 놓기";
        }
        else if (_targets.Count == 0)
        {
            _centerTitle.Text = "변환 불가";
            _centerSub.Text = "지원하지 않는 파일";
        }
        else
        {
            _centerTitle.Text = _files.Length == 1 ? "파일 1개" : $"파일 {_files.Length}개";
            _centerSub.Text = "가운데 = 취소";
        }

        PlaceCentered(_centerTitle, Center - 14);
        PlaceCentered(_centerSub, Center + 7);
    }

    static string[] ReadFiles(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop))
            return [];
        return (data.GetData(DataFormats.FileDrop) as string[] ?? []).Where(System.IO.File.Exists).ToArray();
    }

    static Point Polar(double radius, double angle) =>
        new(Center + radius * Math.Sin(angle), Center - radius * Math.Cos(angle));

    static Geometry Sector(double from, double to, double inner, double outer)
    {
        var large = to - from > Math.PI;
        var figure = new PathFigure { StartPoint = Polar(outer, from), IsClosed = true };
        figure.Segments.Add(new ArcSegment(Polar(outer, to), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(Polar(inner, to), true));
        figure.Segments.Add(new ArcSegment(Polar(inner, from), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    static Geometry Ring(double inner, double outer)
    {
        var c = new Point(Center, Center);
        var geometry = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(c, outer, outer), new EllipseGeometry(c, inner, inner));
        geometry.Freeze();
        return geometry;
    }

    static Ellipse Circle(double radius, Brush fill, Brush? stroke)
    {
        var e = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill, Stroke = stroke };
        Canvas.SetLeft(e, Center - radius);
        Canvas.SetTop(e, Center - radius);
        return e;
    }

    static TextBlock CenterText(double size, FontWeight weight, Brush color) => new()
    {
        FontSize = size,
        FontWeight = weight,
        Foreground = color,
        TextAlignment = TextAlignment.Center,
        Width = InnerRadius * 2 - 16,
        TextTrimming = TextTrimming.CharacterEllipsis,
        IsHitTestVisible = false,
    };

    static void PlaceCentered(TextBlock text, double centerY)
    {
        Canvas.SetLeft(text, Center - text.Width / 2);
        Canvas.SetTop(text, centerY - text.FontSize * 0.7);
    }

    static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
