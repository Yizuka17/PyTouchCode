using Microsoft.UI.Xaml;

namespace PyTouch.App;
public partial class App : Application
{
    internal static string StartupPhase = "App";
    public static MainWindow? Main { get; private set; }
    public App() { InitializeComponent(); UnhandledException += (_, e) => { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PyTouch-crash.log"), StartupPhase + ": " + e.Exception + Environment.NewLine); }; }
    protected override void OnLaunched(LaunchActivatedEventArgs args) { Main = new MainWindow(); Main.Activate(); }
}

