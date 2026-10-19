using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace PyTouch.Core;
public static class Authenticode
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct FileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public nint File, KnownSubject;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TrustData
    {
        public uint Size;
        public nint Policy, Sip;
        public uint UiChoice, RevocationChecks, UnionChoice;
        public nint File;
        public uint StateAction;
        public nint StateData, Url;
        public uint ProviderFlags, UiContext;
        public nint SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    static extern int WinVerifyTrust(nint window, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref TrustData data);
    public static void VerifyPythonInstaller(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var file = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Path = path };
        nint pointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        Marshal.StructureToPtr(file, pointer, false);
        try
        {
            var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1, File = pointer, ProviderFlags = 0x1000 };
            if (WinVerifyTrust(new nint(-1), new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"), ref data) != 0) throw new InvalidDataException("Python Tcl/Tk 安装包签名校验失败。");
            // X509CertificateLoader accepts encoded certificates, not signed MSI
            // containers. This API is used only after WinVerifyTrust verified bytes.
#pragma warning disable SYSLIB0057
            using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            if (!certificate.Subject.StartsWith("CN=Python Software Foundation,", StringComparison.Ordinal)) throw new InvalidDataException("Tcl/Tk 发布者不是 Python Software Foundation。");
        }
        finally { Marshal.DestroyStructure<FileInfo>(pointer); Marshal.FreeHGlobal(pointer); }
    }
}
