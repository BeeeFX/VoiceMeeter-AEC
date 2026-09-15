using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace VoiceMeeterAEC;

public sealed class EngineHost : IDisposable
{
    private readonly ConcurrentQueue<string> _lines = new();
    private Process? _process;
    private RotatingEngineLog? _log;
    private volatile bool _finished = true;
    private Task _exitTask = Task.CompletedTask;

    public event Action<string>? StatusChanged;
    public event Action? DiagnosticsChanged;
    public event Action<int>? Exited;
    public bool Alive => _process is not null && !_finished;
    public bool EverRunning { get; private set; }
    public string CurrentMode { get; private set; } = "auto";
    public string Status { get; private set; } = "ready";
    public float MicrophonePeak { get; private set; }
    public float ReferencePeak { get; private set; }
    public bool ReferenceRoutingUnavailable { get; private set; }
    public string Diagnostics => string.Join(Environment.NewLine, _lines);

    public void Start(string executable, IEnumerable<string> arguments, string logPath)
    {
        if (Alive) return;
        DisposeProcess();
        EverRunning = false;
        MicrophonePeak = ReferencePeak = 0;
        ReferenceRoutingUnavailable = false;
        _lines.Clear();
        if (!File.Exists(executable))
            throw new FileNotFoundException("The audio engine is missing. Reinstall VoiceMeeter AEC.", executable);
        _log = new RotatingEngineLog(logPath);
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        var args = arguments.ToList();
        CurrentMode = args.Contains("--mute") ? "mute" : args.Contains("--bypass") ? "bypass" : args.Contains("--aec") ? "aec" : "auto";
        foreach (var argument in args) startInfo.ArgumentList.Add(argument);
        var process = new Process { StartInfo = startInfo };
        _process = process;
        process.OutputDataReceived += Receive;
        process.ErrorDataReceived += Receive;
        SetStatus("starting");
        try
        {
            if (!process.Start()) throw new InvalidOperationException("The audio engine could not start.");
            _finished = false;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _exitTask = ObserveExitAsync(process);
        }
        catch { DisposeProcess(); SetStatus("stopped"); throw; }
    }

    private async Task ObserveExitAsync(Process process)
    {
        // Drain both output streams before reporting exit; late startup messages must
        // never overwrite Stopped or belong to a subsequent engine session.
        try { await process.WaitForExitAsync().ConfigureAwait(false); }
        catch (InvalidOperationException) when (!ReferenceEquals(process, _process)) { return; }
        if (!ReferenceEquals(process, _process)) return;
        var code = process.ExitCode;
        _finished = true;
        MicrophonePeak = ReferencePeak = 0;
        ReferenceRoutingUnavailable = false;
        SetStatus("stopped");
        Exited?.Invoke(code);
    }

