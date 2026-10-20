namespace PyTouchCode.Core;

public static class AppDataMigration
{
    public static async Task MigrateAsync(string localData)
    {
        string target = Path.Combine(localData, "PyTouchCode");
        string legacy = Path.Combine(localData, "PyTouch");
        if (File.Exists(Path.Combine(target, "settings.json")) || !Directory.Exists(legacy)) return;
        await Task.Run(() => Copy(new DirectoryInfo(legacy), target));
        var store = new SettingsStore(target);
        var settings = await store.LoadAsync();
        string interpreter = WorkspaceFiles.Rebase(settings.Interpreter, legacy, target);
        if (File.Exists(interpreter)) settings.Interpreter = interpreter;
        await store.SaveAsync(settings);
    }

    static void Copy(DirectoryInfo source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var item in source.EnumerateFileSystemInfos())
        {
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            string destination = Path.Combine(target, item.Name == "pytouch-runtime.json" ? "pytouchcode-runtime.json" : item.Name);
            if (item is DirectoryInfo directory) Copy(directory, destination);
            else if (!File.Exists(destination)) File.Copy(item.FullName, destination);
        }
    }
}
