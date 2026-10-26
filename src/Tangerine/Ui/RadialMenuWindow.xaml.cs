using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Tangerine.Core;
using static Tangerine.Interop.NativeMethods;
using Drawing = System.Drawing;

namespace Tangerine.Ui;

/// <summary>
/// 끌고 있는 파일 아래에 뜨는 원형 변환 메뉴. 이 창 자체가 드롭 대상이다.
/// 형식 칸 위에 놓으면 FormatChosen 이 불리고, 가운데나 바깥에 놓으면 아무 일도 없다.
/// 모양은 귤 단면: 껍질 같은 유리 테두리 안에 형식마다 귤 조각이 하나씩 있고, 고른 조각만 귤색이 된다.
/// 재질은 Apple Liquid Glass 를 따른다 (뒤가 흐리게 비치는 유리, 흑백 글자, 색은 고른 것에만).
/// </summary>
public partial class RadialMenuWindow : Window
{
    const double Side = 330;
    const double Center = Side / 2;
    const double PeelRadius = 148;    // 껍질 바깥 = 드롭을 받는 영역
    const double SliceOuter = 138;
    const double SliceInner = 52;
    const double HubRadius = 43;      // 가운데 = 취소
    const double SliceGap = 5;
    const double SliceCorner = 11;
    const double PopOut = 5;          // 고른 조각이 바깥으로 밀려 나오는 거리

    static readonly Duration Quick = TimeSpan.FromMilliseconds(160);
    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    readonly DispatcherTimer _hideTimer;
    readonly List<Slice> _slices = [];
    readonly Image _backdrop = new()
    {
        Width = Side,
        Height = Side,
        Clip = new EllipseGeometry(new Point(Center, Center), PeelRadius, PeelRadius),
        IsHitTestVisible = false,
    };
    readonly Ellipse _peel = Circle(PeelRadius);
    readonly Ellipse _peelRim = Circle(PeelRadius - 0.75);
    readonly Canvas _sliceLayer = new();
    readonly Ellipse _hub = Circle(HubRadius);
    readonly Ellipse _hubRim = Circle(HubRadius - 0.75);
    readonly TextBlock _centerTitle = CenterText(12.5, FontWeights.SemiBold);
    readonly TextBlock _centerSub = CenterText(9.5, FontWeights.Normal);

    Palette _palette = Palette.Current();
    string[] _files = [];
    IReadOnlyList<TargetFormat> _targets = [];
    int _hover = -1;

    /// <summary>파일들과 고른 형식. 드롭 처리가 끝난 뒤에 불린다.</summary>
    public event Action<string[], TargetFormat>? FormatChosen;

    sealed record Slice(Path Shape, TranslateTransform Offset, Point Direction, TextBlock Icon, TextBlock Label, TextBlock? Hint);

    public RadialMenuWindow()
    {
        InitializeComponent();

        // 놓은 뒤 Drop 이 먼저 처리되도록, 버튼을 떼고 조금 있다가 숨긴다.
        // (바로 숨기면 놓는 자리가 아래 창으로 넘어가 탐색기가 파일을 옮겨 버릴 수 있다)
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _hideTimer.Tick += (_, _) => HideMenu();

        _peel.StrokeThickness = 1;
        _peelRim.StrokeThickness = _hubRim.StrokeThickness = 1.5;
        _hub.StrokeThickness = 1;

        Root.Children.Add(Shadow(PeelRadius, 14, 0x40));
        Root.Children.Add(_backdrop);
        Root.Children.Add(_peel);
        Root.Children.Add(_peelRim);
        Root.Children.Add(_sliceLayer);
        Root.Children.Add(Shadow(HubRadius, 9, 0x2C));
        Root.Children.Add(_hub);
        Root.Children.Add(_hubRim);
        Root.Children.Add(_centerTitle);
        Root.Children.Add(_centerSub);

        ApplyPalette();
        SetFiles([]);
    }

