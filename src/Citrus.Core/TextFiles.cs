using System.Text;

namespace Citrus.Core;

public static class TextFiles
{
    /// <summary>글자 파일을 읽는다. BOM 이 있으면 그대로, 없으면 UTF-8 로 읽어 보고 깨지면 한국어 Windows 기본(CP949)으로.</summary>
    public static string Read(string path) => Decode(File.ReadAllBytes(path));

    public static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        try
        {
            return reader.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(949).GetString(bytes);
        }
    }
}
