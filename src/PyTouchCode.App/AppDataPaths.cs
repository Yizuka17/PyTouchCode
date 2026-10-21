namespace PyTouchCode.App;

static class AppDataPaths
{
    public static string Directory
    {
        get
        {
            try
            {
                // CreateProcess/DLL loading must use the physical MSIX cache,
                // not the legacy AppData path redirected by the package VFS.
                return Path.Combine(Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path, "Local", "PyTouchCode");
            }
            catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PyTouchCode");
            }
        }
    }
    public static string ResolveInterpreter(string path)
    {
        string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PyTouchCode") + Path.DirectorySeparatorChar;
        if (!path.StartsWith(legacy, StringComparison.OrdinalIgnoreCase)) return path;
        string physical = Path.Combine(Directory, path[legacy.Length..]);
        return File.Exists(physical) ? physical : path;
    }
}
