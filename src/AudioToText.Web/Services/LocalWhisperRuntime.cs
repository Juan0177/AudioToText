using Whisper.net;
using Whisper.net.LibraryLoader;

namespace AudioToText.Web.Services;

public interface ILocalWhisperRuntime
{
    LocalRuntimeStatusDto GetStatus(string selectedModel);
    void StartDownload(string model);
    void CancelDownload();
    Task<string> TranscribeWavAsync(byte[] wav16kMono, string? language, string model, CancellationToken cancellationToken);
}

/// <summary>
/// Whisper.net runtime: CUDA first, CPU fallback; models cached under ContentRoot/models/whisper.
/// Downloads via HttpClient with byte-level progress (poll GetStatus).
/// </summary>
public sealed class LocalWhisperRuntime : ILocalWhisperRuntime, IDisposable
{
    private readonly object _gate = new();
    private readonly ILogger<LocalWhisperRuntime> _logger;
    private readonly AudioPcmConverter _audio;
    private readonly IFfmpegToolService _ffmpeg;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _modelsDir;

    private string _runtimeLabel = "Unknown";
    private bool _runtimeProbed;
    private bool _isDownloading;
    private int? _downloadPercent;
    private long _bytesDownloaded;
    private long? _bytesTotal;
    private string? _downloadingModel;
    private string? _downloadMessage;
    private CancellationTokenSource? _downloadCts;
    private WhisperFactory? _factory;
    private string? _loadedModelPath;
    private bool _useGpu;

