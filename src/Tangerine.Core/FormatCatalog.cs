namespace Tangerine.Core;

public enum MediaKind
{
    Image,
    Video,
    Audio,
    Document,
    Subtitle,
}

public enum ConversionEngine
{
    /// <summary>Magick.NET (이미지)</summary>
    Magick,

    /// <summary>ffmpeg.exe (영상·오디오, GIF ↔ 영상)</summary>
    Ffmpeg,

    /// <summary>설치된 한글·Word·Excel·PowerPoint 자동화 (문서 → PDF)</summary>
    Document,

    /// <summary>끌어 온 파일·폴더를 모두 묶어 압축 파일 하나를 만든다 (반디집)</summary>
    Archive,

    /// <summary>Windows 기본 PDF 렌더러로 PDF 를 쪽마다 그림으로</summary>
    PdfPages,

    /// <summary>끌어 온 PDF 를 모두 합쳐 PDF 하나를 만든다 (PDFsharp)</summary>
    PdfMerge,

    /// <summary>PDF 를 쪽마다 따로 된 PDF 로 나눈다 (PDFsharp)</summary>
    PdfSplit,

    /// <summary>PowerPoint 로 슬라이드마다 그림 한 장</summary>
    Slides,

    /// <summary>Excel 로 시트 ↔ CSV</summary>
    Spreadsheet,

    /// <summary>자막 SRT ↔ VTT, 글자만 TXT (SubtitleConverter)</summary>
    Subtitle,

    /// <summary>끌어 온 파일·폴더의 전체 경로를 클립보드에</summary>
    CopyPath,
}

/// <param name="Id">형식 식별자. 원형 메뉴에서 고른 칸을 파일마다 다시 찾을 때 쓴다.</param>
/// <param name="Label">원형 메뉴에 크게 쓰는 이름</param>
/// <param name="Extension">점을 포함한 소문자 확장자</param>
/// <param name="Hint">이름 아래 작은 설명 (예: "소리만")</param>
public sealed record TargetFormat(string Id, string Label, string Extension, ConversionEngine Engine, string? Hint = null);

