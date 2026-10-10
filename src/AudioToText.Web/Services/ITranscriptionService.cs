namespace AudioToText.Web.Services;

public interface ITranscriptionService
{
    Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        string fileName,
        string contentType,
        string? language,
        CancellationToken cancellationToken);
}

public sealed record TranscriptionResult(
    string Text,
    string? DetectedLanguage,
    int? WordCount);