    /// <summary>실제 화면 픽셀 좌표 (x, y) 를 가운데로 메뉴를 띄운다.</summary>
    public void ShowAt(int x, int y)
    {
        _hideTimer.Stop();
        _palette = Palette.Current(); // 그사이 라이트·다크 모드나 투명 효과 설정이 바뀌었을 수 있다
        ApplyPalette();
        SetFiles([]);

        if (!IsVisible)
        {
            // 메뉴가 뜨기 전에 그 자리 화면을 찍어 두어야 메뉴 자신이 찍히지 않는다
            _backdrop.Source = _palette.Translucent ? Backdrop(x, y) : null;
            Show();
        }

        // WPF 좌표(DIP)를 거치지 않고 픽셀로 옮긴다. 다른 DPI 모니터로 옮기면 창 크기가 바뀌므로 한 번 더 맞춘다.
        var hwnd = new WindowInteropHelper(this).Handle;
        for (var i = 0; i < 2; i++)
        {
            GetWindowRect(hwnd, out var r);
            var w = r.Right - r.Left;
            var h = r.Bottom - r.Top;
            SetWindowPos(hwnd, HWND_TOPMOST, x - w / 2, y - h / 2, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        if (SystemParameters.ClientAreaAnimation)
        {
            var spring = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
            var pop = new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = spring };
            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        }
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
        if (distance < HubRadius || distance > PeelRadius)
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
        BuildSlices();
        UpdateCenter();
    }

    void SetHover(int index)
    {
        if (index == _hover)
            return;

        var previous = _hover;
        _hover = index;
        if (previous >= 0 && previous < _slices.Count)
            Paint(_slices[previous], false);
        if (index >= 0 && index < _slices.Count)
            Paint(_slices[index], true);
        UpdateCenter();
    }

    /// <summary>고른 조각은 귤색으로 칠하고 바깥으로 살짝 밀어낸다.</summary>
    void Paint(Slice s, bool on)
    {
        var p = _palette;
        s.Shape.Fill = on ? p.Flesh : p.Glass;
        s.Icon.Foreground = s.Label.Foreground = on ? p.OnFlesh : p.Ink;
        if (s.Hint is not null)
            s.Hint.Foreground = on ? p.OnFlesh : p.SubInk;

        var distance = on ? PopOut : 0;
        if (SystemParameters.ClientAreaAnimation)
        {
            var ease = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
            s.Offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(s.Direction.X * distance, Quick) { EasingFunction = ease });
            s.Offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(s.Direction.Y * distance, Quick) { EasingFunction = ease });
        }
        else
        {
            s.Offset.X = s.Direction.X * distance;
            s.Offset.Y = s.Direction.Y * distance;
        }
    }

    void BuildSlices()
    {
        _sliceLayer.Children.Clear();
        _slices.Clear();

        var n = _targets.Count;
        if (n == 0)
        {
            // 아직 무엇을 끄는지 모르거나 바꿀 수 없는 파일일 때도 귤 모양은 보이도록 빈 조각을 깐다
            const int blank = 8;
            for (var i = 0; i < blank; i++)
            {
                var mid = i * 2 * Math.PI / blank;
                _sliceLayer.Children.Add(new Path
                {
                    Data = SliceGeometry(mid - Math.PI / blank, mid + Math.PI / blank),
                    Fill = _palette.Glass,
                    Stroke = _palette.SliceEdge,
                    StrokeThickness = 1,
                    Opacity = 0.7,
                    IsHitTestVisible = false,
                });
            }
            return;
        }

        var step = 2 * Math.PI / n;
        var textWidth = Math.Min(70, 2 * Math.PI * 95 / n - 8);
        var labelRadius = (SliceInner + SliceOuter) / 2 + 2;
        for (var i = 0; i < n; i++)
        {
            var target = _targets[i];
            var mid = i * step;
            var offset = new TranslateTransform();

            // 조각과 그 위 글자를 한 묶음으로 움직인다
            var group = new Canvas { RenderTransform = offset, IsHitTestVisible = false };
            var shape = new Path
            {
                Data = n == 1 ? RoundedRing() : SliceGeometry(mid - step / 2, mid + step / 2),
                Stroke = _palette.SliceEdge,
                StrokeThickness = 1,
            };
            group.Children.Add(shape);

            var at = Polar(labelRadius, mid);
            var hasHint = target.Hint is not null;
            var icon = Text(IconOf(target), 17, FontWeights.Normal, textWidth);
            icon.FontFamily = IconFont;
            Place(icon, at, hasHint ? -27 : -21);
            group.Children.Add(icon);

            var label = Text(target.Label, n > 9 ? 11.5 : 12.5, FontWeights.Bold, textWidth);
            Place(label, at, hasHint ? -5 : 1);
            group.Children.Add(label);

            TextBlock? hint = null;
            if (hasHint)
            {
                hint = Text(target.Hint!, 9.5, FontWeights.Normal, textWidth);
                Place(hint, at, 11);
                group.Children.Add(hint);
            }

            _sliceLayer.Children.Add(group);
            // 조각이 하나(고리 전체)면 밀어낼 방향이 없다
            var direction = n == 1 ? new Point(0, 0) : new Point(Math.Sin(mid), -Math.Cos(mid));
            var slice = new Slice(shape, offset, direction, icon, label, hint);
            _slices.Add(slice);
            Paint(slice, false);
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
            _centerSub.Text = "지원 안 함";
        }
        else
        {
            _centerTitle.Text = _files.Length == 1 ? "파일 1개" : $"파일 {_files.Length}개";
            _centerSub.Text = "여기 = 취소";
        }

        PlaceCentered(_centerTitle, Center - 7);
        PlaceCentered(_centerSub, Center + 9);
    }

    void ApplyPalette()
    {
        var p = _palette;
        _peel.Fill = p.Peel;
        _peel.Stroke = p.Edge;
        _peelRim.Stroke = p.Rim;
        _hub.Fill = p.HubGlass;
        _hub.Stroke = p.Edge;
        _hubRim.Stroke = p.Rim;
        _centerTitle.Foreground = p.Ink;
        _centerSub.Foreground = p.SubInk;
    }

    /// <summary>바꿀 결과가 어떤 종류인지 보여 주는 아이콘 (Segoe Fluent Icons).</summary>
    static string IconOf(TargetFormat target) => target.Id switch
    {
        "zip" => "",
        "pdf" => "",
        "mp4" or "mov" or "mkv" or "webm" or "avi" => "",
        "mp3" or "wav" or "m4a" or "flac" or "ogg" or "opus" or "aiff" => "",
        _ => "",
    };

    /// <summary>
    /// 둥근 모서리의 귤 조각. 조각 사이 틈은 어느 반지름에서나 같은 너비가 되도록 옆선을 평행하게 민 뒤,
    /// 그만큼 작게 만든 모양을 둥근 펜으로 부풀려 모서리를 둥글린다.
    /// </summary>
    static Geometry SliceGeometry(double from, double to)
    {
        const double inset = SliceGap / 2 + SliceCorner;
        const double inner = SliceInner + SliceCorner;
        const double outer = SliceOuter - SliceCorner;
        var outerSpread = Math.Asin(inset / outer);
        var innerSpread = Math.Asin(inset / inner);

        var figure = new PathFigure { StartPoint = Polar(outer, from + outerSpread), IsClosed = true };
        var outerSweep = to - from - 2 * outerSpread;
        var innerSweep = to - from - 2 * innerSpread;
        figure.Segments.Add(new ArcSegment(Polar(outer, to - outerSpread), new Size(outer, outer), 0, outerSweep > Math.PI, SweepDirection.Clockwise, true));
        if (innerSweep > 0)
        {
            figure.Segments.Add(new LineSegment(Polar(inner, to - innerSpread), true));
            figure.Segments.Add(new ArcSegment(Polar(inner, from + innerSpread), new Size(inner, inner), 0, innerSweep > Math.PI, SweepDirection.Counterclockwise, true));
        }
        else
        {
            // 조각이 많아 안쪽 끝이 뾰족해지는 경우: 옆선이 만나는 점 하나로 닫는다
            figure.Segments.Add(new LineSegment(Polar(inset / Math.Sin((to - from) / 2), (from + to) / 2), true));
        }

        var core = new PathGeometry([figure]);
        var round = new Pen(Brushes.Black, 2 * SliceCorner) { LineJoin = PenLineJoin.Round };
        var slice = Geometry.Combine(core, core.GetWidenedPathGeometry(round), GeometryCombineMode.Union, null);
        slice.Freeze();
        return slice;
    }

    static Geometry RoundedRing()
    {
        var c = new Point(Center, Center);
        var ring = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(c, SliceOuter, SliceOuter), new EllipseGeometry(c, SliceInner, SliceInner));
        ring.Freeze();
        return ring;
    }

    /// <summary>
    /// 메뉴가 뜰 자리 (x, y) 의 화면을 찍어 흐리게 만든 유리 뒤 배경.
    /// 투명 창(레이어드 창)에는 Windows 의 창 뒤 흐림을 원 모양으로 자를 수 없어 직접 만든다.
    /// 끄는 동안 뒤 화면은 거의 그대로이므로 한 번 찍은 것으로 충분하다.
    /// </summary>
    static BitmapSource? Backdrop(int x, int y)
    {
        const double margin = 40; // 가장자리까지 고르게 흐리도록 흐림 반경보다 넓게 찍는다
        try
        {
            var scale = ScaleAt(x, y);
            var px = (int)Math.Round((Side + 2 * margin) * scale);
            using var shot = new Drawing.Bitmap(px, px);
            using (var g = Drawing.Graphics.FromImage(shot))
                g.CopyFromScreen(x - px / 2, y - px / 2, 0, 0, shot.Size);

            var hbitmap = shot.GetHbitmap();
            BitmapSource source;
            try
            {
                source = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hbitmap);
            }

            var image = new Image
            {
                Source = source,
                Width = Side + 2 * margin,
                Height = Side + 2 * margin,
                Effect = new BlurEffect { Radius = 24, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
            };
            var host = new Canvas { Width = Side, Height = Side, ClipToBounds = true };
            Canvas.SetLeft(image, -margin);
            Canvas.SetTop(image, -margin);
            host.Children.Add(image);
            host.Measure(new Size(Side, Side));
            host.Arrange(new Rect(0, 0, Side, Side));

            // 한 번 그려 굳혀 두어야 튀어나오는 애니메이션 동안 흐림을 다시 계산하지 않는다
            var size = (int)Math.Round(Side * scale);
            var blurred = new RenderTargetBitmap(size, size, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            blurred.Render(host);
            blurred.Freeze();
            return blurred;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ExternalException or ArgumentException)
        {
            // 잠금 화면·UAC 화면처럼 찍을 수 없는 때는 흐림 없이 유리색만 보여 준다
            return null;
        }
    }

    /// <summary>원이 아래 화면 위에 떠 있어 보이게 하는 부드러운 그림자. 효과(Effect) 대신 그라데이션이라 가볍다.</summary>
    static Ellipse Shadow(double radius, double spread, byte alpha)
    {
        var shadow = Circle(radius + spread);
        var fill = new RadialGradientBrush(
        [
            new GradientStop(Color.FromArgb(alpha, 0, 0, 0), radius / (radius + spread) - 0.02),
            new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1),
        ]) { Center = new Point(0.5, 0.53), GradientOrigin = new Point(0.5, 0.53) };
        fill.Freeze();
        shadow.Fill = fill;
        shadow.IsHitTestVisible = false;
        return shadow;
    }

    static string[] ReadFiles(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop))
            return [];
        // 폴더도 받는다 (압축할 수 있으므로)
        return (data.GetData(DataFormats.FileDrop) as string[] ?? []).Where(p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p)).ToArray();
    }

    static Point Polar(double radius, double angle) =>
        new(Center + radius * Math.Sin(angle), Center - radius * Math.Cos(angle));

    static Ellipse Circle(double radius)
    {
        var e = new Ellipse { Width = radius * 2, Height = radius * 2, IsHitTestVisible = false };
        Canvas.SetLeft(e, Center - radius);
        Canvas.SetTop(e, Center - radius);
        return e;
    }

    static TextBlock Text(string text, double size, FontWeight weight, double width) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        TextAlignment = TextAlignment.Center,
        Width = width,
        IsHitTestVisible = false,
    };

    static void Place(TextBlock text, Point at, double dy)
    {
        Canvas.SetLeft(text, at.X - text.Width / 2);
        Canvas.SetTop(text, at.Y + dy);
    }

    static TextBlock CenterText(double size, FontWeight weight) => new()
    {
        FontSize = size,
        FontWeight = weight,
        TextAlignment = TextAlignment.Center,
        Width = HubRadius * 2 - 10,
        TextTrimming = TextTrimming.CharacterEllipsis,
        IsHitTestVisible = false,
    };

    static void PlaceCentered(TextBlock text, double centerY)
    {
        Canvas.SetLeft(text, Center - text.Width / 2);
        Canvas.SetTop(text, centerY - text.FontSize * 0.7);
    }

    /// <summary>Windows 의 라이트·다크 모드, 투명 효과, 고대비 설정에 맞춘 색.</summary>
    /// <param name="Peel">껍질 (조각 사이 틈과 바깥 테두리로 보이는 유리)</param>
    /// <param name="Glass">고르지 않은 조각</param>
    /// <param name="Flesh">고른 조각 (귤색)</param>
    sealed record Palette(bool Translucent, Brush Peel, Brush Glass, Brush HubGlass, Brush Edge, Brush Rim, Brush Flesh, Brush Ink, Brush SubInk, Brush OnFlesh)
    {
        /// <summary>조각 윤곽. 라이트 모드에서는 흰 빛 테두리가 흰 조각에 묻히므로 옅은 어두운 선을 쓴다.</summary>
        public Brush SliceEdge { get; init; } = Rim;

        public static Palette Current()
        {
            if (SystemParameters.HighContrast)
            {
                return new Palette(false, SystemColors.WindowBrush, SystemColors.WindowBrush, SystemColors.WindowBrush,
                    SystemColors.WindowTextBrush, SystemColors.WindowTextBrush, SystemColors.HighlightBrush,
                    SystemColors.WindowTextBrush, SystemColors.GrayTextBrush, SystemColors.HighlightTextBrush);
            }

            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return For(light: key?.GetValue("AppsUseLightTheme") is not 0, translucent: key?.GetValue("EnableTransparency") is not 0);
        }

        static Palette For(bool light, bool translucent)
        {
            // 투명 효과를 끈 사람에게는 거의 불투명한 판을 보여 준다 (Liquid Glass 의 "투명도 줄이기" 대응)
            byte A(byte glassy) => translucent ? glassy : (byte)0xFF;

            var flesh = Vertical(Color.FromRgb(0xFF, 0x9F, 0x43), Color.FromRgb(0xFF, 0x62, 0x1A));
            var white = Solid(Colors.White);
            return light
                ? new Palette(translucent,
                    // 흰 화면 위에서도 조각 사이 틈이 보이도록 껍질은 옅은 회색 유리
                    Solid(Color.FromArgb(A(0x80), 0xC4, 0xC4, 0xCA)),
                    Vertical(Color.FromArgb(A(0xD6), 0xFF, 0xFF, 0xFF), Color.FromArgb(A(0xAD), 0xFB, 0xF8, 0xF5)),
                    Solid(Color.FromArgb(A(0xE6), 0xFF, 0xFF, 0xFF)),
                    Solid(Color.FromArgb(0x30, 0, 0, 0)),
                    Vertical(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                    flesh,
                    Solid(Color.FromRgb(0x2C, 0x2C, 0x2E)), Solid(Color.FromRgb(0x6E, 0x6E, 0x73)), white)
                {
                    SliceEdge = Solid(Color.FromArgb(0x1F, 0, 0, 0)),
                }
                : new Palette(translucent,
                    Solid(Color.FromArgb(A(0x73), 0x10, 0x10, 0x12)),
                    Vertical(Color.FromArgb(A(0xC2), 0x48, 0x48, 0x4E), Color.FromArgb(A(0xB0), 0x30, 0x30, 0x35)),
                    Solid(Color.FromArgb(A(0xE6), 0x2C, 0x2C, 0x30)),
                    Solid(Color.FromArgb(0x80, 0, 0, 0)),
                    Vertical(Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
                    flesh,
                    Solid(Color.FromRgb(0xF5, 0xF5, 0xF7)), Solid(Color.FromRgb(0xAE, 0xAE, 0xB2)), white);
        }

        /// <summary>위에서 아래로. 유리에 빛이 위에서 비치는 느낌.</summary>
        static Brush Vertical(Color top, Color bottom)
        {
            var brush = new LinearGradientBrush(top, bottom, 90);
            brush.Freeze();
            return brush;
        }

        static SolidColorBrush Solid(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
