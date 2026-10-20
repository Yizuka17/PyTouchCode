using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PyTouchCode.Core;
public sealed record SerialDevice(string Port, string Description, string HardwareId)
{
    public override string ToString() => $"{Port} — {Description}";
}
public static class SerialDevices
{
    public static string ValidatePort(string port)
    {
        port = port.Trim();
        if (!Regex.IsMatch(port, @"^COM[1-9]\d*$", RegexOptions.IgnoreCase)) throw new InvalidDataException("请选择或输入串口，例如 COM3。");
        return port.ToUpperInvariant();
    }
    public static async Task<List<SerialDevice>> DiscoverAsync(string python, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var session = new ProcessSession(); var output = new StringBuilder(); var error = new StringBuilder();
        session.Output += (text, failed) => { lock (output) (failed ? error : output).Append(text); };
        int code = await session.RunAsync(python, ["-c", "import json;from serial.tools.list_ports import comports;print(json.dumps([[p.device,p.description,p.hwid] for p in comports()]))"], Path.GetDirectoryName(Path.GetFullPath(python))!, PythonProcessEnvironment.Create(python), timeout.Token);
        timeout.Token.ThrowIfCancellationRequested();
        if (code != 0) throw new IOException("串口扫描失败。" + error);
        var rows = JsonSerializer.Deserialize<string[][]>(output.ToString()) ?? [];
        return rows.Where(row => row.Length == 3).Select(row => new SerialDevice(ValidatePort(row[0]), row[1], row[2])).OrderBy(row => row.Port.Length).ThenBy(row => row.Port, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
