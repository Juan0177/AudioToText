using System.Diagnostics;
using System.IO.Compression;

namespace AudioToText.Web.Services;

public enum FfmpegSourceKind
{
    Missing,
    Bundled,
    Path,
    Env
}

public sealed class FfmpegStatusDto
{
    public bool Available { get; init; }
    public string Source { get; init; } = "Mancante";
    public string SourceKind { get; init; } = nameof(FfmpegSourceKind.Missing);
    public string? Path { get; init; }
    public bool IsDownloading { get; init; }
    public int? DownloadPercent { get; init; }
    public long BytesDownloaded { get; init; }
    public long? BytesTotal { get; init; }
    public string? Message { get; init; }
    public string Requirements { get; init; } =
        "Download ~80–100 MB. Serve per ogg/opus/m4a/webm (WhatsApp). WAV/MP3 possono funzionare senza.";
}

public interface IFfmpegToolService
{
    FfmpegStatusDto GetStatus();
    string? ResolveExecutablePath();
    void StartDownload();
    void CancelDownload();
}

/// <summary>
/// Resolves ffmpeg (bundled tools/ffmpeg → env → PATH) and can download a portable Windows/Linux build.
/// </summary>
public sealed class FfmpegToolService : IFfmpegToolService, IDisposable
{
    // Pinned essentials build (Windows). Linux uses johnvansickle static amd64.
    private const string WindowsZipUrl =
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    private const string LinuxTarUrl =
        "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz";

    private const long ApproxWindowsBytes = 95L * 1024 * 1024;
    private const long ApproxLinuxBytes = 75L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly IHostEnvironment _env;
    private readonly AppLocalSettingsStore _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FfmpegToolService> _logger;
    private readonly string _toolsDir;

    private bool _isDownloading;
    private int? _downloadPercent;
    private long _bytesDownloaded;
    private long? _bytesTotal;
    private string? _message;
    private CancellationTokenSource? _cts;

    public FfmpegToolService(
        IHostEnvironment env,
        AppLocalSettingsStore settings,
        IHttpClientFactory httpClientFactory,
        ILogger<FfmpegToolService> logger)
    {
        _env = env;
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _toolsDir = Path.Combine(env.ContentRootPath, "tools", "ffmpeg");
        Directory.CreateDirectory(_toolsDir);
    }

    public FfmpegStatusDto GetStatus()
    {
        var (path, kind) = ResolveWithKind();
        lock (_gate)
        {
            return new FfmpegStatusDto
            {
                Available = path is not null,
                Source = kind switch
                {
                    FfmpegSourceKind.Bundled => "Trovato locale (tools/ffmpeg)",
                    FfmpegSourceKind.Path => "Trovato nel PATH di sistema",
                    FfmpegSourceKind.Env => "Trovato via FFMPEG_PATH",
                    _ => "Mancante"
                },
                SourceKind = kind.ToString(),
                Path = path,
                IsDownloading = _isDownloading,
                DownloadPercent = _downloadPercent,
                BytesDownloaded = _bytesDownloaded,
                BytesTotal = _bytesTotal ?? (OperatingSystem.IsWindows() ? ApproxWindowsBytes : ApproxLinuxBytes),
                Message = _message ?? (path is null
                    ? "ffmpeg non installato. Usa «Scarica ffmpeg» per il setup automatico."
                    : $"ffmpeg pronto ({kind}).")
            };
        }
    }

    public string? ResolveExecutablePath() => ResolveWithKind().Path;

