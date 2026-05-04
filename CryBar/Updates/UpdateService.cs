using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CryBar.Updates;

public sealed class UpdateInfo
{
    public required string LatestVersion { get; init; }
    public required string ReleasePageUrl { get; init; }
    public required string AssetUrl { get; init; }
}

public enum UpdateStage { LocatingAsset, Downloading, Extracting, LaunchingUpdater }

public sealed record UpdateProgress(UpdateStage Stage, double? Percent, string Status);

public sealed class UpdateException : Exception
{
    public UpdateStage Stage { get; }
    public UpdateException(UpdateStage stage, string message, Exception? inner = null) : base(message, inner) { Stage = stage; }
}

public static partial class UpdateService
{
    const string OwnerRepo = "CryShana/CryBarEditor";

    [GeneratedRegex(@"(?<major>\d+)\.(?<minor>\d+)\.(?<build>\d+)")]
    private static partial Regex VersionRgx();

    /// <summary>Version of the currently-running application (entry assembly).</summary>
    public static Version GetCurrentVersion()
        => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    public static bool IsVersionNewer(string remoteVersion, Version current)
    {
        var match = VersionRgx().Match(remoteVersion);
        if (!match.Success) return false;

        var major = int.Parse(match.Groups["major"].Value);
        var minor = int.Parse(match.Groups["minor"].Value);
        var build = int.Parse(match.Groups["build"].Value);

        return major > current.Major
            || (major == current.Major && minor > current.Minor)
            || (major == current.Major && minor == current.Minor && build > current.Build);
    }

    public static string BuildAssetUrl(string version)
        => $"https://github.com/{OwnerRepo}/releases/download/{version}/CryBarEditor-{version}.zip";

    public static string BuildReleasePageUrl(string version)
        => $"https://github.com/{OwnerRepo}/releases/tag/{version}";

    [GeneratedRegex(@"href=""(?<link>[^""]+tag/(?<version>\d+\.\d+\.\d+))""")]
    private static partial Regex ReleasesVersionRgx();

    public static async Task<UpdateInfo?> TryGetLatestVersionAsync(HttpClient http, CancellationToken ct = default)
    {
        var releasesUrl = $"https://github.com/{OwnerRepo}/releases";
        try
        {
            var response = await http.GetAsync(releasesUrl, ct);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(ct);

            foreach (Match match in ReleasesVersionRgx().Matches(content))
            {
                var version = match.Groups["version"].Value;
                return new UpdateInfo
                {
                    LatestVersion = version,
                    ReleasePageUrl = "https://github.com" + match.Groups["link"].Value,
                    AssetUrl = BuildAssetUrl(version),
                };
            }
        }
        catch (OperationCanceledException) { throw; }   // don't swallow cancellation
        catch { }
        return null;
    }

    public static void CleanupStaleUpdate(string installDir)
    {
        var updateDir = Path.Combine(installDir, ".update");
        try { if (Directory.Exists(updateDir)) Directory.Delete(updateDir, recursive: true); } catch { }

        var batPath = Path.Combine(installDir, "update.bat");
        try { if (File.Exists(batPath)) File.Delete(batPath); } catch { }
    }

    /// <summary>
    /// Verbatim contents of update.bat. Must use CRLF line endings (cmd.exe parses them).
    /// %1 = parent (editor) PID, %2 = absolute path to editor exe to relaunch.
    /// </summary>
    internal const string UpdateScriptContent =
        "@echo off\r\n" +
        ":wait\r\n" +
        "tasklist /FI \"PID eq %~1\" 2>nul | find \"%~1\" >nul\r\n" +
        "if %errorlevel%==0 (\r\n" +
        "    timeout /t 1 /nobreak >nul\r\n" +
        "    goto wait\r\n" +
        ")\r\n" +
        "robocopy \".update\\extracted\" \".\" /E /MOVE /R:5 /W:1 >nul\r\n" +
        "start \"\" \"%~2\"\r\n";

