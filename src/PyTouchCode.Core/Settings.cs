using System.Text.Json;

namespace PyTouchCode.Core;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public bool Initialized { get; set; }
    public string Theme { get; set; } = "System";
    public string Material { get; set; } = "Mica";
    public double MaterialTransparency { get; set; } = .5;
    public string Accent { get; set; } = "#4C9AFF";
    public bool UseSystemAccent { get; set; } = true;
    public string UiFont { get; set; } = "Segoe UI Variable";
    public double UiFontSize { get; set; } = 14;
    public int UiFontWeight { get; set; } = 400;
    public string EditorFont { get; set; } = "Cascadia Code";
    public double EditorFontSize { get; set; } = 14;
    public int EditorFontWeight { get; set; } = 400;
    public double EditorLineHeight { get; set; } = 1;
    public string TerminalFont { get; set; } = "Cascadia Mono";
    public double TerminalFontSize { get; set; } = 13;
    public int TerminalFontWeight { get; set; } = 400;
    public double TerminalLineHeight { get; set; } = 1;
    public string EditorDarkScheme { get; set; } = "One Half Dark";
    public string EditorLightScheme { get; set; } = "One Half Light";
    public string TerminalDarkScheme { get; set; } = "One Half Dark";
    public string TerminalLightScheme { get; set; } = "One Half Light";
    public List<ColorScheme> CustomColorSchemes { get; set; } = [];
    public int IndentSize { get; set; } = 4;
    public bool UseTabs { get; set; }
    public bool WordWrap { get; set; }
    public string Interpreter { get; set; } = "";
    public bool InterpreterSelectedByUser { get; set; }
    public string ExecutionTarget { get; set; } = "Python";
    public string DevicePort { get; set; } = "";
    public string Shell { get; set; } = "";
    public bool EnableShellProfile { get; set; }
    public string[] ShellArguments { get; set; } = [];
    public Dictionary<string,string> Environment { get; set; } = [];
    public List<string> RecentFiles { get; set; } = [];
    public List<string> RecentFolders { get; set; } = [];
    public string Workspace { get; set; } = "";
    public bool Completion { get; set; } = true;
    public bool GhostText { get; set; } = true;
    public bool AutoCloseBrackets { get; set; } = true;
    public bool OnlineUpdates { get; set; }
    public string ConfigurationUrl { get; set; } = "";

    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("不支持的配置版本。");
        if (new[] { Theme, Material, Accent, UiFont, EditorFont, TerminalFont, Interpreter, Shell, Workspace, ConfigurationUrl }.Any(x => x is null) || RecentFiles is null || RecentFolders is null || Environment is null || ShellArguments is null)
            throw new InvalidDataException("配置包含无效的空值。");
        if (new[] { UiFont, EditorFont, TerminalFont }.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 128)) throw new InvalidDataException("字体名称无效。");
        if (new[] { UiFontSize, EditorFontSize, TerminalFontSize, EditorLineHeight, TerminalLineHeight }.Any(n => !double.IsFinite(n))) throw new InvalidDataException("字号或行高无效。");
        if (!double.IsFinite(MaterialTransparency)) throw new InvalidDataException("材质透明度无效。");
        MaterialTransparency = Math.Clamp(MaterialTransparency, 0, 1);
        UiFontSize = Math.Clamp(UiFontSize, 10, 24);
        EditorFontSize = Math.Clamp(EditorFontSize, 9, 40);
        TerminalFontSize = Math.Clamp(TerminalFontSize, 9, 32);
        EditorLineHeight = Math.Clamp(EditorLineHeight, 1, 2);
        TerminalLineHeight = Math.Clamp(TerminalLineHeight, 1, 2);
        UiFontWeight = Math.Clamp(UiFontWeight, 100, 900);
        EditorFontWeight = Math.Clamp(EditorFontWeight, 100, 900);
        TerminalFontWeight = Math.Clamp(TerminalFontWeight, 100, 900);
        if (CustomColorSchemes is null || CustomColorSchemes.Count > 100 || CustomColorSchemes.Any(s => s is null)) throw new InvalidDataException("自定义配色列表无效（最多 100 项）。");
        foreach (var scheme in CustomColorSchemes) scheme.Validate();
        var names = ColorSchemes.All(this).Select(s => s.Name).ToList();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count) throw new InvalidDataException("自定义配色名称不能重复或覆盖内置方案。");
        foreach (string name in new[] { EditorDarkScheme, EditorLightScheme, TerminalDarkScheme, TerminalLightScheme }) ColorSchemes.Resolve(this, name);
        IndentSize = Math.Clamp(IndentSize, 1, 8);
        if (Theme is not ("System" or "Dark" or "Light")) Theme = "System";
        if (ExecutionTarget is not ("Python" or "MicroPython")) ExecutionTarget = "Python";
        DevicePort ??= "";
        if (Material is not ("Mica" or "MicaAlt" or "Acrylic" or "Solid")) Material = "Solid";
    }
}

public sealed class SettingsStore(string directory)
{
    bool invalidFile;
    public string DirectoryPath { get; } = directory;
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            var value = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(FilePath), JsonOptions) ?? throw new InvalidDataException("配置为空。");
            value.Validate(); invalidFile = false; return value;
        }
        catch { invalidFile = true; throw; }
    }
    public async Task SaveAsync(AppSettings value)
    {
        if (invalidFile) throw new IOException("现有配置未能成功读取。为保留原文件，请先修复配置并重新启动。");
        value.Validate();
        Directory.CreateDirectory(DirectoryPath);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, JsonOptions)); File.Move(temp, FilePath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void Remember(List<string> items, string path)
    {
        items.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
        items.Insert(0, path);
        if (items.Count > 12) items.RemoveRange(12, items.Count - 12);
    }
}
