using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace PyTouchCode.Core;
public sealed record SerialDevice(string Port, string Description, string HardwareId)
{
    public override string ToString() => $"{Port} — {Description}";
}
public static class SerialDevices
{
    static bool IsPort(string port) => Regex.IsMatch(port, @"^COM[1-9][0-9]*$", RegexOptions.IgnoreCase);
    public static string ValidatePort(string port)
    {
        port = port.Trim();
        if (!IsPort(port)) throw new InvalidDataException("请选择或输入串口，例如 COM3。");
        return port.ToUpperInvariant();
    }
    public static Task<List<SerialDevice>> DiscoverAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("串口扫描仅支持 Windows。");
            return DiscoverWindows(ct);
        }, ct);
    }
    [SupportedOSPlatform("windows")]
    static List<SerialDevice> DiscoverWindows(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ports = new Dictionary<string, SerialDevice>(StringComparer.OrdinalIgnoreCase);
        // The serial map includes virtual/legacy ports without PnP metadata.
        // Discovery only reads system metadata; it never opens the serial port.
        try
        {
            using var map = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (map is not null)
                foreach (string name in map.GetValueNames())
                    if (map.GetValue(name) is string port && IsPort(port))
                    { port = ValidatePort(port); ports[port] = new(port, port, ""); }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        nint devices = SetupDiGetClassDevsW(0, null, 0, 0x2 | 0x4); // PRESENT | ALLCLASSES
        if (devices == -1)
        {
            if (ports.Count == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取系统串口列表。");
            return Sort(ports.Values);
        }
        try
        {
            for (uint index = 0; ; index++)
            {
                ct.ThrowIfCancellationRequested();
                var info = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(devices, index, ref info))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) break; // NO_MORE_ITEMS
                    throw new Win32Exception(error, "无法读取系统串口列表。");
                }
                nint key = SetupDiOpenDevRegKey(devices, ref info, 1, 0, 1, 1); // GLOBAL, DEV, QUERY_VALUE
                if (key == -1) continue;
                using var handle = new SafeRegistryHandle(key, ownsHandle: true);
                using var registry = RegistryKey.FromHandle(handle);
                string? port;
                try { port = registry.GetValue("PortName") as string; }
                catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { continue; }
                if (port is null || !IsPort(port)) continue;
                port = ValidatePort(port);
                string description = Property(devices, ref info, 0xC); // FRIENDLYNAME
                if (description.Length == 0) description = Property(devices, ref info, 0); // DEVICEDESC
                ports[port] = new(port, description.Length == 0 ? port : description, Property(devices, ref info, 1)); // HARDWAREID
            }
        }
        finally { SetupDiDestroyDeviceInfoList(devices); }
        ct.ThrowIfCancellationRequested();
        return Sort(ports.Values);
    }
    static List<SerialDevice> Sort(IEnumerable<SerialDevice> ports) => ports.OrderBy(p => p.Port.Length).ThenBy(p => p.Port, StringComparer.OrdinalIgnoreCase).ToList();
    static string Property(nint devices, ref DeviceInfo info, uint property)
    {
        byte[] buffer = new byte[512];
        if (!SetupDiGetDeviceRegistryPropertyW(devices, ref info, property, out _, buffer, (uint)buffer.Length, out uint required))
        {
            if (Marshal.GetLastWin32Error() != 122 || required > 65536) return "";
            buffer = new byte[required];
            if (!SetupDiGetDeviceRegistryPropertyW(devices, ref info, property, out _, buffer, (uint)buffer.Length, out required)) return "";
        }
        // Hardware IDs are REG_MULTI_SZ; the first entry is the most specific.
        return Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(required, (uint)buffer.Length)).Split('\0')[0];
    }
    [StructLayout(LayoutKind.Sequential)]
    struct DeviceInfo { public uint Size; public Guid Class; public uint Instance; public nint Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    static extern nint SetupDiGetClassDevsW(nint classGuid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetupDiEnumDeviceInfo(nint devices, uint index, ref DeviceInfo info);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern nint SetupDiOpenDevRegKey(nint devices, ref DeviceInfo info, uint scope, uint profile, uint keyType, uint access);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetupDiGetDeviceRegistryPropertyW(nint devices, ref DeviceInfo info, uint property, out uint type, [Out] byte[] buffer, uint size, out uint required);
    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetupDiDestroyDeviceInfoList(nint devices);
}
