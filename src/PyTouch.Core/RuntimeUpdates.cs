using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PyTouch.Core;
public sealed class RuntimeUpdates(string dataDirectory)
{
    static readonly HttpClient Client = new(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(5) };
    public static readonly string[] ToolPackages = ["jedi", "debugpy", "mpremote", "ruff"];
    public static string BundledDirectory => Path.Combine(AppContext.BaseDirectory, "runtime", "python");
    public async Task<string?> EnsureBundledAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        string manifest = Path.Combine(BundledDirectory, "pytouch-runtime.json");
        if (!File.Exists(manifest)) return null;
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifest, ct));
        string version = json.RootElement.GetProperty("version").GetString()!;
        int revision = json.RootElement.TryGetProperty("revision", out var r) ? r.GetInt32() : 1;
        string target = Path.Combine(dataDirectory, "runtimes", version + "-r" + revision);
        if (File.Exists(Path.Combine(target, "pytouch-runtime.json"))) return Path.Combine(target, "python.exe");
        progress?.Report("正在准备内置 Python " + version + "…");
        string staging = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(staging);
        try
        {
            await Task.Run(() =>
            {
                foreach (string file in Directory.EnumerateFiles(BundledDirectory, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(BundledDirectory, file);
                    if (relative == "pytouch-runtime.json") continue;
                    var destination = Path.Combine(staging, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination);
                }
            }, ct);
            File.Copy(manifest, Path.Combine(staging, "pytouch-runtime.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (Directory.Exists(target)) throw new IOException("运行环境目录不完整，请先关闭其他 PyTouch 实例再重试。");
            await PublishDirectoryAsync(staging, target, ct);
            return Path.Combine(target, "python.exe");
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    public async Task<string?> CheckPythonAsync(CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await Client.GetStringAsync("https://api.nuget.org/v3-flatcontainer/python/index.json", ct));
        return doc.RootElement.GetProperty("versions").EnumerateArray().Select(x => x.GetString()!)
            .Where(x => x.StartsWith("3.13.") && Version.TryParse(x, out _)).OrderByDescending(Version.Parse).FirstOrDefault();
    }
    public async Task<string> DownloadPythonAsync(string version, IProgress<string>? progress, CancellationToken ct = default)
    {
        if (!Version.TryParse(version, out _) || !version.StartsWith("3.13.")) throw new InvalidDataException("仅支持稳定版 Python 3.13 更新。");
        string target = Path.Combine(dataDirectory, "runtimes", version);
        if (File.Exists(Path.Combine(target, "pytouch-runtime.json"))) return Path.Combine(target, "python.exe");
        string staging = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(staging);
        string url = $"https://api.nuget.org/v3-flatcontainer/python/{version}/python.{version}.nupkg";
        try
        {
            progress?.Report("下载 Python " + version + "…");
            var bytes = await Client.GetByteArrayAsync(url, ct);
            using var registration = JsonDocument.Parse(await Client.GetStringAsync($"https://api.nuget.org/v3/registration5-gz-semver2/python/{version}.json", ct));
            string catalogUrl = registration.RootElement.GetProperty("catalogEntry").GetString()!;
            if (!catalogUrl.StartsWith("https://api.nuget.org/", StringComparison.Ordinal)) throw new InvalidDataException("无效更新来源。");
            using var catalog = JsonDocument.Parse(await Client.GetStringAsync(catalogUrl, ct));
            if (catalog.RootElement.GetProperty("packageHashAlgorithm").GetString() != "SHA512") throw new InvalidDataException("不支持的校验算法。");
            var expected = catalog.RootElement.GetProperty("packageHash").GetString();
            if (Convert.ToBase64String(SHA512.HashData(bytes)) != expected) throw new InvalidDataException("下载校验失败，未安装。");
            using var zip = new ZipArchive(new MemoryStream(bytes));
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith("tools/") || entry.FullName.EndsWith('/')) continue;
                string path = Path.GetFullPath(Path.Combine(staging, entry.FullName[6..]));
                if (!path.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("下载包含非法路径。");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var input = entry.Open(); await using var output = File.Create(path); await input.CopyToAsync(output, ct);
            }
            progress?.Report("获取并验证 Python Tcl/Tk 绘图组件…");
            string msi = Path.Combine(staging, "tcltk.msi");
            await File.WriteAllBytesAsync(msi, await Client.GetByteArrayAsync($"https://www.python.org/ftp/python/{version}/amd64/tcltk.msi", ct), ct);
            Authenticode.VerifyPythonInstaller(msi);
            string cab = Path.Combine(staging, ".tcltk-extract");
            await using (var extraction = new ProcessSession())
            {
                int exit = await extraction.RunAsync(Path.Combine(staging, "python.exe"), [Path.Combine(AppContext.BaseDirectory, "tools", "extract_tcltk.py"), msi, staging, cab], staging, cancellationToken: ct);
                if (exit != 0) throw new IOException("Tcl/Tk 提取失败，原环境未改变。");
            }
            File.Delete(msi); Directory.Delete(cab, true);
            await File.WriteAllTextAsync(Path.Combine(staging, "pytouch-runtime.json"), JsonSerializer.Serialize(new { version, revision = 2, source = url, sha512 = expected }), ct);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await PublishDirectoryAsync(staging, target, ct);
            return Path.Combine(target, "python.exe");
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    static async Task PublishDirectoryAsync(string staging, string target, CancellationToken ct)
    {
        // Antivirus scanners can briefly hold a copied executable without delete
        // sharing. Keep the complete staging directory intact and retry its rename.
        for (int attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { Directory.Move(staging, target); return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 8 && Directory.Exists(staging) && !Directory.Exists(target))
            {
                await Task.Delay(Math.Min(1000, 100 << attempt), ct);
            }
        }
    }
    public static async Task<AppSettings> FetchConfigurationAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidDataException("配置源必须使用 HTTPS。");
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var data = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0) { if (data.Length + count > 256 * 1024) throw new InvalidDataException("配置文件过大。"); await data.WriteAsync(buffer.AsMemory(0, count), ct); }
        var value = JsonSerializer.Deserialize<AppSettings>(data.ToArray(), SettingsStore.JsonOptions) ?? throw new InvalidDataException("配置为空。");
        value.Validate(); return value;
    }
}