    public LocalWhisperRuntime(
        IHostEnvironment environment,
        IConfiguration configuration,
        AudioPcmConverter audio,
        IFfmpegToolService ffmpeg,
        IHttpClientFactory httpClientFactory,
        ILogger<LocalWhisperRuntime> logger)
    {
        _audio = audio;
        _ffmpeg = ffmpeg;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var configured = configuration["Transcription:ModelsPath"];
        _modelsDir = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "models", "whisper")
            : configured;
        Directory.CreateDirectory(_modelsDir);
    }

    public string ModelsDirectory => _modelsDir;

    public LocalRuntimeStatusDto GetStatus(string selectedModel)
    {
        EnsureRuntimeProbed();
        var info = WhisperModelCatalog.Get(selectedModel);
        var path = GetModelPath(selectedModel);
        var ready = File.Exists(path) && new FileInfo(path).Length > 1_000_000;
        var ffmpegStatus = _ffmpeg.GetStatus();

        lock (_gate)
        {
            return new LocalRuntimeStatusDto
            {
                ModelReady = ready,
                ModelPath = ready ? path : null,
                ModelsDirectory = _modelsDir,
                Runtime = _runtimeLabel,
                FfmpegAvailable = ffmpegStatus.Available,
                FfmpegSource = ffmpegStatus.Source,
                IsDownloading = _isDownloading,
                DownloadPercent = _downloadPercent,
                BytesDownloaded = _bytesDownloaded,
                BytesTotal = _bytesTotal ?? info.ApproxBytes,
                DownloadingModel = _downloadingModel,
                Message = _downloadMessage ?? (ready
                    ? $"Modello {selectedModel} pronto ({_runtimeLabel})."
                    : $"Modello {selectedModel} non scaricato. Usa Scarica modello."),
                Models = WhisperModelCatalog.All,
                SelectedModelInfo = info
            };
        }
    }

    public void StartDownload(string model)
    {
        model = WhisperModelCatalog.Get(model).Id;

        lock (_gate)
        {
            if (_isDownloading)
            {
                throw new InvalidOperationException("Download già in corso.");
            }

            _isDownloading = true;
            _downloadPercent = 0;
            _bytesDownloaded = 0;
            _bytesTotal = WhisperModelCatalog.Get(model).ApproxBytes;
            _downloadingModel = model;
            _downloadMessage = $"Avvio download {model}…";
            _downloadCts = new CancellationTokenSource();
            var token = _downloadCts.Token;
            _ = Task.Run(() => DownloadCoreAsync(model, token), CancellationToken.None);
        }
    }

    public void CancelDownload()
    {
        lock (_gate)
        {
            _downloadCts?.Cancel();
            _downloadMessage = "Annullamento download…";
        }
    }

    private async Task DownloadCoreAsync(string model, CancellationToken cancellationToken)
    {
        var path = GetModelPath(model);
        var expected = WhisperModelCatalog.Get(model).ApproxBytes;
        var tmp = path + ".download";

        try
        {
            Directory.CreateDirectory(_modelsDir);
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }

            var client = _httpClientFactory.CreateClient("whisper-models");
            using var response = await client.GetAsync(
                WhisperModelCatalog.HuggingFaceUrl(model),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? expected;
            lock (_gate)
            {
                _bytesTotal = total;
                _downloadMessage = $"Download {model}…";
            }

            await using var remote = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = File.Create(tmp);

            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await remote.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
                var percent = total > 0
                    ? (int)Math.Min(99, written * 100 / total)
                    : (int)Math.Min(99, written * 100 / expected);

                lock (_gate)
                {
                    _bytesDownloaded = written;
                    _bytesTotal = total;
                    _downloadPercent = percent;
                    _downloadMessage =
                        $"Download {model}: {FormatBytes(written)} / {FormatBytes(total)} ({percent}%)";
                }
            }

            await file.FlushAsync(cancellationToken);
            File.Move(tmp, path, overwrite: true);
            InvalidateFactory();

            lock (_gate)
            {
                _bytesDownloaded = written;
                _downloadPercent = 100;
                _downloadMessage = $"Modello {model} scaricato ({FormatBytes(written)}).";
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(tmp);
            lock (_gate)
            {
                _downloadPercent = null;
                _bytesDownloaded = 0;
                _downloadMessage = "Download annullato.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Model download failed for {Model}", model);
            TryDelete(tmp);
            lock (_gate)
            {
                _downloadPercent = null;
                _bytesDownloaded = 0;
                _downloadMessage = "Download modello fallito. Controlla la rete e riprova.";
            }
        }
        finally
        {
            lock (_gate)
            {
                _isDownloading = false;
                _downloadingModel = null;
                _downloadCts?.Dispose();
                _downloadCts = null;
            }
        }
    }

    public async Task<string> TranscribeWavAsync(
        byte[] wav16kMono,
        string? language,
        string model,
        CancellationToken cancellationToken)
    {
        var path = GetModelPath(model);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Modello locale «{model}» non trovato. Vai in Impostazioni → Scarica modello.");
        }

        EnsureRuntimeProbed();
        var factory = GetOrCreateFactory(path);

        try
        {
            return await ProcessWithFactoryAsync(factory, wav16kMono, language, cancellationToken);
        }
        catch (Exception ex) when (_useGpu)
        {
            _logger.LogWarning(ex, "GPU transcription failed; falling back to CPU.");
            ForceCpuRuntime();
            InvalidateFactory();
            factory = GetOrCreateFactory(path);
            return await ProcessWithFactoryAsync(factory, wav16kMono, language, cancellationToken);
        }
    }

    private async Task<string> ProcessWithFactoryAsync(
        WhisperFactory factory,
        byte[] wav16kMono,
        string? language,
        CancellationToken cancellationToken)
    {
        var builder = factory.CreateBuilder();
        if (string.IsNullOrWhiteSpace(language))
        {
            builder.WithLanguageDetection();
        }
        else
        {
            builder.WithLanguage(language);
        }

        await using var processor = builder.Build();
        await using var stream = new MemoryStream(wav16kMono, writable: false);

        var parts = new List<string>();
        await foreach (var segment in processor.ProcessAsync(stream, cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(segment.Text))
            {
                parts.Add(segment.Text.Trim());
            }
        }

        var text = string.Join(' ', parts).Trim();
        return string.IsNullOrEmpty(text) ? "(Nessun parlato rilevato)" : text;
    }

    private WhisperFactory GetOrCreateFactory(string modelPath)
    {
        lock (_gate)
        {
            if (_factory is not null &&
                string.Equals(_loadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                return _factory;
            }

            _factory?.Dispose();
            _factory = WhisperFactory.FromPath(
                modelPath,
                new WhisperFactoryOptions { UseGpu = _useGpu });
            _loadedModelPath = modelPath;
            return _factory;
        }
    }

    private void EnsureRuntimeProbed()
    {
        lock (_gate)
        {
            if (_runtimeProbed)
            {
                return;
            }

            try
            {
                RuntimeOptions.RuntimeLibraryOrder =
                [
                    RuntimeLibrary.Cuda,
                    RuntimeLibrary.Cuda12,
                    RuntimeLibrary.Cpu,
                    RuntimeLibrary.CpuNoAvx
                ];
                _useGpu = true;
                _runtimeLabel = RuntimeOptions.LoadedLibrary?.ToString() ?? "CUDA→CPU (auto)";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CUDA probe failed; using CPU.");
                ForceCpuRuntime_NoLock();
            }

            _runtimeProbed = true;
        }
    }

    private void ForceCpuRuntime()
    {
        lock (_gate)
        {
            ForceCpuRuntime_NoLock();
        }
    }

    private void ForceCpuRuntime_NoLock()
    {
        RuntimeOptions.RuntimeLibraryOrder =
        [
            RuntimeLibrary.Cpu,
            RuntimeLibrary.CpuNoAvx
        ];
        _useGpu = false;
        _runtimeLabel = "CPU";
    }

    private void InvalidateFactory()
    {
        lock (_gate)
        {
            _factory?.Dispose();
            _factory = null;
            _loadedModelPath = null;
        }
    }

    public string GetModelPath(string model) =>
        Path.Combine(_modelsDir, WhisperModelCatalog.ToFileName(model));

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double mb = bytes / (1024.0 * 1024.0);
        return mb >= 1024 ? $"{mb / 1024.0:0.00} GB" : $"{mb:0.0} MB";
    }

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

    public void Dispose()
    {
        CancelDownload();
        InvalidateFactory();
    }
}
