namespace AudioToText.Web.Services;

public sealed class LocalWhisperTranscriptionService : ITranscriptionService
{
    private readonly AppLocalSettingsStore _settings;
    private readonly ILocalWhisperRuntime _runtime;
    private readonly AudioPcmConverter _converter;
    private readonly ILogger<LocalWhisperTranscriptionService> _logger;

    public LocalWhisperTranscriptionService(
        AppLocalSettingsStore settings,
        ILocalWhisperRuntime runtime,
        AudioPcmConverter converter,
        ILogger<LocalWhisperTranscriptionService> logger)
    {
        _settings = settings;
        _runtime = runtime;
        _converter = converter;
        _logger = logger;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        string fileName,
        string contentType,
        string? language,
        CancellationToken cancellationToken)
    {
        var model = _settings.GetLocalModel();
        var status = _runtime.GetStatus(model);
        if (!status.ModelReady)
        {
            throw new InvalidOperationException(
                $"Modello locale «{model}» non pronto. Apri Impostazioni e scarica il modello.");
        }

        byte[] wav;
        try
        {
            wav = await _converter.ToWav16kMonoAsync(audioStream, fileName, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio conversion failed for {File}", fileName);
            throw new InvalidOperationException(
                "Impossibile convertire l’audio. Per ogg/opus usa Impostazioni → Scarica ffmpeg; altrimenti usa wav/mp3.");
        }

        var text = await _runtime.TranscribeWavAsync(wav, language, model, cancellationToken);
        return new TranscriptionResult(text, language, null);
    }
}
