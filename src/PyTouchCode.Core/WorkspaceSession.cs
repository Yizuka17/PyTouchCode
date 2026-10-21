using System.Text.Json;

namespace PyTouchCode.Core;

public sealed class WorkspaceSession
{
    public int SchemaVersion { get; set; } = 1;
    public int ActiveIndex { get; set; } = -1;
    public List<WorkspaceDocument> Documents { get; set; } = [];
}

public sealed class WorkspaceDocument
{
    public string? Path { get; set; }
    public string? Text { get; set; }
    public string EncodingName { get; set; } = "utf-8";
    public bool Bom { get; set; }
    public string NewLine { get; set; } = "\n";
    public string? DiskHash { get; set; }
    public long Anchor { get; set; }
    public long Caret { get; set; }
    public long FirstVisibleLine { get; set; }
    public int XOffset { get; set; }

    public async Task<Document?> RestoreAsync()
    {
        if (Text is not null)
            return new Document { Path = Path is null ? null : System.IO.Path.GetFullPath(Path), Text = Text,
                EncodingName = EncodingName, Bom = Bom, NewLine = NewLine, DiskHash = DiskHash,
                Dirty = Path is not null || Text.Length > 0 };
        if (Path is null) return null;
        // Read clean files again so changes made outside the app are visible.
        return await DocumentFiles.OpenAsync(Path, Bom || EncodingName == "utf-8" ? null : EncodingName);
    }
}

public sealed class WorkspaceSessionStore(string directory)
{
    readonly SemaphoreSlim writes = new(1, 1);
    bool invalidFile;
    public string FilePath { get; } = Path.Combine(directory, "workspace-session.json");

    public async Task<WorkspaceSession?> LoadAsync()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            var session = JsonSerializer.Deserialize<WorkspaceSession>(await File.ReadAllTextAsync(FilePath), SettingsStore.JsonOptions)
                ?? throw new InvalidDataException("工作区记录为空。");
            if (session.SchemaVersion != 1 || session.Documents is null || session.Documents.Any(d => d is null || d.EncodingName is null || d.NewLine is not ("\n" or "\r\n")))
                throw new InvalidDataException("工作区记录无效。");
            invalidFile = false; return session;
        }
        catch { invalidFile = true; throw; }
    }

    public async Task SaveAsync(WorkspaceSession session)
    {
        if (invalidFile) throw new IOException("工作区记录无法读取，原文件已保留。");
        await writes.WaitAsync();
        string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(session, SettingsStore.JsonOptions));
            File.Move(temp, FilePath, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            finally { writes.Release(); }
        }
    }
}
