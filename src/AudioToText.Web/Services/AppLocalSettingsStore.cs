using System.Text.Json;
using System.Text.Json.Nodes;
using AudioToText.Web.Options;

namespace AudioToText.Web.Services;

public sealed class CloudflareSettingsStatus
{
    public bool AccountIdConfigured { get; init; }
    public bool ApiTokenConfigured { get; init; }
}

public sealed class AppSettingsStatusDto
{
    public string Provider { get; init; } = TranscriptionProviders.CloudflareRest;
    public string LocalModel { get; init; } = "base";
    public CloudflareSettingsStatus Cloudflare { get; init; } = new();
    public LocalRuntimeStatusDto Local { get; init; } = new();
    public FfmpegStatusDto Ffmpeg { get; init; } = new();
}

public sealed class LocalRuntimeStatusDto
{
    public bool ModelReady { get; init; }
    public string? ModelPath { get; init; }
    public string ModelsDirectory { get; init; } = string.Empty;
    public string Runtime { get; init; } = "Unknown";
    public bool FfmpegAvailable { get; init; }
    public string FfmpegSource { get; init; } = "Mancante";
    public bool IsDownloading { get; init; }
    public int? DownloadPercent { get; init; }
    public long BytesDownloaded { get; init; }
    public long? BytesTotal { get; init; }
    public string? DownloadingModel { get; init; }
    public string? Message { get; init; }
    public string HardwareHint { get; init; } =
        "GTX 1050 Ti: tiny/base (CPU ok). RTX 5070: small (CUDA) o medium se VRAM basta.";
    public string FfmpegHint { get; init; } =
        "ffmpeg richiesto per vocali WhatsApp (ogg/opus/m4a/webm). WAV/MP3 ok senza. Usa «Scarica ffmpeg» in Impostazioni.";
    public IReadOnlyList<WhisperModelInfo> Models { get; init; } = WhisperModelCatalog.All;
    public WhisperModelInfo? SelectedModelInfo { get; init; }
}

