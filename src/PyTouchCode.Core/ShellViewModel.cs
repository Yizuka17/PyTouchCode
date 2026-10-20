using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PyTouchCode.Core;
public sealed class ShellViewModel : INotifyPropertyChanged
{
    string interpreterLabel = "正在准备 Python…", workspaceLabel = "Python 工作区", sessionStatus = "就绪";
    bool isRunning;
    public string InterpreterLabel { get => interpreterLabel; set => Set(ref interpreterLabel, value); }
    public string WorkspaceLabel { get => workspaceLabel; set => Set(ref workspaceLabel, value); }
    public string SessionStatus { get => sessionStatus; set => Set(ref sessionStatus, value); }
    public bool IsRunning { get => isRunning; set => Set(ref isRunning, value); }
    public event PropertyChangedEventHandler? PropertyChanged;
    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new(name)); }
}
