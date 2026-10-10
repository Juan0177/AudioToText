namespace AudioToText.Web.Options;

public sealed class TranscriptionOptions
{
    public const string SectionName = "Transcription";

    /// <summary>CloudflareRest | Fake</summary>
    public string Provider { get; set; } = "CloudflareRest";

    public long MaxUploadBytes { get; set; } = 25 * 1024 * 1024;

    public string[] AllowedExtensions { get; set; } =
    [
        ".ogg", ".opus", ".m4a", ".mp3", ".wav", ".webm", ".mp4", ".mpeg", ".mpga", ".flac"
    ];
}
