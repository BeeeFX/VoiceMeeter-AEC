// GPL-3.0-only. UI polls snapshots; no PowerShell runs on worker threads.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
public sealed class AecLauncherHost : IDisposable {
    readonly object gate = new object();
    readonly Queue<string> lines = new Queue<string>();
    Process process;
    StreamWriter log;
    string logPath;
    bool running, everRunning;
    string status = "starting";
    public bool Alive { get { return process != null && !process.HasExited; } }
    public bool Running { get { lock (gate) return running && Alive; } }
    public bool EverRunning { get { lock (gate) return everRunning; } }
    public string Status { get { lock (gate) return status; } }
    public string Diagnostics { get { lock (gate) return String.Join(Environment.NewLine, lines.ToArray()); } }
    public int ExitCode { get { return process == null || Alive ? -1 : process.ExitCode; } }
    public void Start(string executable, string arguments, string directory, string logPath) {
        if (process != null) throw new InvalidOperationException("Host already used");
        this.logPath = logPath;
        log = new StreamWriter(logPath, false) { AutoFlush = true };
        process = new Process();
        process.StartInfo = new ProcessStartInfo(executable, arguments) {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        process.OutputDataReceived += Receive;
        process.ErrorDataReceived += Receive;
        try {
            if (!process.Start()) throw new InvalidOperationException("Engine could not start");
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
        } catch { Dispose(); throw; }
    }
    void Receive(object sender, DataReceivedEventArgs e) {
        if (e.Data == null) return;
        lock (gate) {
            lines.Enqueue(e.Data); while (lines.Count > 300) lines.Dequeue();
            if (log != null) { try {
                if (log.BaseStream.Length > 2 * 1024 * 1024) {
                    log.Dispose(); log = null;
                    File.Copy(logPath, logPath + ".previous", true);
                    log = new StreamWriter(logPath, false) { AutoFlush = true };
                }
                log.WriteLine(e.Data);
            } catch (IOException) {} catch (UnauthorizedAccessException) {} }
            if (e.Data.StartsWith("Running.")) { running = everRunning = true; status = "running"; }
            if (e.Data.StartsWith("mode=") || e.Data.StartsWith("Control: mode="))
                status = e.Data.Contains("auto=1") ? "auto" : e.Data.Contains("mode=2") ? "mute" : e.Data.Contains("mode=1") ? "bypass" : "aec";
            if (e.Data.StartsWith("Auto strip")) status = "auto";
            if (e.Data.StartsWith("Closed.")) { running = false; status = "stopped"; }
            if (e.Data.Contains("Retrying the same Insert")) status = "reconnecting";
        }
    }
    public void Send(string key) {
        if (key != "a" && key != "b" && key != "m" && key != "t" && key != "q") throw new ArgumentException("Unknown control");
        if (!Alive) return;
        process.StandardInput.WriteLine(key); process.StandardInput.Flush();
    }
    public bool WaitForExit(int milliseconds) {
        if (process == null) return true;
        if (!process.WaitForExit(milliseconds)) return false;
        process.WaitForExit();
        return true;
    }
    public void Dispose() {
        // Disposing a launcher resource must never kill the audio process.
        if (process != null) { process.Dispose(); process = null; }
        lock (gate) { if (log != null) { log.Dispose(); log = null; } }
    }
}
