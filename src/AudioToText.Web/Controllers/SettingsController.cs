using AudioToText.Web.Options;
using AudioToText.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudioToText.Web.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly AppLocalSettingsStore _store;
    private readonly ILocalWhisperRuntime _localRuntime;

    public SettingsController(AppLocalSettingsStore store, ILocalWhisperRuntime localRuntime)
    {
        _store = store;
        _localRuntime = localRuntime;
    }

    [HttpGet]
    public ActionResult<AppSettingsStatusDto> GetStatus()
    {
        return Ok(BuildStatus());
    }

    [HttpGet("cloudflare")]
    public ActionResult<CloudflareSettingsStatus> GetCloudflareStatus()
    {
        return Ok(_store.GetCloudflareStatus());
    }

    [HttpPut("cloudflare")]
    public async Task<ActionResult<CloudflareSettingsStatus>> SaveCloudflare(
        [FromBody] CloudflareSettingsUpdateRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Body mancante." });
        }

        string? accountId = request.AccountId;
        string? apiToken = request.ApiToken;

        try
        {
            await _store.SaveCloudflareAsync(accountId, apiToken, cancellationToken);
            return Ok(_store.GetCloudflareStatus());
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("cloudflare/account-id")]
    public async Task<ActionResult<CloudflareSettingsStatus>> ClearAccountId(
        CancellationToken cancellationToken)
    {
        await _store.ClearAccountIdAsync(cancellationToken);
        return Ok(_store.GetCloudflareStatus());
    }

    [HttpDelete("cloudflare/api-token")]
    public async Task<ActionResult<CloudflareSettingsStatus>> ClearApiToken(
        CancellationToken cancellationToken)
    {
        await _store.ClearApiTokenAsync(cancellationToken);
        return Ok(_store.GetCloudflareStatus());
    }

    [HttpPut("provider")]
    public async Task<ActionResult<AppSettingsStatusDto>> SetProvider(
        [FromBody] ProviderUpdateRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Provider))
        {
            return BadRequest(new { error = "Provider mancante." });
        }

        try
        {
            var provider = request.Provider.Trim() switch
            {
                "Cloudflare" => TranscriptionProviders.CloudflareRest,
                "Local" => TranscriptionProviders.Local,
                var p when TranscriptionProviders.IsKnown(p) => p,
                _ => throw new InvalidOperationException("Provider non valido.")
            };
            await _store.SetProviderAsync(provider, cancellationToken);
            return Ok(BuildStatus());
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("local-model")]
    public async Task<ActionResult<AppSettingsStatusDto>> SetLocalModel(
        [FromBody] LocalModelUpdateRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Model))
        {
            return BadRequest(new { error = "Modello mancante." });
        }

        try
        {
            await _store.SetLocalModelAsync(request.Model, cancellationToken);
            return Ok(BuildStatus());
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("local/download-model")]
    public async Task<ActionResult<AppSettingsStatusDto>> DownloadModel(
        [FromBody] LocalModelUpdateRequest? request,
        CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(request?.Model)
            ? _store.GetLocalModel()
            : request!.Model!.Trim();

        try
        {
            await _store.SetLocalModelAsync(model, cancellationToken);
            await _localRuntime.DownloadModelAsync(model, cancellationToken);
            return Ok(BuildStatus());
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private AppSettingsStatusDto BuildStatus()
    {
        var model = _store.GetLocalModel();
        return new AppSettingsStatusDto
        {
            Provider = _store.GetProvider(),
            LocalModel = model,
            Cloudflare = _store.GetCloudflareStatus(),
            Local = _localRuntime.GetStatus(model)
        };
    }
}

public sealed class CloudflareSettingsUpdateRequest
{
    public string? AccountId { get; set; }
    public string? ApiToken { get; set; }
}

public sealed class ProviderUpdateRequest
{
    public string? Provider { get; set; }
}

public sealed class LocalModelUpdateRequest
{
    public string? Model { get; set; }
}
