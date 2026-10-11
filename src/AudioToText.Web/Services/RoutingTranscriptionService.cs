using AudioToText.Web.Options;

namespace AudioToText.Web.Services;

/// <summary>
/// Routes to Cloudflare, Local, or Fake based on runtime settings (hot-switchable).
/// </summary>
public sealed class RoutingTranscriptionService : ITranscriptionService
{
    private readonly AppLocalSettingsStore _settings;
    private readonly CloudflareWorkersAiTranscriptionService _cloudflare;
    private readonly LocalWhisperTranscriptionService _local;
    private readonly FakeTranscriptionService _fake;

    public RoutingTranscriptionService(
        AppLocalSettingsStore settings,
        CloudflareWorkersAiTranscriptionService cloudflare,
        LocalWhisperTranscriptionService local,
        FakeTranscriptionService fake)
    {
        _settings = settings;
        _cloudflare = cloudflare;
        _local = local;
        _fake = fake;
    }

    public Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        string fileName,
        string contentType,
        string? language,
        CancellationToken cancellationToken)
    {
        var provider = _settings.GetProvider();
        if (string.Equals(provider, TranscriptionProviders.Fake, StringComparison.OrdinalIgnoreCase))
        {
            return _fake.TranscribeAsync(audioStream, fileName, contentType, language, cancellationToken);
        }

        if (string.Equals(provider, TranscriptionProviders.Local, StringComparison.OrdinalIgnoreCase))
        {
            return _local.TranscribeAsync(audioStream, fileName, contentType, language, cancellationToken);
        }

        return _cloudflare.TranscribeAsync(audioStream, fileName, contentType, language, cancellationToken);
    }
}
