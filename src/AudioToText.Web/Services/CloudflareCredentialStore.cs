using System.Text.Json;
using System.Text.Json.Nodes;
using AudioToText.Web.Options;

namespace AudioToText.Web.Services;

public sealed class CloudflareSettingsStatus
{
    public bool AccountIdConfigured { get; init; }
    public bool ApiTokenConfigured { get; init; }
}

/// <summary>
/// In-memory Cloudflare credentials with persistence to a gitignored local JSON file.
/// Never expose values via API — status booleans only.
/// </summary>
public sealed class CloudflareCredentialStore
{
    public const string DefaultFileName = "appsettings.Local.json";

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _filePath;
    private readonly ILogger<CloudflareCredentialStore> _logger;
    private string _accountId;
    private string _apiToken;

    public CloudflareCredentialStore(
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<CloudflareCredentialStore> logger)
    {
        _logger = logger;
        var configuredPath = configuration["CloudflareLocalSettings:Path"];
        _filePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(environment.ContentRootPath, DefaultFileName)
            : configuredPath;

        var fromConfig = configuration.GetSection(CloudflareOptions.SectionName).Get<CloudflareOptions>()
            ?? new CloudflareOptions();
        _accountId = fromConfig.AccountId?.Trim() ?? string.Empty;
        _apiToken = fromConfig.ApiToken?.Trim() ?? string.Empty;

        LoadFromFileOverlay();
    }

    public string FilePath => _filePath;

    public CloudflareSettingsStatus GetStatus()
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

    public async Task SaveAsync(string? accountId, string? apiToken, CancellationToken cancellationToken)
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
            if (!doc.RootElement.TryGetProperty("Cloudflare", out var cf))
            {
                return;
            }

            // Local file is authoritative for keys it contains (including cleared/empty).
            if (cf.TryGetProperty("AccountId", out var accountEl))
            {
                _accountId = accountEl.GetString()?.Trim() ?? string.Empty;
            }

            if (cf.TryGetProperty("ApiToken", out var tokenEl))
            {
                _apiToken = tokenEl.GetString()?.Trim() ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read local Cloudflare settings file.");
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        string accountId;
        string apiToken;
        lock (_gate)
        {
            accountId = _accountId;
            apiToken = _apiToken;
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

        var tmp = _filePath + ".tmp";
        await File.WriteAllTextAsync(tmp, root.ToJsonString(JsonWriteOptions), cancellationToken);
        File.Move(tmp, _filePath, overwrite: true);
    }

    private static bool IsConfigured(string? value) => !string.IsNullOrWhiteSpace(value);
}
