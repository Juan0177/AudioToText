namespace AudioToText.Web.Options;

public sealed class TranscriptionOptions
{
    public const string SectionName = "Transcription";

    /// <summary>CloudflareRest | Local | Fake</summary>
    public string Provider { get; set; } = "CloudflareRest";

    /// <summary>tiny | base | small | medium</summary>
    public string LocalModel { get; set; } = "base";

    public long MaxUploadBytes { get; set; } = 25 * 1024 * 1024;

    public string[] AllowedExtensions { get; set; } =
    [
        ".ogg", ".opus", ".m4a", ".mp3", ".wav", ".webm", ".mp4", ".mpeg", ".mpga", ".flac"
    ];
}

public static class TranscriptionProviders
{
    public const string CloudflareRest = "CloudflareRest";
    public const string Local = "Local";
    public const string Fake = "Fake";

    public static bool IsKnown(string? provider) =>
        string.Equals(provider, CloudflareRest, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, Local, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, Fake, StringComparison.OrdinalIgnoreCase);
}
