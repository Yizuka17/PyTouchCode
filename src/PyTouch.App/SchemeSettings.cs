using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using PyTouch.Core;

namespace PyTouch.App;
public sealed partial class MainWindow
{
    sealed class SchemeSettings
    {
        public StackPanel View { get; } = Form();
        public List<ColorScheme> Custom { get; }
        public ComboBox EditorDark { get; }
        public ComboBox EditorLight { get; }
        public ComboBox TerminalDark { get; }
        public ComboBox TerminalLight { get; }
        readonly ComboBox catalog;
        readonly Border preview = new() { Padding = new Thickness(14), CornerRadius = new CornerRadius(6) };
        readonly TextBox name = Field("方案名称", "");
        readonly Dictionary<string, TextBox> fields = [];
        readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap };
        ColorScheme selected = new();
        static readonly (string Key, string Label)[] Colors =
        [
            ("Background", "背景"), ("Foreground", "文字"), ("CursorColor", "光标"), ("SelectionBackground", "选区"),
            ("Black", "黑"), ("Red", "红"), ("Green", "绿"), ("Yellow", "黄"), ("Blue", "蓝"), ("Purple", "紫"), ("Cyan", "青"), ("White", "白"),
            ("BrightBlack", "亮黑 / 注释"), ("BrightRed", "亮红"), ("BrightGreen", "亮绿"), ("BrightYellow", "亮黄"), ("BrightBlue", "亮蓝"), ("BrightPurple", "亮紫"), ("BrightCyan", "亮青"), ("BrightWhite", "亮白")
        ];
        IEnumerable<string> Names => ColorSchemes.BuiltIn.Concat(Custom).Select(s => s.Name);
        public SchemeSettings(AppSettings settings)
        {
            Custom = settings.CustomColorSchemes.Select(s => s.Clone()).ToList();
            EditorDark = Choice("编辑器深色配色", Names, settings.EditorDarkScheme);
            EditorLight = Choice("编辑器浅色配色", Names, settings.EditorLightScheme);
            TerminalDark = Choice("终端深色配色", Names, settings.TerminalDarkScheme);
            TerminalLight = Choice("终端浅色配色", Names, settings.TerminalLightScheme);
            catalog = Choice("浏览与预览方案", Names, settings.EditorDarkScheme);
            Add(View, EditorDark, EditorLight, TerminalDark, TerminalLight, catalog, preview);
            var edit = new StackPanel { Spacing = 10 };
            Add(edit, name);
            var colorGrid = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition()); colorGrid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < Colors.Length; i++)
            {
                if (i % 2 == 0) colorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var (key, label) = Colors[i];
                var box = Field(label, "#000000"); fields[key] = box;
                var swatch = new Button { Width = 32, Height = 32, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Bottom };
                ToolTipService.SetToolTip(swatch, "选择" + label + "颜色");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(swatch, "选择" + label + "颜色");
                var flyout = new Flyout(); swatch.Flyout = flyout;
                flyout.Opening += (_, _) =>
                {
                    var picker = new ColorPicker { Color = ValidColor(box.Text) ? ParseColor(box.Text) : Microsoft.UI.Colors.Black, IsAlphaEnabled = false };
                    picker.ColorChanged += (_, args) => box.Text = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
                    flyout.Content = picker;
                };
                box.TextChanged += (_, _) => { if (ValidColor(box.Text)) swatch.Background = new SolidColorBrush(ParseColor(box.Text)); };
                var row = new Grid { ColumnSpacing = 6 };
                row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(box); Grid.SetColumn(swatch, 1); row.Children.Add(swatch);
                Grid.SetColumn(row, i % 2); Grid.SetRow(row, i / 2); colorGrid.Children.Add(row);
            }
            Add(edit, colorGrid, error);
            var save = new Button { Content = "保存到自定义列表" };
            var remove = new Button { Content = "删除此自定义方案" };
            Add(edit, save, remove);
            View.Children.Add(new Expander { Header = "自定义配色", Content = edit, HorizontalAlignment = HorizontalAlignment.Stretch });
            void Select()
            {
                selected = ColorSchemes.BuiltIn.Concat(Custom).First(s => s.Name == (string)catalog.SelectedItem);
                name.Text = ColorSchemes.BuiltIn.Any(s => s.Name == selected.Name) ? selected.Name + " 自定义" : selected.Name;
                foreach (var (key, _) in Colors) fields[key].Text = (string?)typeof(ColorScheme).GetProperty(key)!.GetValue(selected) ?? MixColor(selected.Background, selected.Foreground, .23);
                remove.IsEnabled = Custom.Any(s => s.Name == selected.Name); error.Text = ""; RenderPreview();
            }
            catalog.SelectionChanged += (_, _) => { if (catalog.SelectedItem is string) Select(); };
            save.Click += (_, _) =>
            {
                try
                {
                    var scheme = selected.Clone(); scheme.Name = name.Text.Trim();
                    foreach (var (key, _) in Colors) typeof(ColorScheme).GetProperty(key)!.SetValue(scheme, fields[key].Text.Trim());
                    scheme.Validate();
                    if (ColorSchemes.BuiltIn.Any(s => s.Name.Equals(scheme.Name, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("请为自定义方案起一个不同于内置方案的名字。");
                    Custom.RemoveAll(s => s.Name.Equals(scheme.Name, StringComparison.OrdinalIgnoreCase)); Custom.Add(scheme);
                    RefreshChoices(scheme.Name); error.Text = "已添加。";
                }
                catch (Exception e) { error.Text = e.Message; }
            };
            remove.Click += (_, _) => { Custom.RemoveAll(s => s.Name == selected.Name); RefreshChoices("One Half Dark"); };
            Select();
        }
        static bool ValidColor(string text) => System.Text.RegularExpressions.Regex.IsMatch(text, "^#[0-9a-fA-F]{6}$");
        void RefreshChoices(string selection)
        {
            foreach (var (choice, fallback) in new[] { (EditorDark, "One Half Dark"), (EditorLight, "One Half Light"), (TerminalDark, "One Half Dark"), (TerminalLight, "One Half Light"), (catalog, selection) })
            {
                string value = choice == catalog ? selection : (string)choice.SelectedItem;
                choice.ItemsSource = null; choice.Items.Clear(); foreach (string item in Names) choice.Items.Add(item);
                choice.SelectedItem = Names.Contains(value) ? value : fallback;
            }
        }
        void RenderPreview()
        {
            preview.Background = new SolidColorBrush(ParseColor(selected.Background));
            var stack = new StackPanel { Spacing = 8 };
            var text = new RichTextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 14, Foreground = new SolidColorBrush(ParseColor(selected.Foreground)) };
            var p = new Paragraph();
            p.Inlines.Add(new Run { Text = "# " + selected.Name + "\n", Foreground = new SolidColorBrush(ParseColor(selected.BrightBlack)) });
            p.Inlines.Add(new Run { Text = "def ", Foreground = new SolidColorBrush(ParseColor(selected.Purple)) });
            p.Inlines.Add(new Run { Text = "hello(name):\n    print(" });
            p.Inlines.Add(new Run { Text = "'你好，世界!'", Foreground = new SolidColorBrush(ParseColor(selected.Green)) });
            p.Inlines.Add(new Run { Text = ", name)\n>>> hello('PyTouchCode')" }); text.Blocks.Add(p); stack.Children.Add(text);
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            foreach (string color in selected.Ansi) swatches.Children.Add(new Border { Width = 17, Height = 17, Background = new SolidColorBrush(ParseColor(color)), CornerRadius = new CornerRadius(2) });
            stack.Children.Add(swatches); preview.Child = stack;
        }
        public void Apply(AppSettings settings)
        {
            settings.EditorDarkScheme = (string)EditorDark.SelectedItem; settings.EditorLightScheme = (string)EditorLight.SelectedItem;
            settings.TerminalDarkScheme = (string)TerminalDark.SelectedItem; settings.TerminalLightScheme = (string)TerminalLight.SelectedItem;
            settings.CustomColorSchemes = Custom.Select(s => s.Clone()).ToList();
        }
    }
}