/// <summary>
/// In-memory settings + gitignored appsettings.Local.json persistence.
/// Secrets never exposed via API status DTOs.
/// </summary>
public sealed class AppLocalSettingsStore
{
    public const string DefaultFileName = "appsettings.Local.json";

    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };
    private static readonly HashSet<string> AllowedModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "tiny", "base", "small", "medium"
    };

    private readonly object _gate = new();
    private readonly string _filePath;
    private readonly ILogger<AppLocalSettingsStore> _logger;
    private string _accountId;
    private string _apiToken;
    private string _provider;
    private string _localModel;
    private string _ffmpegPath;

    public AppLocalSettingsStore(
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<AppLocalSettingsStore> logger)
    {
        _logger = logger;
        var configuredPath = configuration["CloudflareLocalSettings:Path"];
        _filePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(environment.ContentRootPath, DefaultFileName)
            : configuredPath;

        var cf = configuration.GetSection(CloudflareOptions.SectionName).Get<CloudflareOptions>()
            ?? new CloudflareOptions();
        var tx = configuration.GetSection(TranscriptionOptions.SectionName).Get<TranscriptionOptions>()
            ?? new TranscriptionOptions();

        _accountId = cf.AccountId?.Trim() ?? string.Empty;
        _apiToken = cf.ApiToken?.Trim() ?? string.Empty;
        _provider = string.IsNullOrWhiteSpace(tx.Provider) ? TranscriptionProviders.CloudflareRest : tx.Provider.Trim();
        _localModel = NormalizeModel(tx.LocalModel);
        _ffmpegPath = configuration["Tools:FfmpegPath"]?.Trim() ?? string.Empty;

        LoadFromFileOverlay();
    }

    public string FilePath => _filePath;

    public CloudflareSettingsStatus GetCloudflareStatus()
    {
        lock (_gate)
        {
            return new CloudflareSettingsStatus
            {
                AccountIdConfigured = IsConfigured(_accountId),
                ApiTokenConfigured = IsConfigured(_apiToken)
            };
        }
    }

    public (string AccountId, string ApiToken) GetCredentials()
    {
        lock (_gate)
        {
            return (_accountId, _apiToken);
        }
    }

    public string GetProvider()
    {
        lock (_gate)
        {
            return _provider;
        }
    }

    public string GetLocalModel()
    {
        lock (_gate)
        {
            return _localModel;
        }
    }

    public string? GetFfmpegPath()
    {
        lock (_gate)
        {
            return string.IsNullOrWhiteSpace(_ffmpegPath) ? null : _ffmpegPath;
        }
    }

    public async Task SetFfmpegPathAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Percorso ffmpeg non valido.");
        }

        var normalized = Path.GetFullPath(path.Trim());
        lock (_gate)
        {
            _ffmpegPath = normalized;
        }

        await PersistAsync(cancellationToken);
        _logger.LogInformation("ffmpeg path set to {Path}.", normalized);
    }

    public async Task SaveCloudflareAsync(string? accountId, string? apiToken, CancellationToken cancellationToken)
    {
        var hasAccount = accountId is not null;
        var hasToken = apiToken is not null;
        if (!hasAccount && !hasToken)
        {
            throw new InvalidOperationException("Specifica Account ID e/o API Token da salvare.");
        }

        if (hasAccount)
        {
            accountId = accountId!.Trim();
            if (accountId.Length == 0)
            {
                throw new InvalidOperationException("Account ID non valido.");
            }
        }

        if (hasToken)
        {
            apiToken = apiToken!.Trim();
            if (apiToken.Length == 0)
            {
                throw new InvalidOperationException("API Token non valido.");
            }
        }

        lock (_gate)
        {
            if (hasAccount)
            {
                _accountId = accountId!;
            }

            if (hasToken)
            {
                _apiToken = apiToken!;
            }
        }

        await PersistAsync(cancellationToken);
        _logger.LogInformation(
            "Cloudflare settings updated (accountId={Account}, apiToken={Token}).",
            hasAccount ? "set" : "unchanged",
            hasToken ? "set" : "unchanged");
    }

    public async Task ClearAccountIdAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _accountId = string.Empty;
        }

        await PersistAsync(cancellationToken);
        _logger.LogInformation("Cloudflare Account ID removed from local settings.");
    }

    public async Task ClearApiTokenAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _apiToken = string.Empty;
        }

        await PersistAsync(cancellationToken);
        _logger.LogInformation("Cloudflare API Token removed from local settings.");
    }

    public async Task SetProviderAsync(string provider, CancellationToken cancellationToken)
    {
        if (!TranscriptionProviders.IsKnown(provider) ||
            string.Equals(provider, TranscriptionProviders.Fake, StringComparison.OrdinalIgnoreCase))
        {
            // Fake is test-only; UI exposes Cloudflare | Local
            if (!string.Equals(provider, TranscriptionProviders.CloudflareRest, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(provider, TranscriptionProviders.Local, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Provider non valido. Usa CloudflareRest o Local.");
            }
        }

        var normalized = string.Equals(provider, TranscriptionProviders.Local, StringComparison.OrdinalIgnoreCase)
            ? TranscriptionProviders.Local
            : TranscriptionProviders.CloudflareRest;

        lock (_gate)
        {
            _provider = normalized;
        }

        await PersistAsync(cancellationToken);
        _logger.LogInformation("Transcription provider set to {Provider}.", normalized);
    }

    public async Task SetLocalModelAsync(string model, CancellationToken cancellationToken)
    {
        var normalized = (model ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedModels.Contains(normalized))
        {
            throw new InvalidOperationException("Modello non valido. Usa tiny, base, small o medium.");
        }

        lock (_gate)
        {
            _localModel = normalized;
        }

        await PersistAsync(cancellationToken);
        _logger.LogInformation("Local Whisper model set to {Model}.", normalized);
    }

    private void LoadFromFileOverlay()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);

            if (doc.RootElement.TryGetProperty("Cloudflare", out var cf))
            {
                if (cf.TryGetProperty("AccountId", out var accountEl))
                {
                    _accountId = accountEl.GetString()?.Trim() ?? string.Empty;
                }

                if (cf.TryGetProperty("ApiToken", out var tokenEl))
                {
                    _apiToken = tokenEl.GetString()?.Trim() ?? string.Empty;
                }
            }

            if (doc.RootElement.TryGetProperty("Transcription", out var tx))
            {
                if (tx.TryGetProperty("Provider", out var providerEl))
                {
                    var p = providerEl.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(p) &&
                        (string.Equals(p, TranscriptionProviders.Local, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(p, TranscriptionProviders.CloudflareRest, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(p, TranscriptionProviders.Fake, StringComparison.OrdinalIgnoreCase)))
                    {
                        _provider = p;
                    }
                }

                if (tx.TryGetProperty("LocalModel", out var modelEl))
                {
                    _localModel = NormalizeModel(modelEl.GetString());
                }
            }

            if (doc.RootElement.TryGetProperty("Tools", out var tools) &&
                tools.TryGetProperty("FfmpegPath", out var ffmpegEl))
            {
                _ffmpegPath = ffmpegEl.GetString()?.Trim() ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read local settings file.");
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        string accountId;
        string apiToken;
        string provider;
        string localModel;
        string ffmpegPath;
        lock (_gate)
        {
            accountId = _accountId;
            apiToken = _apiToken;
            provider = _provider;
            localModel = _localModel;
            ffmpegPath = _ffmpegPath;
        }

        JsonObject root;
        if (File.Exists(_filePath))
        {
            try
            {
                var existing = await File.ReadAllTextAsync(_filePath, cancellationToken);
                root = string.IsNullOrWhiteSpace(existing)
                    ? new JsonObject()
                    : JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
            }
            catch
            {
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        var cloudflare = root["Cloudflare"] as JsonObject ?? new JsonObject();
        cloudflare["AccountId"] = accountId;
        cloudflare["ApiToken"] = apiToken;
        root["Cloudflare"] = cloudflare;

        var transcription = root["Transcription"] as JsonObject ?? new JsonObject();
        transcription["Provider"] = provider;
        transcription["LocalModel"] = localModel;
        root["Transcription"] = transcription;

        var tools = root["Tools"] as JsonObject ?? new JsonObject();
        tools["FfmpegPath"] = ffmpegPath;
        root["Tools"] = tools;

        var tmp = _filePath + ".tmp";
        await File.WriteAllTextAsync(tmp, root.ToJsonString(JsonWriteOptions), cancellationToken);
        File.Move(tmp, _filePath, overwrite: true);
    }

    private static string NormalizeModel(string? model)
    {
        var value = (model ?? "base").Trim().ToLowerInvariant();
        return AllowedModels.Contains(value) ? value : "base";
    }

    private static bool IsConfigured(string? value) => !string.IsNullOrWhiteSpace(value);
}
