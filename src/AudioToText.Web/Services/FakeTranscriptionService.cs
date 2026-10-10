namespace AudioToText.Web.Services;

/// <summary>Dev/test stub when Cloudflare credentials are not configured.</summary>
public sealed class FakeTranscriptionService : ITranscriptionService
{
    public Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        string fileName,
        string contentType,
        string? language,
        CancellationToken cancellationToken)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "auto" : language;
        var text =
            $"[Fake] Trascrizione di prova per «{fileName}» (lingua={lang}). " +
            "Configura Cloudflare:AccountId e Cloudflare:ApiToken per usare Workers AI Whisper.";

        return Task.FromResult(new TranscriptionResult(text, language, 0));
    }
}
