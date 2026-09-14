using System.Threading;
using System.Windows;
using Application = System.Windows.Application;

namespace VoiceMeeterAEC;

public partial class App : Application
{
    private Mutex? _mutex;
    public static EventWaitHandle? ShowSignal { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (UpdateService.TryApplyUpdate(e.Args, out var updateExitCode))
        {
            Shutdown(updateExitCode);
            return;
        }
        _mutex = new Mutex(true, "Local\\VoiceMeeterAECDesktopApp", out var firstInstance);
        if (!e.Args.Contains("--preview") && !e.Args.Contains("--check-ui"))
            ShowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\VoiceMeeterAECShowWindow");
        if (!firstInstance && !e.Args.Contains("--preview") && !e.Args.Contains("--check-ui"))
        {
            ShowSignal?.Set();
            ShowSignal?.Dispose();
            ShowSignal = null;
            Shutdown();
            return;
        }

        var window = new MainWindow(e.Args);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        ShowSignal?.Dispose();
        base.OnExit(e);
    }
}
