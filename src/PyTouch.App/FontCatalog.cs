using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace PyTouch.App;

static class FontCatalog
{
    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    struct LogFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, StrikeOut, CharSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FaceName;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate int EnumFont(nint font, nint metric, uint type, nint context);
    [DllImport("user32.dll")] static extern nint GetDC(nint window);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern int EnumFontFamiliesExW(nint dc, ref LogFont font, EnumFont callback, nint context, uint flags);
    public static IReadOnlyList<string> Families { get; } = Load();
    static string[] Load()
    {
        var families = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var filter = new LogFont { CharSet = 1, FaceName = "" };
        EnumFont callback = (font, _, _, _) =>
        {
            string name = Marshal.PtrToStructure<LogFont>(font).FaceName;
            if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith('@')) families.Add(name);
            return 1;
        };
        nint dc = GetDC(0);
        if (dc == 0) return ["Segoe UI", "Consolas"];
        try { EnumFontFamiliesExW(dc, ref filter, callback, 0, 0); }
        finally { ReleaseDC(0, dc); GC.KeepAlive(callback); }
        return families.Count > 0 ? families.ToArray() : ["Segoe UI", "Consolas"];
    }
}

public sealed partial class MainWindow
{
    static ComboBox FontChoice(string header, string selected)
    {
        var control = new ComboBox { Header = header, IsEditable = true, IsTextSearchEnabled = true, HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
        foreach (string family in FontCatalog.Families) control.Items.Add(family);
        if (control.Items.Contains(selected)) control.SelectedItem = selected;
        else control.Text = selected;
        return control;
    }
    static string FontName(ComboBox choice) => (string.IsNullOrWhiteSpace(choice.Text) ? choice.SelectedItem as string ?? "" : choice.Text).Trim();
    static ComboBox WeightChoice(string header, int selected)
    {
        var choice = new ComboBox { Header = header, HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
        string[] labels = ["纤细", "特细", "细体", "常规", "中等", "半粗", "粗体", "特粗", "黑体"];
        for (int i = 0; i < labels.Length; i++) choice.Items.Add(new ComboBoxItem { Content = $"{labels[i]} ({(i + 1) * 100})", Tag = (i + 1) * 100 });
        choice.SelectedIndex = Math.Clamp((int)Math.Round(selected / 100d) - 1, 0, 8); return choice;
    }
    static int FontWeightValue(ComboBox choice) => (int)((ComboBoxItem)choice.SelectedItem).Tag;
    static Border FontPreview(ComboBox font, NumberBox size, ComboBox weight, PyTouch.Core.ColorScheme palette, NumberBox? lineHeight = null)
    {
        var preview = new TextBlock { Text = "PS C:\\PyTouchCode> python main.py\nHello, 世界!\n0O 1Il  0123456789  {} [] ()", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, Foreground = new SolidColorBrush(ParseColor(palette.Foreground)) };
        var frame = new Border { Child = preview, Padding = new Microsoft.UI.Xaml.Thickness(14), MinHeight = 128, CornerRadius = new Microsoft.UI.Xaml.CornerRadius(6), BorderThickness = new Microsoft.UI.Xaml.Thickness(1), BorderBrush = new SolidColorBrush(ParseColor(MixColor(palette.Background, palette.Foreground, .18))), Background = new SolidColorBrush(ParseColor(palette.Background)), Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 8) };
        void Update()
        {
            string name = FontName(font); if (name.Length > 0) preview.FontFamily = new FontFamily(name);
            if (double.IsFinite(size.Value)) preview.FontSize = size.Value;
            preview.FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)FontWeightValue(weight) };
            if (lineHeight is not null && double.IsFinite(lineHeight.Value) && double.IsFinite(size.Value)) preview.LineHeight = size.Value * 1.35 * lineHeight.Value;
        }
        font.SelectionChanged += (_, _) => font.DispatcherQueue.TryEnqueue(Update); font.TextSubmitted += (_, _) => Update();
        size.ValueChanged += (_, _) => Update(); weight.SelectionChanged += (_, _) => Update();
        if (lineHeight is not null) lineHeight.ValueChanged += (_, _) => Update();
        Update(); return frame;
    }
}


