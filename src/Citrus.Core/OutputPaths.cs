namespace Citrus.Core;

public static class OutputPaths
{
    /// <summary>
    /// 원본과 같은 폴더에 겹치지 않는 결과 파일 이름을 정하고, 빈 파일을 먼저 만들어 자리를 잡아 둔다.
    /// 같은 이름이 있으면 "이름 (1).확장자", "이름 (2).확장자" 순으로 붙인다.
    /// 여러 변환이 동시에 같은 이름을 고르지 않도록 CreateNew 로 만든다.
    /// </summary>
    public static string Reserve(string sourcePath, string extension)
    {
        var full = Path.GetFullPath(sourcePath);
        var dir = Path.GetDirectoryName(full)!;
        var name = Path.GetFileNameWithoutExtension(full);

        for (var i = 0; i < 10_000; i++)
        {
            var candidate = Path.Combine(dir, i == 0 ? name + extension : $"{name} ({i}){extension}");
            try
            {
                using (new FileStream(candidate, FileMode.CreateNew, FileAccess.Write)) { }
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate))
            {
            }
        }

        throw new IOException($"{name}{extension} 을(를) 저장할 이름을 정하지 못했습니다.");
    }
}