    private void Receive(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is not null && ReferenceEquals(sender, _process)) ProcessLine(e.Data);
    }

    internal void ProcessLine(string line)
    {
        if (line.StartsWith("status "))
        {
            MicrophonePeak = Number(line, "mic");
            ReferencePeak = Number(line, "ref");
            ReferenceRoutingUnavailable = Number(line, "ref_route_unknown") != 0;
            if (Number(line, "blocks") > 0) EverRunning = true;
            var automatic = Number(line, "auto") != 0;
            var mode = (int)Number(line, "mode");
            var state = mode == 2 ? "mute"
                : Number(line, "dsp_failed") != 0 ? (automatic ? "auto-error" : "aec-error")
                : mode == 1 ? (automatic ? "auto-bypass" : "bypass")
                : Number(line, "ref_missing") != 0 ? (automatic ? "auto-missing" : "aec-missing")
                : automatic && Number(line, "route_unknown") != 0 ? "auto-unavailable"
                : automatic ? "auto" : "aec";
            SetStatus(Number(line, "blocks") > 0 ? state : "starting");
            return; // Fast meters are not written to the diagnostic log.
        }
        _lines.Enqueue(line);
        while (_lines.Count > 400) _lines.TryDequeue(out _);
        _log?.WriteLine(line);
        DiagnosticsChanged?.Invoke();
        // Configuration summaries and driver->start() are not proof of callbacks.
        if (line.Contains("Retrying the same Insert")) SetStatus("reconnecting");
    }

    private static float Number(string line, string key)
    {
        var prefix = key + "=";
        foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (token.StartsWith(prefix) && float.TryParse(token.AsSpan(prefix.Length), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)) return value;
        return 0;
    }

    public void Send(char command)
    {
        if (command is not ('a' or 'b' or 'm' or 't' or 'q')) throw new ArgumentOutOfRangeException(nameof(command));
        if (!Alive || _process!.HasExited) return;
        _process.StandardInput.WriteLine(command);
        _process.StandardInput.Flush();
        // Preserve the latest user choice even if an earlier telemetry message is queued.
        CurrentMode = command switch { 'a' => "aec", 'b' => "bypass", 'm' => "mute", 't' => "auto", _ => CurrentMode };
    }

    public async Task<bool> StopAsync(TimeSpan timeout)
    {
        if (!Alive) return true;
        try { Send('q'); }
        catch (IOException) when (_process?.HasExited != false) { }
        using var cancellation = new CancellationTokenSource(timeout);
        try { await _exitTask.WaitAsync(cancellation.Token); return true; }
        catch (OperationCanceledException) { return false; }
    }

    internal static bool ShouldRetryStartup(bool pending, bool everRunning, int code, DateTime deadline) =>
        pending && !everRunning && code is 13 or 14 or 20 or 35 && DateTime.Now < deadline;

    internal static async Task RunSelfTestsAsync(string executable)
    {
        using var parser = new EngineHost();
        parser.ProcessLine("Auto strips mask: 0x20 -> A2; route state, not audio-level detection.");
        parser.ProcessLine("Running. Driver started; no callbacks yet.");
        if (parser.EverRunning || parser.Status != "ready") throw new InvalidOperationException("Startup was confirmed without audio.");
        foreach (var (fields, expected) in new[]
        {
            ("mode=0 auto=1", "auto"), ("mode=1 auto=1", "auto-bypass"),
            ("mode=0 auto=1 route_unknown=1", "auto-unavailable"),
            ("mode=0 auto=1 ref_missing=1", "auto-missing"),
            ("mode=0 auto=0 dsp_failed=1", "aec-error"),
            ("mode=2 auto=0 dsp_failed=1 ref_missing=1", "mute")
        })
        {
            parser.ProcessLine("status " + fields + " blocks=3 mic=0.25 ref=0.5");
            if (parser.Status != expected || !parser.EverRunning || parser.MicrophonePeak != 0.25f || parser.ReferencePeak != 0.5f)
                throw new InvalidOperationException("Incorrect audio status: " + expected);
        }
        parser.ProcessLine("status mode=0 auto=0 blocks=0 mic=NaN ref=Infinity");
        if (parser.Status != "starting" || parser.MicrophonePeak != 0 || parser.ReferencePeak != 0)
            throw new InvalidOperationException("Invalid meter or callback state.");

        var temporary = Path.Combine(Path.GetTempPath(), "voicemeeter-aec-host-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var logPath = Path.Combine(temporary, "rotate.log");
            using (var log = new RotatingEngineLog(logPath, 256))
                for (var i = 0; i < 300; i++) log.WriteLine("Record " + i + " " + new string('x', 30));
            if (new FileInfo(logPath).Length > 256 || new FileInfo(logPath + ".previous").Length > 256 ||
                !File.ReadAllText(logPath).Contains("Record 299")) throw new InvalidOperationException("Log rotation failed.");

            using var host = new EngineHost();
            var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.Exited += code => exited.TrySetResult(code);
            host.Start(executable, ["--startup-failure-self-test"], Path.Combine(temporary, "startup.log"));
            var exitCode = await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (exitCode != 14 || host.Status != "stopped" || host.EverRunning ||
                !ShouldRetryStartup(true, host.EverRunning, exitCode, DateTime.Now.AddSeconds(90)) ||
                ShouldRetryStartup(true, true, exitCode, DateTime.Now.AddSeconds(90)) ||
                ShouldRetryStartup(true, false, 16, DateTime.Now.AddSeconds(90)))
                throw new InvalidOperationException("Initial driver errors do not permit the correct startup retry.");

            exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.Start(executable, ["--control-self-test"], Path.Combine(temporary, "control.log"));
            foreach (var (command, expected) in new[] { ('m', "mute"), ('b', "bypass"), ('a', "aec"), ('t', "auto") })
            {
                host.Send(command);
                if (host.CurrentMode != expected) throw new InvalidOperationException("The latest mode was not preserved.");
            }
            host.Send('q');
            if (await exited.Task.WaitAsync(TimeSpan.FromSeconds(10)) != 0 || !host.Diagnostics.Contains("Control pipe: PASS"))
                throw new InvalidOperationException("Native control pipe or output draining failed.");
        }
        finally { Directory.Delete(temporary, true); }
    }

    private void SetStatus(string status) { Status = status; StatusChanged?.Invoke(status); }
    private void DisposeProcess()
    {
        var process = _process;
        _process = null;
        _finished = true;
        process?.Dispose();
        _log?.Dispose();
        _log = null;
    }
    public void Dispose() => DisposeProcess();
}

internal sealed class RotatingEngineLog : IDisposable
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly long _limit;
    private StreamWriter? _writer;
    private bool _disposed;
    internal RotatingEngineLog(string path, long limit = 2 * 1024 * 1024)
    {
        _path = path; _limit = limit;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = Open();
    }
    private StreamWriter Open() => new(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
    internal void WriteLine(string line)
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                _writer ??= Open();
                if (_writer.BaseStream.Length + Encoding.UTF8.GetByteCount(line) + 2 > _limit)
                {
                    _writer.Dispose(); _writer = null;
                    File.Move(_path, _path + ".previous", true);
                    _writer = Open();
                }
                _writer.WriteLine(line);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    public void Dispose() { lock (_gate) { _disposed = true; _writer?.Dispose(); _writer = null; } }
}
