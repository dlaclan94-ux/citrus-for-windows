namespace Tangerine.Core;

/// <summary>형식별 ffmpeg 인자. ProcessStartInfo.ArgumentList 에 하나씩 넣으므로 셸 따옴표 처리는 필요 없다.</summary>
public static class FfmpegArguments
{
    // H.264 + yuv420p 는 가로·세로가 짝수여야 한다 (GIF·세로 영상에서 홀수 크기가 흔함)
    const string EvenSize = "scale=trunc(iw/2)*2:trunc(ih/2)*2";

    // 팔레트를 영상에서 뽑아 GIF 색이 뭉개지지 않게 한다. 너비는 640 까지만.
    const string GifFilter =
        "fps=12,scale='min(640,iw)':-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5";

    static readonly string[] H264 =
    [
        "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p", "-vf", EvenSize,
        "-c:a", "aac", "-b:a", "192k",
    ];

    public static IReadOnlyList<string> Build(string input, string output, string targetId)
    {
        string[] encode = targetId switch
        {
            "mp4" => [.. H264, "-movflags", "+faststart"],
            "mov" => [.. H264, "-movflags", "+faststart"],
            "mkv" => H264,
            // GIF 는 팔레트 색(pal8)이라 VP9 가 받지 못하므로 yuv420p 로 바꿔 넣는다
            "webm" =>
            [
                "-c:v", "libvpx-vp9", "-pix_fmt", "yuv420p", "-crf", "32", "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4",
                "-c:a", "libopus", "-b:a", "128k",
            ],
            "avi" => ["-c:v", "mpeg4", "-q:v", "3", "-tag:v", "XVID", "-c:a", "libmp3lame", "-q:a", "2"],
            "gif" => ["-vf", GifFilter, "-loop", "0", "-an"],
            "mp3" => ["-vn", "-c:a", "libmp3lame", "-q:a", "2"],
            "wav" => ["-vn", "-c:a", "pcm_s16le"],
            "aiff" => ["-vn", "-c:a", "pcm_s16be"],
            "m4a" => ["-vn", "-c:a", "aac", "-b:a", "192k"],
            "flac" => ["-vn", "-c:a", "flac"],
            "ogg" => ["-vn", "-c:a", "libvorbis", "-q:a", "5"],
            "opus" => ["-vn", "-c:a", "libopus", "-b:a", "128k"],
            _ => throw new ArgumentException($"ffmpeg 으로 만들 수 없는 형식입니다: {targetId}", nameof(targetId)),
        };

        // 결과 파일은 OutputPaths.Reserve 가 빈 파일로 먼저 만들어 두므로 -y 로 덮어쓴다
        return ["-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", input, .. encode, output];
    }
}
