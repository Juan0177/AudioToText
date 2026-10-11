using System.Diagnostics;
using System.IO.Compression;
using System.Net;

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
        "Download da GitHub (BtbN) ~80–90 MB. Serve per ogg/opus/m4a/webm (WhatsApp). WAV/MP3 possono funzionare senza.";
}

public interface IFfmpegToolService
{
    FfmpegStatusDto GetStatus();
    string? ResolveExecutablePath();
    void StartDownload();
    void CancelDownload();
}

/// <summary>
/// Resolves ffmpeg (bundled tools/ffmpeg → env → PATH) and downloads a portable
/// Windows/Linux build from GitHub (BtbN/FFmpeg-Builds) — fast vs gyan.dev from EU.
/// </summary>
public sealed class FfmpegToolService : IFfmpegToolService, IDisposable
{
    // Pinned autobuild (n8.1 shared — smaller, GitHub CDN). Floating "latest" as fallback.
    private const string WindowsPrimaryUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-11-13-04/ffmpeg-n8.1.3-17-gc389840894-win64-gpl-shared-8.1.zip";
    private const string WindowsFallbackUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip";

    private const string LinuxPrimaryUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-11-13-04/ffmpeg-n8.1.3-17-gc389840894-linux64-gpl-shared-8.1.tar.xz";
    private const string LinuxFallbackUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linux64-gpl-shared.tar.xz";

    private const long ApproxWindowsBytes = 90L * 1024 * 1024;
    private const long ApproxLinuxBytes = 70L * 1024 * 1024;
    private const int CopyBufferSize = 512 * 1024;

    private readonly object _gate = new();
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

    // Cache resolution so progress polling does not spawn ffmpeg -version repeatedly.
    private string? _resolvedPath;
    private FfmpegSourceKind _resolvedKind = FfmpegSourceKind.Missing;
    private DateTime _resolvedAtUtc = DateTime.MinValue;

