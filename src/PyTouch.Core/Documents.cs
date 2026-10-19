using System.Security.Cryptography;
using System.Text;

namespace PyTouch.Core;
public sealed class Document
{
    public string? Path { get; set; }
    public string Text { get; set; } = "";
    public string EncodingName { get; set; } = "utf-8";
    public bool Bom { get; set; }
    public string NewLine { get; set; } = "\n";
    public bool Dirty { get; set; }
    public string? DiskHash { get; set; }
    public string Name => Path is null ? "未命名.py" : System.IO.Path.GetFileName(Path);
}

public static class DocumentFiles
{
    static DocumentFiles() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static async Task<Document> OpenAsync(string path, string? encodingName = null)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path);
        Encoding encoding;
        int offset = 0;
        if (encodingName is not null) encoding = Encoding.GetEncoding(encodingName, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) { encoding = new UTF8Encoding(true, true); offset = 3; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, true, true); offset = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, true, true); offset = 2; }
        else encoding = new UTF8Encoding(false, true);
        var text = encoding.GetString(bytes, offset, bytes.Length - offset);
        return new Document { Path = System.IO.Path.GetFullPath(path), Text = text, EncodingName = encoding.WebName, Bom = offset > 0,
            NewLine = text.Contains("\r\n") ? "\r\n" : "\n", DiskHash = Hash(bytes) };
    }
    public static async Task SaveAsync(Document document, string path, bool overwriteExternalChanges = false)
    {
        path = System.IO.Path.GetFullPath(path);
        if (!overwriteExternalChanges && document.DiskHash is not null && string.Equals(path, document.Path, StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(path) || Hash(await File.ReadAllBytesAsync(path)) != document.DiskHash)
                throw new IOException("文件已在外部被修改或删除。请另存为，或重新打开后合并修改。");
        }
        string normalized = document.Text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", document.NewLine);
        Encoding encoding = document.EncodingName == "utf-8" ? new UTF8Encoding(document.Bom, true)
            : Encoding.GetEncoding(document.EncodingName, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        byte[] data = encoding.GetBytes(normalized);
        if (document.Bom) data = encoding.GetPreamble().Concat(data).ToArray();
        string temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, ".pytouch-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, data);
            File.Move(temp, path, true);
            document.Path = path; document.DiskHash = Hash(data); document.Dirty = false;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
