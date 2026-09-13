using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace VoiceMeeterAEC;

public sealed class EngineHost : IDisposable
{
    private readonly ConcurrentQueue<string> _lines = new();
    private Process? _process;
    private StreamWriter? _log;
    private bool _everRunning;

    public event Action<string>? StatusChanged;
    public event Action? DiagnosticsChanged;
    public event Action<int>? Exited;

    public bool Alive => _process is { HasExited: false };
    public bool EverRunning => _everRunning;
    public string Status { get; private set; } = "ready";
    public string Diagnostics => string.Join(Environment.NewLine, _lines);

    public void Start(string executable, IEnumerable<string> arguments, string logPath)
    {
        if (Alive) return;
        DisposeProcess();
        _everRunning = false;
        if (!File.Exists(executable))
            throw new FileNotFoundException("The audio engine is missing. Reinstall VoiceMeeter AEC.", executable);

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        _log = new StreamWriter(logPath, false) { AutoFlush = true };
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += Receive;
        _process.ErrorDataReceived += Receive;
        _process.Exited += (_, _) =>
        {
            var code = _process?.ExitCode ?? -1;
            SetStatus("stopped");
            Exited?.Invoke(code);
        };
        SetStatus("starting");
        try
        {
            if (!_process.Start()) throw new InvalidOperationException("The audio engine could not start.");
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        catch
        {
            DisposeProcess();
            throw;
        }
    }

    private void Receive(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null) return;
        _lines.Enqueue(e.Data);
        while (_lines.Count > 400) _lines.TryDequeue(out _);
        try { _log?.WriteLine(e.Data); } catch (IOException) { }
        DiagnosticsChanged?.Invoke();

        if (e.Data.StartsWith("Running.")) { _everRunning = true; SetStatus("running"); }
        else if (e.Data.StartsWith("mode=") || e.Data.StartsWith("Control: mode="))
        {
            if (e.Data.Contains("auto=1")) SetStatus("auto");
            else if (e.Data.Contains("mode=2")) SetStatus("mute");
            else if (e.Data.Contains("mode=1")) SetStatus("bypass");
            else SetStatus("aec");
        }
        else if (e.Data.StartsWith("Auto strip")) SetStatus("auto");
        else if (e.Data.Contains("Retrying the same Insert")) SetStatus("reconnecting");
    }

    public void Send(char command)
    {
        if (command is not ('a' or 'b' or 'm' or 't' or 'q'))
            throw new ArgumentOutOfRangeException(nameof(command));
        if (!Alive) return;
        _process!.StandardInput.WriteLine(command);
        _process.StandardInput.Flush();
    }

    public async Task<bool> StopAsync(TimeSpan timeout)
    {
        if (!Alive) return true;
        Send('q');
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await _process!.WaitForExitAsync(cancellation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    private void DisposeProcess()
    {
        _log?.Dispose();
        _log = null;
        _process?.Dispose();
        _process = null;
    }

    public void Dispose() => DisposeProcess();
}