    public FfmpegToolService(
        IHostEnvironment env,
        AppLocalSettingsStore settings,
        IHttpClientFactory httpClientFactory,
        ILogger<FfmpegToolService> logger)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _toolsDir = Path.Combine(env.ContentRootPath, "tools", "ffmpeg");
        Directory.CreateDirectory(_toolsDir);
    }

    public FfmpegStatusDto GetStatus()
    {
        string? path;
        FfmpegSourceKind kind;
        lock (_gate)
        {
            if (_isDownloading)
            {
                // Avoid PATH/exe probes while downloading — progress is what matters.
                path = _resolvedPath;
                kind = path is not null ? _resolvedKind : FfmpegSourceKind.Missing;
                return new FfmpegStatusDto
                {
                    Available = path is not null,
                    Source = SourceLabel(kind),
                    SourceKind = kind.ToString(),
                    Path = path,
                    IsDownloading = true,
                    DownloadPercent = _downloadPercent,
                    BytesDownloaded = _bytesDownloaded,
                    BytesTotal = _bytesTotal ?? (OperatingSystem.IsWindows() ? ApproxWindowsBytes : ApproxLinuxBytes),
                    Message = _message ?? "Download ffmpeg da GitHub…"
                };
            }
        }

        (path, kind) = ResolveWithKind();
        lock (_gate)
        {
            return new FfmpegStatusDto
            {
                Available = path is not null,
                Source = SourceLabel(kind),
                SourceKind = kind.ToString(),
                Path = path,
                IsDownloading = _isDownloading,
                DownloadPercent = _downloadPercent,
                BytesDownloaded = _bytesDownloaded,
                BytesTotal = _bytesTotal ?? (OperatingSystem.IsWindows() ? ApproxWindowsBytes : ApproxLinuxBytes),
                Message = _message ?? (path is null
                    ? "ffmpeg non installato. Usa «Scarica ffmpeg» (download da GitHub)."
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
                InvalidateCache();
                return;
            }

            _isDownloading = true;
            _downloadPercent = 0;
            _bytesDownloaded = 0;
            _bytesTotal = OperatingSystem.IsWindows() ? ApproxWindowsBytes : ApproxLinuxBytes;
            _message = "Avvio download ffmpeg da GitHub…";
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

    private static string SourceLabel(FfmpegSourceKind kind) => kind switch
    {
        FfmpegSourceKind.Bundled => "Trovato locale (tools/ffmpeg)",
        FfmpegSourceKind.Path => "Trovato nel PATH di sistema",
        FfmpegSourceKind.Env => "Trovato via FFMPEG_PATH",
        _ => "Mancante"
    };

    private (string? Path, FfmpegSourceKind Kind) ResolveWithKind()
    {
        lock (_gate)
        {
            if (_resolvedPath is not null &&
                DateTime.UtcNow - _resolvedAtUtc < TimeSpan.FromSeconds(30) &&
                File.Exists(_resolvedPath))
            {
                return (_resolvedPath, _resolvedKind);
            }
        }

        var bundled = ResolveBundledPath();
        if (bundled is not null)
        {
            return Cache(bundled, FfmpegSourceKind.Bundled);
        }

        var persisted = _settings.GetFfmpegPath();
        if (!string.IsNullOrWhiteSpace(persisted) && File.Exists(persisted) && VerifyRuns(persisted))
        {
            return Cache(persisted, FfmpegSourceKind.Bundled);
        }

        var fromEnv = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv) && VerifyRuns(fromEnv))
        {
            return Cache(fromEnv, FfmpegSourceKind.Env);
        }

        var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, exeName);
            if (File.Exists(candidate) && VerifyRuns(candidate))
            {
                return Cache(candidate, FfmpegSourceKind.Path);
            }
        }

        return Cache(null, FfmpegSourceKind.Missing);
    }

    private (string? Path, FfmpegSourceKind Kind) Cache(string? path, FfmpegSourceKind kind)
    {
        lock (_gate)
        {
            _resolvedPath = path;
            _resolvedKind = kind;
            _resolvedAtUtc = DateTime.UtcNow;
        }

        return (path, kind);
    }

    private void InvalidateCache()
    {
        lock (_gate)
        {
            _resolvedPath = null;
            _resolvedKind = FfmpegSourceKind.Missing;
            _resolvedAtUtc = DateTime.MinValue;
        }
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
        var urls = isWindows
            ? new[] { WindowsPrimaryUrl, WindowsFallbackUrl }
            : new[] { LinuxPrimaryUrl, LinuxFallbackUrl };
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

            Exception? lastError = null;
            var downloaded = false;
            foreach (var url in urls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    lock (_gate)
                    {
                        _downloadPercent = 0;
                        _bytesDownloaded = 0;
                        _message = $"Download ffmpeg da GitHub… ({HostOf(url)})";
                    }

                    await DownloadUrlAsync(url, archivePath, isWindows, cancellationToken);
                    downloaded = true;
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    _logger.LogWarning(ex, "ffmpeg download failed from {Url}, trying next mirror", url);
                    TryDelete(archivePath);
                    lock (_gate)
                    {
                        _message = $"Mirror lento/fallito ({HostOf(url)}), provo fallback…";
                    }
                }
            }

            if (!downloaded)
            {
                throw lastError ?? new InvalidOperationException("Download ffmpeg fallito da tutti i mirror.");
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

            // Shared builds need DLLs/.so next to the exe — copy the whole bin folder.
            var binDir = Path.GetDirectoryName(found)
                ?? throw new InvalidOperationException("Percorso ffmpeg non valido nell’archivio.");
            CopyDirectoryContents(binDir, _toolsDir);

            var target = Path.Combine(_toolsDir, exeName);
            if (!File.Exists(target))
            {
                File.Copy(found, target, overwrite: true);
            }

            if (!OperatingSystem.IsWindows())
            {
                TryChmodExecutable(target);
            }

            if (!VerifyRuns(target))
            {
                throw new InvalidOperationException("ffmpeg scaricato ma non eseguibile su questo sistema.");
            }

            await _settings.SetFfmpegPathAsync(target, CancellationToken.None);
            InvalidateCache();
            Cache(target, FfmpegSourceKind.Bundled);

            TryDelete(archivePath);
            TryDeleteDir(extractDir);

            lock (_gate)
            {
                _downloadPercent = 100;
                _message = $"ffmpeg installato in tools/ffmpeg ({FormatBytes(_bytesDownloaded)}) — GitHub BtbN.";
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
                _message =
                    "Download ffmpeg fallito. Controlla la rete e riprova (mirror GitHub), oppure installa ffmpeg nel PATH.";
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

    private async Task DownloadUrlAsync(
        string url,
        string archivePath,
        bool isWindows,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("ffmpeg-tools");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength
            ?? (isWindows ? ApproxWindowsBytes : ApproxLinuxBytes);
        lock (_gate)
        {
            _bytesTotal = total;
            _message = $"Download ffmpeg da GitHub… {FormatBytes(0)} / {FormatBytes(total)}";
        }

        await using var remote = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = new FileStream(
            archivePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[CopyBufferSize];
        long written = 0;
        var lastProgressUtc = DateTime.MinValue;
        int read;
        while ((read = await remote.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            written += read;

            // Throttle UI progress updates — never block the download loop on polling.
            var now = DateTime.UtcNow;
            if ((now - lastProgressUtc).TotalMilliseconds >= 200 || written >= total)
            {
                lastProgressUtc = now;
                var percent = total > 0 ? (int)Math.Min(99, written * 100 / total) : 0;
                lock (_gate)
                {
                    _bytesDownloaded = written;
                    _bytesTotal = total;
                    _downloadPercent = percent;
                    _message = $"Download ffmpeg (GitHub): {FormatBytes(written)} / {FormatBytes(total)} ({percent}%)";
                }
            }
        }

        lock (_gate)
        {
            _bytesDownloaded = written;
            _bytesTotal = written > 0 ? written : total;
            _downloadPercent = 99;
        }
    }

    private static void CopyDirectoryContents(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(destDir, name), overwrite: true);
        }
    }

    private static async Task ExtractTarXzAsync(string archivePath, string extractDir, CancellationToken ct)
    {
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
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
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

    private static void TryChmodExecutable(string target)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

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
            // best-effort
        }
    }

    private static string HostOf(string url)
    {
        try
        {
            return new Uri(url).Host;
        }
        catch
        {
            return "mirror";
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
