using AudioToText.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudioToText.Web.Controllers;

[ApiController]
[Route("api/settings/cloudflare")]
public sealed class SettingsController : ControllerBase
{
    private readonly CloudflareCredentialStore _store;

    public SettingsController(CloudflareCredentialStore store)
    {
        _store = store;
    }

    [HttpGet]
    public ActionResult<CloudflareSettingsStatus> GetStatus()
    {
        return Ok(_store.GetStatus());
    }

    [HttpPut]
    public async Task<ActionResult<CloudflareSettingsStatus>> Save(
        [FromBody] CloudflareSettingsUpdateRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Body mancante." });
        }

        // Only apply fields that were explicitly sent (non-null).
        string? accountId = null;
        string? apiToken = null;
        if (request.AccountId is not null)
        {
            accountId = request.AccountId;
        }

        if (request.ApiToken is not null)
        {
            apiToken = request.ApiToken;
        }

        try
        {
            await _store.SaveAsync(accountId, apiToken, cancellationToken);
            return Ok(_store.GetStatus());
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("account-id")]
    public async Task<ActionResult<CloudflareSettingsStatus>> ClearAccountId(
        CancellationToken cancellationToken)
    {
        await _store.ClearAccountIdAsync(cancellationToken);
        return Ok(_store.GetStatus());
    }

    [HttpDelete("api-token")]
    public async Task<ActionResult<CloudflareSettingsStatus>> ClearApiToken(
        CancellationToken cancellationToken)
    {
        await _store.ClearApiTokenAsync(cancellationToken);
        return Ok(_store.GetStatus());
    }
}

public sealed class CloudflareSettingsUpdateRequest
{
    public string? AccountId { get; set; }
    public string? ApiToken { get; set; }
}
