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
    private readonly CloudflareOptions _options;
    private readonly ILogger<CloudflareWorkersAiTranscriptionService> _logger;

    public CloudflareWorkersAiTranscriptionService(
        HttpClient httpClient,
        IOptions<CloudflareOptions> options,
        ILogger<CloudflareWorkersAiTranscriptionService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        string fileName,
        string contentType,
        string? language,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AccountId) ||
            string.IsNullOrWhiteSpace(_options.ApiToken))
        {
            throw new InvalidOperationException(
                "Cloudflare AccountId/ApiToken non configurati. Imposta i secret o usa Transcription:Provider=Fake.");
        }

        await using var memory = new MemoryStream();
        await audioStream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("File audio vuoto.");
        }

        var modelSegment = Uri.EscapeDataString(_options.Model);
        var url = $"{_options.BaseUrl.TrimEnd('/')}/accounts/{_options.AccountId}/ai/run/{modelSegment}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);

        // Binary body is the documented ASR input for @cf/openai/whisper.
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
