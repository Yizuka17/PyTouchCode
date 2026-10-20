namespace PyTouchCode.Core;

public static class WorkspaceFiles
{
    public static string ChildPath(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 255 ||
            name.EndsWith(' ') || name.EndsWith('.') || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)))
            throw new IOException("文件名无效。");
        string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3]))
            throw new IOException("这个名称由 Windows 保留。");
        return Path.Combine(Path.GetFullPath(directory), name);
    }

    public static string Create(string directory, string name, bool folder)
    {
        string path = ChildPath(directory, name);
        if (Path.Exists(path)) throw new IOException("同名文件或文件夹已存在。");
        if (folder) Directory.CreateDirectory(path);
        else using (new FileStream(path, FileMode.CreateNew, FileAccess.Write)) { }
        return path;
    }

    public static string Rename(string path, string name)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string parent = Path.GetDirectoryName(path) ?? throw new IOException("无法重命名磁盘根目录。");
        string target = ChildPath(parent, name);
        if (path == target) return path;
        bool folder = Directory.Exists(path);
        if (!folder && !File.Exists(path)) throw new FileNotFoundException("文件已被移动或删除。", path);
        if (!string.Equals(path, target, StringComparison.OrdinalIgnoreCase) && Path.Exists(target))
            throw new IOException("同名文件或文件夹已存在。");
        // The destination is always a validated sibling. Move never overwrites.
        if (folder) Directory.Move(path, target); else File.Move(path, target);
        return target;
    }

    public static string Rebase(string path, string oldRoot, string newRoot)
    {
        oldRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(oldRoot));
        if (string.Equals(path, oldRoot, StringComparison.OrdinalIgnoreCase)) return newRoot;
        string prefix = oldRoot + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? Path.Combine(newRoot, path[prefix.Length..]) : path;
    }
}
