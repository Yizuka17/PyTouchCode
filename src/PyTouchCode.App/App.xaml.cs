using Microsoft.UI.Xaml;

namespace PyTouchCode.App;
public partial class App : Application
{
    internal static string StartupPhase = "App";
    public static MainWindow? Main { get; private set; }
    public App() { InitializeComponent(); UnhandledException += (_, e) => { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PyTouchCode-crash.log"), StartupPhase + ": " + e.Exception + Environment.NewLine); }; }
    protected override void OnLaunched(LaunchActivatedEventArgs args) { Main = new MainWindow(); Main.Activate(); }
}

