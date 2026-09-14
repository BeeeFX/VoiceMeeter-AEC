using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace VoiceMeeterAEC;

internal sealed record UpdateRelease(
    Version Version,
    string VersionText,
    Uri PackageUrl,
    Uri ChecksumUrl,
    Uri ReleasePage);

internal static class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/BeeeFX/VoiceMeeter-AEC/releases/latest";
    private const long MaximumPackageBytes = 250L * 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();

    internal static Version CurrentVersion => NormalizeVersion(
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0));

    internal static string CurrentVersionText => FormatVersion(CurrentVersion);

    internal static async Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseRelease(json, CurrentVersion);
    }

    internal static async Task<string> DownloadAndStageAsync(
        UpdateRelease release,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var updateRoot = Path.Combine(AppSettings.DataDirectory, "updates");
        Directory.CreateDirectory(updateRoot);
        var workDirectory = Path.Combine(updateRoot, release.VersionText + "-" + Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(workDirectory, "package.zip");
        var extractedPath = Path.Combine(workDirectory, "package");
        Directory.CreateDirectory(workDirectory);

        try
        {
            var checksumText = await DownloadTextAsync(release.ChecksumUrl, cancellationToken);
            var expectedHash = ParseChecksum(checksumText);
            await DownloadFileAsync(release.PackageUrl, packagePath, progress, cancellationToken);
            string actualHash;
            await using (var packageStream = File.OpenRead(packagePath))
                actualHash = Convert.ToHexString(await SHA256.HashDataAsync(
                    packageStream, cancellationToken)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedHash), Convert.FromHexString(actualHash)))
                throw new InvalidDataException("The downloaded package did not match its published SHA-256 checksum.");

            ExtractPackage(packagePath, extractedPath);
            RequirePackageFile(extractedPath, "VoiceMeeter AEC.exe");
            RequirePackageFile(extractedPath, "voicemeeter-aec.exe");
            return extractedPath;
        }
        catch
        {
            TryDeleteDirectory(workDirectory);
            throw;
        }
    }

    internal static void StartInstaller(string stagedPackage, bool restartEngine)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The application path is unavailable.");
        var installDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        EnsureInstallDirectoryWritable(installDirectory);

        var updaterDirectory = Path.Combine(AppSettings.DataDirectory, "updater");
        Directory.CreateDirectory(updaterDirectory);
        var updaterPath = Path.Combine(updaterDirectory, "VoiceMeeter-AEC-Updater-" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(processPath, updaterPath, true);

        var start = new ProcessStartInfo
        {
            FileName = updaterPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = updaterDirectory
        };
        start.ArgumentList.Add("--apply-update");
        start.ArgumentList.Add("--parent-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add("--staging");
        start.ArgumentList.Add(Path.GetFullPath(stagedPackage));
        start.ArgumentList.Add("--install-dir");
        start.ArgumentList.Add(installDirectory);
        if (restartEngine) start.ArgumentList.Add("--restart-engine");
        Process.Start(start)?.Dispose();
    }

    internal static bool TryApplyUpdate(string[] arguments, out int exitCode)
    {
        exitCode = 0;
        if (!arguments.Contains("--apply-update")) return false;

        try
        {
            var parentId = int.Parse(RequiredArgument(arguments, "--parent-pid"));
            var stagingDirectory = Path.GetFullPath(RequiredArgument(arguments, "--staging"));
            var installDirectory = Path.GetFullPath(RequiredArgument(arguments, "--install-dir"));
            RequireContainedPath(Path.Combine(AppSettings.DataDirectory, "updates"), stagingDirectory);
            var restartEngine = arguments.Contains("--restart-engine");
            ApplyUpdate(parentId, stagingDirectory, installDirectory, restartEngine);
        }
        catch (Exception exception)
        {
            exitCode = 1;
            System.Windows.MessageBox.Show(
                "VoiceMeeter AEC could not finish the update. Your previous files were restored when possible.\n\n" + exception.Message,
                "Update failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return true;
    }

    internal static void CleanupOldUpdateFiles()
    {
        TryDeleteDirectory(Path.Combine(AppSettings.DataDirectory, "updates"));
        TryDeleteDirectory(Path.Combine(AppSettings.DataDirectory, "updater"));
    }

    internal static void RunSelfTests()
    {
        var hash = new string('a', 64);
        var json = $$"""
        {
          "tag_name": "v9.8.7",
          "html_url": "https://github.com/BeeeFX/VoiceMeeter-AEC/releases/tag/v9.8.7",
          "assets": [
            { "name": "VoiceMeeter-AEC-9.8.7-Windows-x64.zip", "browser_download_url": "https://github.com/BeeeFX/VoiceMeeter-AEC/releases/download/v9.8.7/app.zip" },
            { "name": "VoiceMeeter-AEC-9.8.7-Windows-x64.zip.sha256", "browser_download_url": "https://github.com/BeeeFX/VoiceMeeter-AEC/releases/download/v9.8.7/app.zip.sha256" }
          ]
        }
        """;
        var parsed = ParseRelease(json, new Version(1, 0));
        if (parsed?.Version != new Version(9, 8, 7) || ParseChecksum(hash + "  package.zip") != hash)
            throw new InvalidOperationException("Update metadata parsing is invalid.");
        if (ParseRelease(json, new Version(10, 0)) is not null)
            throw new InvalidOperationException("Older releases must not be offered as updates.");

        var temporary = Path.Combine(Path.GetTempPath(), "voicemeeter-aec-update-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporary);
            var validZip = Path.Combine(temporary, "valid.zip");
            using (var archive = ZipFile.Open(validZip, ZipArchiveMode.Create))
            {
                using var app = new StreamWriter(archive.CreateEntry("VoiceMeeter AEC.exe").Open());
                app.Write("app");
            }
            var extracted = Path.Combine(temporary, "valid");
            ExtractPackage(validZip, extracted);
            if (!File.Exists(Path.Combine(extracted, "VoiceMeeter AEC.exe")))
                throw new InvalidOperationException("A valid update package was not extracted.");

            var unsafeZip = Path.Combine(temporary, "unsafe.zip");
            using (var archive = ZipFile.Open(unsafeZip, ZipArchiveMode.Create))
            {
                using var escaped = new StreamWriter(archive.CreateEntry("../escaped.txt").Open());
                escaped.Write("unsafe");
            }
            try
            {
                ExtractPackage(unsafeZip, Path.Combine(temporary, "unsafe"));
                throw new InvalidOperationException("An unsafe update path was accepted.");
            }
            catch (InvalidDataException) { }

            var stagedCopy = Path.Combine(temporary, "staged-copy");
            var installedCopy = Path.Combine(temporary, "installed-copy");
            Directory.CreateDirectory(Path.Combine(stagedCopy, "docs"));
            Directory.CreateDirectory(installedCopy);
            File.WriteAllText(Path.Combine(stagedCopy, "VoiceMeeter AEC.exe"), "new app");
            File.WriteAllText(Path.Combine(stagedCopy, "voicemeeter-aec.exe"), "new engine");
            File.WriteAllText(Path.Combine(stagedCopy, "docs", "guide.txt"), "new guide");
            File.WriteAllText(Path.Combine(installedCopy, "VoiceMeeter AEC.exe"), "old app");
            CopyPackageWithRollback(stagedCopy, installedCopy, Path.Combine(temporary, "backup"));
            if (File.ReadAllText(Path.Combine(installedCopy, "VoiceMeeter AEC.exe")) != "new app" ||
                File.ReadAllText(Path.Combine(installedCopy, "docs", "guide.txt")) != "new guide")
                throw new InvalidOperationException("The staged update was not copied correctly.");

            var stagedRollback = Path.Combine(temporary, "staged-rollback");
            var installedRollback = Path.Combine(temporary, "installed-rollback");
            Directory.CreateDirectory(stagedRollback);
            Directory.CreateDirectory(installedRollback);
            File.WriteAllText(Path.Combine(stagedRollback, "a-first.txt"), "new first");
            File.WriteAllText(Path.Combine(stagedRollback, "z-locked.txt"), "new locked");
            File.WriteAllText(Path.Combine(installedRollback, "a-first.txt"), "old first");
            File.WriteAllText(Path.Combine(installedRollback, "z-locked.txt"), "old locked");
            using (File.Open(Path.Combine(installedRollback, "z-locked.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try
                {
                    CopyPackageWithRollback(stagedRollback, installedRollback, Path.Combine(temporary, "rollback-backup"));
                    throw new InvalidOperationException("A locked update destination was unexpectedly replaced.");
                }
                catch (IOException) { }
            }
            if (File.ReadAllText(Path.Combine(installedRollback, "a-first.txt")) != "old first")
                throw new InvalidOperationException("A failed update did not restore the previous files.");
        }
        finally
        {
            TryDeleteDirectory(temporary);
        }
    }

    private static UpdateRelease? ParseRelease(string json, Version currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("The release has no version tag.");
        if (!TryParseVersion(tag, out var version))
            throw new InvalidDataException("The latest release has an unsupported version tag.");
        if (version <= NormalizeVersion(currentVersion)) return null;

        var versionText = FormatVersion(version);
        var packageName = $"VoiceMeeter-AEC-{versionText}-Windows-x64.zip";
        var checksumName = packageName + ".sha256";
        Uri? packageUrl = null;
        Uri? checksumUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (name is null || url is null || !TryCreateGitHubUri(url, out var uri)) continue;
            if (name.Equals(packageName, StringComparison.OrdinalIgnoreCase)) packageUrl = uri;
            if (name.Equals(checksumName, StringComparison.OrdinalIgnoreCase)) checksumUrl = uri;
        }
        if (packageUrl is null || checksumUrl is null)
            throw new InvalidDataException("The latest release does not contain a verified Windows x64 package.");

        var pageText = root.GetProperty("html_url").GetString();
        if (pageText is null || !TryCreateGitHubUri(pageText, out var releasePage))
            throw new InvalidDataException("The release page address is invalid.");
        return new UpdateRelease(version, versionText, packageUrl, checksumUrl, releasePage);
    }

    private static async Task<string> DownloadTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 16_384)
            throw new InvalidDataException("The checksum file is unexpectedly large.");
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (text.Length > 16_384) throw new InvalidDataException("The checksum file is unexpectedly large.");
        return text;
    }

    private static async Task DownloadFileAsync(Uri uri, string destination, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var length = response.Content.Headers.ContentLength;
        if (length is > MaximumPackageBytes)
            throw new InvalidDataException("The update package is unexpectedly large.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, true);
        var buffer = new byte[81_920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaximumPackageBytes) throw new InvalidDataException("The update package is unexpectedly large.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            if (length is > 0) progress?.Report((int)Math.Clamp(total * 100 / length.Value, 0, 100));
        }
        progress?.Report(100);
    }

    private static void ExtractPackage(string packagePath, string destination)
    {
        Directory.CreateDirectory(destination);
        var destinationRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update package contains an unsafe path.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    private static void ApplyUpdate(int parentId, string stagingDirectory, string installDirectory, bool restartEngine)
    {
        RequirePackageFile(stagingDirectory, "VoiceMeeter AEC.exe");
        RequirePackageFile(stagingDirectory, "voicemeeter-aec.exe");
        RequirePackageFile(installDirectory, "VoiceMeeter AEC.exe");
        WaitForParent(parentId);

        var backupDirectory = Path.Combine(AppSettings.DataDirectory, "update-backup-" + Guid.NewGuid().ToString("N"));
        CopyPackageWithRollback(stagingDirectory, installDirectory, backupDirectory);

        var installedApp = Path.Combine(installDirectory, "VoiceMeeter AEC.exe");
        var start = new ProcessStartInfo { FileName = installedApp, UseShellExecute = true, WorkingDirectory = installDirectory };
        start.ArgumentList.Add("--after-update");
        if (restartEngine) start.ArgumentList.Add("--restart-engine");
        Process.Start(start)?.Dispose();
    }

    private static void CopyPackageWithRollback(string stagingDirectory, string installDirectory, string backupDirectory)
    {
        var copiedFiles = new List<string>();
        var keepBackup = false;
        try
        {
            foreach (var source in Directory.EnumerateFiles(stagingDirectory, "*", SearchOption.AllDirectories)
                         .OrderBy(path => Path.GetRelativePath(stagingDirectory, path), StringComparer.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(stagingDirectory, source);
                var destination = SafeDestination(installDirectory, relative);
                if (File.Exists(destination))
                {
                    var backup = SafeDestination(backupDirectory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(destination, backup, true);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                copiedFiles.Add(relative);
                File.Copy(source, destination, true);
            }
        }
        catch (Exception copyException)
        {
            var restoreErrors = new List<Exception>();
            foreach (var relative in copiedFiles.AsEnumerable().Reverse())
            {
                try
                {
                    var backup = SafeDestination(backupDirectory, relative);
                    var destination = SafeDestination(installDirectory, relative);
                    if (File.Exists(backup)) File.Copy(backup, destination, true);
                    else File.Delete(destination);
                }
                catch (Exception restoreException) { restoreErrors.Add(restoreException); }
            }
            if (restoreErrors.Count > 0)
            {
                keepBackup = true;
                throw new AggregateException(
                    $"The update failed and some files could not be restored. The backup is at {backupDirectory}.",
                    new[] { copyException }.Concat(restoreErrors));
            }
            throw;
        }
        finally
        {
            if (!keepBackup) TryDeleteDirectory(backupDirectory);
        }
    }

    private static string SafeDestination(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!destination.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update contains an unsafe destination path.");
        return destination;
    }

    private static void RequireContainedPath(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The staged update path is invalid.");
    }

    private static void EnsureInstallDirectoryWritable(string installDirectory)
    {
        var marker = Path.Combine(installDirectory, ".voicemeeter-aec-update-" + Guid.NewGuid().ToString("N"));
        try { File.WriteAllText(marker, "update check"); }
        finally { try { File.Delete(marker); } catch { } }
    }

    private static void WaitForParent(int parentId)
    {
        try
        {
            using var parent = Process.GetProcessById(parentId);
            if (!parent.WaitForExit(30_000)) throw new TimeoutException("The running application did not close in time.");
        }
        catch (ArgumentException) { }
    }

    private static void RequirePackageFile(string root, string name)
    {
        if (!File.Exists(Path.Combine(root, name)))
            throw new InvalidDataException("The update package is incomplete: " + name + " is missing.");
    }

    private static string RequiredArgument(string[] arguments, string name)
    {
        var index = Array.IndexOf(arguments, name);
        if (index < 0 || index + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1]))
            throw new ArgumentException("Missing updater argument: " + name);
        return arguments[index + 1];
    }

    private static string ParseChecksum(string text)
    {
        var match = Regex.Match(text, "(?i)(?<![0-9a-f])[0-9a-f]{64}(?![0-9a-f])");
        if (!match.Success) throw new InvalidDataException("The release checksum file is invalid.");
        return match.Value.ToLowerInvariant();
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var value = tag.Trim().TrimStart('v', 'V');
        var separator = value.IndexOfAny(['-', '+']);
        if (separator >= 0) value = value[..separator];
        if (!Version.TryParse(value, out var parsed))
        {
            version = new Version();
            return false;
        }
        version = NormalizeVersion(parsed);
        return true;
    }

    private static bool TryCreateGitHubUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            parsed.Scheme == Uri.UriSchemeHttps &&
            (parsed.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
             parsed.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }

    private static Version NormalizeVersion(Version version) => new(
        Math.Max(0, version.Major), Math.Max(0, version.Minor), Math.Max(0, version.Build));

    private static string FormatVersion(Version version) => $"{version.Major}.{version.Minor}.{version.Build}";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VoiceMeeter-AEC/" + CurrentVersionText);
        return client;
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { }
    }
}
