using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AudioToText.Tests;

public class SettingsApiTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public SettingsApiTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "audiototext-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "appsettings.Local.json");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Transcription:Provider", "Fake");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
            builder.UseSetting("CloudflareLocalSettings:Path", _settingsPath);
            builder.UseSetting("Cloudflare:AccountId", "");
            builder.UseSetting("Cloudflare:ApiToken", "");
        });
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public async Task GetStatus_InitiallyNotConfigured_AndDoesNotLeakSecrets()
    {
        var response = await _client.GetAsync("/api/settings/cloudflare");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("accountIdConfigured").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("apiTokenConfigured").GetBoolean());
        Assert.Equal(2, doc.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain("acct-", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Put_SavesValues_ReturnsStatusOnly_ClearsEcho()
    {
        const string account = "acct-secret-should-not-echo";
        const string token = "tok-secret-should-not-echo";

        var put = await _client.PutAsJsonAsync(
            "/api/settings/cloudflare",
            new { accountId = account, apiToken = token });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var putBody = await put.Content.ReadAsStringAsync();
        Assert.DoesNotContain(account, putBody, StringComparison.Ordinal);
        Assert.DoesNotContain(token, putBody, StringComparison.Ordinal);

        using (var doc = JsonDocument.Parse(putBody))
        {
            Assert.True(doc.RootElement.GetProperty("accountIdConfigured").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("apiTokenConfigured").GetBoolean());
            Assert.Equal(2, doc.RootElement.EnumerateObject().Count());
        }

        var get = await _client.GetAsync("/api/settings/cloudflare");
        var getBody = await get.Content.ReadAsStringAsync();
        Assert.DoesNotContain(account, getBody, StringComparison.Ordinal);
        Assert.DoesNotContain(token, getBody, StringComparison.Ordinal);

        Assert.True(File.Exists(_settingsPath));
        var fileJson = await File.ReadAllTextAsync(_settingsPath);
        Assert.Contains(account, fileJson);
        Assert.Contains(token, fileJson);
    }

    [Fact]
    public async Task Put_EmptyAccountId_ReturnsBadRequest()
    {
        var response = await _client.PutAsync(
            "/api/settings/cloudflare",
            new StringContent("""{"accountId":"   "}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ClearsConfiguredFlags()
    {
        await _client.PutAsJsonAsync(
            "/api/settings/cloudflare",
            new { accountId = "acct-to-clear", apiToken = "tok-to-clear" });

        var delAccount = await _client.DeleteAsync("/api/settings/cloudflare/account-id");
        Assert.Equal(HttpStatusCode.OK, delAccount.StatusCode);
        using (var doc = JsonDocument.Parse(await delAccount.Content.ReadAsStringAsync()))
        {
            Assert.False(doc.RootElement.GetProperty("accountIdConfigured").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("apiTokenConfigured").GetBoolean());
        }

        var delToken = await _client.DeleteAsync("/api/settings/cloudflare/api-token");
        Assert.Equal(HttpStatusCode.OK, delToken.StatusCode);
        var body = await delToken.Content.ReadAsStringAsync();
        Assert.DoesNotContain("tok-to-clear", body);
        using (var doc = JsonDocument.Parse(body))
        {
            Assert.False(doc.RootElement.GetProperty("accountIdConfigured").GetBoolean());
            Assert.False(doc.RootElement.GetProperty("apiTokenConfigured").GetBoolean());
        }
    }
}
