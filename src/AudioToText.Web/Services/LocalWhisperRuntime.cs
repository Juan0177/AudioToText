using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace AudioToText.Web.Services;

public interface ILocalWhisperRuntime
{
    LocalRuntimeStatusDto GetStatus(string selectedModel);
    Task DownloadModelAsync(string model, CancellationToken cancellationToken);
    Task<string> TranscribeWavAsync(byte[] wav16kMono, string? language, string model, CancellationToken cancellationToken);
}

/// <summary>
/// Whisper.net runtime: CUDA first, CPU fallback; models cached under ContentRoot/models/whisper.
/// </summary>
public sealed class LocalWhisperRuntime : ILocalWhisperRuntime, IDisposable
{
    private readonly object _gate = new();
    private readonly IHostEnvironment _environment;
    private readonly ILogger<LocalWhisperRuntime> _logger;
    private readonly AudioPcmConverter _audio;
    private readonly string _modelsDir;

    private string _runtimeLabel = "Unknown";
    private bool _runtimeProbed;
    private bool _isDownloading;
    private int? _downloadPercent;
    private string? _downloadMessage;
    private WhisperFactory? _factory;
    private string? _loadedModelPath;
    private bool _useGpu;

    public LocalWhisperRuntime(
        IHostEnvironment environment,
        IConfiguration configuration,
        AudioPcmConverter audio,
        ILogger<LocalWhisperRuntime> logger)
    {
        _environment = environment;
        _audio = audio;
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
        var path = GetModelPath(selectedModel);
        var ready = File.Exists(path) && new FileInfo(path).Length > 1_000_000;

        lock (_gate)
        {
            return new LocalRuntimeStatusDto
            {
                ModelReady = ready,
                ModelPath = ready ? path : null,
                ModelsDirectory = _modelsDir,
                Runtime = _runtimeLabel,
                FfmpegAvailable = _audio.IsFfmpegAvailable(),
                IsDownloading = _isDownloading,
                DownloadPercent = _downloadPercent,
                Message = _downloadMessage ?? (ready
                    ? $"Modello {selectedModel} pronto ({_runtimeLabel})."
                    : $"Modello {selectedModel} non scaricato. Usa Scarica modello.")
            };
        }
    }

    public async Task DownloadModelAsync(string model, CancellationToken cancellationToken)
    {
        model = model.Trim().ToLowerInvariant();
        var path = GetModelPath(model);
        var ggml = WhisperModelCatalog.ToGgmlType(model);

        lock (_gate)
        {
            if (_isDownloading)
            {
                throw new InvalidOperationException("Download già in corso.");
            }

            _isDownloading = true;
            _downloadPercent = 0;
            _downloadMessage = $"Download {model}…";
        }

        try
        {
            Directory.CreateDirectory(_modelsDir);
            var tmp = path + ".download";
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }

            await using (var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(
                             ggml,
                             QuantizationType.NoQuantization,
                             cancellationToken))
            await using (var file = File.Create(tmp))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;
                // Hugging Face may not send Content-Length; report MB written.
                while ((read = await modelStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                    lock (_gate)
                    {
                        _downloadPercent = (int)Math.Min(99, written / (1024 * 1024)); // MB as coarse progress
                        _downloadMessage = $"Download {model}: {written / (1024 * 1024)} MB…";
                    }
                }
            }

            File.Move(tmp, path, overwrite: true);
            InvalidateFactory();

            lock (_gate)
            {
                _downloadPercent = 100;
                _downloadMessage = $"Modello {model} scaricato.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Model download failed for {Model}", model);
            lock (_gate)
            {
                _downloadMessage = "Download modello fallito. Riprova.";
            }

            throw new InvalidOperationException("Download del modello Whisper fallito. Controlla la rete e riprova.");
        }
        finally
        {
            lock (_gate)
            {
                _isDownloading = false;
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

                // Probe by reading LoadedLibrary after a tiny factory lifecycle needs a model;
                // until a model exists, report preferred order intent.
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

    public void Dispose()
    {
        InvalidateFactory();
    }
}
