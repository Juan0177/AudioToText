using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AudioToText.Tests;

public class ProviderSettingsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public ProviderSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "audiototext-provider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var settingsPath = Path.Combine(_tempDir, "appsettings.Local.json");
        var modelsPath = Path.Combine(_tempDir, "models");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Transcription:Provider", "Fake");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
            builder.UseSetting("CloudflareLocalSettings:Path", settingsPath);
            builder.UseSetting("Transcription:ModelsPath", modelsPath);
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
            // ignore
        }
    }

    [Fact]
    public async Task GetSettings_IncludesProviderAndLocalStatus_WithoutSecrets()
    {
        var response = await _client.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        Assert.True(doc.RootElement.TryGetProperty("provider", out _));
        Assert.True(doc.RootElement.TryGetProperty("localModel", out _));
        Assert.True(doc.RootElement.TryGetProperty("local", out var local));
        Assert.True(local.TryGetProperty("modelReady", out _));
        Assert.True(local.TryGetProperty("runtime", out _));
        Assert.True(local.TryGetProperty("bytesTotal", out _));
        Assert.True(local.TryGetProperty("models", out var models));
        Assert.True(models.GetArrayLength() >= 4);
        Assert.True(doc.RootElement.TryGetProperty("ffmpeg", out var ffmpeg));
        Assert.True(ffmpeg.TryGetProperty("available", out _));
        Assert.True(ffmpeg.TryGetProperty("source", out _));
        Assert.True(ffmpeg.TryGetProperty("requirements", out _));
        Assert.DoesNotContain("tok-", body);
    }

    [Fact]
    public async Task GetFfmpegStatus_ReturnsStatusDto()
    {
        var response = await _client.GetAsync("/api/settings/ffmpeg");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("source", out _));
        Assert.True(doc.RootElement.TryGetProperty("sourceKind", out _));
        Assert.Contains("ogg", doc.RootElement.GetProperty("requirements").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostTldr_ReturnsExtractiveSummary()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/transcriptions/tldr",
            new
            {
                text =
                    "Ciao, ti chiamo più tardi per confermare l'appuntamento di domani alle tre. " +
                    "Porta i documenti. Se piove ci spostiamo online. Fammi sapere."
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("tldr", out var tldr));
        Assert.Equal("extractive", tldr.GetProperty("method").GetString());
        Assert.False(string.IsNullOrWhiteSpace(tldr.GetProperty("summary").GetString()));
    }

    [Fact]
    public async Task StartDownload_ReturnsAccepted_AndProgressEndpointWorks()
    {
        var start = await _client.PostAsJsonAsync(
            "/api/settings/local/download-model",
            new { model = "tiny" });
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);

        var progress = await _client.GetAsync("/api/settings/local/download-progress");
        Assert.Equal(HttpStatusCode.OK, progress.StatusCode);
        using var doc = JsonDocument.Parse(await progress.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("isDownloading", out _));
        Assert.True(doc.RootElement.TryGetProperty("downloadPercent", out _));

        await _client.PostAsync("/api/settings/local/download-cancel", null);
    }

    [Fact]
    public async Task PutProvider_Local_PersistsAndReturnsStatus()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/settings/provider",
            new { provider = "Local" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Local", doc.RootElement.GetProperty("provider").GetString());
        Assert.False(doc.RootElement.GetProperty("local").GetProperty("modelReady").GetBoolean());
    }

    [Fact]
    public async Task PutLocalModel_ValidatesAndSaves()
    {
        var bad = await _client.PutAsJsonAsync(
            "/api/settings/local-model",
            new { model = "huge" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await _client.PutAsJsonAsync(
            "/api/settings/local-model",
            new { model = "tiny" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using var doc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        Assert.Equal("tiny", doc.RootElement.GetProperty("localModel").GetString());
    }

    [Fact]
    public async Task LocalTranscribe_WithoutModel_ReturnsClearError()
    {
        await _client.PutAsJsonAsync("/api/settings/provider", new { provider = "Local" });
        await _client.PutAsJsonAsync("/api/settings/local-model", new { model = "tiny" });

        using var content = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
        bytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        content.Add(bytes, "file", "sample.wav");

        var response = await _client.PostAsync("/api/transcriptions", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Impostazioni", body, StringComparison.OrdinalIgnoreCase);
    }
}
