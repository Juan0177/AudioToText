namespace AudioToText.Web.Options;

public sealed class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    public string AccountId { get; set; } = string.Empty;

    public string ApiToken { get; set; } = string.Empty;

    /// <summary>Workers AI model id, e.g. @cf/openai/whisper</summary>
    public string Model { get; set; } = "@cf/openai/whisper";

    public string BaseUrl { get; set; } = "https://api.cloudflare.com/client/v4";
}