    public void StartDownload()
    {
        lock (_gate)
        {
            if (_isDownloading)
            {
                throw new InvalidOperationException("Download ffmpeg già in corso.");
            }

            if (ResolveBundledPath() is not null)
            {
                _message = "ffmpeg locale già presente.";
                return;
            }

            _isDownloading = true;
            _downloadPercent = 0;
            _bytesDownloaded = 0;
            _bytesTotal = OperatingSystem.IsWindows() ? ApproxWindowsBytes : ApproxLinuxBytes;
            _message = "Avvio download ffmpeg…";
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => DownloadCoreAsync(token), CancellationToken.None);
        }
    }

    public void CancelDownload()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _message = "Annullamento download ffmpeg…";
        }
    }

    private (string? Path, FfmpegSourceKind Kind) ResolveWithKind()
    {
        var bundled = ResolveBundledPath();
        if (bundled is not null)
        {
            return (bundled, FfmpegSourceKind.Bundled);
        }

        var persisted = _settings.GetFfmpegPath();
        if (!string.IsNullOrWhiteSpace(persisted) && File.Exists(persisted) && VerifyRuns(persisted))
        {
            return (persisted, FfmpegSourceKind.Bundled);
        }

        var fromEnv = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv) && VerifyRuns(fromEnv))
        {
            return (fromEnv, FfmpegSourceKind.Env);
        }

        var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, exeName);
            if (File.Exists(candidate) && VerifyRuns(candidate))
            {
                return (candidate, FfmpegSourceKind.Path);
            }
        }

        return (null, FfmpegSourceKind.Missing);
    }

    private string? ResolveBundledPath()
    {
        var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var direct = Path.Combine(_toolsDir, exeName);
        if (File.Exists(direct) && VerifyRuns(direct))
        {
            return direct;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(_toolsDir, exeName, SearchOption.AllDirectories))
            {
                if (VerifyRuns(file))
                {
                    return file;
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private async Task DownloadCoreAsync(CancellationToken cancellationToken)
    {
        var isWindows = OperatingSystem.IsWindows();
        var url = isWindows ? WindowsZipUrl : LinuxTarUrl;
        var archivePath = Path.Combine(
            _toolsDir,
            isWindows ? "ffmpeg-download.zip" : "ffmpeg-download.tar.xz");
        var extractDir = Path.Combine(_toolsDir, "extract");

        try
        {
            Directory.CreateDirectory(_toolsDir);
            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, recursive: true);
            }

            Directory.CreateDirectory(extractDir);

            var client = _httpClientFactory.CreateClient("ffmpeg-tools");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength
                ?? (isWindows ? ApproxWindowsBytes : ApproxLinuxBytes);
            lock (_gate)
            {
                _bytesTotal = total;
                _message = "Download ffmpeg…";
            }

            await using (var remote = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = File.Create(archivePath))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await remote.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                    var percent = total > 0 ? (int)Math.Min(99, written * 100 / total) : 0;
                    lock (_gate)
                    {
                        _bytesDownloaded = written;
                        _bytesTotal = total;
                        _downloadPercent = percent;
                        _message = $"Download ffmpeg: {FormatBytes(written)} / {FormatBytes(total)} ({percent}%)";
                    }
                }
            }

            lock (_gate)
            {
                _downloadPercent = 99;
                _message = "Estrazione ffmpeg…";
            }

            if (isWindows)
            {
                ZipFile.ExtractToDirectory(archivePath, extractDir, overwriteFiles: true);
            }
            else
            {
                await ExtractTarXzAsync(archivePath, extractDir, cancellationToken);
            }

            var exeName = isWindows ? "ffmpeg.exe" : "ffmpeg";
            var found = Directory.EnumerateFiles(extractDir, exeName, SearchOption.AllDirectories).FirstOrDefault();
            if (found is null)
            {
                throw new InvalidOperationException("Archivio scaricato ma ffmpeg non trovato al suo interno.");
            }

            var target = Path.Combine(_toolsDir, exeName);
            File.Copy(found, target, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(
                        target,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                catch
                {
                    // best-effort chmod
                }
            }

            if (!VerifyRuns(target))
            {
                throw new InvalidOperationException("ffmpeg scaricato ma non eseguibile su questo sistema.");
            }

            await _settings.SetFfmpegPathAsync(target, CancellationToken.None);

            // Cleanup extract + archive to save disk
            TryDelete(archivePath);
            TryDeleteDir(extractDir);

            lock (_gate)
            {
                _downloadPercent = 100;
                _message = $"ffmpeg installato in tools/ffmpeg ({FormatBytes(_bytesDownloaded)}).";
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                _downloadPercent = null;
                _bytesDownloaded = 0;
                _message = "Download ffmpeg annullato.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ffmpeg download failed");
            lock (_gate)
            {
                _downloadPercent = null;
                _bytesDownloaded = 0;
                _message = "Download ffmpeg fallito. Controlla la rete e riprova, oppure installa ffmpeg nel PATH.";
            }
        }
        finally
        {
            lock (_gate)
            {
                _isDownloading = false;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }

    private static async Task ExtractTarXzAsync(string archivePath, string extractDir, CancellationToken ct)
    {
        // Prefer system tar (available on modern Linux).
        var psi = new ProcessStartInfo
        {
            FileName = "tar",
            ArgumentList = { "-xJf", archivePath, "-C", extractDir },
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Impossibile avviare tar per estrarre ffmpeg.");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException("Estrazione archivio ffmpeg fallita: " + Truncate(err, 200));
        }
    }

    private static bool VerifyRuns(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                ArgumentList = { "-version" },
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(8000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var mb = bytes / (1024.0 * 1024.0);
        return mb >= 1024 ? $"{mb / 1024.0:0.00} GB" : $"{mb:0.0} MB";
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        CancelDownload();
    }
}