/// <summary>어떤 파일을 어떤 형식으로 바꿀 수 있는지 정한다.</summary>
public static class FormatCatalog
{
    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpeg"] = ".jpg",
        [".jpe"] = ".jpg",
        [".jfif"] = ".jpg",
        [".tif"] = ".tiff",
        [".heif"] = ".heic",
        [".aif"] = ".aiff",
        [".oga"] = ".ogg",
    };

    static readonly Dictionary<string, MediaKind> Kinds = BuildKinds();

    public static readonly IReadOnlyList<TargetFormat> ImageTargets =
    [
        Image("jpg", "JPG"),
        Image("png", "PNG"),
        Image("webp", "WEBP"),
        Image("avif", "AVIF"),
        Image("gif", "GIF"),
        Image("bmp", "BMP"),
        Image("tiff", "TIFF"),
        Image("ico", "ICO", "아이콘"),
        Image("pdf", "PDF"),
    ];

    public static readonly IReadOnlyList<TargetFormat> VideoTargets =
    [
        Media("mp4", "MP4"),
        Media("mov", "MOV"),
        Media("mkv", "MKV"),
        Media("webm", "WEBM"),
        Media("avi", "AVI"),
        Media("gif", "GIF", "움짤"),
        Media("mp3", "MP3", "소리만"),
        Media("wav", "WAV", "소리만"),
        Media("m4a", "M4A", "소리만"),
    ];

    public static readonly IReadOnlyList<TargetFormat> AudioTargets =
    [
        Media("mp3", "MP3"),
        Media("wav", "WAV"),
        Media("m4a", "M4A"),
        Media("flac", "FLAC"),
        Media("ogg", "OGG"),
        Media("opus", "OPUS"),
        Media("aiff", "AIFF"),
    ];

    public static readonly IReadOnlyList<TargetFormat> DocumentTargets =
    [
        new("pdf", "PDF", ".pdf", ConversionEngine.Document),
    ];

    /// <summary>PowerPoint: PDF 와 슬라이드마다 그림.</summary>
    public static readonly IReadOnlyList<TargetFormat> SlideTargets =
    [
        .. DocumentTargets,
        new("jpg", "JPG", ".jpg", ConversionEngine.Slides, "장마다"),
        new("png", "PNG", ".png", ConversionEngine.Slides, "장마다"),
    ];

    /// <summary>Excel: PDF 와 시트마다 CSV.</summary>
    public static readonly IReadOnlyList<TargetFormat> SheetTargets =
    [
        .. DocumentTargets,
        new("csv", "CSV", ".csv", ConversionEngine.Spreadsheet, "시트마다"),
    ];

    public static readonly IReadOnlyList<TargetFormat> CsvTargets =
    [
        new("xlsx", "XLSX", ".xlsx", ConversionEngine.Spreadsheet, "엑셀"),
    ];

    public static readonly IReadOnlyList<TargetFormat> SubtitleTargets =
    [
        new("srt", "SRT", ".srt", ConversionEngine.Subtitle),
        new("vtt", "VTT", ".vtt", ConversionEngine.Subtitle),
        new("txt", "TXT", ".txt", ConversionEngine.Subtitle, "대사만"),
    ];

    /// <summary>PDF 는 쪽마다 그림 한 장씩.</summary>
    public static readonly IReadOnlyList<TargetFormat> PdfTargets =
    [
        new("split", "나누기", ".pdf", ConversionEngine.PdfSplit, "쪽마다 PDF"),
        new("jpg", "JPG", ".jpg", ConversionEngine.PdfPages, "쪽마다"),
        new("png", "PNG", ".png", ConversionEngine.PdfPages, "쪽마다"),
    ];

    /// <summary>PDF 만 2개 이상 끌어 왔을 때 메뉴 첫 칸에 나오는 합치기.</summary>
    public static readonly TargetFormat PdfMerge = new("merge", "합치기", ".pdf", ConversionEngine.PdfMerge, "PDF 하나로");

    /// <summary>어떤 파일·폴더든 메뉴 끝에서 두 번째 칸에 나오는 압축.</summary>
    public static readonly TargetFormat Zip = new("zip", "ZIP", ".zip", ConversionEngine.Archive, "압축");

    /// <summary>어떤 파일·폴더든 메뉴 마지막 칸에 나오는 경로 복사.</summary>
    public static readonly TargetFormat CopyPath = new("path", "경로", "", ConversionEngine.CopyPath, "복사");

    /// <summary>GIF 는 이미지이면서 짧은 영상이기도 해서 영상으로도 바꿀 수 있다.</summary>
    static readonly IReadOnlyList<TargetFormat> GifExtras =
    [
        Media("mp4", "MP4", "영상"),
        Media("webm", "WEBM", "영상"),
    ];

    /// <summary>확장자를 소문자로, 같은 형식의 다른 이름(.jpeg → .jpg 등)은 하나로 맞춘다.</summary>
    public static string NormalizeExtension(string extension)
    {
        var ext = extension.ToLowerInvariant();
        return Aliases.TryGetValue(ext, out var alias) ? alias : ext;
    }

    public static MediaKind? KindOf(string path) =>
        Kinds.TryGetValue(NormalizeExtension(Path.GetExtension(path)), out var kind) ? kind : null;

    /// <summary>파일 하나가 바뀔 수 있는 모든 형식 (지금과 같은 형식도 포함).</summary>
    public static IReadOnlyList<TargetFormat> TargetsFor(string path)
    {
        var ext = NormalizeExtension(Path.GetExtension(path));
        return KindOf(path) switch
        {
            MediaKind.Image when ext == ".gif" => [.. ImageTargets, .. GifExtras],
            MediaKind.Image => ImageTargets,
            MediaKind.Video => VideoTargets,
            MediaKind.Audio => AudioTargets,
            MediaKind.Document when ext == ".pdf" => PdfTargets,
            MediaKind.Document when ext is ".ppt" or ".pptx" => SlideTargets,
            MediaKind.Document when ext is ".xls" or ".xlsx" => SheetTargets,
            MediaKind.Document when ext == ".csv" => CsvTargets,
            MediaKind.Document => DocumentTargets,
            MediaKind.Subtitle => SubtitleTargets,
            _ => [],
        };
    }

    /// <summary>이미 그 형식이라 바꿀 필요가 없는지. 나누기·합치기는 PDF → PDF 라도 할 일이 있다.</summary>
    public static bool IsSameFormat(string path, TargetFormat target) =>
        target.Engine is not (ConversionEngine.PdfSplit or ConversionEngine.PdfMerge)
        && NormalizeExtension(Path.GetExtension(path)) == target.Extension;

    /// <summary>
    /// 여러 파일을 한꺼번에 끌어 왔을 때 원형 메뉴에 보여 줄 형식.
    /// 모든 파일이 바뀔 수 있는 형식만 남기고, 모든 파일이 이미 그 형식이면 뺀다.
    /// (JPG 와 PNG 를 같이 끌어 와 JPG 를 고르면 PNG 만 바뀌고 JPG 는 건너뛴다.)
    /// 무엇을 끌어 오든 마지막 두 칸은 압축(ZIP) → 경로 복사다. 폴더가 섞이면 이 둘만 할 수 있다.
    /// PDF 만 여러 개면 합치기가 첫 칸에 붙는다.
    /// </summary>
    public static IReadOnlyList<TargetFormat> MenuTargets(IEnumerable<string> paths)
    {
        var files = paths.ToList();
        if (files.Count == 0)
            return [];
        if (files.Any(Directory.Exists))
            return [Zip, CopyPath];

        IEnumerable<TargetFormat> common = TargetsFor(files[0]);
        foreach (var file in files.Skip(1))
        {
            var ids = TargetsFor(file).Select(t => t.Id).ToHashSet();
            common = common.Where(t => ids.Contains(t.Id));
        }

        var targets = common.Where(t => !files.All(f => IsSameFormat(f, t)));
        return files.Count > 1 && files.All(f => NormalizeExtension(Path.GetExtension(f)) == ".pdf")
            ? [PdfMerge, .. targets, Zip, CopyPath]
            : [.. targets, Zip, CopyPath];
    }

    /// <summary>원형 메뉴에서 고른 형식을 이 파일에 맞는 변환 방법으로 다시 찾는다.</summary>
    public static TargetFormat? Resolve(string path, string targetId) =>
        TargetsFor(path).FirstOrDefault(t => t.Id == targetId);

    static TargetFormat Image(string id, string label, string? hint = null) =>
        new(id, label, "." + id, ConversionEngine.Magick, hint);

    static TargetFormat Media(string id, string label, string? hint = null) =>
        new(id, label, "." + id, ConversionEngine.Ffmpeg, hint);

    static Dictionary<string, MediaKind> BuildKinds()
    {
        var kinds = new Dictionary<string, MediaKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in new[] { ".jpg", ".png", ".webp", ".heic", ".avif", ".gif", ".bmp", ".tiff", ".ico", ".svg" })
            kinds[ext] = MediaKind.Image;
        foreach (var ext in new[] { ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".webm", ".wmv", ".flv", ".mpg", ".mpeg", ".3gp", ".ts", ".mts", ".m2ts" })
            kinds[ext] = MediaKind.Video;
        foreach (var ext in new[] { ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wma", ".aiff" })
            kinds[ext] = MediaKind.Audio;
        foreach (var ext in new[] { ".hwp", ".hwpx", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".pdf", ".csv" })
            kinds[ext] = MediaKind.Document;
        foreach (var ext in new[] { ".srt", ".vtt" })
            kinds[ext] = MediaKind.Subtitle;
        return kinds;
    }
}
