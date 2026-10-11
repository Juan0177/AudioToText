using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AudioToText.Web.Options;
using Microsoft.Extensions.Options;

namespace AudioToText.Web.Services;

public sealed class CloudflareWorkersAiTranscriptionService : ITranscriptionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly CloudflareCredentialStore _credentials;
    private readonly IOptionsMonitor<CloudflareOptions> _options;
    private readonly ILogger<CloudflareWorkersAiTranscriptionService> _logger;

    public CloudflareWorkersAiTranscriptionService(
        HttpClient httpClient,
        CloudflareCredentialStore credentials,
        IOptionsMonitor<CloudflareOptions> options,
        ILogger<CloudflareWorkersAiTranscriptionService> logger)
    {
        _httpClient = httpClient;
        _credentials = credentials;
        _options = options;
        _logger = logger;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        string fileName,
        string contentType,
        string? language,
        CancellationToken cancellationToken)
    {
        var (accountId, apiToken) = _credentials.GetCredentials();
        var options = _options.CurrentValue;

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(apiToken))
        {
            throw new InvalidOperationException(
                "Cloudflare Account ID / API Token non configurati. Apri Impostazioni e salvali localmente.");
        }

        await using var memory = new MemoryStream();
        await audioStream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("File audio vuoto.");
        }

        var url = BuildRunUrl(options.BaseUrl, accountId, options.Model);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        var mediaType = string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType;
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Workers AI failed ({Status}) for {File}: {Body}",
                (int)response.StatusCode,
                fileName,
                Truncate(body, 500));
            throw new InvalidOperationException(
                "Trascrizione non riuscita sul servizio Cloudflare. Controlla token, account e modello.");
        }

        var envelope = JsonSerializer.Deserialize<CfAiEnvelope>(body, JsonOptions);
        if (envelope?.Success != true || envelope.Result is null)
        {
            var err = envelope?.Errors?.FirstOrDefault()?.Message ?? "Risposta Workers AI non valida.";
            throw new InvalidOperationException(err);
        }

        var text = (envelope.Result.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            text = "(Nessun parlato rilevato)";
        }

        return new TranscriptionResult(text, language, envelope.Result.WordCount);
    }

    /// <summary>
    /// Workers AI model ids are path segments (e.g. @cf/openai/whisper).
    /// Do not EscapeDataString the whole model — that encodes '/' and breaks CF routing.
    /// </summary>
    public static string BuildRunUrl(string baseUrl, string accountId, string model)
    {
        var modelPath = model.Trim().TrimStart('/');
        return $"{baseUrl.TrimEnd('/')}/accounts/{accountId}/ai/run/{modelPath}";
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private sealed class CfAiEnvelope
    {
        public bool Success { get; set; }
        public CfAiResult? Result { get; set; }
        public List<CfAiError>? Errors { get; set; }
    }

    private sealed class CfAiResult
    {
        public string? Text { get; set; }

        [JsonPropertyName("word_count")]
        public int? WordCount { get; set; }
    }

    private sealed class CfAiError
    {
        public string? Message { get; set; }
    }
}