    /// <summary>
    /// Writes update.bat to <paramref name="installDir"/> and launches it detached via cmd.exe.
    /// Throws <see cref="UpdateException"/> with stage <see cref="UpdateStage.LaunchingUpdater"/> on failure.
    /// </summary>
    public static void LaunchUpdater(string installDir, string exePath, int currentPid)
    {
        var batPath = Path.Combine(installDir, "update.bat");
        try
        {
            File.WriteAllText(batPath, UpdateScriptContent);
        }
        catch (Exception ex)
        {
            throw new UpdateException(UpdateStage.LaunchingUpdater, $"Could not write update script: {ex.Message}", ex);
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{batPath}\" {currentPid} \"{exePath}\"\"",
                WorkingDirectory = installDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var proc = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null");

            // Brief check: if cmd died immediately, the script also failed to start.
            Thread.Sleep(50);
            if (proc.HasExited && proc.ExitCode != 0)
                throw new InvalidOperationException($"cmd.exe exited immediately with code {proc.ExitCode}");
        }
        catch (UpdateException) { throw; }
        catch (Exception ex)
        {
            throw new UpdateException(UpdateStage.LaunchingUpdater, $"Could not start updater: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Downloads <paramref name="info"/>'s asset to <c>installDir/.update/source.zip</c>, then extracts to
    /// <c>installDir/.update/extracted/</c>. Reports progress; throws <see cref="UpdateException"/> on failure.
    /// The .update folder is recreated fresh each call (any existing one is deleted first).
    /// </summary>
    public static async Task DownloadAndExtractAsync(
        HttpClient http,
        UpdateInfo info,
        string installDir,
        IProgress<UpdateProgress> progress,
        CancellationToken ct)
    {
        var updateDir = Path.Combine(installDir, ".update");
        var zipPath = Path.Combine(updateDir, "source.zip");
        var extractDir = Path.Combine(updateDir, "extracted");

        // Fresh .update folder every call.
        try { if (Directory.Exists(updateDir)) Directory.Delete(updateDir, recursive: true); } catch { }
        Directory.CreateDirectory(extractDir);

        // ----- Stage: LocatingAsset (HEAD) -----
        progress.Report(new UpdateProgress(UpdateStage.LocatingAsset, null, "Locating release asset..."));
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, info.AssetUrl);
            using var headResp = await http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!headResp.IsSuccessStatusCode)
                throw new UpdateException(UpdateStage.LocatingAsset,
                    $"ZIP asset not found at expected URL (HTTP {(int)headResp.StatusCode}): {info.AssetUrl}");
        }
        catch (UpdateException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            throw new UpdateException(UpdateStage.LocatingAsset, $"Could not reach GitHub: {ex.Message}", ex);
        }

        // ----- Stage: Downloading -----
        progress.Report(new UpdateProgress(UpdateStage.Downloading, 0, "Downloading..."));
        try
        {
            using var resp = await http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                throw new UpdateException(UpdateStage.Downloading, $"Download failed (HTTP {(int)resp.StatusCode}).");

            var total = resp.Content.Headers.ContentLength ?? -1L;

            using var src = await resp.Content.ReadAsStreamAsync(ct);
            using var dst = File.Create(zipPath);

            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0)
                {
                    var pct = (double)read / total;
                    progress.Report(new UpdateProgress(
                        UpdateStage.Downloading,
                        pct,
                        $"Downloading... {FormatBytes(read)} / {FormatBytes(total)} ({pct * 100:F0}%)"));
                }
                else
                {
                    progress.Report(new UpdateProgress(
                        UpdateStage.Downloading, null, $"Downloading... {FormatBytes(read)}"));
                }
            }
        }
        catch (UpdateException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            throw new UpdateException(UpdateStage.Downloading, $"Download failed: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new UpdateException(UpdateStage.Downloading, $"Could not write download to disk: {ex.Message}", ex);
        }

        // ----- Stage: Extracting -----
        progress.Report(new UpdateProgress(UpdateStage.Extracting, null, "Extracting..."));
        try
        {
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            try { File.Delete(zipPath); } catch { /* best-effort cleanup; harmless if it stays */ }
        }
        catch (Exception ex)
        {
            throw new UpdateException(UpdateStage.Extracting, $"Could not extract update archive: {ex.Message}", ex);
        }
    }

    static string FormatBytes(long bytes)
    {
        const double KiB = 1024, MiB = KiB * 1024, GiB = MiB * 1024;
        if (bytes >= GiB) return $"{bytes / GiB:F2} GB";
        if (bytes >= MiB) return $"{bytes / MiB:F2} MB";
        if (bytes >= KiB) return $"{bytes / KiB:F1} KB";
        return $"{bytes} B";
    }
}
