using System.Text.Json;
using System.Text.RegularExpressions;

namespace PyTouchCode.Core;

public sealed class ColorScheme
{
    public string Name { get; set; } = "";
    public string Background { get; set; } = "#282C34";
    public string Foreground { get; set; } = "#DCDFE4";
    public string CursorColor { get; set; } = "#FFFFFF";
    public string? SelectionBackground { get; set; }
    public string Black { get; set; } = "#282C34";
    public string Red { get; set; } = "#E06C75";
    public string Green { get; set; } = "#98C379";
    public string Yellow { get; set; } = "#E5C07B";
    public string Blue { get; set; } = "#61AFEF";
    public string Purple { get; set; } = "#C678DD";
    public string Cyan { get; set; } = "#56B6C2";
    public string White { get; set; } = "#DCDFE4";
    public string BrightBlack { get; set; } = "#7F848E";
    public string BrightRed { get; set; } = "#E06C75";
    public string BrightGreen { get; set; } = "#98C379";
    public string BrightYellow { get; set; } = "#E5C07B";
    public string BrightBlue { get; set; } = "#61AFEF";
    public string BrightPurple { get; set; } = "#C678DD";
    public string BrightCyan { get; set; } = "#56B6C2";
    public string BrightWhite { get; set; } = "#FFFFFF";
    [System.Text.Json.Serialization.JsonIgnore]
    public string[] Ansi => [Black, Red, Green, Yellow, Blue, Purple, Cyan, White, BrightBlack, BrightRed, BrightGreen, BrightYellow, BrightBlue, BrightPurple, BrightCyan, BrightWhite];
    public ColorScheme Clone() => (ColorScheme)MemberwiseClone();
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80) throw new InvalidDataException("配色方案名称应为 1–80 个字符。");
        if (Ansi.Concat([Background, Foreground, CursorColor]).Any(c => c is null || !Regex.IsMatch(c, "^#[0-9a-fA-F]{6}$")) || SelectionBackground is not null && !Regex.IsMatch(SelectionBackground, "^#[0-9a-fA-F]{6}$"))
            throw new InvalidDataException("配色颜色应使用 #RRGGBB 格式。");
    }
}

public static class ColorSchemes
{
    public static IReadOnlyList<ColorScheme> BuiltIn { get; } = Load();
    static ColorScheme[] Load()
    {
        using var stream = typeof(ColorSchemes).Assembly.GetManifestResourceStream("PyTouchCode.Core.ColorSchemes.json")!;
        return JsonSerializer.Deserialize<ColorScheme[]>(stream, SettingsStore.JsonOptions)!;
    }
    public static IEnumerable<ColorScheme> All(AppSettings settings) => BuiltIn.Concat(settings.CustomColorSchemes);
    public static ColorScheme Resolve(AppSettings settings, string name) => All(settings).FirstOrDefault(s => s.Name == name) ?? throw new InvalidDataException("找不到配色方案：" + name);
    public static ColorScheme ForEditor(AppSettings settings, bool dark) => Resolve(settings, dark ? settings.EditorDarkScheme : settings.EditorLightScheme);
    public static ColorScheme ForTerminal(AppSettings settings, bool dark) => Resolve(settings, dark ? settings.TerminalDarkScheme : settings.TerminalLightScheme);
}
